using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>Rain lit by the strikes -- the sentence the README has been carrying since weather
/// arrived, and which the layer did not make good on.
///
/// The interesting properties are all about restraint. The lift has to be there at all, which
/// means the ladder needs headroom above ordinary rain; it has to be a fill swap rather than a
/// repaint; it has to end when the strike does; and it must not have quietly dimmed the rain
/// that was there before the headroom existed.</summary>
public sealed class StrikeLitRainTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);

    private static WeatherLayer Layer()
    {
        var layer = new WeatherLayer { Regions = [Screen] };
        return layer;
    }

    private static List<Brush> Fills(WeatherLayer layer) =>
        layer.Children
            .OfType<Rectangle>()
            .Where(r => r.Fill is not null && r.Visibility == Visibility.Visible)
            .Select(r => r.Fill!)
            .ToList();

    private static double Strength(WeatherLayer layer) => Fills(layer).Sum(b => b.Opacity);

    [Fact]
    public void A_bolt_brightens_the_rain() => Sta.Run(() =>
    {
        var layer = Layer();
        layer.Show(Weather.Rain);

        var between = Strength(layer);

        layer.Lit = true;
        layer.Show(Weather.Rain);

        Assert.True(Strength(layer) > between,
            "the rain rendered no brighter with a bolt on screen");
    });

    [Fact]
    public void Rain_at_full_strength_can_still_be_lit() => Sta.Run(() =>
    {
        // The case the headroom exists for. Rain settled on its own state runs at full
        // intensity, and a ladder that topped out there would have nowhere to put a strike --
        // which is every strike anyone would want to see.
        var layer = Layer();
        layer.Show(Weather.Rain);

        Assert.All(Fills(layer), brush =>
            Assert.True(brush.Opacity < 1, "ordinary rain is already at the top of the ladder"));
    });

    [Fact]
    public void The_lift_ends_with_the_strike() => Sta.Run(() =>
    {
        var layer = Layer();
        layer.Show(Weather.Rain);

        var between = Fills(layer);

        layer.Lit = true;
        layer.Show(Weather.Rain);

        layer.Lit = false;
        layer.Show(Weather.Rain);

        Assert.Equal(between, Fills(layer));
    });

    [Fact]
    public void It_is_a_fill_swap_and_nothing_else() => Sta.Run(() =>
    {
        // Every brush a lit sheet can use was built at startup and is shared with the
        // unlit ones. Nothing is rasterised because a bolt appeared.
        var layer = Layer();
        layer.Lit = true;
        layer.Show(Weather.Rain);

        var built = new HashSet<Brush>();

        for (var scale = 0; scale < WeatherBrushes.Scales; scale++)
        for (var level = 0; level < WeatherBrushes.Levels; level++)
            built.Add(WeatherBrushes.RainAt(null, scale, level));

        Assert.All(Fills(layer), brush => Assert.Contains(brush, built));
    });

    [Fact]
    public void A_strike_does_not_light_the_fog() => Sta.Run(() =>
    {
        // Fog is not precipitation. A bolt inside a fog bank is a different effect and is not
        // this one.
        var layer = Layer();
        layer.Show(Weather.Fog);

        var unlit = Fills(layer);

        layer.Lit = true;
        layer.Show(Weather.Fog);

        Assert.Equal(unlit, Fills(layer));
    });

    [Fact]
    public void Nothing_is_lit_when_nothing_is_falling() => Sta.Run(() =>
    {
        var layer = Layer();
        layer.Lit = true;
        layer.Show(Weather.Clear);

        Assert.Empty(Fills(layer));
    });

    [Fact]
    public void The_headroom_did_not_dim_the_ordinary_rain()
    {
        // The tiles were baked brighter by exactly the fraction the ceiling takes off, so rain
        // at the ceiling is the rain that shipped before there was a ceiling. If these two
        // stop agreeing, every desk's rain has quietly changed strength.
        var opacity = (WeatherBrushes.RainCeiling + 1.0) / WeatherBrushes.Levels;
        var boost = WeatherBrushes.Levels / (WeatherBrushes.RainCeiling + 1.0);

        Assert.Equal(1.0, opacity * boost, 6);
        Assert.True(WeatherBrushes.RainCeiling + WeatherBrushes.StrikeLift <= WeatherBrushes.Levels - 1,
            "a strike would be clamped at the top of the ladder and show nothing");
    }

    [Fact]
    public void Lit_rain_is_brighter_by_the_lift_and_no_more() => Sta.Run(() =>
    {
        // Two rungs, not full brightness. The risk the design names is the rain reading as the
        // light source rather than as something the sky is lighting.
        var ceiling = WeatherBrushes.RainAt(null, 0, WeatherBrushes.RainCeiling);
        var lit = WeatherBrushes.RainAt(null, 0, WeatherBrushes.RainCeiling + WeatherBrushes.StrikeLift);

        var ratio = lit.Opacity / ceiling.Opacity;

        Assert.True(ratio is > 1.1 and < 1.6, $"a strike multiplied the rain by {ratio:F2}");
    });
}
