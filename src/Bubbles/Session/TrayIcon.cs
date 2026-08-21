using System.Diagnostics;
using System.Windows.Forms;

using Bubbles.Overlay;
using Bubbles.Zone;

namespace Bubbles.Session;

/// <summary>The only thing you can actually click, since the overlay itself is transparent to the mouse.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly OverlayWindow _overlay;
    private readonly IdleController _idle;
    private readonly Updater _updater;
    private readonly Action _exit;
    private readonly ToolStripMenuItem _update;
    private readonly ToolStripMenuItem _blackoutNow;
    private readonly List<ToolStripMenuItem> _zoneOnly = new();
    private readonly ToolStripMenuItem _pause;
    private readonly ToolStripMenuItem _alwaysOn;
    private readonly ToolStripMenuItem _startup;
    private readonly ToolStripMenuItem _pin;
    private readonly List<(ToolStripMenuItem Item, Func<Settings, bool> IsCurrent)> _checks = new();
    private Settings _settings;

    public TrayIcon(Settings settings, OverlayWindow overlay, IdleController idle, Updater updater, Action exit)
    {
        _settings = settings;
        _overlay = overlay;
        _idle = idle;
        _updater = updater;
        _exit = exit;

        _update = new ToolStripMenuItem("Check for updates", null, async (_, _) => await CheckForUpdates())
        {
            Visible = true,
        };

        _pause = new ToolStripMenuItem("Pause", null, (_, _) => TogglePause()) { CheckOnClick = true };
        _alwaysOn = new ToolStripMenuItem("Always on (ignore idle timer)", null,
            (_, _) => Tweak(s => s.AlwaysOn = !s.AlwaysOn));
        _startup = new ToolStripMenuItem("Start with Windows", null,
            (_, _) => Startup.Set(!Startup.IsEnabled));

        var menu = new ContextMenuStrip { ShowImageMargin = false };
        menu.Items.Add(new ToolStripMenuItem($"Bubbles v{Updater.Current.ToString(3)}") { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Start bubbles now", () => _idle.StartNow()));
        _blackoutNow = Item("Black screen now", () => _idle.BlackoutNow());
        menu.Items.Add(_blackoutNow);
        menu.Items.Add(_pause);
        menu.Items.Add(_alwaysOn);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Submenu("Start after",
            Choice("30 seconds", s => s.IdleSeconds = 30, s => s.IdleSeconds == 30),
            Choice("1 minute",   s => s.IdleSeconds = 60, s => s.IdleSeconds == 60),
            Choice("2 minutes",  s => s.IdleSeconds = 120, s => s.IdleSeconds == 120),
            Choice("5 minutes",  s => s.IdleSeconds = 300, s => s.IdleSeconds == 300),
            Choice("10 minutes", s => s.IdleSeconds = 600, s => s.IdleSeconds == 600)));
        menu.Items.Add(Submenu("Black screen after",
            Choice("Never",      s => s.BlackoutSeconds = 0, s => s.BlackoutSeconds == 0),
            Choice("2 minutes",  s => s.BlackoutSeconds = 120, s => s.BlackoutSeconds == 120),
            Choice("5 minutes",  s => s.BlackoutSeconds = 300, s => s.BlackoutSeconds == 300),
            Choice("10 minutes", s => s.BlackoutSeconds = 600, s => s.BlackoutSeconds == 600),
            Choice("30 minutes", s => s.BlackoutSeconds = 1800, s => s.BlackoutSeconds == 1800)));
        menu.Items.Add(Submenu("Dim the desktop",
            Choice("Not at all",  s => s.Dim = 0.00, s => Near(s.Dim, 0.00)),
            Choice("A little",    s => s.Dim = 0.30, s => Near(s.Dim, 0.30)),
            Choice("Half",        s => s.Dim = 0.55, s => Near(s.Dim, 0.55)),
            Choice("A lot",       s => s.Dim = 0.80, s => Near(s.Dim, 0.80)),
            Choice("Almost black", s => s.Dim = 0.95, s => Near(s.Dim, 0.95))));
        // Two explicit choices rather than a checkbox. A lone checkable entry reads as a
        // command -- "ask for a PIN" sounds like something you are about to do -- and an
        // absent tick looks exactly like a tick you failed to notice. With a pair, one is
        // always ticked, and the label says which without opening anything.
        _pin = Submenu("Ask for a PIN",
            Choice("Never", s => s.LockAfterBlackout = false, s => !s.LockAfterBlackout),
            Choice("After the black screen", s => s.LockAfterBlackout = true, s => s.LockAfterBlackout));
        menu.Items.Add(_pin);
        menu.Items.Add(Submenu("Hold off while",
            Toggle("The microphone is in use", s => s.PauseWhileMicrophoneInUse, (s, v) => s.PauseWhileMicrophoneInUse = v),
            Toggle("The camera is in use", s => s.PauseWhileCameraInUse, (s, v) => s.PauseWhileCameraInUse = v),
            Toggle("A full-screen app is running", s => s.PauseInFullScreen, (s, v) => s.PauseInFullScreen = v)));
        menu.Items.Add(Submenu("Theme",
            Choice("The Zone — S.T.A.L.K.E.R. artifacts", s => s.Theme = OverlayTheme.Zone,
                   s => s.Theme == OverlayTheme.Zone),
            Choice("Soap bubbles — the original", s => s.Theme = OverlayTheme.Soap,
                   s => s.Theme == OverlayTheme.Soap),
            new ToolStripSeparator(),
            ZoneOnly(Toggle("Veles artifact detector", s => s.ShowDetector, (s, v) => s.ShowDetector = v)),
            ZoneOnly(Toggle("Animate artifacts (costs CPU)", s => s.Animated, (s, v) => s.Animated = v)),
            ZoneOnly(Toggle("Emission blackout", s => s.Emission, (s, v) => s.Emission = v)),
            ZoneOnly(Toggle("Lightning during an Emission", s => s.Lightning, (s, v) => s.Lightning = v)),
            Toggle("Hide pointer when idle", s => s.HideCursor, (s, v) => s.HideCursor = v),
            Toggle("Dim monitor backlights when dark", s => s.DimMonitorBacklight, (s, v) => s.DimMonitorBacklight = v),
            Toggle("Switch HDR off when dark", s => s.DisableHdrDuringBlackout, (s, v) => s.DisableHdrDuringBlackout = v)));
        menu.Items.Add(Submenu("Look",
            Item("More bubbles",  () => Tweak(s => s.BubbleCount += 4)),
            Item("Fewer bubbles", () => Tweak(s => s.BubbleCount -= 4)),
            Item("Bigger",        () => Tweak(s => { s.MinRadius *= 1.2; s.MaxRadius *= 1.2; })),
            Item("Smaller",       () => Tweak(s => { s.MinRadius /= 1.2; s.MaxRadius /= 1.2; })),
            Item("Faster",        () => Tweak(s => s.Speed *= 1.4)),
            Item("Slower",        () => Tweak(s => s.Speed /= 1.4)),
            Item("Brighter",      () => Tweak(s => s.Opacity += 0.1)),
            Item("Dimmer",        () => Tweak(s => s.Opacity -= 0.1)),
            Item("Float upward / bounce", () => Tweak(s => s.Buoyancy = s.Buoyancy > 0 ? 0 : 22))));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Edit settings\u2026", EditSettings));
        menu.Items.Add(Item("Reload settings", ReloadSettings));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("Exit", () => _exit()));
        menu.Opening += (_, _) => RefreshChecks();
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
            IdleController.Stage.Bubbles => "Bubbles \u2014 running",
            IdleController.Stage.Blackout => "Bubbles \u2014 black screen",
            _ => "Bubbles",
        };
    }

    /// <summary>Marks a menu entry as meaningless outside the Zone theme.</summary>
    private ToolStripMenuItem ZoneOnly(ToolStripMenuItem item)
    {
        _zoneOnly.Add(item);
        return item;
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

        _update.Text = "Checking\u2026";
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

    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.02;

    private static ToolStripMenuItem Item(string text, Action action) =>
        new(text, null, (_, _) => action());

    private static ToolStripMenuItem Submenu(string text, params ToolStripItem[] children)
    {
        var item = new ToolStripMenuItem(text);
        item.DropDownItems.AddRange(children);
        return item;
    }

    /// <summary>A checkbox entry bound straight to a bool setting.</summary>
    private ToolStripMenuItem Toggle(string text, Func<Settings, bool> get, Action<Settings, bool> set)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => Tweak(s => set(s, !get(s))));
        _checks.Add((item, get));
        return item;
    }

    /// <summary>A menu entry that sets a value and shows a tick when that value is active.</summary>
    private ToolStripMenuItem Choice(string text, Action<Settings> set, Func<Settings, bool> isCurrent)
    {
        var item = new ToolStripMenuItem(text, null, (_, _) => Tweak(set));
        _checks.Add((item, isCurrent));
        return item;
    }

    private void RefreshChecks()
    {
        foreach (var (item, isCurrent) in _checks)
            item.Checked = isCurrent(_settings);
        _alwaysOn.Checked = _settings.AlwaysOn;
        _startup.Checked = Startup.IsEnabled;

        // Say it on the face of the menu, not only on the tick inside.
        _pin.Text = _settings.LockAfterBlackout
            ? "Ask for a PIN:  after the black screen"
            : "Ask for a PIN:  never";
        RefreshUpdateItem();

        // Artifacts, the detector and Emissions are all Zone furniture; greying them out is
        // clearer than leaving settings on offer that the current theme ignores.
        var zone = _settings.Theme == OverlayTheme.Zone;
        foreach (var item in _zoneOnly) item.Enabled = zone;

        _blackoutNow.Text = zone && _settings.Emission ? "Emission now" : "Black screen now";
    }

    private void TogglePause()
    {
        _overlay.Paused = _pause.Checked;
    }

    private void Tweak(Action<Settings> change)
    {
        change(_settings);
        _settings.Clamped();
        _overlay.Apply(_settings);
        _idle.Apply(_settings);
        _updater.Apply(_settings);
    }

    private void EditSettings()
    {
        _settings.Save();
        Process.Start(new ProcessStartInfo(Settings.FilePath) { UseShellExecute = true });
    }

    private void ReloadSettings()
    {
        _settings = Settings.Load();
        _overlay.Apply(_settings);
        _idle.Apply(_settings);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
