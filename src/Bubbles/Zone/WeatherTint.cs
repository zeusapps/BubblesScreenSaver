using System.Windows.Media;

namespace Bubbles.Zone;

/// <summary>What colour the weather is, as opposed to what it is painted with.
///
/// Split out of <see cref="WeatherBrushes"/> because that class caches WPF brushes and
/// transforms, and a WPF object belongs to the thread that built it. Anything that only wants a
/// colour -- the keyboard lighting, a test -- would otherwise claim the brush cache for its own
/// thread simply by asking, and every later caller on the real thread would throw. The maths
/// here has no thread affinity at all.</summary>
public static class WeatherTint
{
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

    /// <summary>The colour a family's streaks are actually drawn in, and the colour its haze is.
    ///
    /// Internal because this is the thing that has to be told apart on a dark desktop, and the
    /// family tints on their own do not answer that -- they are mixed into a base and lifted
    /// before anything is drawn, and it was that mix, not the palettes, that once arrived as
    /// four barely different off-whites.</summary>
    public static Color Rain(Anomaly? family) => Of(Drop, family);

    public static Color Fog(Anomaly? family) => Of(Haze, family);

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

    private static Color Of(Color base_, Anomaly? family)
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
}
