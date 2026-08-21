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

            if (reachedBlack && _settings.LockAfterBlackout) SessionLock.Request();
            reachedBlack = false;
        };

        // If the session locks by any other route -- Win+L, a policy, the lid -- the blackout
        // is over whether this app likes it or not: the lock screen is what is on display, and
        // input to it happens on the secure desktop where the idle timer cannot see it. Left
        // alone, a monitor dimmed over DDC/CI would stay dimmed with a sign-in prompt on it
        // that nobody can read. So stand down and put the displays back.
        Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
        {
            if (e.Reason != Microsoft.Win32.SessionSwitchReason.SessionLock) return;

            _displays.Leave();
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
