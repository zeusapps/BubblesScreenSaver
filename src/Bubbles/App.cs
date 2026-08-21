using System.Windows;

using Bubbles.Displays;
using Bubbles.Interop;
using Bubbles.Overlay;
using Bubbles.Session;

namespace Bubbles;

public sealed class App : Application
{
    /// <summary>Set by --emission-demo: run one Emission straight away and quit, ignoring the
    /// idle timer entirely. Waiting for a real idle period makes the Emission almost impossible
    /// to test, since any stray mouse movement cancels it.</summary>
    public static bool EmissionDemo { get; set; }

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
