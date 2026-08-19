using System.Windows;

namespace Bubbles;

public sealed class App : Application
{
    private Settings _settings = new();
    private OverlayWindow? _overlay;
    private IdleController? _idle;
    private TrayIcon? _tray;
    private Updater? _updater;
    private ExternalDisplays? _displays;

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

        // Anything a previous run left dimmed goes back first.
        _displays = new ExternalDisplays();
        _displays.RecoverFromCrash();

        _overlay = new OverlayWindow(_settings);
        _overlay.WentDark += () =>
        {
            if (_settings.DimExternalMonitors)
                _displays.Dim(_settings.ExternalMonitorStandby);
        };
        _overlay.LeftDark += () => _displays.Restore();
        _overlay.Show();                       // creates the HWND so the Win32 setup can run
        _overlay.HideBubbles(immediate: true); // ...then gets out of the way until you go idle

        _idle = new IdleController(_settings, _overlay);
        _tray = new TrayIcon(_settings, _overlay, _idle, _updater, Shutdown);
        _idle.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Never leave somebody with a dark monitor because this app went away.
        _displays?.Restore();

        _idle?.Dispose();
        _updater?.Dispose();
        _tray?.Dispose();
        _overlay?.Close();
        _settings.Save();
        base.OnExit(e);
    }
}
