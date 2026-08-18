using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bubbles;

/// <summary>Pre-renders a handful of soap-bubble sprites once, at startup.
/// Everything after that is just GPU-side transforms of these bitmaps, which is why
/// a hundred bubbles cost roughly nothing.</summary>
public static class BubbleArt
{
    private const int SpriteSize = 512;

    private static readonly Color[] Tints =
    {
        Color.FromRgb(150, 214, 255), // ice blue
        Color.FromRgb(198, 176, 255), // lavender
        Color.FromRgb(255, 178, 219), // rose
        Color.FromRgb(160, 255, 214), // mint
        Color.FromRgb(255, 222, 160), // warm gold
        Color.FromRgb(255, 255, 255), // plain glass
    };

    private static BitmapSource[]? _skins;

    public static int SkinCount => Tints.Length;

    public static BitmapSource[] Skins => _skins ??= Tints.Select(Render).ToArray();

    private static BitmapSource Render(Color tint)
    {
        const double s = SpriteSize;
        var centre = new Point(s / 2, s / 2);
        var r = s / 2 - 2;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // Everything lives inside the circle.
            dc.PushClip(new EllipseGeometry(centre, r, r));

            // --- the film itself: hollow in the middle, bright at the rim -------------
            var film = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
            };
            film.GradientStops.Add(new GradientStop(Argb(0, tint), 0.00));
            film.GradientStops.Add(new GradientStop(Argb(6, tint), 0.55));
            film.GradientStops.Add(new GradientStop(Argb(22, tint), 0.78));
            film.GradientStops.Add(new GradientStop(Argb(60, tint), 0.90));
            film.GradientStops.Add(new GradientStop(Argb(150, Colors.White), 0.955));
            film.GradientStops.Add(new GradientStop(Argb(225, tint), 0.985));
            film.GradientStops.Add(new GradientStop(Argb(0, tint), 1.00));
            film.Freeze();
            dc.DrawEllipse(film, null, centre, r, r);

            // --- iridescence: a second, off-centre sheen so the rim isn't uniform -----
            var sheen = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.28, 0.78),
                Center = new Point(0.28, 0.78),
                RadiusX = 0.72,
                RadiusY = 0.72,
            };
            sheen.GradientStops.Add(new GradientStop(Argb(0, Colors.White), 0.55));
            sheen.GradientStops.Add(new GradientStop(Argb(46, Complement(tint)), 0.86));
            sheen.GradientStops.Add(new GradientStop(Argb(0, Colors.White), 1.00));
            sheen.Freeze();
            dc.DrawEllipse(sheen, null, centre, r, r);

            // --- big soft highlight, upper left --------------------------------------
            DrawGlow(dc, new Point(s * 0.33, s * 0.28), s * 0.20, s * 0.15, 132);

            // --- tight specular dot ---------------------------------------------------
            DrawGlow(dc, new Point(s * 0.30, s * 0.24), s * 0.065, s * 0.05, 205);

            // --- dim bounce light from below right ------------------------------------
            DrawGlow(dc, new Point(s * 0.70, s * 0.76), s * 0.20, s * 0.13, 52);

            dc.Pop();
        }

        var bmp = new RenderTargetBitmap(SpriteSize, SpriteSize, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    private static void DrawGlow(DrawingContext dc, Point at, double rx, double ry, byte alpha)
    {
        var glow = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        glow.GradientStops.Add(new GradientStop(Argb(alpha, Colors.White), 0.0));
        glow.GradientStops.Add(new GradientStop(Argb((byte)(alpha * 0.35), Colors.White), 0.5));
        glow.GradientStops.Add(new GradientStop(Argb(0, Colors.White), 1.0));
        glow.Freeze();
        dc.DrawEllipse(glow, null, at, rx, ry);
    }

    private static Color Argb(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>Rough opposite hue, used for the iridescent band.</summary>
    private static Color Complement(Color c) =>
        Color.FromRgb((byte)(255 - c.R / 2), (byte)(255 - c.G / 2), (byte)(255 - c.B / 2));

    /// <summary>A small, deliberately opaque bubble for the notification area.</summary>
    public static System.Drawing.Icon CreateTrayIcon()
    {
        const int size = 64;
        var centre = new Point(size / 2.0, size / 2.0);
        var r = size / 2.0 - 2;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.3),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
            };
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(235, 235, 250, 255), 0.0));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(200, 120, 195, 255), 0.62));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.93));
            fill.GradientStops.Add(new GradientStop(Color.FromArgb(255, 90, 160, 235), 1.0));
            fill.Freeze();

            dc.DrawEllipse(fill, null, centre, r, r);
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), null,
                new Point(size * 0.34, size * 0.28), size * 0.10, size * 0.075);
        }

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bmp));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;

        using var gdi = new System.Drawing.Bitmap(stream);
        var handle = gdi.GetHicon();
        // Clone so the icon survives DestroyIcon on the temporary handle.
        using var temp = System.Drawing.Icon.FromHandle(handle);
        var icon = (System.Drawing.Icon)temp.Clone();
        DestroyIcon(handle);
        return icon;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
