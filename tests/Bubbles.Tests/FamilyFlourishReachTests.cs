using System.Windows;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>The two families that reach past the flourish.
///
/// Both are meant to be a parameter handed to machinery that already exists, not new drawing --
/// Electrical advances the storm's own clock onto a strike the schedule was always going to
/// produce, and Thermic moves the fog damping that an Emission already moves. These assert the
/// machinery is capable of what is asked of it.</summary>
public sealed class FamilyFlourishReachTests
{
    private static LightningLayer Storm()
    {
        var layer = new LightningLayer { Ambient = true };
        layer.Regions = [new Rect(0, 0, 1920, 1080)];
        return layer;
    }

    [Fact]
    public void A_strike_can_always_be_brought_forward() => Sta.Run(() =>
    {
        // Whatever moment a pickup lands on, there is a next strike to advance onto. The
        // ambient schedule wraps, so this holds at the end of the window as well as inside it.
        var storm = Storm();

        for (var t = 0.0; t < 120; t += 0.37)
        {
            var wait = storm.NextStrikeIn(t);

            Assert.True(wait >= 0, $"the next strike was {wait}s away, which is in the past");
            Assert.True(wait < 48, $"nothing was scheduled within the storm's own window at {t}s");
        }
    });

    [Fact]
    public void Advancing_onto_it_produces_the_strike() => Sta.Run(() =>
    {
        // The point of asking rather than inventing: the bolt that arrives is one the schedule
        // already held, drawn by the layer that already draws it.
        var storm = Storm();

        for (var t = 0.0; t < 90; t += 1.3)
        {
            var advanced = t + storm.NextStrikeIn(t);

            Assert.True(storm.HasStrike(advanced + 0.01),
                $"advancing from {t}s landed on no strike at all");
        }
    });

    [Fact]
    public void Nothing_is_added_to_the_schedule() => Sta.Run(() =>
    {
        // Asking must not be a way of getting extra lightning. The schedule for a region is a
        // pure function of the region, and asking about it leaves it alone.
        var before = LightningLayer.BuildAmbientSchedule(0);

        var storm = Storm();
        for (var t = 0.0; t < 60; t += 0.7) storm.NextStrikeIn(t);

        Assert.Equal(before, LightningLayer.BuildAmbientSchedule(0));
    });

    [Fact]
    public void Fog_damping_is_a_knob_that_already_existed() => Sta.Run(() =>
    {
        // Thermic thins the fog through the same property an Emission uses to pull it out of
        // the way. Nothing new is drawn; a sheet moves down the ladder it was already on.
        var layer = new WeatherLayer { Regions = [new Rect(0, 0, 1920, 1080)] };

        layer.FogDamping = 1;
        layer.Show(Weather.Fog);

        var full = Fills(layer);

        layer.FogDamping = 0.55;
        layer.Show(Weather.Fog);

        var thinned = Fills(layer);

        Assert.NotEmpty(thinned);
        Assert.True(thinned.Sum() < full.Sum(), "damping the fog did not thin it");

        layer.FogDamping = 1;
        layer.Show(Weather.Fog);

        Assert.Equal(full, Fills(layer));

        static List<double> Fills(WeatherLayer l) =>
            l.Children
                .OfType<System.Windows.Shapes.Rectangle>()
                .Where(r => r.Fill is not null && r.Visibility == Visibility.Visible)
                .Select(r => r.Fill!.Opacity)
                .ToList();
    });
}
