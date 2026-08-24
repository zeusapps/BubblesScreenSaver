using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Bubbles.Displays;
using Bubbles.Overlay;
using Bubbles.Zone;

namespace Bubbles;

/// <summary>`Bubbles.exe --export &lt;dir&gt;` renders the artwork to PNGs and exits.
/// Lets the visuals be reviewed without taking over somebody's screen.</summary>
internal static class Export
{
    public static void Run(string directory)
    {
        Directory.CreateDirectory(directory);

        Save(ArtifactSheet(), Path.Combine(directory, "artifacts.png"));
        Save(DetectorPanel(), Path.Combine(directory, "detector.png"));
        Save(EmissionStrip(), Path.Combine(directory, "emission.png"));
        Save(MotionStrip(), Path.Combine(directory, "motion.png"));
        Save(HeroShot(), Path.Combine(directory, "hero.png"));
        Save(LightningStrip(), Path.Combine(directory, "lightning.png"));
        Save(WeatherStrip(), Path.Combine(directory, "weather.png"));
        Save(FamilyWeatherStrip(), Path.Combine(directory, "families.png"));
        Save(ScreensStrip(), Path.Combine(directory, "screens.png"));
    }

    // ---- the ten artifacts, on the sort of dark ground they'll actually sit on ----------
    private static FrameworkElement ArtifactSheet()
    {
        const int columns = 4;
        var grid = new UniformGrid
        {
            Columns = columns,
            Rows = (Artifacts.Count + columns - 1) / columns,
            Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0E, 0x0C)),
        };

        foreach (var (art, i) in Artifacts.All.Select((a, i) => (a, i)))
        {
            var cell = new StackPanel { Margin = new Thickness(8) };

            cell.Children.Add(Snapshot(i, time: 0, size: 190));

            cell.Children.Add(new TextBlock
            {
                Text = art.Name,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x5A)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 10),
            });

            grid.Children.Add(cell);
        }

        return grid;
    }

    // ---- the detector, fed a plausible field -------------------------------------------
    private static FrameworkElement DetectorPanel()
    {
        var pda = new Detector
        {
            Opacity = 1,
            Margin = new Thickness(30),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
        };

        var host = new Grid { Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0E, 0x0C)) };
        host.Children.Add(pda);

        // Measure once so the readout knows its own size, then feed it a field and let it fill in.
        Measure(host, new Size(double.PositiveInfinity, double.PositiveInfinity));

        // BubbleCount is a density -- artifacts on a baseline screen -- so asking for a literal
        // fourteen on a canvas this size means converting the count back into one.
        var panel = new Rect(0, 0, 1400, 760);
        var settings = new Settings
        {
            BubbleCount = MonitorRegions.DensityFor(14, new[] { panel }),
            MinRadius = 40,
            MaxRadius = 130,
        };
        var field = new BubbleField(settings, new Random(21)) { SkinCount = Artifacts.Count };
        field.SetRegions(new[] { panel });
        field.Resize(new Size(panel.Width, panel.Height));

        pda.Tick(1.0, field, new Rect(0, 0, 1400, 760));
        pda.ResetPosition();   // the drift would otherwise carry it off the export canvas
        Measure(host, new Size(double.PositiveInfinity, double.PositiveInfinity));
        return host;
    }

    // ---- three moments of an Emission, over a stand-in desktop --------------------------
    private static FrameworkElement EmissionStrip()
    {
        var strip = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

        strip.Children.Add(Moment("buildup", scrim: 0.86, sky: 0.94, flash: 0.00, artifacts: 1.00, bolt: 5.4));
        strip.Children.Add(Moment("wavefront", scrim: 0.86, sky: 0.86, flash: 0.85, artifacts: 1.00, bolt: 0));
        strip.Children.Add(Moment("dark", scrim: 1.00, sky: 0.00, flash: 0.00, artifacts: 0.00, bolt: 0));

        return strip;
    }

    private static FrameworkElement Moment(string label, double scrim, double sky, double flash,
        double artifacts, double bolt)
    {
        const double w = 460, h = 300;
        var layers = new Grid { Width = w, Height = h };

        // Stand-in desktop, so the compositing is judged against something lit.
        layers.Children.Add(new Rectangle { Fill = MockDesktop() });
        layers.Children.Add(new Rectangle { Fill = Brushes.Black, Opacity = scrim });

        layers.Children.Add(new Rectangle { Fill = OverlayWindow.EmissionSkyBrush(), Opacity = sky });

        if (bolt > 0) layers.Children.Add(new LightningLayer { Time = bolt });

        var canvas = new Canvas { Opacity = artifacts, ClipToBounds = true };
        var rng = new Random(3);
        for (var i = 0; i < 7; i++)
        {
            var size = 70 + rng.NextDouble() * 120;
            var image = Snapshot(rng.Next(Artifacts.Count), time: 1.7, size: size);
            Canvas.SetLeft(image, rng.NextDouble() * (w - size));
            Canvas.SetTop(image, rng.NextDouble() * (h - size));
            canvas.Children.Add(image);
        }
        layers.Children.Add(canvas);
        layers.Children.Add(new Rectangle { Fill = OverlayWindow.ShockwaveLightBrush(), Opacity = flash });

        layers.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8),
        });

        return new Border { Child = layers, Margin = new Thickness(6) };
    }

    /// <summary>One artifact, frozen at a given moment, sized for a sheet.</summary>
    private static FrameworkElement Snapshot(int skin, double time, double size) => new Viewbox
    {
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        Child = new ArtifactVisual { Skin = skin, Time = time },
    };

    /// <summary>A strip of one artifact over time, to show that it actually moves.</summary>
    private static FrameworkElement MotionStrip()
    {
        var rows = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(0x0C, 0x0E, 0x0C)) };
        int[] picks = { 0, 4, 8, 13 };

        foreach (var skin in picks)
        {
            var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

            row.Children.Add(new TextBlock
            {
                Text = Artifacts.All[skin].Name,
                Width = 110,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x46, 0xF0, 0x78)),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
            });

            for (var frame = 0; frame < 6; frame++)
                row.Children.Add(Snapshot(skin, time: frame * 0.9, size: 120));

            rows.Children.Add(row);
        }

        return rows;
    }

    /// <summary>A promotional shot of the overlay in use, composed entirely from stand-in
    /// furniture. Nothing here is a real desktop -- the point is to show the app without
    /// showing anybody's screen.</summary>
    private static FrameworkElement HeroShot()
    {
        const double w = 1600, h = 900;
        var layers = new Grid { Width = w, Height = h };

        layers.Children.Add(new Rectangle { Fill = MockDesktop() });
        layers.Children.Add(StandInWindows(w, h));
        layers.Children.Add(new Rectangle { Fill = Brushes.Black, Opacity = 0.55 });

        var canvas = new Canvas { ClipToBounds = true, Opacity = 0.9 };
        var rng = new Random(11);

        for (var i = 0; i < 13; i++)
        {
            var size = 150 + rng.NextDouble() * 210;
            var visual = Snapshot(i % Artifacts.Count, time: i * 2.3, size: size);
            Canvas.SetLeft(visual, rng.NextDouble() * (w - size));
            Canvas.SetTop(visual, rng.NextDouble() * (h - size));
            canvas.Children.Add(visual);
        }

        layers.Children.Add(canvas);

        var shot = new Rect(0, 0, w, h);
        var settings = new Settings
        {
            BubbleCount = MonitorRegions.DensityFor(18, new[] { shot }),
            MinRadius = 50,
            MaxRadius = 140,
        };
        var field = new BubbleField(settings, new Random(22)) { SkinCount = Artifacts.Count };
        field.SetRegions(new[] { shot });
        field.Resize(new Size(w, h));

        var detector = new Detector { Opacity = 1 };
        var host = new Canvas();
        host.Children.Add(detector);
        Measure(host, new Size(w, h));
        detector.Tick(1.0, field, new Rect(0, 0, w, h));
        Canvas.SetLeft(detector, 96);
        Canvas.SetTop(detector, h - 470);
        layers.Children.Add(host);

        return layers;
    }

    /// <summary>Blank window furniture, so the overlay has something to sit over.</summary>
    private static FrameworkElement StandInWindows(double w, double h)
    {
        var canvas = new Canvas();
        var chrome = new SolidColorBrush(Color.FromRgb(0x1E, 0x22, 0x28));
        var bar = new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x38));
        var line = new SolidColorBrush(Color.FromArgb(0x30, 0xC8, 0xD0, 0xD8));
        chrome.Freeze();
        bar.Freeze();
        line.Freeze();

        void Window(double x, double y, double width, double height, int lines)
        {
            var panel = new Grid { Width = width, Height = height };
            panel.Children.Add(new Rectangle { Fill = chrome, RadiusX = 8, RadiusY = 8 });
            panel.Children.Add(new Rectangle
            {
                Fill = bar,
                Height = 26,
                RadiusX = 8,
                RadiusY = 8,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
            });

            var text = new StackPanel { Margin = new Thickness(22, 48, 22, 0) };
            for (var i = 0; i < lines; i++)
            {
                text.Children.Add(new Rectangle
                {
                    Height = 7,
                    Width = width - 44 - (i % 3) * (width * 0.18),
                    Fill = line,
                    Margin = new Thickness(0, 0, 0, 13),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                });
            }
            panel.Children.Add(text);

            Canvas.SetLeft(panel, x);
            Canvas.SetTop(panel, y);
            canvas.Children.Add(panel);
        }

        Window(70, 60, 780, 520, 12);
        Window(900, 130, 620, 640, 15);
        return canvas;
    }

    /// <summary>A laptop panel beside a larger external, as the layers actually treat them.
    ///
    /// The whole point of drawing per monitor is only visible on two monitors of different sizes,
    /// which is exactly the setup nobody has to hand when they are changing this code. So the
    /// layout is simulated: two regions of the right shape and ratio, handed to the same layers
    /// the overlay uses, rendered offscreen.
    ///
    /// Three things to judge here, and each of them was wrong before per-monitor-layers:
    /// artifacts at the same density per unit area on both, bolts scaled by the screen they land
    /// on rather than by the tallest, and the sky's horizon at the same relative height on each.
    /// The regions are life size -- a real laptop beside a real 4K -- and the whole thing is
    /// scaled down at the end to fit an image. Life size matters: density is a count, and at
    /// quarter scale the desktop only earns seven artifacts, so rounding one of them either way
    /// swings the figure by a third and the comparison says nothing.</summary>
    private static FrameworkElement ScreensStrip()
    {
        var laptop = new Rect(0, 540, 1920, 1080);
        var external = new Rect(1920, 0, 3840, 2160);
        var regions = new[] { laptop, external };

        var strip = new StackPanel();
        strip.Children.Add(ScreensMoment("calm: artifact density and the horizon", regions, bolt: 0));

        // Both screens mid-strike, which is as rare as it sounds -- the schedules are seeded per
        // screen precisely so they do not flash together. Picked deliberately so the two bolts
        // can be compared side by side.
        strip.Children.Add(ScreensMoment("both screens striking: bolts scale to their own screen",
            regions, bolt: 0.95));

        return strip;
    }

    private static FrameworkElement ScreensMoment(string label, IReadOnlyList<Rect> regions, double bolt)
    {
        var w = regions[^1].Right;
        var h = regions.Max(r => r.Bottom);

        var layers = new Grid { Width = w, Height = h };

        // The desktop the overlay is not covering. Anything outside a screen is off the desktop
        // entirely, so it stays black and shows where the monitors are not.
        layers.Children.Add(new Rectangle { Fill = Brushes.Black });

        var sky = new SkyLayer { Fill = OverlayWindow.EmissionSkyBrush(), Regions = regions, Opacity = 0.94 };
        layers.Children.Add(sky);

        if (bolt > 0)
            layers.Children.Add(new LightningLayer { Regions = regions, Time = bolt });

        // Density is the setting's own doing: a count per baseline screen, dealt by area.
        var settings = new Settings { BubbleCount = 22, MinRadius = 26, MaxRadius = 74 };
        var field = new BubbleField(settings, new Random(23)) { SkinCount = Artifacts.Count };
        field.SetRegions(regions);
        field.Resize(new Size(w, h));

        var canvas = new Canvas { Opacity = 0.9, ClipToBounds = true };

        foreach (var b in field.Bubbles)
        {
            var image = Snapshot(b.Skin, time: 1.7, size: b.Radius * 2);
            Canvas.SetLeft(image, b.X - b.Radius);
            Canvas.SetTop(image, b.Y - b.Radius);
            canvas.Children.Add(image);
        }

        layers.Children.Add(canvas);

        // Per-screen counts, so the density claim is readable rather than something to count by
        // eye across two hundred artifacts.
        var counts = new int[regions.Count];
        foreach (var b in field.Bubbles) counts[b.Region]++;

        for (var i = 0; i < regions.Count; i++)
        {
            var area = regions[i].Width * regions[i].Height;
            var note = new TextBlock
            {
                Text = $"{counts[i]} artifacts / {counts[i] / area * 100_000:N2} per 100k sq",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 38,
                Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(regions[i].Left + 18, regions[i].Top + 12, 0, 0),
            };

            layers.Children.Add(note);
        }

        layers.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 42,
            Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20),
        });

        Measure(layers, new Size(w, h));

        // Scaled only for the export. Everything above ran at the size a real desktop would be.
        var shrunk = new Viewbox { Child = layers, Width = w / 4, Height = h / 4 };
        Measure(shrunk, new Size(w / 4, h / 4));

        return new Border { Child = shrunk, Margin = new Thickness(3) };
    }

    /// <summary>The four weather states over the artifacts, and one frame mid-change.
    ///
    /// Over the artifacts rather than over a black card, because where weather sits in the stack
    /// is the point: fog and rain go in front, and what has to be judged is whether the artifacts
    /// still read through them.</summary>
    private static FrameworkElement WeatherStrip()
    {
        var strip = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

        strip.Children.Add(WeatherMoment("clear", Weather.Clear));
        strip.Children.Add(WeatherMoment("fog", Weather.Fog));
        strip.Children.Add(WeatherMoment("rain", Weather.Rain));

        // Caught just after the storm's first strike. Without a bolt in it the storm panel is
        // indistinguishable from the rain one, which is the opposite of what it is for.
        strip.Children.Add(WeatherMoment("storm", Weather.Storm, bolt: 3.02));

        strip.Children.Add(WeatherMoment("fog to rain", Weather.Rain, Weather.Fog, 0.5));

        return strip;
    }

    /// <summary>Each family's weather, with that family's artifacts drifting in it.
    ///
    /// The four panels are the whole point of the tint: they have to be told apart at a glance,
    /// and they have to still look like rain. The last two are what a strike and a collection do
    /// to it, which are the other two things weather now answers to.</summary>
    private static FrameworkElement FamilyWeatherStrip()
    {
        var strip = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };

        foreach (var family in Enum.GetValues<Anomaly>())
            strip.Children.Add(WeatherMoment(
                family.ToString().ToLowerInvariant(), Weather.Rain, family: family));

        // A bolt overhead, so the lift the rain takes from it can be judged against the
        // unlit panel beside it.
        strip.Children.Add(WeatherMoment(
            "electrical, lit", Weather.Storm, family: Anomaly.Electrical, bolt: 3.02, lit: true));

        // A pickup, at the middle of the panel because there is no detector in these.
        strip.Children.Add(WeatherMoment(
            "thermic, collected", Weather.Rain, family: Anomaly.Thermic, flourish: true));

        return strip;
    }

    /// <param name="bolt">Seconds into the ambient storm's own clock, or 0 for no lightning.</param>
    /// <param name="family">The anomaly family the weather is coloured by, or null for untinted.</param>
    /// <param name="lit">Whether a strike is on screen, which brightens the precipitation.</param>
    /// <param name="flourish">Whether to show the burst a collection leaves.</param>
    private static FrameworkElement WeatherMoment(string label, Weather current,
        Weather? outgoing = null, double progress = 1, double bolt = 0,
        Anomaly? family = null, bool lit = false, bool flourish = false)
    {
        // Rendered at better than a full fog tile across, then scaled down. At panel size the
        // fog tile never repeated inside the frame, so the seam that made it look like a grid of
        // boxes could not appear in the very image meant to review it.
        const double w = 1600, h = 1000;
        var layers = new Grid { Width = w, Height = h };

        layers.Children.Add(new Rectangle { Fill = MockDesktop() });
        layers.Children.Add(new Rectangle { Fill = Brushes.Black, Opacity = 0.55 });

        // Behind the artifacts, where the sky belongs -- so a strike silhouettes them rather
        // than covering them, the same as during an Emission.
        if (bolt > 0)
            layers.Children.Add(new LightningLayer { Ambient = true, Time = bolt });

        var canvas = new Canvas { Opacity = 0.85, ClipToBounds = true };
        var rng = new Random(5);

        // A tinted panel shows that family's own artifacts, because the whole claim is that the
        // sky takes its colour from what is drifting in it -- and a green sky over four orange
        // artifacts shows the opposite.
        var kinds = family is { } anomaly
            ? Enumerable.Range(0, Artifacts.Count).Where(i => Artifacts.All[i].Family == anomaly).ToArray()
            : Enumerable.Range(0, Artifacts.Count).ToArray();

        for (var i = 0; i < 5; i++)
        {
            var size = 280 + rng.NextDouble() * 360;
            var image = Snapshot(kinds[rng.Next(kinds.Length)], time: 1.7, size: size);
            Canvas.SetLeft(image, rng.NextDouble() * (w - size));
            Canvas.SetTop(image, rng.NextDouble() * (h - size));
            canvas.Children.Add(image);
        }

        layers.Children.Add(canvas);

        // In front of the artifacts, where it is in the overlay.
        var weather = new WeatherLayer { Width = w, Height = h, Family = family, Lit = lit };
        layers.Children.Add(weather);

        layers.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 44,
            Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 20),
        });

        // Laid out before the weather is asked for, so its own bounds are known -- there is no
        // display layout here, so that fallback is what it draws against.
        Measure(layers, new Size(w, h));

        // The tint arrives by cross-fade, and a still frame wants the end of it rather than the
        // start -- a panel caught at progress zero shows the tint that was there before.
        weather.Tick(WeatherCycle.CrossFade);
        weather.Show(current, outgoing, progress);

        if (flourish && family is { } collected)
            weather.FlourishAt(new Point(w / 2, h / 2), collected, phase: 0.3);

        var shrunk = new Viewbox { Child = layers, Width = w / 4, Height = h / 4 };
        Measure(shrunk, new Size(w / 4, h / 4));

        return new Border { Child = shrunk, Margin = new Thickness(3) };
    }

    /// <summary>The sky at successive moments of a strike, with nothing else in the way.</summary>
    private static FrameworkElement LightningStrip()
    {
        var strip = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        double[] moments = { 0.92, 1.00, 1.08, 1.16, 1.24, 1.32 };

        foreach (var moment in moments)
        {
            var layers = new Grid { Width = 300, Height = 260 };
            layers.Children.Add(new Rectangle { Fill = Brushes.Black });
            layers.Children.Add(new Rectangle { Fill = OverlayWindow.EmissionSkyBrush(), Opacity = 0.9 });
            layers.Children.Add(new LightningLayer { Time = moment });
            layers.Children.Add(new TextBlock
            {
                Text = $"t+{moment:F2}s",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0xFF, 0xFF)),
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6),
            });

            strip.Children.Add(new Border { Child = layers, Margin = new Thickness(3) });
        }

        return strip;
    }

    private static Brush MockDesktop()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x1C, 0x24, 0x30), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x39, 0x2A, 0x22), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x12, 0x16, 0x1A), 1.0));
        return brush;
    }

    private static void Measure(FrameworkElement element, Size available)
    {
        element.Measure(available);
        element.Arrange(new Rect(new Point(0, 0), element.DesiredSize));
        element.UpdateLayout();
    }

    private static void Save(FrameworkElement element, string path)
    {
        Measure(element, new Size(double.PositiveInfinity, double.PositiveInfinity));

        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);
        if (width <= 0 || height <= 0) return;

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
