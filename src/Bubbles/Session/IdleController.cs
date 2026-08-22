using System.Windows.Threading;

using Bubbles.Interop;
using Bubbles.Overlay;

namespace Bubbles.Session;

/// <summary>Drives the whole thing off the system idle timer:
/// working -> bubbles -> black screen, and straight back to working on the first keypress.
///
/// Nothing here changes a power state. An earlier version turned the monitor off with
/// SC_MONITORPOWER; on a Modern Standby laptop that suspends the whole machine, and with a
/// retry timer it became a wake/sleep loop. Blackout is now purely something we draw.</summary>
public sealed class IdleController : IDisposable
{
    public enum Stage
    {
        Active,    // you're using the machine; nothing is drawn
        Bubbles,   // idle: bubbles drifting over the live desktop
        Blackout,  // idle longer: a plain black screen, unlit on OLED
    }

    private readonly DispatcherTimer _timer;
    private readonly OverlayWindow _overlay;
    private readonly IdleClock _clock = new();
    private Settings _settings;

    // A forced start has to survive the very click that requested it, so it's cancelled by the
    // *next* input rather than by "input happened recently" -- which is always true at that moment.
    private bool _forceBubbles;
    private bool _forceBlackout;
    private uint? _forceBaseline;
    private int _ticks;
    private string? _heldOffBy;

    public Stage Current { get; private set; } = Stage.Active;

    public event Action<Stage>? StageChanged;

    public IdleController(Settings settings, OverlayWindow overlay)
    {
        _settings = settings;
        _overlay = overlay;

        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(400),
        };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start() => _timer.Start();

    public void Apply(Settings settings)
    {
        _settings = settings;
        Tick();
    }

    /// <summary>Shows the bubbles right now, without waiting out the idle timer.</summary>
    public void StartNow() => Force(bubbles: true, blackout: false);

    /// <summary>Goes to a black screen right now. Any input brings the desktop straight back.</summary>
    public void BlackoutNow() => Force(bubbles: true, blackout: true);

    private void Force(bool bubbles, bool blackout)
    {
        _forceBubbles = bubbles;
        _forceBlackout = blackout;
        _forceBaseline = NativeInput.LastInputTick();
        Tick();
    }

    private void Tick()
    {
        // The menu click that armed a force is the baseline; anything after it cancels.
        if (_forceBaseline is { } baseline && NativeInput.LastInputTick() != baseline)
        {
            _forceBaseline = null;
            _forceBubbles = false;
            _forceBlackout = false;
        }

        // Somebody on a call is not idle, whatever the input timer says. A deliberate request
        // from the tray still wins -- if you ask for it, you get it.
        var held = _forceBubbles || _forceBlackout ? HoldOff.None : UserBusy.Held(_settings);

        if (held.Reason != _heldOffBy)
        {
            _heldOffBy = held.Reason;
            Diagnostics.Log(held.Reason is null
                ? "no longer held off"
                : $"holding off: {held.Reason} (artifacts={held.Artifacts} blackout={held.Blackout})");
        }

        // Measured through the clock, so that hanging up starts the countdown again instead of
        // arriving with every threshold already passed.
        //
        // Only a *total* hold-off stops the countdown. Discounting time while a reason still
        // permits blackout would freeze the clock short of BlackoutSeconds, and the blackout
        // that reason deliberately allowed would never arrive -- an album would hold the screen
        // lit for ever, which is the failure this is meant to fix.
        var idle = _clock.Elapsed(NativeInput.IdleSeconds(), held.Total, Environment.TickCount64);

        var wantsBlackout = _forceBlackout
                            || (_settings.BlackoutSeconds > 0 && idle >= _settings.BlackoutSeconds);
        var wantsBubbles = _forceBubbles || _settings.AlwaysOn || idle >= _settings.IdleSeconds;

        var next = Resolve(wantsBubbles, wantsBlackout, held);

        if (++_ticks % 25 == 0 || next != Current)
        {
            Diagnostics.Log($"tick idle={idle:N1}s cur={Current} next={next} " +
                            $"idleCfg={_settings.IdleSeconds} blackCfg={_settings.BlackoutSeconds} " +
                            $"fB={_forceBubbles} fK={_forceBlackout} " +
                            $"holdA={held.Artifacts} holdB={held.Blackout}");
        }

        // A blackout arrived at while the artifacts are unwelcome must not be an Emission:
        // that is twelve seconds of the very artifacts the reason withheld.
        _overlay.ArtifactsWelcome = !held.Artifacts;

        if (next != Current) Enter(next);
    }

    /// <summary>The furthest stage asked for that is not suppressed.
    ///
    /// Deliberately not a ceiling. A ceiling on the ordered chain Active &lt; Bubbles &lt; Blackout
    /// cannot express the case that matters -- music suppressing the artifacts while permitting
    /// the blackout -- because Blackout is the *further* stage. So with music playing the
    /// overlay sits at Active right past IdleSeconds and then goes straight to black, never
    /// having shown an artifact.</summary>
    internal static Stage Resolve(bool wantsBubbles, bool wantsBlackout, HoldOff held) =>
        wantsBlackout && !held.Blackout ? Stage.Blackout
        : wantsBubbles && !held.Artifacts ? Stage.Bubbles
        : Stage.Active;

    private void Enter(Stage stage)
    {
        Diagnostics.Log($"ENTER {Current} -> {stage}");
        Current = stage;

        switch (stage)
        {
            case Stage.Active:
                _overlay.SetBlackout(false);
                _overlay.HideBubbles();
                break;

            case Stage.Bubbles:
                _overlay.SetBlackout(false);
                _overlay.ShowBubbles();
                break;

            case Stage.Blackout:
                _overlay.ShowBubbles();
                _overlay.SetBlackout(true);
                break;
        }

        StageChanged?.Invoke(stage);
    }

    public void Dispose() => _timer.Stop();
}
