using System.IO;
using System.Windows.Threading;

namespace Bubbles.Displays;

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
    private static string HdrStateFile => Path.Combine(Settings.Directory, "hdr-state.json");

    private readonly MonitorBacklight _backlight = new();
    private readonly DispatcherTimer _afterModeChange;
    private readonly DispatcherTimer _afterReconnect;

    // Displays owed their HDR back. A display unplugged while its HDR is off keeps the setting
    // when it returns -- Windows persists that per display -- so the record has to outlive the
    // disconnection, and the process.
    private readonly PendingRestore<DisplayInfo.Target> _hdrTurnedOff = new(
        HdrStateFile,
        HdrKey,
        "HDR",
        target => target.Name);

    private Settings _settings;
    private bool _dark;

    public DisplayBlackout(Settings settings)
    {
        _settings = settings;

        // A monitor unplugged mid-blackout comes back through this event, and it is owed its
        // brightness. The delay lets the link settle before DDC/CI is asked anything.
        _afterReconnect = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _afterReconnect.Tick += (_, _) =>
        {
            _afterReconnect.Stop();

            // Not while the screen is meant to be dark. Switching HDR off is itself a display
            // change, so without this guard the retry fired moments later and undid the very
            // blackout that had just been set up.
            if (_dark) return;

            _backlight.RestorePending();
            RestoreHdrPending();
        };

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) =>
        {
            _afterReconnect.Stop();
            _afterReconnect.Start();
        };

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
        // Only displays that stand to gain. Switching HDR off is worth a mode change solely
        // because it revives DDC/CI so a backlight can be dimmed -- and the built-in panel has
        // no DDC/CI backlight to reach, so toggling it costs a re-sync and buys nothing.
        var enabled = DisplayInfo.WithHdrEnabled().Where(t => !t.Internal).ToList();

        if (enabled.Count == 0)
        {
            Diagnostics.Log("no external display has HDR on; leaving HDR alone");
            return false;
        }

        // Recorded before the change, so a crash between here and the restore is recoverable.
        _hdrTurnedOff.Remember(enabled);

        var turnedOff = enabled.Where(target => DisplayInfo.SetHdr(target, false)).ToList();

        // Anything that refused the change is not owed anything back.
        _hdrTurnedOff.Forget(enabled.Except(turnedOff));

        if (turnedOff.Count == 0) return false;

        Diagnostics.Log($"HDR switched off on {turnedOff.Count} display(s): " +
                        string.Join(", ", turnedOff.Select(t => t.Name)));
        return true;
    }

    private void RestoreHdr() => RestoreHdrWhereAttached("restore");

    /// <summary>Retries any display still owed its HDR, which is what a reconnection is for.</summary>
    public void RestoreHdrPending() => RestoreHdrWhereAttached("reconnect");

    private void RestoreHdrWhereAttached(string why) => _hdrTurnedOff.Settle(owed => owed
        .Where(target =>
        {
            DisplayInfo.SetHdr(target, true);

            // Believe it only once the display says so. A disconnected one cannot be asked,
            // which is exactly when the record must be kept.
            return DisplayInfo.HdrEnabled(target) == true;
        })
        .Select(HdrKey)
        .ToList(), why);

    /// <summary>Identity for the owed-HDR record. Adapter and target id rather than the name,
    /// because two identical monitors report the same name.</summary>
    private static string HdrKey(DisplayInfo.Target target) =>
        $"{target.AdapterHigh}:{target.AdapterLow}:{target.Id}";

    private void RestoreHdrFromDisk()
    {
        _hdrTurnedOff.Load();
        RestoreHdrWhereAttached("previous run");
    }
}
