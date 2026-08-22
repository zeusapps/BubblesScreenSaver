using System.Windows;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>Lightning used to be one storm spread across the union of the screens, so each
/// monitor saw a fraction of it and every bolt was scaled by the tallest panel rather than the
/// one it landed on. These assert the per-screen behaviour that replaced it.</summary>
public sealed class LightningRegionsTests
{
    [Fact]
    public void Every_screen_gets_a_full_storm()
    {
        // Not a share of one. Three monitors used to mean a third of a storm each.
        //
        var first = LightningLayer.BuildSchedule(0);
        var second = LightningLayer.BuildSchedule(1);
        var third = LightningLayer.BuildSchedule(2);

        Assert.Equal(first.Length, second.Length);
        Assert.Equal(first.Length, third.Length);
        Assert.NotEmpty(first);
    }

    [Fact]
    public void Screens_do_not_flash_in_lockstep()
    {
        // Identical schedules would strobe the whole desk at once, which reads as one sky
        // failing rather than as weather.
        var first = LightningLayer.BuildSchedule(0);
        var second = LightningLayer.BuildSchedule(1);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_schedule_starts_sparse_and_crowds_as_the_pressure_builds()
    {
        var schedule = LightningLayer.BuildSchedule(0);

        var firstGap = schedule[1] - schedule[0];
        var lastGap = schedule[^1] - schedule[^2];

        Assert.True(lastGap < firstGap, $"gaps did not close: {firstGap:N2}s then {lastGap:N2}s");
    }

    [Fact]
    public void A_schedule_is_the_same_every_run()
    {
        // Derived from time and a hash, so nothing is stored and a run is reproducible.
        Assert.Equal(LightningLayer.BuildSchedule(2), LightningLayer.BuildSchedule(2));
    }

    [Fact]
    public void With_no_regions_there_is_nothing_to_show() => Sta.Run(() =>
    {
        // No layout yet and no size either: the fallback has nothing to fall back to.
        var layer = new LightningLayer();

        Assert.False(layer.HasStrike(1.0));
    });

    [Fact]
    public void One_region_strikes_when_its_schedule_says_so() => Sta.Run(() =>
    {
        var layer = new LightningLayer { Regions = new[] { new Rect(0, 0, 1920, 1080) } };
        var schedule = LightningLayer.BuildSchedule(0);

        Assert.True(layer.HasStrike(schedule[0]));
        Assert.False(layer.HasStrike(schedule[0] - 0.2));
    });

    [Fact]
    public void A_second_screen_adds_its_own_strikes_without_taking_any_away() => Sta.Run(() =>
    {
        var one = new Rect(0, 0, 1920, 1080);
        var two = new Rect(1920, 0, 1920, 1080);

        var single = new LightningLayer { Regions = new[] { one } };
        var paired = new LightningLayer { Regions = new[] { one, two } };

        var extra = 0;

        for (var t = 0.0; t < 14; t += 0.01)
        {
            if (single.HasStrike(t)) Assert.True(paired.HasStrike(t), $"lost a strike at t={t:N2}");
            else if (paired.HasStrike(t)) extra++;
        }

        Assert.True(extra > 0, "the second screen contributed no strikes of its own");
    });

    [Fact]
    public void A_layout_that_changes_nothing_leaves_the_schedules_alone() => Sta.Run(() =>
    {
        var regions = new[] { new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 2560, 1440) };
        var layer = new LightningLayer { Regions = regions };

        var before = Sample(layer);
        layer.Regions = new[] { regions[0], regions[1] };

        Assert.Equal(before, Sample(layer));
    });

    private static bool[] Sample(LightningLayer layer)
    {
        var samples = new bool[1400];
        for (var i = 0; i < samples.Length; i++) samples[i] = layer.HasStrike(i * 0.01);
        return samples;
    }


}
