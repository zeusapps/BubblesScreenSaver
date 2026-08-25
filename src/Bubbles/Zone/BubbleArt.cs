using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bubbles.Zone;

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
        var bmp = RenderBubble(64);

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

    /// <summary>The sizes Windows asks for. The Start Menu, search, Alt-Tab, the taskbar and the
    /// file properties dialog each pick a different one, and a single size stretched to the rest
    /// is visibly soft at exactly the places somebody is looking for the application.</summary>
    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>Writes the bubble as a multi-size `.ico`, for the executable's own icon.
    ///
    /// The same drawing as the tray's and the window's, at more sizes -- an application with two
    /// different icons looks like two applications, and this is the one people meet first.
    ///
    /// PNG-compressed frames throughout, which Windows has read since Vista and which keeps a
    /// 256-pixel frame from costing a quarter of a megabyte on its own.</summary>
    public static void WriteIcon(Stream output)
    {
        var frames = IconSizes.Select(size =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(RenderBubble(size)));

            using var buffer = new MemoryStream();
            encoder.Save(buffer);
            return buffer.ToArray();
        }).ToList();

        using var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true);

        // ICONDIR: reserved, type 1 (icon), how many images follow.
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)frames.Count);

        // Every directory entry is 16 bytes, and they all precede the image data.
        var offset = 6 + (frames.Count * 16);

        for (var i = 0; i < frames.Count; i++)
        {
            // 256 is written as 0: the field is one byte and 256 does not fit in it.
            var size = IconSizes[i];
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);   // not a palette
            writer.Write((byte)0);   // reserved
            writer.Write((ushort)1); // colour planes
            writer.Write((ushort)32);
            writer.Write(frames[i].Length);
            writer.Write(offset);

            offset += frames[i].Length;
        }

        foreach (var frame in frames) writer.Write(frame);
    }

    /// <summary>The same bubble as an <see cref="ImageSource"/>, for the settings window's title
    /// bar and taskbar button.
    ///
    /// Rendered larger than the tray's, because this one is scaled down into several sizes at
    /// once and a 64-pixel source shows it. It is deliberately the same drawing: an app with two
    /// different icons looks like two apps.</summary>
    public static ImageSource CreateWindowIcon() => RenderBubble(256);

    /// <summary>The bubble itself, at whatever size is asked for. Everything is proportional to
    /// <paramref name="size"/> so the two callers get the same picture.</summary>
    private static BitmapSource RenderBubble(int size)
    {
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
        bmp.Freeze();
        return bmp;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
