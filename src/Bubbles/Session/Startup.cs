using System.IO;

using Microsoft.Win32;

namespace Bubbles.Session;

/// <summary>The per-user Run value, as the registration needs to see it.
///
/// An interface because the real one writes to the hive of whoever is running the tests, and
/// "starts with Windows" is not a thing to switch on for somebody by running a test suite.</summary>
internal interface IRunKey
{
    /// <summary>What is registered, or null if nothing is.</summary>
    string? Value { get; }

    void Set(string value);

    void Delete();
}

/// <summary>Start Menu entries, as the registration needs to see them. An interface for the
/// same reason: the real one puts a shortcut in somebody's actual Start Menu.</summary>
internal interface IShortcuts
{
    bool Exists(string path);

    void Write(string path, string target);

    void Remove(string path);
}

/// <summary>Everything "start with Windows" means on this machine, and the rules tying the two
/// halves of it together.
///
/// There are two, because Windows uses two mechanisms and only writes one of them for you.
/// The Run value is what actually starts the application at login. The Start Menu shortcut is
/// what makes it findable: Windows search indexes Start Menu entries and does not index the
/// Run key, so an application that writes only the latter cannot be found by name at all.
/// There is no installer here to have created one, so until this existed there was no way to
/// start the application again after exiting it from the tray, short of knowing its path.
///
/// The Run value stays the authority on whether startup is on. The shortcut is a convenience
/// written alongside it, and its absence must never make the tray say startup is off -- the
/// registry is what the operating system acts on, and the menu reflects the operating system.
///
/// This does tie being findable to starting at login, which are not quite the same wish.
/// Disclosed rather than solved: one call site means no path can write half of the pair, and a
/// second setting is more surface than the problem deserves.</summary>
internal sealed class StartupRegistration
{
    private readonly IRunKey _run;
    private readonly IShortcuts _shortcuts;
    private readonly Func<string> _exePath;
    private readonly string _shortcutPath;

    internal StartupRegistration(
        IRunKey run, IShortcuts shortcuts, Func<string> exePath, string shortcutPath)
    {
        _run = run;
        _shortcuts = shortcuts;
        _exePath = exePath;
        _shortcutPath = shortcutPath;
    }

    /// <summary>Whether the application is registered to start with Windows. Read from the Run
    /// value alone: it is what the system acts on, and it can be changed outside this
    /// application entirely.</summary>
    public bool IsEnabled =>
        _run.Value is { } existing && existing.Contains("Bubbles", StringComparison.OrdinalIgnoreCase);

    public void Set(bool enabled)
    {
        // The registration first, and on its own. It is the half that matters, and it must be
        // recorded whatever the Start Menu does next.
        if (enabled) _run.Set($"\"{_exePath()}\"");
        else _run.Delete();

        Try(() =>
        {
            if (enabled) _shortcuts.Write(_shortcutPath, _exePath());
            else _shortcuts.Remove(_shortcutPath);
        },
        enabled ? "write the Start Menu entry" : "remove the Start Menu entry");
    }

    /// <summary>Gives the entry to an installation that was registered before there was one to
    /// give. Idempotent: a machine that already has it is left alone, and a machine that is not
    /// registered gets nothing -- this hands out what was missed, it does not decide policy.</summary>
    public void Reconcile()
    {
        if (!IsEnabled) return;

        Try(() =>
        {
            if (_shortcuts.Exists(_shortcutPath)) return;

            _shortcuts.Write(_shortcutPath, _exePath());
            Diagnostics.Log("startup: wrote the Start Menu entry that was missing");
        },
        "reconcile the Start Menu entry");
    }

    /// <summary>A machine that refuses a shortcut is not a machine that should fail to run the
    /// screensaver, and there is nowhere to report it to from a tray toggle.</summary>
    private static void Try(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"startup: could not {what} ({ex.GetType().Name}: {ex.Message})");
        }
    }
}

/// <summary>The settings window's startup control, as a decision separated from the checkbox.
///
/// Every other control in that window is a lens onto one `Settings` object, which is what makes
/// the window's promises work: the snapshot taken on opening holds every value, Cancel puts the
/// snapshot back, and the file is written once on the way out. This one is not. Startup lives in
/// the registry and the Start Menu, it can be turned off from Task Manager while the window is
/// open, and storing a copy of it here would disagree with the machine the first time that
/// happened.
///
/// So the window's rules are restated for it rather than inherited, and they are not symmetric:
///
/// <code>
/// Cancel            puts it back    -- Cancel means undo what you did in this window
/// Restore defaults  leaves it       -- that means put the screensaver back, and whether the
///                                      application starts with Windows is not one of the
///                                      screensaver's defaults
/// </code>
///
/// Restore-defaults needs no code here at all, which is the point of writing it down: startup is
/// not in `Settings`, so the defaults path cannot reach it even by accident. The omission is
/// load-bearing and reads as an oversight otherwise.</summary>
internal sealed class StartupControl
{
    private readonly Func<bool> _isEnabled;
    private readonly Action<bool> _set;
    private readonly bool _onOpen;

    /// <param name="isEnabled">How to read the machine. Asked every time rather than cached,
    /// because it can change while the window is open.</param>
    /// <param name="set">How to change it. Takes effect at once, as the tray entry it replaces
    /// did -- there is nothing to defer until the window closes.</param>
    internal StartupControl(Func<bool> isEnabled, Action<bool> set)
    {
        _isEnabled = isEnabled;
        _set = set;
        _onOpen = isEnabled();
    }

    /// <summary>What the machine says right now.</summary>
    public bool Current => _isEnabled();

    public void Toggle(bool on) => _set(on);

    /// <summary>Puts it back to how it was when the window opened. Returns whether it wrote.
    ///
    /// Only when the value actually moved. Cancelling a window in which the box was never
    /// touched must not write to somebody's registry and Start Menu on its way out, and the end
    /// state is identical either way -- so this is the difference that has to be asserted on.</summary>
    public bool Cancel()
    {
        if (_isEnabled() == _onOpen) return false;

        _set(_onOpen);
        return true;
    }
}

/// <summary>Registers the app in the per-user Run key, so it comes back after a reboot, and
/// in the per-user Start Menu, so it can be found again after it has been closed.</summary>
internal static class Startup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Bubbles";

    /// <summary>Per-user, matching the Run key it is written beside: no elevation, and nothing
    /// written for anybody else's account.</summary>
    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "Bubbles.lnk");

    private static readonly StartupRegistration Registration = new(
        new RegistryRunKey(),
        new ShellShortcuts(),
        () => Environment.ProcessPath ?? "",
        ShortcutPath);

    public static bool IsEnabled => Registration.IsEnabled;

    public static void Set(bool enabled) => Registration.Set(enabled);

    /// <summary>Called once at startup, for installations registered before the Start Menu
    /// entry existed. Costs a file existence check on every machine that already has one.</summary>
    public static void Reconcile() => Registration.Reconcile();

    private sealed class RegistryRunKey : IRunKey
    {
        public string? Value
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                    return key?.GetValue(ValueName) as string;
                }
                catch
                {
                    return null;
                }
            }
        }

        public void Set(string value)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                key?.SetValue(ValueName, value);
            }
            catch
            {
            }
        }

        public void Delete()
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            catch
            {
            }
        }
    }

    /// <summary>A `.lnk` written through the shell's own scripting object. There is no managed
    /// API for shell links, and this is the ordinary route to one.
    ///
    /// Deliberately throws rather than swallowing: what to do about a failure is
    /// <see cref="StartupRegistration"/>'s decision, and it has the registration in hand to
    /// know that the important half already succeeded.</summary>
    private sealed class ShellShortcuts : IShortcuts
    {
        public bool Exists(string path) => File.Exists(path);

        public void Write(string path, string target)
        {
            var type = Type.GetTypeFromProgID("WScript.Shell")
                       ?? throw new InvalidOperationException("WScript.Shell is not registered");

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            dynamic shell = Activator.CreateInstance(type)!;
            var link = shell.CreateShortcut(path);

            link.TargetPath = target;
            link.WorkingDirectory = Path.GetDirectoryName(target) ?? "";
            link.Description = "Bubbles screensaver";
            link.Save();
        }

        public void Remove(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
