using System.Windows;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>Lightning used to be one storm spread across the union of the screens, so each
/// monitor saw a fraction of it and every bolt was scaled by the tallest panel rather than the
/// one it landed on. These assert the per-screen behaviour that replaced it.</summary>
public sealed class LightningRegionsTests
{
    /// <summary>When the Emission reaches black. Matches OverlayWindow's DarknessAt.</summary>
    private const double Darkness = 12.5;

    [Fact]
    public void Every_screen_gets_a_full_storm()
    {
        // Not a share of one. Three monitors used to mean a third of a storm each.
        //
        // Comparable rather than identical: each screen jitters its own gaps and the schedule
        // runs until the sky goes dark, so the counts land near each other without matching.
        var counts = new[] { 0, 1, 2 }
            .Select(r => LightningLayer.BuildSchedule(r, Darkness).Length)
            .ToArray();

        Assert.All(counts, c => Assert.True(c > 0));
        Assert.True(counts.Max() - counts.Min() <= 5,
            $"screens got wildly different storms: {string.Join(", ", counts)}");
    }

    [Fact]
    public void Screens_do_not_flash_in_lockstep()
    {
        // Identical schedules would strobe the whole desk at once, which reads as one sky
        // failing rather than as weather.
        var first = LightningLayer.BuildSchedule(0, Darkness);
        var second = LightningLayer.BuildSchedule(1, Darkness);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void A_schedule_starts_sparse_and_crowds_as_the_pressure_builds()
    {
        var schedule = LightningLayer.BuildSchedule(0, Darkness);

        var firstGap = schedule[1] - schedule[0];
        var lastGap = schedule[^1] - schedule[^2];

        Assert.True(lastGap < firstGap, $"gaps did not close: {firstGap:N2}s then {lastGap:N2}s");
    }

    [Fact]
    public void A_schedule_is_the_same_every_run()
    {
        // Derived from time and a hash, so nothing is stored and a run is reproducible.
        Assert.Equal(LightningLayer.BuildSchedule(2, Darkness), LightningLayer.BuildSchedule(2, Darkness));
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
        var schedule = LightningLayer.BuildSchedule(0, Darkness);

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

    [Fact]
    public void An_emission_carries_half_again_as_much_lightning_as_it_used_to()
    {
        // 18 strikes used to land before the screen went black -- not the 22 that were
        // scheduled, because the last four were scheduled into the darkness. The increase had
        // to come from the gaps: raising the count only adds more strikes nobody sees.
        var schedule = LightningLayer.BuildSchedule(0, Darkness);

        Assert.Equal(27, schedule.Length);
    }

    [Fact]
    public void Every_screen_gets_more_lightning_than_the_old_desktop_did()
    {
        // The gaps are jittered per screen, so the exact count differs between them. Measured
        // across 64 screens the counts run 25 to 30 and average 26.8 -- about half again as
        // much on average, and never worse than a 39% rise.
        var counts = Enumerable.Range(0, 64)
            .Select(r => LightningLayer.BuildSchedule(r, Darkness).Length)
            .ToArray();

        Assert.All(counts, c => Assert.InRange(c, 25, 31));
        Assert.All(counts, c => Assert.True(c > 18, $"a screen got {c}, no better than before"));
        Assert.True(counts.Average() > 26, $"average {counts.Average():N1} is short of half again");
    }

    [Fact]
    public void No_strike_is_scheduled_where_nobody_can_see_it()
    {
        // The whole reason the schedule terminates rather than counting: four of the old 22
        // started after the screen had already reached black.
        foreach (var region in new[] { 0, 1, 2 })
            Assert.All(LightningLayer.BuildSchedule(region, Darkness), t => Assert.True(t <= Darkness));
    }

    [Fact]
    public void Retuning_the_timeline_retunes_the_schedule_with_it()
    {
        // No constant to re-derive by hand when the Emission's length changes.
        var shorter = LightningLayer.BuildSchedule(0, 6.0);
        var longer = LightningLayer.BuildSchedule(0, 20.0);

        Assert.True(shorter.Length < 27);
        Assert.True(longer.Length > 27);
        Assert.All(shorter, t => Assert.True(t <= 6.0));
        Assert.All(longer, t => Assert.True(t <= 20.0));
    }

    [Fact]
    public void A_schedule_that_would_never_end_is_still_bounded()
    {
        // Guards a future retune of the gap curve from hanging the render thread.
        Assert.True(LightningLayer.BuildSchedule(0, 1_000_000).Length <= 200);
    }
}
