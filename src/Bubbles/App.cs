using System.Windows;

using Bubbles.Displays;
using Bubbles.Interop;
using Bubbles.Overlay;
using Bubbles.Session;
using Bubbles.Zone;

namespace Bubbles;

public sealed class App : Application
{
    /// <summary>Set by --emission-demo: run one Emission straight away and quit, ignoring the
    /// idle timer entirely. Waiting for a real idle period makes the Emission almost impossible
    /// to test, since any stray mouse movement cancels it.</summary>
    public static bool EmissionDemo { get; set; }

    /// <summary>Walk through every weather state, with the cross-fades between them.</summary>
    public static bool WeatherDemo { get; set; }

    private Settings _settings = new();
    private OverlayWindow? _overlay;
    private IdleController? _idle;
    private TrayIcon? _tray;
    private Updater? _updater;
    private DisplayBlackout? _displays;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Nothing here is a "main window" -- the app lives in the tray until told to quit.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = Settings.Load();

        // Subscribe before anything can ask, so a lock is never missed.
        SessionState.Watch();

        _updater = new Updater(_settings);

        // A download staged since last time is swapped in now, before anything is on screen,
        // and the process relaunches into it.
        if (_settings.AutoUpdate)
        {
            _updater.Start();
            if (_updater.Staged is not null && _updater.SwapIn())
            {
                Shutdown();
                return;
            }
        }

        // Anything a previous run left dimmed or with HDR off goes back first.
        _displays = new DisplayBlackout(_settings);
        _displays.RecoverFromCrash();

        _overlay = new OverlayWindow(_settings);
        // Whether the screen genuinely arrived at black, as opposed to an Emission that was
        // interrupted on the way there. LeftDark fires for both; the lock must only follow the
        // first, or walking in mid-animation would lock you out of your own machine.
        var reachedBlack = false;

        _overlay.WentDark += () =>
        {
            reachedBlack = true;
            _displays.Enter();
        };

        _overlay.LeftDark += () =>
        {
            // Displays first, always. Locking before the backlight is back would leave the
            // sign-in screen too dark to read on a monitor that had been dimmed over DDC/CI --
            // and the lock screen is the one thing this app cannot draw over to explain itself.
            _displays.Leave();

            // Not if it is already locked: this same path runs when a lock arriving by any
            // other route stands the blackout down, and asking again would be noise.
            if (reachedBlack && _settings.LockAfterBlackout && !SessionState.Locked)
                SessionLock.Request();
            reachedBlack = false;
        };

        _overlay.Show();                       // creates the HWND so the Win32 setup can run
        _overlay.HideBubbles(immediate: true); // ...then gets out of the way until you go idle

        _idle = new IdleController(_settings, _overlay);
        _tray = new TrayIcon(_settings, _overlay, _idle, _updater, Shutdown);

        if (EmissionDemo)
        {
            RunEmissionDemo();
            return;
        }

        if (WeatherDemo)
        {
            RunWeatherDemo();
            return;
        }

        _idle.Start();
    }

    /// <summary>Shows the artifacts, runs one Emission, then quits. The idle controller is
    /// never started, so nothing the user does interrupts it.</summary>
    private void RunEmissionDemo()
    {
        if (_overlay is null) return;

        _overlay.ShowBubbles();

        var toEmission = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5),
        };
        toEmission.Tick += (_, _) =>
        {
            toEmission.Stop();
            _overlay.SetBlackout(true);
        };
        toEmission.Start();

        var toExit = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(24),
        };
        toExit.Tick += (_, _) =>
        {
            toExit.Stop();
            Shutdown();
        };
        toExit.Start();
    }

    /// <summary>Shows the artifacts and walks through the four weather states, cross-fading
    /// between them, then quits.
    ///
    /// Weather changes about once a minute in normal use, which is right for a screensaver and
    /// useless for looking at it: four states and the fades between them would take five minutes
    /// of sitting still. This holds each for a few seconds instead. The fades are the real ones,
    /// run at the real length -- they are half of what there is to judge.</summary>
    private void RunWeatherDemo()
    {
        if (_overlay is null) return;

        _overlay.ShowBubbles();

        var order = new[] { Weather.Clear, Weather.Fog, Weather.Rain, Weather.Storm, Weather.Clear };

        // A family per state, so the demo shows the tints changing as well as the weather. In
        // use a tint holds for twenty-five seconds and only moves when the field swings by three
        // artifacts, which is the same reason the states are pinned here rather than waited for.
        var tints = new[]
        {
            Anomaly.Chemical, Anomaly.Electrical, Anomaly.Thermic, Anomaly.Gravitational, Anomaly.Chemical,
        };

        var tinted = -1;

        // Long enough that the storm shows lightning. Ambient strikes are seconds apart, so a
        // five-second hold could pass without one and the state looked like plain rain.
        const double hold = 9.0;
        var fade = WeatherCycle.CrossFade;
        var step = hold + fade;

        var clock = System.Diagnostics.Stopwatch.StartNew();

        var tick = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50),
        };

        tick.Tick += (_, _) =>
        {
            var at = clock.Elapsed.TotalSeconds;
            var index = (int)(at / step);

            if (index >= order.Length - 1)
            {
                tick.Stop();
                Shutdown();
                return;
            }

            var into = at - index * step;

            // Held, then faded into the next. Progress is of the incoming state, matching what
            // the cycle reports, so the overlay is driven exactly as it is in normal use.
            var progress = into <= hold ? 1 : Math.Clamp((into - hold) / fade, 0, 1);

            var to = progress >= 1 ? order[index] : order[index + 1];
            Weather? from = progress >= 1 ? null : order[index];

            _overlay.PinWeather(to, from, progress);

            // Handed over at the same moment the state is, so the two cross-fades run together
            // -- which is the case the two-live-sheets limit exists for.
            if (index != tinted)
            {
                tinted = index;
                _overlay.PinFamily(tints[index]);
            }
        };

        tick.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Never leave somebody with a dark monitor, or HDR off, because this app went away.
        _displays?.Leave();

        _idle?.Dispose();
        _updater?.Dispose();
        _tray?.Dispose();
        _overlay?.Close();
        _settings.Save();
        base.OnExit(e);
    }
}
