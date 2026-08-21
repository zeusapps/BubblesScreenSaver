using System.Runtime.InteropServices;

namespace Bubbles.Interop;

/// <summary>Whether the session is locked.
///
/// A locked session is not an idle session. The sign-in screen lives on the secure desktop,
/// which no ordinary process can draw on -- so a screensaver that carries on regardless is
/// animating into nothing, keeping a GPU busy for an audience of no one, and waiting to
/// ambush whoever unlocks. Worse, its blackout would go on dimming monitors over DDC/CI
/// behind a sign-in prompt it cannot draw over to explain itself.
///
/// Input to the lock screen is invisible from here -- GetLastInputInfo does not see the secure
/// desktop -- so the idle timer would keep climbing the whole time the machine sits locked and
/// conclude the user had left. Treating it as a hold-off puts that right too, because time
/// held off is discounted rather than counted as time away.</summary>
internal static class SessionState
{
    private const int NotificationForThisSession = 0;
    private const int SessionLock = 0x7;
    private const int SessionUnlock = 0x8;

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSRegisterSessionNotification(IntPtr window, int flags);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr server, uint session, int infoClass, out IntPtr buffer, out uint bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);

    private static bool _locked;

    static SessionState()
    {
        // The event tells us about every change from here on. The initial reading has to come
        // from somewhere else, because the app can be started by a scheduled task or a fast
        // relaunch while the machine is already locked, and would otherwise spend until the
        // first unlock believing it was not.
        _locked = QueryLocked() ?? false;

        Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
        {
            switch (e.Reason)
            {
                case Microsoft.Win32.SessionSwitchReason.SessionLock:
                    _locked = true;
                    Diagnostics.Log("session locked; standing down");
                    break;

                case Microsoft.Win32.SessionSwitchReason.SessionUnlock:
                    _locked = false;
                    Diagnostics.Log("session unlocked");
                    break;
            }
        };
    }

    public static bool Locked => _locked;

    /// <summary>Called once at startup so the static constructor runs before anything asks.</summary>
    public static void Watch()
    {
    }

    /// <summary>The session's own lock flag, or null if it cannot be read -- which is not the
    /// same as unlocked, so the caller decides what to assume.</summary>
    private static bool? QueryLocked()
    {
        // WTS_SESSIONSTATE_LOCK, from WTSQuerySessionInformation class 25 (WTSSessionInfoEx).
        // The flags field sits at a fixed offset in WTSINFOEXW; reading the whole struct is not
        // worth it for one bit.
        const int SessionInfoEx = 25;
        const int LockStateOffset = 16;

        var buffer = IntPtr.Zero;

        try
        {
            if (!WTSQuerySessionInformationW(IntPtr.Zero, WTSGetActiveConsoleSessionId(),
                    SessionInfoEx, out buffer, out var bytes) || bytes < LockStateOffset + 4)
            {
                return null;
            }

            // 0 = locked, 1 = unlocked. Yes, that way round.
            return Marshal.ReadInt32(buffer, LockStateOffset) == 0;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero) WTSFreeMemory(buffer);
        }
    }
}
