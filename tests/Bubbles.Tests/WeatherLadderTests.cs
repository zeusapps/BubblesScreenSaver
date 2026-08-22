using System.Windows;
using System.Windows.Media;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>How finely the intensity ladder is divided.
///
/// The ladder was eight rungs on the reasoning that a six-second cross-fade makes that one step
/// every three quarters of a second, too slow to see. It was visible, and fog is where: a rung
/// is an opacity step of one eighth, which across a desktop-wide sheet is a jump of nearly three
/// percent in alpha, and a flat area that size bands at about one. Rain hid it because thin
/// scattered streaks have no flat area to band across.
///
/// These assert the granularity, not the appearance -- but the number they assert came from
/// measuring what banding costs, which is the part a screenshot cannot show.</summary>
public sealed class WeatherLadderTests
{
    /// <summary>The most a single rung may change a sheet's opacity.
    ///
    /// Large flat areas start showing steps around one percent of alpha. The fog tile's own
    /// alpha is about 0.22 at its strongest, so a rung may move opacity by roughly four percent
    /// before the step it makes is visible -- taken with margin.</summary>
    private const double Finest = 0.04;

    [Fact]
    public void No_rung_steps_far_enough_to_band() => Sta.Run(() =>
    {
        for (var level = 1; level < WeatherBrushes.Levels; level++)
        {
            var step = WeatherBrushes.FogAt(null, level).Opacity
                     - WeatherBrushes.FogAt(null, level - 1).Opacity;

            Assert.True(step <= Finest,
                $"rung {level} raises the fog by {step:P1}, which bands across a whole desktop");
        }
    });

    [Fact]
    public void A_cross_fade_climbs_the_whole_ladder() => Sta.Run(() =>
    {
        // Every rung has to be reachable by an intensity a fade actually passes through, or the
        // extra rungs are decoration and the visible steps are as far apart as they ever were.
        var reached = new HashSet<int>();

        for (var t = 0.0; t <= 1.0; t += 1.0 / 600)
            reached.Add(WeatherBrushes.LevelFor(t));

        for (var level = 0; level < WeatherBrushes.Levels; level++)
            Assert.Contains(level, reached);
    });

    [Fact]
    public void A_finer_ladder_costs_no_extra_bitmaps() => Sta.Run(() =>
    {
        // The reason this fix is affordable: a rung is another brush over the bitmap the whole
        // ladder already shares. If a rung ever owned a bitmap, thirty-two of them would be
        // thirty-two times the memory and the trade would be a bad one.
        var bitmaps = new HashSet<ImageSource>();

        for (var level = 0; level < WeatherBrushes.Levels; level++)
            bitmaps.Add(((ImageBrush)WeatherBrushes.FogAt(null, level)).ImageSource);

        Assert.Single(bitmaps);
    });

    [Fact]
    public void Rain_keeps_its_headroom_and_its_brightness() => Sta.Run(() =>
    {
        // The three constants have to stay in proportion however Levels moves: ordinary rain at
        // three quarters of the ladder, a strike spending exactly the quarter left above it.
        Assert.Equal(0.75, (WeatherBrushes.RainCeiling + 1.0) / WeatherBrushes.Levels, 6);
        Assert.Equal(WeatherBrushes.Levels - 1, WeatherBrushes.RainCeiling + WeatherBrushes.StrikeLift);

        var ceiling = WeatherBrushes.RainAt(null, 0, WeatherBrushes.RainCeiling);
        var lit = WeatherBrushes.RainAt(null, 0, WeatherBrushes.RainCeiling + WeatherBrushes.StrikeLift);

        Assert.Equal(4.0 / 3, lit.Opacity / ceiling.Opacity, 6);
    });
}
