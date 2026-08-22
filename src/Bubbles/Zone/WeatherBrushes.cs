using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bubbles.Zone;

/// <summary>The tiles weather is drawn from, pre-baked at a ladder of intensities.
///
/// Intensity is baked into the brushes rather than applied as opacity, for the reason the
/// lightning already records: compositing a full-desktop layer at partial opacity forces WPF onto
/// an intermediate surface. Lightning can afford it because a strike lasts 0.42s inside a 12.5s
/// Emission; weather is on screen for as long as the screensaver is, and a cross-fade happens
/// every minute. Two states are live during a cross-fade, so an opacity approach would put two
/// desktop-sized surfaces up at once, every minute, for ever.
///
/// Every tile is rasterised once into a bitmap and tiled as an ImageBrush. Built as vector
/// drawings they were re-rasterised as the scroll transform moved them, which for fog meant a
/// hundred-odd radial gradients redrawn across the whole desktop, every frame, for as long as
/// the fog lasted. That was the one thing this file set out not to do.
///
/// Intensity is the brush's own Opacity, not a repainted tile. A Brush's opacity is folded into
/// the fill as it rasterises; it is UIElement.Opacity that forces WPF onto an intermediate
/// surface, and that is what the ladder exists to avoid. One bitmap per sheet, eight brushes
/// sharing it.</summary>
internal static class WeatherBrushes
{
    /// <summary>Steps in the intensity ladder. Eight, as everywhere else in this app -- the
    /// cross-fade runs over six seconds, so eight steps is a change every three quarters of a
    /// second and the ladder is invisible.</summary>
    public const int Levels = 8;

    /// <summary>Tile sizes in DIP. Rain is tiled at three scales so it has parallax, which is
    /// also what stops the repeat reading as a repeat.</summary>
    private static readonly (double Width, double Height, double Alpha, double Thickness)[] RainScales =
    [
        (180, 240, 0.26, 1.0),   // far: fine, faint, slow
        (260, 320, 0.34, 1.4),   // middle
        (380, 460, 0.42, 1.9),   // near: fatter, brighter, fast
    ];

    /// <summary>How long one tile takes to fall past, per scale. Nearer sheets fall faster,
    /// which is the parallax.</summary>
    public static readonly double[] RainPeriods = [1.5, 1.05, 0.72];

    /// <summary>Sideways travel as a fraction of the fall. Rain leans one way; this is the lean,
    /// and it has to match the slant baked into the streaks or they skid rather than fall.</summary>
    public const double RainSlant = 0.22;

    /// <summary>How far one fog tile drifts sideways, and how long it takes. Slow enough that
    /// it reads as air moving rather than as something sliding across the screen.</summary>
    public const double FogPeriod = 90;

    private static readonly Color Drop = Color.FromRgb(0xC6, 0xD8, 0xE8);
    private static readonly Color Haze = Color.FromRgb(0xB8, 0xC2, 0xC0);

    /// <summary>One scroll per sheet, shared by every intensity of that sheet.
    ///
    /// Shared deliberately. Each level is a different brush object, so if each carried its own
    /// transform the rain would jump back to the top of its loop every time the cross-fade moved
    /// it a rung -- eight visible stutters per transition. One transform per sheet means changing
    /// intensity is a Fill assignment and nothing else moves.</summary>
    private static readonly TranslateTransform[] RainScrolls = BuildScrolls(RainScales.Length);
    private static readonly TranslateTransform FogScroll = new();

    private static readonly Brush[][] Rain = BuildRain();
    private static readonly Brush[] Fog = BuildFog();

    /// <summary>The scroll to animate for one rain sheet.</summary>
    public static TranslateTransform RainScroll(int scale) =>
        RainScrolls[Math.Clamp(scale, 0, RainScrolls.Length - 1)];

    public static TranslateTransform FogDrift() => FogScroll;

    private static TranslateTransform[] BuildScrolls(int count)
    {
        var scrolls = new TranslateTransform[count];
        for (var i = 0; i < count; i++) scrolls[i] = new TranslateTransform();
        return scrolls;
    }

    public static int Scales => RainScales.Length;

    /// <summary>A rain tile at one parallax scale and one intensity.</summary>
    public static Brush RainAt(int scale, int level) =>
        Rain[Math.Clamp(scale, 0, Rain.Length - 1)][Math.Clamp(level, 0, Levels - 1)];

    public static Brush FogAt(int level) => Fog[Math.Clamp(level, 0, Levels - 1)];

    /// <summary>Turns an intensity in 0..1 into a rung on the ladder. Below the bottom rung
    /// nothing is drawn at all, so a state on its way out stops costing anything before it is
    /// formally finished.</summary>
    public static int LevelFor(double intensity) =>
        intensity <= 0.02 ? -1 : (int)Math.Clamp(Math.Round(intensity * (Levels - 1)), 0, Levels - 1);

    private static Brush[][] BuildRain()
    {
        var scales = new Brush[RainScales.Length][];

        for (var s = 0; s < RainScales.Length; s++)
        {
            var (w, h, alpha, thickness) = RainScales[s];

            {
                const double f = 1.0;
                var pen = new Pen(
                    new SolidColorBrush(Color.FromArgb((byte)(255 * alpha * f), Drop.R, Drop.G, Drop.B)),
                    thickness)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                };
                pen.Freeze();

                var streaks = new GeometryGroup();

                // Slanted, and unevenly spaced, so the tile does not read as a comb. The slant
                // is the same across all three scales -- rain falls one way.
                for (var i = 0; i < 9; i++)
                {
                    var x = w * Hash(s * 31 + i, 3);
                    var y = h * Hash(s * 31 + i, 11);
                    var length = h * (0.10 + Hash(s * 31 + i, 17) * 0.09);

                    streaks.Children.Add(new LineGeometry(
                        new Point(x, y),
                        new Point(x + length * RainSlant, y + length)));
                }

                streaks.Freeze();

                var drawing = new GeometryDrawing(null, pen, streaks);
                drawing.Freeze();

                // Full resolution: streaks are thin, hard-edged lines and a half-scale bitmap
                // turns them to mush.
                scales[s] = Ladder(Rasterise(drawing, w, h, 1.0), w, h, RainScrolls[s]);
            }
        }

        return scales;
    }

    private static Brush[] BuildFog()
    {
        const double w = 1400, h = 900;

        var blobs = new DrawingGroup();

        {
            const double f = 1.0;

            // A continuous base, with the patches on top of it.
            //
            // Patches alone left gaps: seven discs on a tile this size do not cover it, and
            // between them the screen was not fogged at all, so what read was circles of
            // varying strength rather than weather. Fog is thicker in places, never absent in
            // places. The base is what makes it fog; the patches are what stop it being a
            // uniform grey sheet over the desktop.
            var floor = new SolidColorBrush(Color.FromArgb((byte)(30 * f), Haze.R, Haze.G, Haze.B));
            floor.Freeze();

            var ground = new GeometryDrawing(floor, null, new RectangleGeometry(new Rect(0, 0, w, h)));
            ground.Freeze();
            blobs.Children.Add(ground);

            // Enough of them, and wide enough, that every point of the tile is inside several.
            // Overlap is what makes the variation continuous instead of a pattern of discs.
            for (var i = 0; i < 16; i++)
            {
                var cx = w * Hash(i, 5);
                var cy = h * Hash(i, 9);
                var rx = w * (0.30 + Hash(i, 13) * 0.30);
                var ry = h * (0.28 + Hash(i, 19) * 0.28);

                // Capped well below full: fog sits in front of the artifacts, and the point is
                // to soften them, not to hide them behind weather. Lower per patch than when
                // there were seven, because sixteen of them overlap far more.
                //
                // This number has been wrong twice, both times because the export panel was
                // smaller than one fog tile, so it never showed more than a corner of the
                // thinnest part and fog looked far weaker than it is. Judge it against a frame
                // at least a tile across.
                var peak = (byte)(26 * f * (0.6 + Hash(i, 23) * 0.4));

                var gradient = new RadialGradientBrush
                {
                    GradientOrigin = new Point(0.5, 0.5),
                    Center = new Point(0.5, 0.5),
                    RadiusX = 0.5,
                    RadiusY = 0.5,
                };

                // A long, gentle falloff. The old one held nearly half its strength to 55% of
                // the radius and then ran out, which gave every patch a findable edge.
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(peak, Haze.R, Haze.G, Haze.B), 0));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(peak * 0.72), Haze.R, Haze.G, Haze.B), 0.35));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(peak * 0.30), Haze.R, Haze.G, Haze.B), 0.68));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, Haze.R, Haze.G, Haze.B), 1));
                gradient.Freeze();

                // Drawn nine times, once per neighbouring tile position. A DrawingBrush clips
                // its drawing to the viewbox, so a blob overhanging an edge was cut off square
                // and the tiling showed as a grid of boxes. Repeating it at every offset means
                // whatever is cut from one side arrives from the other and the seam disappears.
                foreach (var dx in new[] { -w, 0.0, w })
                foreach (var dy in new[] { -h, 0.0, h })
                {
                    // Only the offsets that can actually reach the tile are worth drawing.
                    if (cx + dx + rx < 0 || cx + dx - rx > w) continue;
                    if (cy + dy + ry < 0 || cy + dy - ry > h) continue;

                    var blob = new GeometryDrawing(
                        gradient,
                        null,
                        new EllipseGeometry(new Point(cx + dx, cy + dy), rx, ry));
                    blob.Freeze();

                    blobs.Children.Add(blob);
                }
            }

        }

        blobs.Freeze();

        // Half resolution. Fog is smooth by definition, so nothing in it survives being
        // rasterised at 700x450 and tiled back up, and the bitmap costs a quarter as much.
        return Ladder(Rasterise(blobs, w, h, 0.5), w, h, FogScroll);
    }

    /// <summary>Draws a tile once into a bitmap, so the scroll moves pixels rather than
    /// re-running the drawing.</summary>
    private static BitmapSource Rasterise(Drawing drawing, double w, double h, double scale)
    {
        var visual = new DrawingVisual();

        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.DrawDrawing(drawing);
            dc.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Round(w * scale)),
            Math.Max(1, (int)Math.Round(h * scale)),
            96, 96, PixelFormats.Pbgra32);

        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>The eight intensities of one sheet, all sharing a single bitmap and a single
    /// scroll. Sharing the scroll matters: each level is a different brush object, so a
    /// per-level transform would send the sheet back to the top of its loop every time the
    /// cross-fade moved it a rung.</summary>
    private static Brush[] Ladder(BitmapSource tile, double w, double h, Transform scroll)
    {
        var levels = new Brush[Levels];

        for (var level = 0; level < Levels; level++)
        {
            levels[level] = new ImageBrush(tile)
            {
                Opacity = (level + 1.0) / Levels,
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, w, h),
                ViewportUnits = BrushMappingMode.Absolute,
                Transform = scroll,
            };
        }

        return levels;
    }

    /// <summary>The same hash the lightning uses, for the same reason: a tile that is the same
    /// every run without anything being stored.</summary>
    private static double Hash(int a, int b)
    {
        var x = Math.Sin(a * 91.7 + b * 47.3) * 24634.6345;
        return x - Math.Floor(x);
    }

    /// <summary>One tile's size at a parallax scale, so the scroll knows how far a loop is.</summary>
    public static Size RainTile(int scale) =>
        new(RainScales[Math.Clamp(scale, 0, RainScales.Length - 1)].Width,
            RainScales[Math.Clamp(scale, 0, RainScales.Length - 1)].Height);

    public static Size FogTile() => new(1400, 900);
}
