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
    /// <summary>Steps in the intensity ladder.
    ///
    /// This was eight, as everywhere else in this app, on the reasoning that a six-second
    /// cross-fade makes eight steps one change every three quarters of a second, which is too
    /// slow to see. That was wrong, and fog is where it showed: a rung is an opacity step of one
    /// eighth, which over a desktop-wide sheet at the fog's own alpha is a jump of nearly three
    /// percent -- and a flat area that large starts banding at about one. Rain hid it, because a
    /// step across thin scattered streaks has no large flat area to band across. Fog is nothing
    /// but large flat area.
    ///
    /// Thirty-two costs nothing worth counting. A rung is another ImageBrush over the bitmap the
    /// whole ladder already shares, so a finer ladder buys smoothness with a few hundred bytes
    /// of brush objects and not one extra pixel.</summary>
    public const int Levels = 32;

    /// <summary>The rung ordinary precipitation tops out at.
    ///
    /// Below the top of the ladder on purpose, so there is somewhere brighter for rain to go
    /// while a bolt is on screen. Without the headroom a strike could not light rain that was
    /// already falling at full strength, which is every strike worth lighting.
    ///
    /// The tiles are baked proportionally brighter to compensate, so rain at this rung is
    /// exactly the rain that was there before the headroom existed.
    ///
    /// Three quarters of the ladder, expressed as a fraction rather than a number so that it
    /// and <see cref="StrikeLift"/> move together with <see cref="Levels"/>. Three constants
    /// that have to stay in proportion are three chances to break the proportion.</summary>
    public const int RainCeiling = Levels * 3 / 4 - 1;

    /// <summary>How many rungs a strike lifts the precipitation: the quarter of the ladder the
    /// ceiling leaves free. Enough that the rain visibly answers the sky, little enough that it
    /// still reads as lit rather than as the rain itself flashing.</summary>
    public const int StrikeLift = Levels / 4;

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

    /// <summary>The untinted sheets: rain a cold grey-blue, fog a flat grey-green. What the
    /// weather looks like with nothing drifting in it, and the base every family tint is mixed
    /// into.</summary>
    private static readonly Color Drop = Color.FromRgb(0xC6, 0xD8, 0xE8);
    private static readonly Color Haze = Color.FromRgb(0xB8, 0xC2, 0xC0);

    /// <summary>How far a family's colour pulls the sheet away from the base.
    ///
    /// Not all the way. Rain in an acid-green sky is still rain, and a sheet taking its colour
    /// wholesale from the artifacts stops reading as weather and starts reading as a filter over
    /// the desktop.
    ///
    /// This was two thirds, which on a black desktop was not enough: the sheets are drawn at low
    /// alpha, so what reaches the eye is already most of the way to the base grey, and four
    /// families arrived as four barely different off-whites. The base still shows through in the
    /// streak shapes and the tile's own alpha; what it no longer does is decide the hue.</summary>
    private const double TintStrength = 0.85;

    /// <summary>One scroll per sheet, shared by every intensity of that sheet.
    ///
    /// Shared deliberately. Each level is a different brush object, so if each carried its own
    /// transform the rain would jump back to the top of its loop every time the cross-fade moved
    /// it a rung -- eight visible stutters per transition. One transform per sheet means changing
    /// intensity is a Fill assignment and nothing else moves.</summary>
    private static readonly TranslateTransform[] RainScrolls = BuildScrolls(RainScales.Length);
    private static readonly TranslateTransform FogScroll = new();

    /// <summary>One set of sheets per tint, built the first time that tint is asked for.
    ///
    /// Lazy because a tinted tile is a different bitmap, and four families' worth is some ten
    /// megabytes of them -- most of which a given desk will never show. Kept once built, because
    /// rasterising is the one thing this file exists to do exactly once.
    ///
    /// <see cref="Untinted"/> is the grey sheet, kept beside them because it is what the export
    /// panels and a field with nothing in it draw.</summary>
    private static readonly Dictionary<Anomaly, Sheets> Tinted = new();
    private static Sheets? Untinted;

    private sealed record Sheets(Brush[][] Rain, Brush[] Fog);

    private static Sheets For(Anomaly? family)
    {
        if (family is not { } anomaly) return Untinted ??= Build(null);

        if (Tinted.TryGetValue(anomaly, out var built)) return built;

        built = Build(anomaly);
        Tinted[anomaly] = built;
        return built;
    }

    private static Sheets Build(Anomaly? family) =>
        new(BuildRain(RainTint(family)), BuildFog(FogTint(family)));

    /// <summary>The colour a family's streaks are actually drawn in, and the colour its haze is.
    ///
    /// Internal because this is the thing that has to be told apart on a dark desktop, and the
    /// family tints on their own do not answer that -- they are mixed into a base and lifted
    /// before anything is drawn, and it was that mix, not the palettes, that once arrived as
    /// four barely different off-whites.</summary>
    internal static Color RainTint(Anomaly? family) => Tint(Drop, family);

    internal static Color FogTint(Anomaly? family) => Tint(Haze, family);

    /// <summary>Mixes a family's colour into one of the base colours, lifting the result if the
    /// mix came out dark.
    ///
    /// A floor, not a target. Normalising every tint back to the grey sheet's own brightness
    /// made all four of them pale variations of white, which is the one thing a tint must not
    /// be. What the floor is for is the family that mixes darker than the rest -- a sheet the
    /// desktop swallows is not weather -- and lifting only those leaves the other three the
    /// colours their artifacts actually are.
    ///
    /// Brightness here is the colour's, not the sheet's. How strongly the weather reads is the
    /// intensity ladder's business, and nothing in this method touches it.</summary>
    /// <summary>How dark a tint may be, against the untinted sheet's own brightness. Below this
    /// a family's weather disappears into the desktop rather than falling in front of it.</summary>
    private const double DarkestTint = 0.75;

    private static Color Tint(Color base_, Anomaly? family)
    {
        if (family is not { } anomaly) return base_;

        var tint = AnomalyTint.Of(anomaly);

        var r = base_.R + (tint.R - base_.R) * TintStrength;
        var g = base_.G + (tint.G - base_.G) * TintStrength;
        var b = base_.B + (tint.B - base_.B) * TintStrength;

        var mixed = Luminance(r, g, b);
        var floor = Luminance(base_.R, base_.G, base_.B) * DarkestTint;
        var scale = mixed >= floor || mixed <= 0 ? 1 : floor / mixed;

        return Color.FromRgb(Channel(r * scale), Channel(g * scale), Channel(b * scale));

        static double Luminance(double r, double g, double b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
        static byte Channel(double v) => (byte)Math.Clamp(Math.Round(v), 0, 255);
    }

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

    /// <summary>Builds one family's tiles ahead of being asked for them.
    ///
    /// Rasterising a family costs twenty to eighty milliseconds, which is one to five dropped
    /// frames. Left until the family first takes the sky, that lands inside the render loop --
    /// the fill happens on the first frame of the tint cross-fade, so the change of colour and
    /// the stall it causes are the same moment. Measured, not assumed: the freeze was visible.
    ///
    /// So the cost is paid up front instead, at idle priority, before anything needs it. Cheap
    /// to call again -- a family already built returns immediately.</summary>
    public static void Warm(Anomaly? family) => For(family);

    /// <summary>A rain tile at one parallax scale and one intensity, in a family's colour.
    /// A null family is the untinted sheet.</summary>
    public static Brush RainAt(Anomaly? family, int scale, int level)
    {
        var rain = For(family).Rain;
        return rain[Math.Clamp(scale, 0, rain.Length - 1)][Math.Clamp(level, 0, Levels - 1)];
    }

    public static Brush FogAt(Anomaly? family, int level) =>
        For(family).Fog[Math.Clamp(level, 0, Levels - 1)];

    /// <summary>Turns an intensity in 0..1 into a rung on the ladder. Below the bottom rung
    /// nothing is drawn at all, so a state on its way out stops costing anything before it is
    /// formally finished.</summary>
    public static int LevelFor(double intensity) => LevelFor(intensity, Levels - 1);

    /// <summary>The same, for precipitation, which tops out below the ladder's own top so a
    /// strike has somewhere to lift it to.</summary>
    public static int RainLevelFor(double intensity) => LevelFor(intensity, RainCeiling);

    /// <summary>The cutoff below which nothing is drawn, as a fraction of one rung.
    ///
    /// A fraction rather than a fixed intensity, because it has to stay below the bottom rung's
    /// own boundary however many rungs there are. It was a flat 0.02, which sat below rung zero
    /// on an eight-rung ladder and above it on a thirty-two-rung one -- so making the ladder
    /// finer silently deleted its bottom step, and a fade began by jumping to the second.</summary>
    private const double Cutoff = 0.25;

    private static int LevelFor(double intensity, int ceiling) =>
        intensity <= Cutoff / ceiling
            ? -1
            : (int)Math.Clamp(Math.Round(intensity * ceiling), 0, ceiling);

    private static Brush[][] BuildRain(Color drop)
    {
        var scales = new Brush[RainScales.Length][];

        for (var s = 0; s < RainScales.Length; s++)
        {
            var (w, h, alpha, thickness) = RainScales[s];

            {
                const double f = 1.0;
                // Baked up to what the top of the ladder should show, so that RainCeiling --
                // where ordinary rain sits -- lands on exactly the alpha named in the table.
                var lit = alpha * Levels / (RainCeiling + 1.0);

                var pen = new Pen(
                    new SolidColorBrush(Color.FromArgb((byte)(255 * lit * f), drop.R, drop.G, drop.B)),
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

    private static Brush[] BuildFog(Color haze)
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
            var floor = new SolidColorBrush(Color.FromArgb((byte)(30 * f), haze.R, haze.G, haze.B));
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
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(peak, haze.R, haze.G, haze.B), 0));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(peak * 0.72), haze.R, haze.G, haze.B), 0.35));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(peak * 0.30), haze.R, haze.G, haze.B), 0.68));
                gradient.GradientStops.Add(new GradientStop(Color.FromArgb(0, haze.R, haze.G, haze.B), 1));
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
