using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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

        var settings = new Settings { BubbleCount = 14, MinRadius = 40, MaxRadius = 130 };
        var field = new BubbleField(settings) { SkinCount = Artifacts.Count };
        field.SetRegions(new[] { new Rect(0, 0, 1400, 760) });
        field.Resize(new Size(1400, 760));

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

        var settings = new Settings { BubbleCount = 18, MinRadius = 50, MaxRadius = 140 };
        var field = new BubbleField(settings) { SkinCount = Artifacts.Count };
        field.SetRegions(new[] { new Rect(0, 0, w, h) });
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
