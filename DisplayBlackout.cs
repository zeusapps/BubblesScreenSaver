using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace Bubbles;

/// <summary>Takes the displays as dark as they will go once the screen has reached black, and
/// puts everything back exactly as it was.
///
/// Two levers, in a strict order that matters:
///
/// HDR first. While it is on, Windows owns the luminance pipeline and the monitor's DDC/CI
/// channel is dead -- brightness and power-mode writes are accepted and discarded. Turning it
/// off is a display mode change, which is why it is a setting rather than something this does
/// quietly; it takes a second or two to re-sync, so the backlight is only touched afterwards.
///
/// Backlight second, and on the way back it is restored *first*. Re-enabling HDR kills DDC
/// again, so restoring in the other order would leave a monitor at zero brightness with no
/// remaining way to reach it.
///
/// Both steps are written to disk before they happen, so a session that ends badly is undone
/// on the next run rather than leaving somebody with a dark or washed-out screen.</summary>
public sealed class DisplayBlackout
{
    private sealed record SavedHdr(uint AdapterLow, int AdapterHigh, uint Id, string Name);

    private static string HdrStateFile => Path.Combine(Settings.Directory, "hdr-state.json");

    private readonly MonitorBacklight _backlight = new();
    private readonly DispatcherTimer _afterModeChange;

    private List<DisplayInfo.Target> _hdrTurnedOff = new();
    private Settings _settings;
    private bool _dark;

    public DisplayBlackout(Settings settings)
    {
        _settings = settings;

        // Long enough for the displays to re-sync after HDR is switched off, so DDC/CI is
        // answering by the time the backlight is asked to move.
        _afterModeChange = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _afterModeChange.Tick += (_, _) =>
        {
            _afterModeChange.Stop();
            if (_dark) DimBacklights();
        };
    }

    public void Apply(Settings settings) => _settings = settings;

    /// <summary>Undoes anything a previous run left behind. Order matters here too: brightness
    /// while DDC is still reachable, then HDR.</summary>
    public void RecoverFromCrash()
    {
        _backlight.RecoverFromCrash();
        RestoreHdrFromDisk();
    }

    /// <summary>The screen has reached black.</summary>
    public void Enter()
    {
        if (_dark) return;
        _dark = true;

        var disabled = _settings.DisableHdrDuringBlackout && TurnHdrOff();

        if (disabled) _afterModeChange.Start();
        else DimBacklights();
    }

    /// <summary>Something happened; put it all back.</summary>
    public void Leave()
    {
        _afterModeChange.Stop();
        if (!_dark) return;
        _dark = false;

        // Brightness before HDR, always.
        _backlight.Restore();
        RestoreHdr();
    }

    private void DimBacklights()
    {
        if (_settings.DimMonitorBacklight) _backlight.Dim(_settings.MonitorStandby);
    }

    private bool TurnHdrOff()
    {
        var enabled = DisplayInfo.WithHdrEnabled();
        if (enabled.Count == 0) return false;

        // Recorded before the change, so a crash between here and the restore is recoverable.
        Persist(enabled);

        var turnedOff = new List<DisplayInfo.Target>();
        foreach (var target in enabled)
        {
            if (DisplayInfo.SetHdr(target, false)) turnedOff.Add(target);
        }

        _hdrTurnedOff = turnedOff;

        if (turnedOff.Count == 0)
        {
            Persist(new List<DisplayInfo.Target>());
            return false;
        }

        Diagnostics.Log($"HDR switched off on {turnedOff.Count} display(s): " +
                        string.Join(", ", turnedOff.Select(t => t.Name)));
        return true;
    }

    private void RestoreHdr()
    {
        if (_hdrTurnedOff.Count == 0) return;

        foreach (var target in _hdrTurnedOff) DisplayInfo.SetHdr(target, true);

        Diagnostics.Log($"HDR restored on {_hdrTurnedOff.Count} display(s)");
        _hdrTurnedOff = new List<DisplayInfo.Target>();
        Persist(new List<DisplayInfo.Target>());
    }

    private void RestoreHdrFromDisk()
    {
        try
        {
            if (!File.Exists(HdrStateFile)) return;

            var saved = JsonSerializer.Deserialize<List<SavedHdr>>(File.ReadAllText(HdrStateFile));
            if (saved is { Count: > 0 })
            {
                Diagnostics.Log($"restoring HDR on {saved.Count} display(s) left off by a previous run");

                foreach (var entry in saved)
                {
                    DisplayInfo.SetHdr(
                        new DisplayInfo.Target(entry.AdapterLow, entry.AdapterHigh, entry.Id, entry.Name), true);
                }
            }

            File.Delete(HdrStateFile);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"HDR recovery failed: {ex.Message}");
        }
    }

    private static void Persist(List<DisplayInfo.Target> targets)
    {
        try
        {
            if (targets.Count == 0)
            {
                if (File.Exists(HdrStateFile)) File.Delete(HdrStateFile);
                return;
            }

            Directory.CreateDirectory(Settings.Directory);
            File.WriteAllText(HdrStateFile, JsonSerializer.Serialize(
                targets.Select(t => new SavedHdr(t.AdapterLow, t.AdapterHigh, t.Id, t.Name))));
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"could not record HDR state: {ex.Message}");
        }
    }
}
