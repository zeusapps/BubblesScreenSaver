using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>What the weather layer puts on screen for a given state.
///
/// Asserted through the sheets it builds and the brushes it hangs on them, not through pixels:
/// the questions are how many screens are covered, whether two states are live at once, and
/// whether anything is left running when the answer is nothing -- and a bitmap answers none of
/// those directly.</summary>
public sealed class WeatherLayerTests
{
    private static readonly Rect One = new(0, 0, 1920, 1080);
    private static readonly Rect Two = new(1920, 0, 3840, 2160);

    private static WeatherLayer Layer(params Rect[] regions)
    {
        var layer = new WeatherLayer();
        if (regions.Length > 0) layer.Regions = regions;
        return layer;
    }

    /// <summary>The sheets currently filled with something, by the rectangle they cover.</summary>
    private static List<Rect> Showing(WeatherLayer layer)
    {
        var showing = new List<Rect>();

        foreach (var child in layer.Children)
        {
            if (child is not Rectangle r || r.Fill is null || r.Visibility != Visibility.Visible)
                continue;

            showing.Add(new Rect(Canvas.GetLeft(r), Canvas.GetTop(r), r.Width, r.Height));
        }

        return showing;
    }

    private static int Sheets(WeatherLayer layer) => Showing(layer).Count;

    /// <summary>A cycle pinned to one state, so a layer can be asked about it directly.</summary>
    private static WeatherCycle Pinned(Weather state)
    {
        // Rolled until it lands on the wanted state. The roll never repeats the current state,
        // so every state is reachable within a few.
        for (var seed = 0; seed < 500; seed++)
        {
            var cycle = new WeatherCycle(new Random(seed));
            if (cycle.Current == state) return cycle;
        }

        throw new InvalidOperationException($"no seed produced {state}");
    }

    [Fact]
    public void Clear_weather_draws_nothing() => Sta.Run(() =>
    {
        var layer = Layer(One);
        layer.Show(Pinned(Weather.Clear));

        Assert.Empty(Showing(layer));
    });

    [Fact]
    public void Rain_covers_every_screen() => Sta.Run(() =>
    {
        var layer = Layer(One, Two);
        layer.Show(Pinned(Weather.Rain));

        var covered = Showing(layer).Select(r => new Rect(r.X, r.Y, r.Width, r.Height)).ToHashSet();

        Assert.Contains(One, covered);
        Assert.Contains(Two, covered);
    });

    [Fact]
    public void Rain_and_fog_are_different_weather() => Sta.Run(() =>
    {
        var rain = Layer(One);
        rain.Show(Pinned(Weather.Rain));

        var fog = Layer(One);
        fog.Show(Pinned(Weather.Fog));

        Assert.NotEqual(Sheets(rain), Sheets(fog));
    });

    [Fact]
    public void A_storm_rains() => Sta.Run(() =>
    {
        // The lightning is on the sky layer behind the artifacts; the rain is this layer's half.
        var storm = Layer(One);
        storm.Show(Pinned(Weather.Storm));

        var rain = Layer(One);
        rain.Show(Pinned(Weather.Rain));

        Assert.Equal(Sheets(rain), Sheets(storm));
    });

    [Fact]
    public void Rain_falls_at_several_depths() => Sta.Run(() =>
    {
        // Parallax, which is also what stops the tiling repeat reading as a repeat.
        var layer = Layer(One);
        layer.Show(Pinned(Weather.Rain));

        var brushes = layer.Children
            .OfType<Rectangle>()
            .Where(r => r.Fill is not null && r.Visibility == Visibility.Visible)
            .Select(r => r.Fill)
            .Distinct()
            .Count();

        Assert.True(brushes >= 3, $"rain used {brushes} tile scales, expected at least 3");
    });

    [Fact]
    public void Density_per_screen_comes_from_the_tile_not_from_a_count() => Sta.Run(() =>
    {
        // A tile is a fixed size in DIP, so a screen four times the area carries four times as
        // much rain without anything being counted. What has to hold is that each screen gets
        // its own sheet covering exactly itself.
        var layer = Layer(One, Two);
        layer.Show(Pinned(Weather.Rain));

        foreach (var sheet in Showing(layer))
            Assert.True(sheet == One || sheet == Two, $"a sheet covered {sheet}, which is no screen");
    });

    [Fact]
    public void An_emission_pulls_the_fog_out_but_leaves_the_rain() => Sta.Run(() =>
    {
        var fog = Layer(One);
        fog.FogDamping = 0;
        fog.Show(Pinned(Weather.Fog));

        Assert.Empty(Showing(fog));

        var storm = Layer(One);
        storm.FogDamping = 0;
        storm.Show(Pinned(Weather.Storm));

        Assert.NotEmpty(Showing(storm));
    });

    [Fact]
    public void Stopping_empties_the_layer() => Sta.Run(() =>
    {
        // Nothing is drawn once the screen is black.
        var layer = Layer(One);
        layer.Show(Pinned(Weather.Rain));
        Assert.NotEmpty(Showing(layer));

        layer.Stop();

        Assert.Empty(Showing(layer));
    });

    [Fact]
    public void With_no_regions_it_covers_its_own_bounds() => Sta.Run(() =>
    {
        // How the offline renderers get weather, with no display layout at all.
        var layer = new WeatherLayer();
        layer.Measure(new Size(460, 300));
        layer.Arrange(new Rect(0, 0, 460, 300));
        layer.UpdateLayout();

        layer.Show(Pinned(Weather.Rain));

        Assert.NotEmpty(Showing(layer));
        Assert.All(Showing(layer), r => Assert.Equal(new Rect(0, 0, 460, 300), r));
    });

    [Fact]
    public void A_layout_that_changes_nothing_leaves_the_sheets_alone() => Sta.Run(() =>
    {
        var layer = Layer(One, Two);
        layer.Show(Pinned(Weather.Rain));

        var before = layer.Children.OfType<Rectangle>().ToArray();
        layer.Regions = new[] { One, Two };

        Assert.Equal(before, layer.Children.OfType<Rectangle>().ToArray());
    });
}
