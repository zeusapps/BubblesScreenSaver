using System.Runtime.InteropServices;

namespace Bubbles;

/// <summary>Win32 bits needed to make a WPF window an invisible, click-through, always-on-top sheet of glass.</summary>
internal static class Native
{
    public const int GWL_EXSTYLE = -20;

    public const int WS_EX_TRANSPARENT = 0x00000020; // skipped during hit-testing -> clicks fall through
    public const int WS_EX_TOOLWINDOW  = 0x00000080; // no alt-tab entry
    public const int WS_EX_NOACTIVATE  = 0x08000000; // never takes focus

    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOZORDER   = 0x0004;

    public const int SM_XVIRTUALSCREEN  = 76;
    public const int SM_YVIRTUALSCREEN  = 77;
    public const int SM_CXVIRTUALSCREEN = 78;
    public const int SM_CYVIRTUALSCREEN = 79;

    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindowLongPtrW(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int nIndex);

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref Margins margins);

    /// <summary>Extends the DWM frame over the whole client area, which turns fully-transparent
    /// pixels into real holes while keeping the window hardware accelerated. (Unlike WPF's
    /// AllowsTransparency, which forces the entire window onto the software renderer.)</summary>
    public static bool MakeGlass(IntPtr hwnd)
    {
        var m = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        return DwmExtendFrameIntoClientArea(hwnd, ref m) == 0;
    }

    public static void MakeClickThrough(IntPtr hwnd, bool clickThrough)
    {
        var ex = (long)GetWindowLongPtrW(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        if (clickThrough) ex |= WS_EX_TRANSPARENT;
        else ex &= ~WS_EX_TRANSPARENT;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, (IntPtr)ex);
    }

    /// <summary>Bounds of the whole virtual desktop, in physical pixels.</summary>
    public static (int X, int Y, int W, int H) VirtualScreen() =>
    (
        GetSystemMetrics(SM_XVIRTUALSCREEN),
        GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN),
        GetSystemMetrics(SM_CYVIRTUALSCREEN)
    );
}
