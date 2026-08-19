using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Bubbles;

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
/// rather than leaving somebody with a dark screen and no idea why.</summary>
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

    private sealed record Saved(string Device, string Description, uint Brightness);

    private static string StateFile => Path.Combine(Settings.Directory, "display-state.json");

    private List<Saved> _saved = new();
    private bool _dimmed;

    /// <summary>True when at least one monitor takes backlight commands.</summary>
    public bool Available { get; private set; }

    public MonitorBacklight()
    {
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
        try
        {
            if (!File.Exists(StateFile)) return;

            var saved = JsonSerializer.Deserialize<List<Saved>>(File.ReadAllText(StateFile));
            if (saved is { Count: > 0 })
            {
                _saved = saved;
                _dimmed = true;
                Diagnostics.Log($"restoring {saved.Count} monitor(s) left dimmed by a previous run");
                Restore();
            }
            else
            {
                File.Delete(StateFile);
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"display recovery failed: {ex.Message}");
        }
    }

    /// <summary>Takes the backlight to its minimum, remembering where it was.</summary>
    public void Dim(bool alsoStandby)
    {
        if (_dimmed) return;

        var saved = new List<Saved>();

        ForEachMonitor((handle, device, description) =>
        {
            uint minimum = 0, current = 0, maximum = 0;
            if (!GetMonitorBrightness(handle, ref minimum, ref current, ref maximum)) return;

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
                Diagnostics.Log($"{device} ignored the backlight request " +
                                $"(asked {current}->{minimum}, still reads {verifyCurrent}); " +
                                "DDC/CI is probably disabled or blocked on this link");
                return;
            }

            saved.Add(new Saved(device, description, current));
            if (alsoStandby) SetVCPFeature(handle, (byte)PowerModeVcp, PowerStandby);
        });

        if (saved.Count == 0)
        {
            Diagnostics.Log("no monitor accepted a backlight change");
            return;
        }

        _saved = saved;
        _dimmed = true;

        try
        {
            Directory.CreateDirectory(Settings.Directory);
            File.WriteAllText(StateFile, JsonSerializer.Serialize(saved));
        }
        catch
        {
            // Recovery is a nicety; dimming still works without it.
        }

        Diagnostics.Log($"dimmed {saved.Count} external monitor(s)" + (alsoStandby ? " and asked for standby" : ""));
    }

    /// <summary>Puts every monitor back where it was found.</summary>
    public void Restore()
    {
        if (!_dimmed) return;
        _dimmed = false;

        ForEachMonitor((handle, device, description) =>
        {
            // Keyed by display device rather than enumeration order, so unplugging one monitor
            // cannot make another get somebody else's brightness back.
            var saved = _saved.FirstOrDefault(s => s.Device == device);
            if (saved is null) return;

            // Wake before brightness: a monitor in standby ignores everything else.
            SetVCPFeature(handle, (byte)PowerModeVcp, PowerOn);
            SetMonitorBrightness(handle, saved.Brightness);
        });

        _saved = new List<Saved>();

        try
        {
            if (File.Exists(StateFile)) File.Delete(StateFile);
        }
        catch
        {
        }

        Diagnostics.Log("external monitors restored");
    }

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
