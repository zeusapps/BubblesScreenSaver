using Microsoft.Win32;
using System.Runtime.InteropServices;

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
    private static string? _reason;

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

    /// <summary>Why the overlay is holding off, or null if it need not.</summary>
    public static string? Reason(Settings settings)
    {
        if (DateTime.UtcNow - _checkedAt < CacheFor) return _reason;
        _checkedAt = DateTime.UtcNow;
        _reason = Evaluate(settings);
        return _reason;
    }

    private static string? Evaluate(Settings settings)
    {
        if (settings.PauseWhileMicrophoneInUse && DeviceInUse("microphone", out var micApp))
            return $"microphone in use by {micApp}";

        if (settings.PauseWhileCameraInUse && DeviceInUse("webcam", out var camApp))
            return $"camera in use by {camApp}";

        if (settings.PauseInFullScreen && FullScreen(out var state))
            return $"a full-screen or presenting application is running ({state})";

        return null;
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
