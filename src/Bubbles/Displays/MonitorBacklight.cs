using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Bubbles.Displays;

/// <summary>Turns monitor backlights down while the screen is blacked out.
///
/// Drawing black is a complete answer on OLED, where a black pixel is unlit. An LCD is still
/// backlit behind that black and goes on glowing. The obvious design would be to detect which
/// is which -- but Windows exposes no reliable way to ask a display what panel technology it
/// uses, and guessing from model strings or connection type is exactly the sort of assumption
/// that breaks on somebody else's desk.
///
/// So this asks about capability rather than technology: every monitor is offered a backlight
/// change, and whichever accept one get it. That is correct for any arrangement -- all OLED,
/// none, or a mixture -- because black already covers OLED, and lowering an OLED's luminance
/// does no harm either. Nothing is treated differently for being internal or external.
///
/// Brightness over DDC/CI is a display-side setting, not a power state, so nothing about the
/// machine's power management is touched. The original value is written to disk before
/// anything changes, so even a session that ends badly restores the monitor on the next run
/// rather than leaving somebody with a dark screen and no idea why.
///
/// A monitor that is unplugged while dim keeps its entry until the brightness has actually
/// been put back. That bookkeeping lives in <see cref="PendingRestore{T}"/>, which spells out
/// why it is delicate.</summary>
public sealed class MonitorBacklight
{
    private const uint PowerModeVcp = 0xD6;
    private const uint PowerOn = 1;
    private const uint PowerStandby = 4;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PhysicalMonitor
    {
        public IntPtr Handle;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr rect, MonitorEnumProc callback, IntPtr data);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, ref uint count);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint count, [In] PhysicalMonitor[] monitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetMonitorBrightness(IntPtr monitor, ref uint minimum, ref uint current, ref uint maximum);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr monitor, uint brightness);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfoEx info);

    /// <summary>Public so the state file can be serialised, and so the bookkeeping can be
    /// exercised without a monitor attached.</summary>
    public sealed record Saved(string Device, string Description, uint Brightness);

    private static string StateFile => Path.Combine(Settings.Directory, "display-state.json");

    // Brightness still owed back to a monitor. Survives the monitor going away, and the process.
    private readonly PendingRestore<Saved> _owed;
    private bool _dimmed;

    /// <summary>True when at least one monitor takes backlight commands.</summary>
    public bool Available { get; private set; }

    public MonitorBacklight() : this(StateFile)
    {
    }

    /// <param name="stateFile">Where the record of owed brightness survives a crash.</param>
    public MonitorBacklight(string stateFile)
    {
        _owed = new PendingRestore<Saved>(stateFile, entry => entry.Device, "backlight");

        ForEachMonitor((handle, _, _) =>
        {
            uint minimum = 0, current = 0, maximum = 0;
            if (GetMonitorBrightness(handle, ref minimum, ref current, ref maximum)) Available = true;
        });
    }

    /// <summary>Current backlight readings, for diagnostics and the --dim-test flag.</summary>
    public List<string> Read()
    {
        var readings = new List<string>();

        ForEachMonitor((handle, device, description) =>
        {
            uint minimum = 0, current = 0, maximum = 0;
            readings.Add(GetMonitorBrightness(handle, ref minimum, ref current, ref maximum)
                ? $"  {device}  {description.Trim()}: brightness {current} (range {minimum}..{maximum})"
                : $"  {device}  {description.Trim()}: no backlight control over DDC/CI");
        });

        return readings;
    }

    /// <summary>Puts back anything a previous run left dimmed. Call once at startup.</summary>
    public void RecoverFromCrash()
    {
        _owed.Load();
        RestoreWhatIsAttached("previous run");
    }

    /// <summary>Takes the backlight to its minimum, remembering where it was.</summary>
    public void Dim(bool alsoStandby)
    {
        if (_dimmed) return;

        var accepted = new List<Saved>();

        // One enumeration, deliberately. An earlier version read every monitor in a first pass
        // and dimmed them in a second, which is tidier to read and does not work: opening and
        // destroying the physical monitor handles twice in quick succession leaves this
        // display accepting the write, verifying it, and then sliding back to full brightness
        // a second later. Measured, not theorised.
        ForEachMonitor((handle, device, description) =>
        {
            uint minimum = 0, current = 0, maximum = 0;
            if (!GetMonitorBrightness(handle, ref minimum, ref current, ref maximum)) return;

            // On disk before the change it describes, monitor by monitor. Recording the whole
            // set afterwards would leave a window in which everything is at minimum with
            // nothing to say what it was -- exactly the stranded-monitor failure this record
            // exists to prevent. Anything already owed keeps its first original, so a monitor
            // that reconnected at zero does not get zero written down as its brightness.
            var original = new Saved(device, description, current);
            _owed.Remember([original]);

            // The brightness goes down first, so that a monitor which refuses the standby
            // request is still dark rather than untouched.
            SetMonitorBrightness(handle, minimum);

            // A monitor whose DDC/CI channel is disabled or blocked accepts these writes and
            // returns success while changing nothing, so the only trustworthy check is to read
            // the value back. Without this the app would happily report that it had dimmed a
            // monitor that is still at full brightness.
            uint verifyMinimum = 0, verifyCurrent = 0, verifyMaximum = 0;
            var readable = GetMonitorBrightness(handle, ref verifyMinimum, ref verifyCurrent, ref verifyMaximum);
            var took = readable && verifyCurrent != current;

            if (!took && current != minimum)
            {
                // Nothing moved, so nothing is owed back.
                _owed.Forget([original]);

                Diagnostics.Log($"{device} ignored the backlight request " +
                                $"(asked {current}->{minimum}, still reads {verifyCurrent}). " +
                                "The usual cause is HDR: while it is on, Windows owns the " +
                                "luminance pipeline and the monitor's own brightness is locked. " +
                                "Run --dim-test to see which displays have HDR enabled.");
                return;
            }

            accepted.Add(original);

            if (alsoStandby) SetVCPFeature(handle, (byte)PowerModeVcp, PowerStandby);
        });

        if (accepted.Count == 0)
        {
            Diagnostics.Log("no monitor accepted a backlight change");
            return;
        }

        _dimmed = true;

        Diagnostics.Log($"dimmed {accepted.Count} external monitor(s)" + (alsoStandby ? " and asked for standby" : ""));
    }

    /// <summary>Puts every monitor to full brightness without touching the record, standing in
    /// for a monitor that has raised its own backlight. Only for --hold-test: it makes the
    /// drift reproducible on demand instead of waiting an hour for a panel to do it by
    /// itself.</summary>
    internal void SimulateExternalChange()
    {
        ForEachMonitor((handle, _, _) =>
        {
            uint minimum = 0, current = 0, maximum = 0;
            if (GetMonitorBrightness(handle, ref minimum, ref current, ref maximum))
                SetMonitorBrightness(handle, maximum);
        });
    }

    /// <summary>Puts the dim back on any monitor that has quietly drifted up again while the
    /// screen is still meant to be dark.
    ///
    /// Monitors do this on their own. Nothing in Windows raised the backlight and no display
    /// event was logged, but an hour into a blackout the panel was lit again -- a monitor
    /// resetting its own brightness when it leaves its internal power-save, or an ambient-light
    /// or adaptive-backlight feature deciding it knows better. DDC/CI is a request, not a lock.
    ///
    /// So the state is held rather than set once. Nothing is recorded here: the original was
    /// written down when the dim began and must not be replaced by whatever the monitor has
    /// wandered to, or the value it is owed becomes the value it drifted to.</summary>
    /// <returns>The monitors that had to be put back.</returns>
    public List<string> Reassert()
    {
        var owed = _owed.Owed;
        if (owed.Count == 0) return new List<string>();

        var dimmedAgain = new List<string>();

        ForEachMonitor((handle, device, _) =>
        {
            if (owed.All(entry => entry.Device != device)) return;

            uint minimum = 0, current = 0, maximum = 0;
            if (!GetMonitorBrightness(handle, ref minimum, ref current, ref maximum)) return;

            // Still where it was put; nothing to do, and nothing written to the monitor.
            if (current <= minimum) return;

            SetMonitorBrightness(handle, minimum);

            uint verifyMinimum = 0, verifyCurrent = 0, verifyMaximum = 0;
            if (GetMonitorBrightness(handle, ref verifyMinimum, ref verifyCurrent, ref verifyMaximum) &&
                verifyCurrent <= verifyMinimum)
            {
                dimmedAgain.Add(device);
            }
        });

        return dimmedAgain;
    }

    /// <summary>Puts every monitor back where it was found. Anything not currently attached
    /// keeps its entry and is dealt with when it reappears.</summary>
    public void Restore()
    {
        _dimmed = false;
        RestoreWhatIsAttached("restore");
    }

    /// <summary>Retries anything still owed. Called when the displays change, since that is
    /// exactly when a monitor that was unplugged mid-blackout comes back.</summary>
    public void RestorePending() => RestoreWhatIsAttached("reconnect");

    private void RestoreWhatIsAttached(string why) => _owed.Settle(owed =>
    {
        var done = new List<string>();

        ForEachMonitor((handle, device, _) =>
        {
            // Keyed by display device rather than enumeration order, so unplugging one monitor
            // cannot make another get somebody else's brightness back.
            var saved = owed.FirstOrDefault(entry => entry.Device == device);
            if (saved is null) return;

            // Wake before brightness: a monitor in standby ignores everything else.
            SetVCPFeature(handle, (byte)PowerModeVcp, PowerOn);
            SetMonitorBrightness(handle, saved.Brightness);

            // Believe it only once the monitor reads back what it was given: a DDC/CI channel
            // that is disabled accepts the write and changes nothing.
            uint minimum = 0, current = 0, maximum = 0;
            if (GetMonitorBrightness(handle, ref minimum, ref current, ref maximum) && current == saved.Brightness)
                done.Add(device);
        });

        return done;
    }, why);

    /// <summary>Opens every physical monitor in turn, and always closes them again. The
    /// callback receives the display device name, which is stable enough to key state on.</summary>
    private static void ForEachMonitor(Action<IntPtr, string, string> body)
    {
        var screens = new List<IntPtr>();

        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, _, _, _) =>
            {
                screens.Add(monitor);
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"monitor enumeration failed: {ex.Message}");
            return;
        }

        foreach (var screen in screens)
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            var device = GetMonitorInfoW(screen, ref info) ? info.DeviceName : screen.ToString();

            uint count = 0;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(screen, ref count) || count == 0) continue;

            var monitors = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(screen, count, monitors)) continue;

            try
            {
                foreach (var monitor in monitors)
                {
                    // Plenty of panels answer nothing here -- most internal ones, and any
                    // monitor whose DDC/CI channel is off. That is expected, not an error.
                    try
                    {
                        body(monitor.Handle, device, monitor.Description);
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Log($"{device} command failed: {ex.Message}");
                    }
                }
            }
            finally
            {
                DestroyPhysicalMonitors(count, monitors);
            }
        }
    }
}
