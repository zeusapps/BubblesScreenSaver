using Microsoft.Win32;
using System.Runtime.InteropServices;

using Bubbles.Interop;

namespace Bubbles.Session;

/// <summary>Works out whether somebody is busy despite not touching the keyboard.
///
/// An idle timer measures input, and a video call produces none: you sit still, listening, and
/// the screensaver decides you have left. So before anything is drawn, this asks whether the
/// microphone or camera is in use, and whether a full-screen or presenting application is on
/// screen.
///
/// The microphone and camera state comes from the same records Windows keeps for the privacy
/// indicator in the taskbar -- an app currently holding the device has a start time and no stop
/// time. It is readable without elevation, it covers packaged and desktop apps alike, and it is
/// what the operating system itself trusts.</summary>
internal static class UserBusy
{
    private const string ConsentStore =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    // Rechecking the registry on every tick would be wasteful for something that changes on a
    // human timescale.
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(2);

    private static DateTime _checkedAt = DateTime.MinValue;
    private static HoldOff _held = HoldOff.None;

    private enum NotificationState
    {
        NotPresent = 1,
        Busy = 2,
        FullScreenDirect3D = 3,
        PresentationMode = 4,
        AcceptsNotifications = 5,
        QuietTime = 6,
        FullScreenApp = 7,
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out NotificationState state);

    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public WindowRect Monitor;
        public WindowRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect bounds);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr window, System.Text.StringBuilder name, int count);

    /// <summary>Which stages are being held off, and why.</summary>
    public static HoldOff Held(Settings settings)
    {
        if (DateTime.UtcNow - _checkedAt < CacheFor) return _held;
        _checkedAt = DateTime.UtcNow;
        _held = Evaluate(settings);
        return _held;
    }

    /// <summary>Why the overlay is holding off, or null if it need not.</summary>
    public static string? Reason(Settings settings) => Held(settings).Reason;

    private static HoldOff Evaluate(Settings settings)
    {
        // First, and with no setting to turn it off. There is no arrangement in which drawing
        // a screensaver onto a screen the user cannot see is the right thing to do, and the
        // blackout behind it would dim monitors under a sign-in prompt it cannot draw over.
        if (SessionState.Locked) return HoldOff.Everything("the session is locked");

        if (settings.PauseWhileMicrophoneInUse && DeviceInUse("microphone", out var micApp))
            return HoldOff.Everything($"microphone in use by {micApp}");

        if (settings.PauseWhileCameraInUse && DeviceInUse("webcam", out var camApp))
            return HoldOff.Everything($"camera in use by {camApp}");

        if (settings.PauseInFullScreen && FullScreen(out var state))
            return HoldOff.Everything($"a full-screen or presenting application is running ({state})");

        if (settings.PauseInFullScreen && FillsAScreen(out var window))
            return HoldOff.Everything($"a window is filling the screen ({window})");

        // Before the meter, because it can tell watching from listening and the meter cannot.
        // Video first: silent footage produces nothing for the meter to hear at all, which is
        // the case that defeated every other signal here.
        if (settings.PauseWhileMediaPlaying)
        {
            var playing = MediaSessions.Playing(out var player);

            if (playing == MediaKind.Video)
                return HoldOff.Everything($"video is playing in {player}");

            if (playing == MediaKind.Music)
                return HoldOff.ArtifactsOnly($"music is playing in {player}");
        }

        if (settings.PauseWhileAudioPlaying &&
            Sound.Playing(AudioActivity.Peak(), Environment.TickCount64))
        {
            return HoldOff.Everything("sound is playing");
        }

        return HoldOff.None;
    }

    private static readonly SoundWatch Sound = new();

    /// <summary>Whether a window's bounds are a monitor's bounds, give or take a pixel.
    ///
    /// Equality on all four edges, deliberately, rather than "covers at least". A maximised
    /// window is not fullscreen and must not read as one -- holding off for any maximised
    /// window is the QUNS_BUSY mistake, which would keep the screensaver from ever running.
    ///
    /// The tempting test is whether the window stops short of the bottom, leaving room for the
    /// taskbar. That works right up until somebody hides their taskbar, at which point the work
    /// area becomes the whole monitor and every maximised window looks fullscreen. Equality
    /// does not care: a maximised window overshoots its monitor by the width of its invisible
    /// resize border, and a fullscreen one lands exactly on it.</summary>
    internal static bool FillsMonitor(
        int windowLeft, int windowTop, int windowRight, int windowBottom,
        int monitorLeft, int monitorTop, int monitorRight, int monitorBottom)
    {
        const int Slack = 2;

        return Math.Abs(windowLeft - monitorLeft) <= Slack &&
               Math.Abs(windowTop - monitorTop) <= Slack &&
               Math.Abs(windowRight - monitorRight) <= Slack &&
               Math.Abs(windowBottom - monitorBottom) <= Slack;
    }

    /// <summary>The foreground window and how it measures up against its monitor, so the
    /// fullscreen decision can be inspected rather than guessed at. A maximised window must
    /// come out as *not* filling the screen, or this repeats the QUNS_BUSY mistake.</summary>
    internal static string DescribeForeground()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return "  no foreground window";

        var className = new System.Text.StringBuilder(256);
        GetClassNameW(window, className, className.Capacity);

        if (!GetWindowRect(window, out var bounds)) return $"  {className}: no rect";

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        GetMonitorInfoW(MonitorFromWindow(window, MonitorDefaultToNearest), ref info);

        var fills = FillsAScreen(out _);

        return string.Join(Environment.NewLine,
            $"  class      {className}",
            $"  window     {bounds.Left},{bounds.Top} to {bounds.Right},{bounds.Bottom}",
            $"  monitor    {info.Monitor.Left},{info.Monitor.Top} to {info.Monitor.Right},{info.Monitor.Bottom}",
            $"  work area  {info.Work.Left},{info.Work.Top} to {info.Work.Right},{info.Work.Bottom}",
            $"  fills the screen: {fills}");
    }

    /// <summary>Whether the foreground window covers a whole monitor.
    ///
    /// SHQueryUserNotificationState only reports FullScreenDirect3D for a browser playing video
    /// intermittently, and never for a window that merely fills the screen, so geometry is the
    /// more dependable question. A *maximised* window is not this: it stops at the work area
    /// and leaves the taskbar showing, whereas fullscreen covers the monitor entirely -- which
    /// is what makes this usable where QUNS_BUSY was not.</summary>
    private static bool FillsAScreen(out string what)
    {
        what = string.Empty;

        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;

            // The desktop is always "fullscreen" and never means somebody is watching it.
            var className = new System.Text.StringBuilder(256);
            GetClassNameW(window, className, className.Capacity);

            var name = className.ToString();
            if (name is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;

            if (!GetWindowRect(window, out var bounds)) return false;

            var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfoW(monitor, ref info)) return false;

            if (!FillsMonitor(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom,
                    info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom))
            {
                return false;
            }

            what = name;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True while any application is holding the given capability open.</summary>
    private static bool DeviceInUse(string capability, out string? app)
    {
        app = null;

        try
        {
            using var root = Registry.CurrentUser.OpenSubKey($@"{ConsentStore}\{capability}");
            if (root is null) return false;

            foreach (var name in root.GetSubKeyNames())
            {
                using var branch = root.OpenSubKey(name);
                if (branch is null) continue;

                // Desktop applications are nested one level deeper, under NonPackaged.
                if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var child in branch.GetSubKeyNames())
                    {
                        using var entry = branch.OpenSubKey(child);
                        if (InUse(entry))
                        {
                            app = Pretty(child);
                            return true;
                        }
                    }

                    continue;
                }

                if (InUse(branch))
                {
                    app = Pretty(name);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"could not read {capability} usage: {ex.Message}");
        }

        return false;
    }

    /// <summary>An app that started using the device and has not stopped is using it now.</summary>
    private static bool InUse(RegistryKey? key)
    {
        if (key is null) return false;

        var started = key.GetValue("LastUsedTimeStart");
        var stopped = key.GetValue("LastUsedTimeStop");

        return started is long start && start > 0 && stopped is long stop && stop == 0;
    }

    /// <summary>Registry keys store paths with # instead of \, so turn one back into a name.</summary>
    private static string Pretty(string key)
    {
        var path = key.Replace('#', '\\');
        var name = path.Contains('\\') ? path[(path.LastIndexOf('\\') + 1)..] : path;
        return string.IsNullOrWhiteSpace(name) ? key : name;
    }

    private static bool FullScreen(out NotificationState state)
    {
        state = NotificationState.AcceptsNotifications;

        try
        {
            if (SHQueryUserNotificationState(out state) != 0) return false;
        }
        catch
        {
            return false;
        }

        // Only the unambiguous ones. QUNS_BUSY sounds right and is useless: it reports true
        // for any maximised window covering the screen, which on a normal desktop is nearly
        // always, and would hold the screensaver off permanently. Measured on a plain
        // maximised terminal. QuietTime is Focus Assist, which says do not interrupt rather
        // than somebody is sitting here.
        return state is NotificationState.FullScreenDirect3D or NotificationState.PresentationMode;
    }
}
