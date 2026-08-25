using System.Windows.Forms;

using Bubbles.Overlay;
using Bubbles.Zone;

namespace Bubbles.Session;

/// <summary>The only thing you can actually click, since the overlay itself is transparent to the
/// mouse.
///
/// Commands only. Every setting lives in the settings window, because a menu cannot show a value
/// -- only a tick -- and it has no room, which is how the theme submenu came to hold the pointer,
/// the backlight and HDR, and how two entries came to be built and never added at all.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly OverlayWindow _overlay;
    private readonly IdleController _idle;
    private readonly Updater _updater;
    private readonly SettingsHost _host;
    private readonly Action _exit;
    private readonly ToolStripMenuItem _update;
    private readonly ToolStripMenuItem _blackoutNow;
    private readonly ToolStripMenuItem _pause;
    private SettingsWindow? _settingsWindow;

    public TrayIcon(SettingsHost host, OverlayWindow overlay, IdleController idle, Updater updater,
                    Action exit)
    {
        _host = host;
        _overlay = overlay;
        _idle = idle;
        _updater = updater;
        _exit = exit;

        _update = new ToolStripMenuItem("Check for updates", null, async (_, _) => await CheckForUpdates());
        _pause = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause()) { CheckOnClick = true };

        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(new ToolStripMenuItem($"Bubbles v{Updater.Current.ToString(3)}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Start now", () => _idle.StartNow()));
        _blackoutNow = Item("Black screen now", () => _idle.BlackoutNow());
        menu.Items.Add(_blackoutNow);
        menu.Items.Add(_pause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Settings…", ShowSettings));
        menu.Items.Add(_update);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Exit", () => _exit()));
        menu.Opening += (_, _) => Refresh();
        _updater.StateChanged += RefreshUpdateItem;

        _icon = new NotifyIcon
        {
            Icon = BubbleArt.CreateTrayIcon(),
            Text = "Bubbles",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => _idle.StartNow();

        _idle.StageChanged += stage => _icon.Text = stage switch
        {
            IdleController.Stage.Bubbles => "Bubbles — running",
            IdleController.Stage.Blackout => "Bubbles — black screen",
            _ => "Bubbles",
        };
    }

    /// <summary>Opens the settings window, or brings back the one already open.
    ///
    /// Single-instance because two windows editing one settings object would each be showing
    /// values the other was changing.</summary>
    public void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_host, _idle);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    private void Refresh()
    {
        RefreshUpdateItem();

        // The command describes what it is about to do. Under the Zone with Emissions on, that
        // is twelve seconds of storm before the screen goes black, which "Black screen now"
        // would rather understate.
        var settings = _host.Current;
        _blackoutNow.Text = settings.Theme == OverlayTheme.Zone && settings.Emission
            ? "Emission now"
            : "Black screen now";
    }

    private void RefreshUpdateItem()
    {
        _update.Text = _updater.Staged is { } staged
            ? $"Install v{staged.ToString(3)} and restart"
            : "Check for updates";
    }

    private async Task CheckForUpdates()
    {
        // A staged update means the click is an instruction to take it now.
        if (_updater.Staged is not null)
        {
            if (_updater.SwapIn()) _exit();
            else Notify("Update could not be applied", "Bubbles cannot write to its own folder.");
            return;
        }

        _update.Text = "Checking…";
        var outcome = await _updater.CheckAsync(manual: true);
        RefreshUpdateItem();

        if (outcome is not null) Notify("Bubbles", outcome);
    }

    private void Notify(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(4000);
    }

    private static ToolStripMenuItem Item(string text, Action action) =>
        new(text, null, (_, _) => action());

    private void TogglePause()
    {
        _overlay.Paused = _pause.Checked;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
