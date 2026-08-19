using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Bubbles;

/// <summary>Turns the backlight down on external monitors while the screen is blacked out.
///
/// Drawing black is enough for an OLED panel, where black pixels are simply unlit. An LCD is
/// still fully backlit behind that black, so it goes on glowing in a dark room. Most external
/// monitors expose their backlight over DDC/CI, which is a display-side setting rather than a
/// power state -- nothing about the machine's power management is touched, and it is put back
/// exactly as it was.
///
/// The original brightness is written to disk before anything changes, so a session that ends
/// badly still restores the monitor on the next run rather than leaving somebody with a dark
/// screen and no idea why.</summary>
public sealed class ExternalDisplays
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

    private sealed record Saved(int Index, string Description, uint Brightness);

    private static string StateFile => Path.Combine(Settings.Directory, "display-state.json");

    private List<Saved> _saved = new();
    private bool _dimmed;

    /// <summary>True when at least one monitor takes backlight commands.</summary>
    public bool Available { get; private set; }

    public ExternalDisplays()
    {
        ForEachMonitor((_, _, _) => Available = true);
    }

    /// <summary>Current backlight readings, for diagnostics and the --dim-test flag.</summary>
    public List<string> Read()
    {
        var readings = new List<string>();

        ForEachMonitor((handle, index, description) =>
        {
            uint minimum = 0, current = 0, maximum = 0;
            readings.Add(GetMonitorBrightness(handle, ref minimum, ref current, ref maximum)
                ? $"  [{index}] {description.Trim()}: brightness {current} (of {minimum}..{maximum})"
                : $"  [{index}] {description.Trim()}: no backlight control");
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

        ForEachMonitor((handle, index, description) =>
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
                Diagnostics.Log($"monitor {index} ignored the backlight request " +
                                $"(asked {current}->{minimum}, still reads {verifyCurrent}); " +
                                "DDC/CI is probably disabled or blocked on this link");
                return;
            }

            saved.Add(new Saved(index, description, current));
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

        ForEachMonitor((handle, index, description) =>
        {
            var saved = _saved.FirstOrDefault(s => s.Index == index && s.Description == description)
                        ?? _saved.FirstOrDefault(s => s.Index == index);
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

    /// <summary>Opens every physical monitor in turn, and always closes them again.</summary>
    private static void ForEachMonitor(Action<IntPtr, int, string> body)
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

        var index = 0;

        foreach (var screen in screens)
        {
            uint count = 0;
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(screen, ref count) || count == 0) continue;

            var monitors = new PhysicalMonitor[count];
            if (!GetPhysicalMonitorsFromHMONITOR(screen, count, monitors)) continue;

            try
            {
                foreach (var monitor in monitors)
                {
                    // An internal laptop panel typically answers nothing here; that is fine,
                    // it is also the panel that does not need this.
                    try
                    {
                        body(monitor.Handle, index, monitor.Description);
                    }
                    catch (Exception ex)
                    {
                        Diagnostics.Log($"monitor {index} command failed: {ex.Message}");
                    }

                    index++;
                }
            }
            finally
            {
                DestroyPhysicalMonitors(count, monitors);
            }
        }
    }
}
