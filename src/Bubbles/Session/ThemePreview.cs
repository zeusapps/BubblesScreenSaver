using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using Bubbles.Overlay;
using Bubbles.Zone;

namespace Bubbles.Session;

/// <summary>A small picture of what a theme looks like, for the settings window.
///
/// A theme is the one setting in that window a name describes poorly. Everything else is a
/// quantity or a yes-or-no -- two minutes means two minutes -- and the screensaver is held off
/// while the window is open, so without this the only way to see which theme is selected is to
/// close the window and wait out the idle delay.
///
/// Drawn by the same types the overlay draws with, so it cannot quietly stop being true the way
/// a stored screenshot would. That is the rule <c>--export</c> already holds the README images
/// to.
///
/// Deliberately fixed: it does not read Dim, Opacity, BubbleCount or the rest. It answers
/// *which theme is this*, not *what do my settings look like* -- the window cannot honestly
/// answer the second, and a picture that moved when a slider was dragged would look as though
/// it were trying to. Fixed artwork also keeps it legible, since settings that dim nearly to
/// black would otherwise offer two identical dark rectangles to choose between.</summary>
internal static class ThemePreview
{
    public const double Width = 190;
    public const double Height = 76;

    /// <summary>How much of the stand-in desktop the dimming takes.
    ///
    /// Lighter than the shipped default of 0.55. At full size a dimmed desktop still reads,
    /// because there is a whole screen of it; at 190 by 76 the same figure leaves a rectangle
    /// that may as well be black -- which hides the one thing this layer is here to show.</summary>
    private const double Dim = 0.34;

    private static readonly Dictionary<OverlayTheme, BitmapSource?> Cache = new();

    /// <summary>The picture for a theme, rendered once and kept.
    ///
    /// Null if it could not be drawn. The caller shows nothing in that case: this is an addition
    /// to a dropdown that already worked, and must not become a way for the settings window --
    /// the only place several settings can be reached at all -- to fail to open.</summary>
    public static BitmapSource? For(OverlayTheme theme)
    {
        if (Cache.TryGetValue(theme, out var cached)) return cached;

        BitmapSource? rendered;

        try
        {
            rendered = Render(theme);
        }
        catch (Exception e)
        {
            Diagnostics.Log($"theme preview for {theme} could not be drawn: {e.Message}");
            rendered = null;
        }

        Cache[theme] = rendered;
        return rendered;
    }

    private static BitmapSource Render(OverlayTheme theme)
    {
        // The overlay's own layer order: the desktop, the dimming over it, then the artwork.
        // Showing the desktop through the dimming is the point -- compositing through to what is
        // underneath is the most distinctive thing this screensaver does, and a picture on a
        // black background hides exactly that.
        var layers = new Grid { Width = Width, Height = Height };
        layers.Children.Add(new Rectangle { Fill = Desktop() });
        layers.Children.Add(new Rectangle { Fill = Brushes.Black, Opacity = Dim });
        layers.Children.Add(theme switch
        {
            OverlayTheme.Zone => ZoneArtwork(),
            OverlayTheme.Soap => SoapArtwork(),
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "no preview for this theme"),
        });

        layers.Measure(new Size(Width, Height));
        layers.Arrange(new Rect(0, 0, Width, Height));
        layers.UpdateLayout();

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(Width), (int)Math.Ceiling(Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(layers);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Something to composite through that is plainly not anybody's screen.</summary>
    private static Brush Desktop()
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
        };
        // Brighter than Export's equivalent, for the same reason the dimming is lighter: this is
        // a thumbnail, and the gradient has to survive being one.
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x3A, 0x4E, 0x66), 0.0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x7A, 0x5A, 0x46), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x28, 0x30, 0x3A), 1.0));
        brush.Freeze();
        return brush;
    }

    /// <summary>Artifacts spread across the families, so the preview shows the variety rather
    /// than four of the same thing.</summary>
    private static UIElement ZoneArtwork()
    {
        var canvas = new Canvas { ClipToBounds = true, Opacity = 0.9 };

        // Seeded, like Export's sheets. A preview that differed between openings would read as a
        // defect, and one that differed between machines could not be written down in a spec.
        var rng = new Random(7);
        var step = Math.Max(1, Artifacts.Count / 5);

        for (var i = 0; i < 5; i++)
        {
            var size = 30 + rng.NextDouble() * 22;
            var visual = new Viewbox
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                Child = new ArtifactVisual { Skin = i * step % Artifacts.Count, Time = i * 2.3 },
            };

            Canvas.SetLeft(visual, rng.NextDouble() * (Width - size));
            Canvas.SetTop(visual, rng.NextDouble() * (Height - size));
            canvas.Children.Add(visual);
        }

        return canvas;
    }

    private static UIElement SoapArtwork()
    {
        var canvas = new Canvas { ClipToBounds = true, Opacity = 0.85 };
        var rng = new Random(7);
        var skins = BubbleArt.Skins;

        for (var i = 0; i < 5; i++)
        {
            var size = 30 + rng.NextDouble() * 26;
            var sprite = new Image
            {
                Source = skins[i % skins.Length],
                Width = size,
                Height = size,
            };

            Canvas.SetLeft(sprite, rng.NextDouble() * (Width - size));
            Canvas.SetTop(sprite, rng.NextDouble() * (Height - size));
            canvas.Children.Add(sprite);
        }

        return canvas;
    }
}
