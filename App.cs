using System.Windows;

namespace Bubbles;

public sealed class App : Application
{
    private Settings _settings = new();
    private OverlayWindow? _overlay;
    private IdleController? _idle;
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Nothing here is a "main window" -- the app lives in the tray until told to quit.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settings = Settings.Load();

        _overlay = new OverlayWindow(_settings);
        _overlay.Show();                       // creates the HWND so the Win32 setup can run
        _overlay.HideBubbles(immediate: true); // ...then gets out of the way until you go idle

        _idle = new IdleController(_settings, _overlay);
        _tray = new TrayIcon(_settings, _overlay, _idle, Shutdown);
        _idle.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _idle?.Dispose();
        _tray?.Dispose();
        _overlay?.Close();
        _settings.Save();
        base.OnExit(e);
    }
}
