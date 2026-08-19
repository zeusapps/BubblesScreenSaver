using System.Runtime.InteropServices;

namespace Bubbles.Interop;

/// <summary>Reading the system idle timer. Nothing in this app touches display or system
/// power any more -- see the note below.</summary>
///
/// <remarks>
/// This used to broadcast WM_SYSCOMMAND / SC_MONITORPOWER to turn the panel off.
/// Do not bring that back. On a Modern Standby (S0ix) machine -- which most current
/// laptops are -- that message does not power down the display, it puts the entire
/// system into standby. Combined with a retry timer it produced an inescapable
/// wake/sleep loop that could only be broken by holding the power button.
///
/// There is no supported "display off, system running" API on Modern Standby.
/// The blackout stage draws a black screen instead, which on an OLED panel means
/// genuinely unlit pixels -- the same result for burn-in, with no power state involved.
/// </remarks>
internal static class NativeInput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo plii);

    [DllImport("kernel32.dll")]
    private static extern uint GetTickCount();

    /// <summary>Tick count of the last real keyboard or mouse input. Changes only when a
    /// human touches something, which makes it a reliable "has anything happened since?" marker.</summary>
    public static uint LastInputTick()
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref info) ? info.dwTime : GetTickCount();
    }

    /// <summary>Seconds since the last real keyboard or mouse input, system-wide.</summary>
    public static double IdleSeconds() =>
        // Both are 32-bit millisecond tick counts, so unchecked subtraction survives the ~49-day wrap.
        unchecked(GetTickCount() - LastInputTick()) / 1000.0;
}
