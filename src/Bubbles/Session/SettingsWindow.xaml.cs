using System.Globalization;
using System.Windows;
using System.Windows.Controls;

using Bubbles.Overlay;
using Bubbles.Zone;

namespace Bubbles.Session;

/// <summary>Every setting the app persists, in one window.
///
/// Rows are built from a table rather than written out in XAML, so that a control's range comes
/// from <see cref="Settings.Range"/> -- the same constants <see cref="Settings.Clamped"/> enforces
/// -- instead of being restated by hand next to it. A slider whose maximum disagreed with the
/// clamp would look like it accepted a value and then moved it somewhere else.
///
/// Edits apply as they are made, because that is how the app already works: one settings object,
/// mutated in place, handed to whoever is listening. The cost is that there is no natural way to
/// back out, so the window keeps a snapshot of how things stood when it opened and Cancel puts it
/// back.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsHost _host;
    private readonly IdleController _idle;
    private readonly Settings _opened;
    private readonly List<Action> _refreshers = new();
    private readonly List<FrameworkElement> _zoneOnly = new();

    // Refreshing the controls after an edit writes to those controls, which would come straight
    // back as another edit. The refresh is not optional -- Clamped may have moved the value the
    // user just set -- so the write has to be ignored rather than avoided.
    private bool _refreshing;

    public SettingsWindow(SettingsHost host, IdleController idle)
    {
        _host = host;
        _idle = idle;
        _opened = host.Snapshot();

        InitializeComponent();

        // The same bubble the tray shows. Without it the window gets the generic placeholder in
        // its title bar and on the taskbar, which reads as somebody else's dialog.
        Icon = BubbleArt.CreateWindowIcon();

        BuildGroups();
        RefreshAll();

        // Reading this window without touching the keyboard is exactly what the idle timer
        // misreads as absence, and covering the window you are configuring the screensaver in
        // would be the most conspicuous possible way to get that wrong.
        _idle.AppHold = HoldOff.Everything("the settings window is open");
    }

    protected override void OnClosed(EventArgs e)
    {
        _idle.AppHold = HoldOff.None;

        // Once, here, rather than on every edit: dragging a slider would otherwise write the
        // file a few dozen times a second to no purpose.
        _host.Save();
        base.OnClosed(e);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _host.Restore(_opened);
        Close();
    }

    private void OnRestoreDefaults(object sender, RoutedEventArgs e)
    {
        // Everything except the file's version, which records how the values already on disk are
        // to be read. Resetting it would re-run the density migration against numbers that were
        // already written in the new meaning.
        var version = _host.Current.SettingsVersion;

        Edit(current =>
        {
            new Settings().CopyTo(current);
            current.SettingsVersion = version;
        });
    }

    /// <summary>Applies a change and then re-reads every control, because the clamp may have
    /// moved more than the one value that was touched.</summary>
    private void Edit(Action<Settings> change)
    {
        if (_refreshing) return;
        _host.Edit(change);
        RefreshAll();
    }

    private void RefreshAll()
    {
        _refreshing = true;

        try
        {
            foreach (var refresh in _refreshers) refresh();

            // Disabled rather than hidden. Offering a setting the current theme ignores invites
            // you to change it and conclude the app is broken; hiding it makes the window change
            // shape for reasons that are not on screen.
            var zone = _host.Current.Theme == OverlayTheme.Zone;
            foreach (var element in _zoneOnly) element.IsEnabled = zone;
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void BuildGroups()
    {
        Groups.Children.Add(Group("When it starts",
            Choice("Start the screensaver after", Durations.Idle,
                   s => s.IdleSeconds, MoveStartDelay),

            // Named as time after the screensaver starts because that is what the clamp
            // enforces: BlackoutSeconds is floored at IdleSeconds, so a delay set below it does
            // not mean what it says.
            Choice("Go to a black screen after a further", Durations.Blackout,
                   BlackoutGap, (s, v) => s.BlackoutSeconds = v < 0 ? 0 : s.IdleSeconds + v),
            Note("Measured from the moment the screensaver appears, not from your last keypress."),
            Check("Ask for a PIN after the black screen",
                  s => s.LockAfterBlackout, (s, v) => s.LockAfterBlackout = v)));

        Groups.Children.Add(Group("Hold off while",
            Check("The microphone is in use",
                  s => s.PauseWhileMicrophoneInUse, (s, v) => s.PauseWhileMicrophoneInUse = v),
            Check("The camera is in use",
                  s => s.PauseWhileCameraInUse, (s, v) => s.PauseWhileCameraInUse = v),
            Check("A full-screen app is running",
                  s => s.PauseInFullScreen, (s, v) => s.PauseInFullScreen = v),
            Check("Sound is playing",
                  s => s.PauseWhileAudioPlaying, (s, v) => s.PauseWhileAudioPlaying = v),
            Check("A video is playing (music still blacks out)",
                  s => s.PauseWhileMediaPlaying, (s, v) => s.PauseWhileMediaPlaying = v)));

        Groups.Children.Add(Group("Theme",
            Themes(),
            ZoneOnly(Check("Veles artifact detector", s => s.ShowDetector, (s, v) => s.ShowDetector = v)),
            ZoneOnly(Check("Animate artifacts (costs CPU)", s => s.Animated, (s, v) => s.Animated = v)),
            ZoneOnly(Check("Emission blackout", s => s.Emission, (s, v) => s.Emission = v)),
            ZoneOnly(Check("Lightning during an Emission", s => s.Lightning, (s, v) => s.Lightning = v)),
            ZoneOnly(Check("Weather", s => s.Weather, (s, v) => s.Weather = v)),
            ZoneOnly(Slide("Collect radius", Settings.Range.CollectRadiusMin,
                           Settings.Range.CollectRadiusMax, 1,
                           s => s.CollectRadius, (s, v) => s.CollectRadius = v, "0 DIP"))));

        Groups.Children.Add(Group("What it looks like",
            Slide("Dim the desktop", Settings.Range.DimMin, Settings.Range.DimMax, 0.01,
                  s => s.Dim, (s, v) => s.Dim = v, "P0"),
            Slide("Brightness", Settings.Range.OpacityMin, Settings.Range.OpacityMax, 0.01,
                  s => s.Opacity, (s, v) => s.Opacity = v, "P0"),
            Slide("How many shapes", Settings.Range.BubbleCountMin, Settings.Range.BubbleCountMax, 1,
                  s => s.BubbleCount, (s, v) => s.BubbleCount = (int)Math.Round(v), "0"),
            Note("A density, quoted against a 1920 by 1080 screen. A larger desktop carries proportionally more."),
            Slide("Smallest", Settings.Range.MinRadiusMin, Settings.Range.MinRadiusMax, 1,
                  s => s.MinRadius, (s, v) => s.MinRadius = v, "0 DIP"),
            Slide("Largest", Settings.Range.MinRadiusMin, Settings.Range.MaxRadiusMax, 1,
                  s => s.MaxRadius, (s, v) => s.MaxRadius = v, "0 DIP"),
            Slide("Speed", Settings.Range.SpeedMin, Settings.Range.SpeedMax, 1,
                  s => s.Speed, (s, v) => s.Speed = v, "0 DIP/s"),
            Slide("Speed variation", Settings.Range.SpeedVarianceMin, Settings.Range.SpeedVarianceMax, 0.01,
                  s => s.SpeedVariance, (s, v) => s.SpeedVariance = v, "P0"),
            Slide("Float upward", Settings.Range.BuoyancyMin, Settings.Range.BuoyancyMax, 1,
                  s => s.Buoyancy, (s, v) => s.Buoyancy = v, "0"),
            Note("Zero bounces off the edges; higher values drift up like real bubbles."),
            Slide("Wobble", Settings.Range.WobbleMin, Settings.Range.WobbleMax, 0.005,
                  s => s.Wobble, (s, v) => s.Wobble = v, "0.000"),
            Slide("Fade in over", Settings.Range.FadeInSecondsMin, Settings.Range.FadeInSecondsMax, 0.1,
                  s => s.FadeInSeconds, (s, v) => s.FadeInSeconds = v, "0.0 s"),
            Slide("Frame rate limit", Settings.Range.MaxFpsMin, Settings.Range.MaxFpsMax, 1,
                  s => s.MaxFps, (s, v) => s.MaxFps = (int)Math.Round(v), "0 fps"),
            Note("Zero leaves the frame rate unlimited.")));

        Groups.Children.Add(Group("The screen",
            Check("Hide the pointer when idle", s => s.HideCursor, (s, v) => s.HideCursor = v),
            Check("Let clicks through to what is underneath",
                  s => s.ClickThrough, (s, v) => s.ClickThrough = v),
            Check("Dim monitor backlights when dark",
                  s => s.DimMonitorBacklight, (s, v) => s.DimMonitorBacklight = v),
            Check("Switch HDR off when dark",
                  s => s.DisableHdrDuringBlackout, (s, v) => s.DisableHdrDuringBlackout = v)));

        Groups.Children.Add(Group("Updates",
            Check("Check for updates automatically", s => s.AutoUpdate, (s, v) => s.AutoUpdate = v),
            Slide("Check every", Settings.Range.UpdateCheckHoursMin, Settings.Range.UpdateCheckHoursMax, 1,
                  s => s.UpdateCheckHours, (s, v) => s.UpdateCheckHours = v, "0 h")));

        // Apart from the everyday controls, and labelled with what it actually does.
        //
        // An earlier version of this label claimed the setting could suspend the whole machine
        // on a Modern Standby laptop. That is untrue, and it described a different mechanism
        // entirely: the SC_MONITORPOWER broadcast this app abandoned years ago, recorded in the
        // comments on IdleController and NativeInput. What this setting sends is a DDC/CI power
        // request to external monitors, which cannot reach the operating system's power state
        // at all -- and reaches nothing whatever on a machine with only its built-in panel,
        // since that has no DDC/CI channel to answer on.
        Groups.Children.Add(Group("Power (advanced)",
            Check("Ask external monitors to sleep during a black screen",
                  s => s.MonitorStandby, (s, v) => s.MonitorStandby = v),
            Note("Sent over DDC/CI, so it reaches external monitors only and does nothing for a "
                 + "laptop's own screen. Minimum backlight is nearly as dark and always comes "
                 + "back; a monitor asked to sleep can take a moment to wake, and a few want "
                 + "their power button. Needs “Dim monitor backlights when dark” to be on."),
            Check("Carry an Emission onto the keyboard backlight",
                  s => s.KeyboardLighting, (s, v) => s.KeyboardLighting = v),
            Note("ASUS Aura keyboards only, and it needs Windows’ Dynamic Lighting switched off "
                 + "(Settings › Personalization › Dynamic Lighting). While that is on, Windows owns "
                 + "the keys and repaints its own colour over everything sent here — the writes are "
                 + "accepted and discarded with no error, so the keys hold one colour, ignore the "
                 + "Emission and stay lit through the blackout. The keyboard is handed back on "
                 + "waking, and whatever manages your lighting takes it from there."),
            Check("Carry the weather onto the keyboard too",
                  s => s.KeyboardWeather, (s, v) => s.KeyboardWeather = v),
            Note("Fog, rain and storms tint the keys the colour the sky is, much fainter than an "
                 + "Emission so an Emission still comes as a surprise; a clear sky leaves them "
                 + "unlit. Needs the setting above. Holds the keyboard for as long as the "
                 + "screensaver is up rather than for an Emission’s twelve seconds, and your own "
                 + "lighting does not run for that whole time."),
            Check("Switch Dynamic Lighting off while the keyboard is borrowed",
                  s => s.StandDynamicLightingDown, (s, v) => s.StandDynamicLightingDown = v),
            Note("Changes a Windows setting on your behalf and puts back whatever it found — on "
                 + "waking, on exit, and at the next start if this one ends badly. It is what "
                 + "makes “Carry an Emission onto the keyboard backlight” work without going "
                 + "into Windows Settings yourself, and it needs that setting on. While it is "
                 + "out, Settings › Personalization › Dynamic Lighting will read as off; a "
                 + "machine that already had it off is left off. With the weather setting on it "
                 + "lasts as long as that loan does — the whole time the screensaver is up, not "
                 + "an Emission’s twelve seconds.")));
    }

    /// <summary>Moves the start delay, carrying the blackout along behind it.
    ///
    /// The window offers the blackout as a gap -- "after a further five minutes" -- so the gap is
    /// what the user chose and the gap is what must survive. Setting the start delay alone would
    /// not: the clamp floors `BlackoutSeconds` at `IdleSeconds`, so raising the start delay past
    /// it quietly closes the gap to nothing, and a screen told to go black five minutes after the
    /// artifacts would start going black with them.</summary>
    private static void MoveStartDelay(Settings s, double seconds)
    {
        var gap = BlackoutGap(s);
        s.IdleSeconds = seconds;

        // Never stays never. Any real gap is re-measured from where the screensaver now starts.
        if (gap >= 0) s.BlackoutSeconds = seconds + gap;
    }

    /// <summary>The blackout delay as the window presents it: time after the screensaver
    /// appears, which is what the clamp actually enforces.
    ///
    /// <see cref="Durations.Never"/> rather than zero for "no blackout at all", because zero is
    /// already taken: a blackout delay equal to the start delay is a real setting, meaning the
    /// screen goes black the moment the artifacts would have appeared. Folding the two together
    /// would read a configured blackout back as "never" and switch it off on the way out.</summary>
    private static double BlackoutGap(Settings s) =>
        s.BlackoutSeconds <= 0 ? Durations.Never : Math.Max(0, s.BlackoutSeconds - s.IdleSeconds);

    private static GroupBox Group(string header, params FrameworkElement[] rows)
    {
        var panel = new StackPanel();
        foreach (var row in rows) panel.Children.Add(row);
        return new GroupBox { Header = header, Content = panel };
    }

    private TextBlock Note(string text) =>
        new() { Text = text, Style = TryFindResource("Note") as Style };

    private T ZoneOnly<T>(T element) where T : FrameworkElement
    {
        _zoneOnly.Add(element);
        return element;
    }

    private FrameworkElement Check(string label, Func<Settings, bool> get, Action<Settings, bool> set)
    {
        var box = new CheckBox { Content = label };
        box.Click += (_, _) => Edit(s => set(s, box.IsChecked == true));
        _refreshers.Add(() => box.IsChecked = get(_host.Current));
        return box;
    }

    /// <summary>A slider whose range is the range the clamp enforces, with the value it is
    /// currently at shown beside it.
    ///
    /// <paramref name="step"/> is the granularity the setting is worth having. A slider is a
    /// continuous control over a few hundred pixels, so without one it writes wherever the thumb
    /// physically landed -- an opacity of 0.7025083612040077 and a buoyancy of -2.4E-13, which
    /// are not values anybody chose and are unreadable in settings.json.</summary>
    private FrameworkElement Slide(string label, double min, double max, double step,
                            Func<Settings, double> get, Action<Settings, double> set, string format)
    {
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Margin = new Thickness(0, 0, 8, 0),

            // Snap the thumb as well as the value, so what is written is what was seen.
            TickFrequency = step,
            IsSnapToTickEnabled = true,
        };
        var readout = new TextBlock { MinWidth = 62, TextAlignment = TextAlignment.Right };

        slider.ValueChanged += (_, _) => Edit(s => set(s, Snap(slider.Value, step)));

        _refreshers.Add(() =>
        {
            var value = get(_host.Current);
            slider.Value = Math.Clamp(value, min, max);
            readout.Text = value.ToString(format, CultureInfo.CurrentCulture);
        });

        return Row(label, slider, readout);
    }

    /// <summary>The nearest multiple of <paramref name="step"/>.
    ///
    /// Rounded a second time because dividing and multiplying by a step leaves binary-float
    /// residue -- 0.45 comes back as 0.45000000000000007 -- and adding zero so that a negative
    /// value landing on nothing is written as 0 rather than -0.</summary>
    private static double Snap(double value, double step) =>
        Math.Round(Math.Round(value / step, MidpointRounding.AwayFromZero) * step, 6) + 0.0;

    private FrameworkElement Choice(string label, (string Text, double Value)[] options,
                             Func<Settings, double> get, Action<Settings, double> set)
    {
        var box = new ComboBox();
        foreach (var option in options) box.Items.Add(option.Text);

        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0) Edit(s => set(s, options[box.SelectedIndex].Value));
        };

        _refreshers.Add(() =>
        {
            var value = get(_host.Current);

            // The nearest offered duration, so a value typed into settings.json by hand still
            // selects something rather than leaving the box blank.
            var best = 0;
            for (var i = 1; i < options.Length; i++)
                if (Math.Abs(options[i].Value - value) < Math.Abs(options[best].Value - value))
                    best = i;
            box.SelectedIndex = best;
        });

        return Row(label, box, null);
    }

    /// <summary>The theme, chosen from pictures rather than from a list of names.
    ///
    /// A dropdown is the wrong control for this one setting. Every other setting in the window
    /// is a quantity or a yes-or-no and reads perfectly well as words; a theme is a picture, and
    /// naming it "The Zone" tells somebody who has not seen it nothing at all. So: a card each,
    /// image first, with the name underneath as the caption rather than as the control.</summary>
    private FrameworkElement Themes()
    {
        var cards = new StackPanel
        {
            Orientation = Orientation.Horizontal,

            // The Zone-only checkboxes follow immediately underneath, and without this they sit
            // against the cards as though they were part of them.
            Margin = new Thickness(0, 2, 0, 12),
        };
        var buttons = new List<(RadioButton Button, OverlayTheme Theme)>();

        foreach (var (theme, title, subtitle) in new[]
                 {
                     (OverlayTheme.Zone, "The Zone", "S.T.A.L.K.E.R. artifacts"),
                     (OverlayTheme.Soap, "Soap bubbles", "The original"),
                 })
        {
            var card = new RadioButton
            {
                GroupName = "Theme",
                Style = (Style)FindResource("ThemeCard"),
                Content = CardFace(theme, title, subtitle),
            };

            var chosen = theme;
            card.Checked += (_, _) => Edit(s => s.Theme = chosen);

            buttons.Add((card, theme));
            cards.Children.Add(card);
        }

        _refreshers.Add(() =>
        {
            foreach (var (button, theme) in buttons)
                button.IsChecked = _host.Current.Theme == theme;
        });

        return cards;
    }

    /// <summary>What sits inside a theme card: the picture, then the name and the line under
    /// it. The picture is flush to the top edge -- the card clips it to its own corners.</summary>
    private static FrameworkElement CardFace(OverlayTheme theme, string title, string subtitle)
    {
        var face = new StackPanel { Width = ThemePreview.Width };

        // Null when it could not be drawn. The card then shows its name and nothing else, which
        // is no worse than the dropdown it replaced.
        var picture = ThemePreview.For(theme);

        if (picture is not null)
        {
            face.Children.Add(new Image
            {
                Source = picture,
                Width = ThemePreview.Width,
                Height = ThemePreview.Height,
                Stretch = System.Windows.Media.Stretch.None,
                SnapsToDevicePixels = true,
            });
        }

        var caption = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
        caption.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
        });
        caption.Children.Add(new TextBlock
        {
            Text = subtitle,
            Opacity = 0.65,
            FontSize = 11,
            Margin = new Thickness(0, 2, 0, 0),
        });

        face.Children.Add(caption);
        return face;
    }

    private static FrameworkElement Row(string label, UIElement control, UIElement? trailing)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(text, 0);
        grid.Children.Add(text);

        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        if (trailing is not null)
        {
            Grid.SetColumn(trailing, 2);
            grid.Children.Add(trailing);
        }

        return grid;
    }

    /// <summary>The durations the delay boxes offer. Every one is inside the range the clamp
    /// permits, so choosing one can never produce a value that is then moved.</summary>
    private static class Durations
    {
        public static readonly (string Text, double Value)[] Idle =
        {
            ("30 seconds", 30),
            ("1 minute", 60),
            ("2 minutes", 120),
            ("5 minutes", 300),
            ("10 minutes", 600),
            ("30 minutes", 1800),
        };

        /// <summary>Stands for "no blackout", which zero cannot: see
        /// <see cref="BlackoutGap"/>.</summary>
        public const double Never = -1;

        public static readonly (string Text, double Value)[] Blackout =
        {
            ("Never", Never),
            ("As soon as the screensaver starts", 0),
            ("1 minute", 60),
            ("2 minutes", 120),
            ("5 minutes", 300),
            ("10 minutes", 600),
            ("30 minutes", 1800),
        };
    }
}
