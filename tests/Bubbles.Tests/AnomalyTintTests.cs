using System.Windows.Media;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>The colour each family lends to the weather.
///
/// The question these answer is not "is it the right green" -- that is a judgement made by
/// looking at it. It is whether a tint derived by averaging four palettes still reads as a
/// colour at all, and whether the four are told apart. Averaging pale artifacts is exactly how
/// a family ends up tinting the sky off-white.</summary>
public sealed class AnomalyTintTests
{
    private static readonly Anomaly[] Families = Enum.GetValues<Anomaly>();

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    private static double Saturation(Color c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B));
        double min = Math.Min(c.R, Math.Min(c.G, c.B));
        return max <= 0 ? 0 : (max - min) / max;
    }

    [Fact]
    public void Every_family_has_a_tint()
    {
        foreach (var family in Families)
            Assert.NotEqual(default, AnomalyTint.Of(family));
    }

    [Fact]
    public void Every_tint_is_light_enough_to_show_on_a_dark_desktop()
    {
        // The sheets are drawn at low alpha over whatever is behind them, and a screensaver's
        // desktop is usually dark. A tint darker than the desktop subtracts light rather than
        // adding it, which is not weather -- it is a smudge.
        foreach (var family in Families)
        {
            var tint = AnomalyTint.Of(family);
            Assert.True(Luminance(tint) > 0.4,
                $"{family} tints with {tint}, luminance {Luminance(tint):F2} -- too dark to read");
        }
    }

    [Fact]
    public void Every_tint_is_a_colour_rather_than_a_grey()
    {
        // Chemical and Electrical both average to something very close to white before the
        // saturation floor is applied. Without it the sky is the same off-white whatever is
        // drifting in it, and the whole change shows nothing.
        foreach (var family in Families)
        {
            var tint = AnomalyTint.Of(family);
            Assert.True(Saturation(tint) >= 0.4,
                $"{family} tints with {tint}, saturation {Saturation(tint):F2} -- reads as grey");
        }
    }

    [Fact]
    public void The_four_are_told_apart()
    {
        var seen = new List<Color>();

        foreach (var family in Families)
        {
            var tint = AnomalyTint.Of(family);

            foreach (var other in seen)
            {
                var distance = Math.Abs(tint.R - other.R)
                             + Math.Abs(tint.G - other.G)
                             + Math.Abs(tint.B - other.B);

                Assert.True(distance > 90, $"{tint} and {other} are too close to tell apart");
            }

            seen.Add(tint);
        }
    }

    [Fact]
    public void The_dark_family_takes_its_shell()
    {
        // Gravitational cores are near-black. Taking them would tint nothing, so the shell --
        // the part of one of those artifacts that is actually visible -- is what lends the
        // colour. The assertion is that the tint is nowhere near the cores.
        var tint = AnomalyTint.Of(Anomaly.Gravitational);
        var cores = Artifacts.All.Where(a => a.Family == Anomaly.Gravitational).Select(a => a.Core);

        Assert.True(Luminance(tint) > cores.Max(Luminance) + 0.3,
            $"the gravitational tint {tint} is as dark as its cores");
    }

    [Fact]
    public void A_skin_index_finds_its_family()
    {
        for (var skin = 0; skin < Artifacts.Count; skin++)
            Assert.Equal(Artifacts.All[skin].Family, AnomalyTint.FamilyOf(skin));

        // The field wraps its skins when the theme offers more than the roster carries.
        Assert.Equal(AnomalyTint.FamilyOf(0), AnomalyTint.FamilyOf(Artifacts.Count));
    }
}
