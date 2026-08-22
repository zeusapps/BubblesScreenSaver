using System.Diagnostics;
using System.Windows;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>Rasterising a family's tiles must never happen inside a frame.
///
/// This is the one thing about tinted weather that was got wrong and only a running desktop
/// showed: the tiles were built the first time a family took the sky, which is the first frame
/// of the tint cross-fade -- so the change of colour and a twenty-to-eighty millisecond stall
/// were the same moment, and the rain visibly froze. The design had called it "a few
/// milliseconds, off the render path". It was neither.
///
/// What holds now is that a warmed family costs nothing to show. The warming itself is done at
/// idle priority before anything needs it.</summary>
public sealed class WeatherWarmupTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);

    [Fact]
    public void A_warmed_family_costs_nothing_to_show() => Sta.Run(() =>
    {
        foreach (var family in Enum.GetValues<Anomaly>()) WeatherBrushes.Warm(family);
        WeatherBrushes.Warm(null);

        var layer = new WeatherLayer { Regions = [Screen] };

        // Every family taken up in turn, each with the cross-fade run out, timed as a whole.
        // Cold, this walks through every rasterisation there is -- some 150ms of them.
        var clock = Stopwatch.StartNew();

        foreach (var family in Enum.GetValues<Anomaly>())
        {
            layer.Family = family;
            layer.Tick(WeatherCycle.CrossFade);
            layer.Show(Weather.Rain);
            layer.Show(Weather.Fog);
        }

        clock.Stop();

        // A frame is 16.7ms at 60fps. Showing all four families warmed should not cost even one.
        Assert.True(clock.Elapsed.TotalMilliseconds < 16,
            $"showing four warmed families took {clock.Elapsed.TotalMilliseconds:F1}ms, " +
            "which is a dropped frame -- something is still rasterising on the render path");
    });

    [Fact]
    public void Warming_twice_builds_nothing_the_second_time() => Sta.Run(() =>
    {
        WeatherBrushes.Warm(Anomaly.Chemical);

        var clock = Stopwatch.StartNew();
        for (var i = 0; i < 1000; i++) WeatherBrushes.Warm(Anomaly.Chemical);
        clock.Stop();

        Assert.True(clock.Elapsed.TotalMilliseconds < 5,
            $"a thousand warm calls took {clock.Elapsed.TotalMilliseconds:F1}ms");
    });

    [Fact]
    public void Warming_covers_every_family_the_census_can_pick() => Sta.Run(() =>
    {
        // The warm-up walks the enum. A family added to Anomaly without being warmed would go
        // back to rasterising inside a frame, which is exactly the defect this guards.
        foreach (var family in Enum.GetValues<Anomaly>())
        {
            WeatherBrushes.Warm(family);
            Assert.NotNull(WeatherBrushes.RainAt(family, 0, 4));
        }
    });
}
