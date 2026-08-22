using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>The tinted sheets: what changes when the weather takes a family's colour, and --
/// more to the point -- what does not.
///
/// A tint is a different bitmap, so the risk is not that it fails to appear but that it quietly
/// takes something else with it: the intensity ladder, the shared scroll, or the rule that a
/// tile is rasterised once and never again.
///
/// A tint arrives by cross-fade, so every test that wants to see one has to let the fade land
/// first. That is what <see cref="Settled"/> is for, and forgetting it is the reason a tint test
/// can pass while asserting nothing.</summary>
public sealed class WeatherTintTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);
    private static readonly Anomaly[] Families = Enum.GetValues<Anomaly>();

    /// <summary>A layer showing one family, with the cross-fade that brought it in already
    /// finished.</summary>
    private static WeatherLayer Settled(Anomaly? family)
    {
        var layer = new WeatherLayer { Regions = [Screen], Family = family };
        layer.Tick(WeatherCycle.CrossFade);
        return layer;
    }

    /// <summary>Every rain brush belonging to one family, at every rung. What identifies a
    /// sheet as that family's, whatever intensity it happens to be showing at.</summary>
    private static HashSet<Brush> AllRungs(Anomaly? family)
    {
        var brushes = new HashSet<Brush>();

        for (var scale = 0; scale < WeatherBrushes.Scales; scale++)
        for (var level = 0; level < WeatherBrushes.Levels; level++)
            brushes.Add(WeatherBrushes.RainAt(family, scale, level));

        return brushes;
    }

    private static List<Brush> Fills(WeatherLayer layer) =>
        layer.Children
            .OfType<Rectangle>()
            .Where(r => r.Fill is not null && r.Visibility == Visibility.Visible)
            .Select(r => r.Fill)
            .ToList();

    // -- the tiles ------------------------------------------------------------------------

    [Fact]
    public void A_tinted_sheet_is_no_more_opaque_than_an_untinted_one() => Sta.Run(() =>
    {
        // The rung is what says how strongly the weather reads. If a tint moved it as well, a
        // Thermic sky would be heavier than a Chemical one for no reason anybody could name.
        var plain = Settled(null);
        plain.Show(Weather.Rain);

        var plainOpacities = Fills(plain).Select(b => b.Opacity).ToList();
        Assert.NotEmpty(plainOpacities);

        foreach (var family in Families)
        {
            var tinted = Settled(family);
            tinted.Show(Weather.Rain);

            var fills = Fills(tinted);

            Assert.Equal(plainOpacities, fills.Select(b => b.Opacity).ToList());
            Assert.All(fills, brush => Assert.DoesNotContain(brush, Fills(plain)));
        }
    });

    [Fact]
    public void The_same_holds_at_every_rung_of_the_ladder() => Sta.Run(() =>
    {
        for (var level = 0; level < WeatherBrushes.Levels; level++)
        {
            var rung = WeatherBrushes.LevelFor((level + 1.0) / WeatherBrushes.Levels);

            foreach (var family in Families)
            {
                Assert.Equal(
                    WeatherBrushes.RainAt(null, 0, rung).Opacity,
                    WeatherBrushes.RainAt(family, 0, rung).Opacity);

                Assert.Equal(
                    WeatherBrushes.FogAt(null, rung).Opacity,
                    WeatherBrushes.FogAt(family, rung).Opacity);
            }
        }
    });

    [Fact]
    public void A_tint_is_rasterised_once_and_kept() => Sta.Run(() =>
    {
        // The one thing this file exists to do exactly once. Asking twice must hand back the
        // same brush over the same bitmap, or a tint change would repaint the desktop.
        foreach (var family in Families)
        {
            Assert.Same(WeatherBrushes.RainAt(family, 1, 4), WeatherBrushes.RainAt(family, 1, 4));
            Assert.Same(WeatherBrushes.FogAt(family, 4), WeatherBrushes.FogAt(family, 4));
        }
    });

    [Fact]
    public void Every_tint_shares_one_scroll() => Sta.Run(() =>
    {
        // A tint change swaps the fill and nothing else. If each tint carried its own transform
        // the rain would jump back to the top of its loop every time the sky changed colour --
        // the same stutter the shared scroll was introduced to remove.
        for (var scale = 0; scale < WeatherBrushes.Scales; scale++)
        {
            var scroll = WeatherBrushes.RainScroll(scale);

            foreach (var family in Families)
                Assert.Same(scroll, ((TileBrush)WeatherBrushes.RainAt(family, scale, 3)).Transform);
        }

        foreach (var family in Families)
            Assert.Same(WeatherBrushes.FogDrift(), ((TileBrush)WeatherBrushes.FogAt(family, 3)).Transform);
    });

    [Fact]
    public void The_four_families_are_four_different_sheets() => Sta.Run(() =>
    {
        var seen = new List<Brush>();

        foreach (var family in Families)
        {
            var brush = WeatherBrushes.RainAt(family, 2, 5);
            Assert.DoesNotContain(brush, seen);
            seen.Add(brush);
        }

        Assert.DoesNotContain(WeatherBrushes.RainAt(null, 2, 5), seen);
    });

    [Fact]
    public void A_layer_that_was_never_told_a_family_draws_what_it_always_drew() => Sta.Run(() =>
    {
        // The migration step: the tinted tiles go in behind the existing lookup, and a layer
        // with no tint draws exactly the sheets it drew before there were any.
        var layer = new WeatherLayer { Regions = [Screen] };
        layer.Show(Weather.Rain);

        Assert.Equal(
            Fills(layer),
            new[] { 0, 1, 2 }.Select(s => WeatherBrushes.RainAt(null, s, WeatherBrushes.RainLevelFor(1))).ToList());
    });

    // -- the cross-fade -------------------------------------------------------------------

    [Fact]
    public void The_outgoing_tint_fades_out_as_the_incoming_one_fades_in() => Sta.Run(() =>
    {
        var layer = Settled(Anomaly.Chemical);
        layer.Show(Weather.Rain);

        var chemical = Fills(layer);
        Assert.NotEmpty(chemical);

        layer.Family = Anomaly.Thermic;

        // Half way: both are on screen, which is the whole point of a cross-fade. Compared
        // against every rung of each family rather than against the brushes captured above --
        // both tints are dimmer half way through than either is on its own.
        layer.Tick(WeatherCycle.CrossFade / 2);
        layer.Show(Weather.Rain);

        var midway = Fills(layer);

        Assert.Contains(midway, brush => AllRungs(Anomaly.Chemical).Contains(brush));
        Assert.Contains(midway, brush => AllRungs(Anomaly.Thermic).Contains(brush));

        // Landed: the old tint is gone entirely.
        layer.Tick(WeatherCycle.CrossFade);
        layer.Show(Weather.Rain);

        var thermic = Fills(layer);

        Assert.Equal(chemical.Count, thermic.Count);
        Assert.All(thermic, brush => Assert.DoesNotContain(brush, chemical));
    });

    [Fact]
    public void A_tint_change_is_not_a_hard_cut() => Sta.Run(() =>
    {
        // The failure this guards against is the tint simply being assigned: one frame of
        // acid green, the next of amber, across the whole desktop.
        var layer = Settled(Anomaly.Chemical);
        layer.Show(Weather.Rain);

        var chemical = Fills(layer);

        layer.Family = Anomaly.Thermic;
        layer.Tick(1.0 / 60);
        layer.Show(Weather.Rain);

        Assert.All(Fills(layer), brush => Assert.Contains(brush, chemical));
    });

    [Fact]
    public void At_most_two_tints_are_ever_live() => Sta.Run(() =>
    {
        // Including when a state change and a tint change coincide, which is the one moment the
        // layer is already at its busiest.
        var layer = Settled(Anomaly.Chemical);
        layer.Show(Weather.Rain);

        var families = new[] { Anomaly.Thermic, Anomaly.Electrical, Anomaly.Gravitational, Anomaly.Chemical };

        for (var i = 0; i < families.Length; i++)
        {
            layer.Family = families[i];

            // A weather change running at the same time, walked frame by frame through its own
            // cross-fade.
            for (var frame = 0; frame < WeatherCycle.CrossFade * 60; frame++)
            {
                var progress = frame / (WeatherCycle.CrossFade * 60);

                layer.Tick(1.0 / 60);
                layer.Show(Weather.Rain, i % 2 == 0 ? Weather.Fog : Weather.Storm, progress);

                foreach (var kind in Fills(layer).GroupBy(Kind))
                    Assert.True(kind.Count() <= 2,
                        $"{kind.Count()} sheets of {kind.Key} were live at once");
            }
        }

        // Sheets of the same kind: the fog sheet, or one of the three rain scales. Two of any
        // one of those is the cross-fade; three is a bug.
        static string Kind(Brush brush) =>
            brush is TileBrush tile ? $"{tile.Viewport}" : brush.ToString() ?? "?";
    });

    [Fact]
    public void A_second_change_mid_fade_does_not_leave_three_live() => Sta.Run(() =>
    {
        var layer = Settled(Anomaly.Chemical);
        layer.Show(Weather.Rain);

        layer.Family = Anomaly.Thermic;
        layer.Tick(WeatherCycle.CrossFade / 3);
        layer.Show(Weather.Rain);

        layer.Family = Anomaly.Electrical;
        layer.Tick(WeatherCycle.CrossFade / 3);
        layer.Show(Weather.Rain);

        // Three rain scales, at most two tints of each.
        Assert.True(Fills(layer).Count <= WeatherBrushes.Scales * 2,
            $"{Fills(layer).Count} rain sheets were live on one screen");
    });

    [Fact]
    public void Stopping_forgets_a_fade_in_flight() => Sta.Run(() =>
    {
        // Coming back from a blackout should not resume a cross-fade between two colours
        // nobody has seen for hours.
        var layer = Settled(Anomaly.Chemical);
        layer.Show(Weather.Rain);

        layer.Family = Anomaly.Thermic;
        layer.Tick(WeatherCycle.CrossFade / 2);
        layer.Show(Weather.Rain);

        layer.Stop();
        layer.Show(Weather.Rain);

        var thermic = Settled(Anomaly.Thermic);
        thermic.Show(Weather.Rain);

        Assert.Equal(Fills(thermic), Fills(layer));
    });
}
