using System.Runtime.InteropServices;

namespace Bubbles.Interop;

/// <summary>Hands the machine back to Windows' own lock screen.
///
/// Deliberately not a PIN box of our own. A prompt drawn by this app would be security
/// theatre: the overlay is click-through and does not hold the keyboard, so Alt+Tab, the task
/// manager, a remote session, or simply killing the process would all walk straight past it,
/// and it would have to hold a credential to check against. LockWorkStation puts the real
/// thing up -- the secure desktop, on which no ordinary process can draw or listen -- so what
/// unlocks the machine is whatever already unlocks it: PIN, password, or Windows Hello.</summary>
internal static class SessionLock
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();

    /// <summary>Locks the session. Already-locked is not an error, and neither is a policy
    /// that forbids it -- there is nothing sensible to do about either but carry on.</summary>
    public static void Request()
    {
        try
        {
            if (LockWorkStation())
            {
                Diagnostics.Log("session locked");
                return;
            }

            Diagnostics.Log($"could not lock the session: {Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"could not lock the session: {ex.Message}");
        }
    }
}
