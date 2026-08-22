using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>What the weather costs per frame, which is the constraint every decision in this
/// change was made under.
///
/// Weather is on screen for as long as the screensaver is. Repainting a desktop-wide tile per
/// frame is the defect this layer was rebuilt to remove in v1.8.1, and a tint that repaints one
/// would put it straight back. These assert the two things that keep that true: a steady frame
/// touches nothing, and a tile is rasterised once per tint and then reused for ever.</summary>
public sealed class WeatherCostTests
{
    private static readonly Rect Screen = new(0, 0, 3840, 2160);
    private static readonly Anomaly[] Families = Enum.GetValues<Anomaly>();

    private static List<Brush?> Fills(WeatherLayer layer) =>
        layer.Children.OfType<Rectangle>().Select(r => (Brush?)r.Fill).ToList();

    [Fact]
    public void A_steady_frame_changes_nothing() => Sta.Run(() =>
    {
        // Same weather, same tint, no strike: the sheets that were up stay up, filled with the
        // brushes they already had. Nothing is assigned, so nothing is rasterised.
        var layer = new WeatherLayer { Regions = [Screen], Family = Anomaly.Thermic };
        layer.Tick(WeatherCycle.CrossFade);
        layer.Show(Weather.Rain);

        var settled = Fills(layer);

        for (var frame = 0; frame < 600; frame++)
        {
            layer.Tick(1.0 / 60);
            layer.Show(Weather.Rain);
        }

        Assert.Equal(settled, Fills(layer));
    });

    [Fact]
    public void A_family_is_rasterised_once_and_then_never_again() => Sta.Run(() =>
    {
        // The bitmaps behind a family's brushes, before and after that family has been on and
        // off screen a hundred times.
        var before = Bitmaps(Anomaly.Gravitational);

        var layer = new WeatherLayer { Regions = [Screen] };

        for (var i = 0; i < 100; i++)
        {
            layer.Family = i % 2 == 0 ? Anomaly.Gravitational : Anomaly.Chemical;
            layer.Tick(WeatherCycle.CrossFade);
            layer.Show(Weather.Rain);
            layer.Stop();
        }

        Assert.Equal(before, Bitmaps(Anomaly.Gravitational));
    });

    [Fact]
    public void Every_tint_costs_the_same_handful_of_bitmaps() => Sta.Run(() =>
    {
        // Three rain scales and one fog tile per tint, whatever else changes. The eight rungs
        // share one bitmap each -- that is what the ladder is, and a rung that owned a bitmap
        // would multiply this by eight.
        foreach (var family in Families)
            Assert.Equal(WeatherBrushes.Scales + 1, Bitmaps(family).Count);
    });

    [Fact]
    public void All_five_tints_together_are_a_bounded_amount_of_memory() => Sta.Run(() =>
    {
        // Four families plus the untinted sheet, which is the most a run can ever hold. Named
        // rather than left implicit, because "lazy and cached" is only reassuring if the total
        // it converges on is known.
        var bytes = 0L;
        var seen = new HashSet<BitmapSource>();

        foreach (var family in Families.Cast<Anomaly?>().Append(null))
        foreach (var bitmap in Bitmaps(family))
        {
            if (!seen.Add(bitmap)) continue;
            bytes += (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;
        }

        Assert.True(bytes < 16 * 1024 * 1024,
            $"every tint's tiles together came to {bytes / (1024.0 * 1024):F1} MB");
    });

    [Fact]
    public void A_strike_costs_no_bitmaps_at_all() => Sta.Run(() =>
    {
        // The lift is a rung on a ladder that was built at startup, not a tile drawn brighter.
        var before = Bitmaps(Anomaly.Chemical);

        var layer = new WeatherLayer { Regions = [Screen], Family = Anomaly.Chemical };
        layer.Tick(WeatherCycle.CrossFade);

        for (var frame = 0; frame < 300; frame++)
        {
            layer.Lit = frame % 20 < 5;
            layer.Show(Weather.Storm);
        }

        Assert.Equal(before, Bitmaps(Anomaly.Chemical));
    });

    /// <summary>The distinct bitmaps behind one tint's sheets.</summary>
    private static HashSet<BitmapSource> Bitmaps(Anomaly? family)
    {
        var bitmaps = new HashSet<BitmapSource>();

        for (var scale = 0; scale < WeatherBrushes.Scales; scale++)
        for (var level = 0; level < WeatherBrushes.Levels; level++)
            bitmaps.Add((BitmapSource)((ImageBrush)WeatherBrushes.RainAt(family, scale, level)).ImageSource);

        for (var level = 0; level < WeatherBrushes.Levels; level++)
            bitmaps.Add((BitmapSource)((ImageBrush)WeatherBrushes.FogAt(family, level)).ImageSource);

        return bitmaps;
    }
}
