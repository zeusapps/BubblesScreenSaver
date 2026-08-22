using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>What the sky does when the detector picks something up.
///
/// The flourish is the only local thing this layer can do. Its sheets are desktop-wide tiles and
/// a tile cannot be disturbed in one place without being repainted, so a collection gets an
/// element of its own -- and the questions worth asking are about that element's cost: that
/// there is never more than one, that it is taken out rather than left at zero opacity, and that
/// it goes with everything else at blackout.</summary>
public sealed class CollectionFlourishTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);
    private static readonly Point Detector = new(900, 500);

    private static WeatherLayer Layer()
    {
        var layer = new WeatherLayer { Regions = [Screen] };
        layer.Show(Weather.Rain);
        return layer;
    }

    /// <summary>The flourish elements in the tree: the children that are not weather sheets.
    /// Counted from the tree rather than taken from the layer's own tally, so the two have to
    /// agree.</summary>
    private static List<Rectangle> Bursts(WeatherLayer layer) =>
        layer.Children
            .OfType<Rectangle>()
            .Where(r => r.Fill is RadialGradientBrush)
            .ToList();

    [Fact]
    public void A_collection_leaves_a_burst_where_it_happened() => Sta.Run(() =>
    {
        var layer = Layer();
        layer.Flourish(Detector, Anomaly.Thermic);

        var burst = Assert.Single(Bursts(layer));

        Assert.Equal(Detector.X, Canvas.GetLeft(burst) + burst.Width / 2, 3);
        Assert.Equal(Detector.Y, Canvas.GetTop(burst) + burst.Height / 2, 3);
    });

    [Fact]
    public void It_is_in_the_family_colour() => Sta.Run(() =>
    {
        foreach (var family in Enum.GetValues<Anomaly>())
        {
            var layer = Layer();
            layer.Flourish(Detector, family);

            var fill = Assert.IsType<RadialGradientBrush>(Assert.Single(Bursts(layer)).Fill);
            var tint = AnomalyTint.Of(family);

            Assert.All(fill.GradientStops, stop =>
            {
                Assert.Equal(tint.R, stop.Color.R);
                Assert.Equal(tint.G, stop.Color.G);
                Assert.Equal(tint.B, stop.Color.B);
            });
        }
    });

    [Fact]
    public void Only_ever_one_is_alive() => Sta.Run(() =>
    {
        // Collections arriving as fast as the detector's cooldown allows, and then faster than
        // that, which is the case the layer's single slot exists for.
        var layer = Layer();

        for (var i = 0; i < 50; i++)
        {
            layer.Flourish(new Point(100 + i * 7, 400), (Anomaly)(i % 4));

            Assert.Single(Bursts(layer));
            Assert.Equal(1, layer.Flourishes);
        }
    });

    [Fact]
    public void It_is_shorter_than_the_cooldown_that_bounds_it()
    {
        // The bound comes from a constant in another file. If the cooldown ever drops below the
        // flourish's life, two would overlap -- and the layer's single slot would start cutting
        // one short rather than merely never being needed.
        Assert.True(WeatherLayer.FlourishLife < BubbleField.CollectCooldown,
            $"a flourish lives {WeatherLayer.FlourishLife}s inside a {BubbleField.CollectCooldown}s cooldown");
    }

    [Fact]
    public void It_sits_above_the_rain_and_below_everything_else() => Sta.Run(() =>
    {
        // Above the sheets so it reads as something in the air rather than behind the rain.
        // Below the detector by virtue of being on this layer at all -- the detector is a layer
        // of its own, above this one.
        var layer = Layer();
        layer.Flourish(Detector, Anomaly.Chemical);

        var burst = Assert.Single(Bursts(layer));

        Assert.Equal(layer.Children.Count - 1, layer.Children.IndexOf(burst));
    });

    [Fact]
    public void Blackout_takes_it_with_everything_else() => Sta.Run(() =>
    {
        var layer = Layer();
        layer.Flourish(Detector, Anomaly.Gravitational);

        layer.Stop();

        Assert.Empty(Bursts(layer));
        Assert.Equal(0, layer.Flourishes);
    });

    [Fact]
    public void A_monitor_arriving_does_not_strand_the_slot() => Sta.Run(() =>
    {
        // Rebuilding empties the tree. A layer still holding the old element would have its one
        // slot occupied for ever, and no flourish would ever show again.
        var layer = Layer();
        layer.Flourish(Detector, Anomaly.Chemical);

        layer.Regions = [Screen, new Rect(1920, 0, 2560, 1440)];

        Assert.Empty(Bursts(layer));

        layer.Flourish(Detector, Anomaly.Chemical);
        Assert.Single(Bursts(layer));
    });
}
