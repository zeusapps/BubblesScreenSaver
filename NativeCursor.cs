using System.Runtime.InteropServices;

namespace Bubbles;

/// <summary>Hides the mouse pointer while the overlay is up.
///
/// A white arrow parked on one pixel of a black OLED screen for ten minutes is exactly the
/// burn-in this app exists to prevent, and the pointer is drawn by the compositor above
/// every window, so it cannot simply be painted over.
///
/// This sets the cursor image for the current thread and nothing more. It is deliberately
/// self-healing: the moment the mouse moves, the window underneath receives WM_SETCURSOR
/// and restores its own pointer -- which is also the moment the overlay goes away. If this
/// process dies while the pointer is hidden, the next mouse movement brings it back.
/// No system-wide cursor state is ever written.</summary>
internal static class NativeCursor
{
    private const int IDC_ARROW = 32512;

    [DllImport("user32.dll")]
    private static extern IntPtr SetCursor(IntPtr hCursor);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursorW(IntPtr hInstance, IntPtr lpCursorName);

    public static void Hide() => SetCursor(IntPtr.Zero);

    public static void Restore() => SetCursor(LoadCursorW(IntPtr.Zero, IDC_ARROW));
}
