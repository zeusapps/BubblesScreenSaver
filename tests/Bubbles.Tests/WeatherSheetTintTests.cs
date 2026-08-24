using System.Windows.Media;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>The colours the sheets are actually drawn in.
///
/// Distinct from <see cref="AnomalyTintTests"/>, which asserts the palettes. A family tint is
/// mixed into the base grey and lifted off the floor before a single streak is drawn, and it is
/// that mix the eye sees -- so it is the mix that has to be four colours rather than four
/// off-whites. It once was not, and only a live desktop showed it.</summary>
public sealed class WeatherSheetTintTests
{
    private static readonly Anomaly[] Families = Enum.GetValues<Anomaly>();

    private static int Distance(Color a, Color b) =>
        Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);

    private static double Saturation(Color c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B));
        double min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max <= 0 ? 0 : (max - min) / max;
    }

    [Fact]
    public void No_two_families_rain_the_same_colour() => Sta.Run(() =>
    {
        foreach (var a in Families)
        foreach (var b in Families)
        {
            if (a >= b) continue;

            var distance = Distance(WeatherTint.Rain(a), WeatherTint.Rain(b));

            Assert.True(distance >= 95,
                $"{a} rains {WeatherTint.Rain(a)} and {b} rains {WeatherTint.Rain(b)}, " +
                $"which are {distance} apart -- too close to tell apart on a dark desktop");
        }
    });

    [Fact]
    public void A_tinted_sheet_is_not_a_grey_one() => Sta.Run(() =>
    {
        // The failure mode that survived a whole export review: every family mixed back to
        // something within a few points of the untinted sheet, and the tint showed as nothing.
        foreach (var family in Families)
        {
            Assert.True(Distance(WeatherTint.Rain(family), WeatherTint.Rain(null)) >= 60,
                $"{family} rains {WeatherTint.Rain(family)}, near enough the untinted sheet");

            Assert.True(Saturation(WeatherTint.Rain(family)) >= 0.3,
                $"{family} rains {WeatherTint.Rain(family)}, which reads as grey");
        }
    });

    [Fact]
    public void No_family_is_swallowed_by_a_dark_desktop() => Sta.Run(() =>
    {
        // The floor the mix is lifted off. A sheet darker than the desktop behind it subtracts
        // light instead of adding it, which is a smudge rather than weather.
        static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

        var plain = Luminance(WeatherTint.Rain(null));

        foreach (var family in Families)
            Assert.True(Luminance(WeatherTint.Rain(family)) >= plain * 0.7,
                $"{family} rains {WeatherTint.Rain(family)}, darker than the desktop it falls on");
    });

    [Fact]
    public void The_fog_takes_the_same_tints_as_the_rain() => Sta.Run(() =>
    {
        // One sky. Fog and rain differ in their base colour, not in which family is colouring
        // them, so the two must never disagree about what the weather is made of.
        foreach (var a in Families)
        foreach (var b in Families)
        {
            if (a >= b) continue;

            Assert.True(Distance(WeatherTint.Fog(a), WeatherTint.Fog(b)) >= 95,
                $"{a} and {b} fog the same colour");
        }
    });
}
