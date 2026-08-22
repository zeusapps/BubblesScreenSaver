using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>The weather cycle: what shows, for how long, and how it hands over. Asserted without
/// a window or a clock, because every one of these is a property of the sequence rather than of
/// anything on screen -- and watching for a minute to find out whether a state ever repeats is
/// not a test.</summary>
public sealed class WeatherCycleTests
{
    private static WeatherCycle Cycle(int seed = 7) => new(new Random(seed));

    /// <summary>Runs the cycle forward, collecting each state as it becomes current.</summary>
    private static List<Weather> Sequence(WeatherCycle cycle, int changes)
    {
        var seen = new List<Weather> { cycle.Current };

        for (var guard = 0; seen.Count <= changes && guard < 2_000_000; guard++)
        {
            cycle.Tick(0.1);
            if (cycle.Current != seen[^1]) seen.Add(cycle.Current);
        }

        return seen;
    }

    [Fact]
    public void A_change_is_always_a_change()
    {
        // Rolling the weather that is already showing is a minute in which nothing happens.
        var seen = Sequence(Cycle(), changes: 60);

        for (var i = 1; i < seen.Count; i++)
            Assert.NotEqual(seen[i - 1], seen[i]);
    }

    [Fact]
    public void All_four_states_come_up()
    {
        var seen = Sequence(Cycle(), changes: 80);

        Assert.Contains(Weather.Clear, seen);
        Assert.Contains(Weather.Fog, seen);
        Assert.Contains(Weather.Rain, seen);
        Assert.Contains(Weather.Storm, seen);
    }

    [Fact]
    public void Storms_stay_rare_enough_to_be_events()
    {
        var seen = Sequence(Cycle(), changes: 400);
        var storms = seen.Count(s => s == Weather.Storm) / (double)seen.Count;

        // Weighted 15 of 100, and a roll never repeats the current state, so the share lands a
        // little above the raw weight. Well clear of the flat 25% a four-card deck would give.
        Assert.InRange(storms, 0.08, 0.22);
    }

    [Fact]
    public void Weather_holds_for_about_a_minute()
    {
        var cycle = Cycle();
        var dwells = new List<double>();

        for (var change = 0; change < 40; change++)
        {
            var was = cycle.Current;
            var held = 0.0;

            while (cycle.Current == was)
            {
                cycle.Tick(0.05);
                held += 0.05;
            }

            // The cross-fade of the previous change is spent before the dwell starts counting.
            dwells.Add(held);
            while (cycle.Outgoing is not null) cycle.Tick(0.05);
        }

        Assert.All(dwells, d => Assert.InRange(d, 45 - 0.1, 75 + WeatherCycle.CrossFade + 0.1));
        Assert.True(dwells.Distinct().Count() > 1, "every dwell was the same length");
    }

    [Fact]
    public void A_change_cross_fades_rather_than_cutting()
    {
        var cycle = Cycle();
        var was = cycle.Current;

        while (cycle.Current == was) cycle.Tick(0.05);

        Assert.Equal(was, cycle.Outgoing);
        Assert.True(cycle.Progress < 0.2, $"the incoming state arrived at {cycle.Progress:N2}, not from nothing");
    }

    [Fact]
    public void A_transition_ends_with_exactly_one_state_live()
    {
        var cycle = Cycle();
        var was = cycle.Current;

        while (cycle.Current == was) cycle.Tick(0.05);
        while (cycle.Outgoing is not null) cycle.Tick(0.05);

        Assert.Null(cycle.Outgoing);
        Assert.Equal(1, cycle.Progress);
    }

    [Fact]
    public void Progress_runs_from_nothing_to_everything_and_never_leaves_it()
    {
        var cycle = Cycle();
        var was = cycle.Current;

        while (cycle.Current == was) cycle.Tick(0.05);

        var last = -1.0;

        while (cycle.Outgoing is not null)
        {
            Assert.InRange(cycle.Progress, 0, 1);
            Assert.True(cycle.Progress >= last, "the cross-fade went backwards");
            last = cycle.Progress;
            cycle.Tick(0.05);
        }
    }

    [Fact]
    public void Settled_weather_reports_itself_as_fully_arrived()
    {
        // So a caller can render Current at Progress and Outgoing at 1 - Progress with no
        // special case for "not transitioning".
        var cycle = Cycle();

        Assert.Null(cycle.Outgoing);
        Assert.Equal(1, cycle.Progress);
    }

    [Fact]
    public void An_emission_holds_the_weather_where_it_is()
    {
        // The burning sky is the show. A weather change underneath it is a second one.
        var cycle = Cycle();
        while (cycle.Outgoing is not null) cycle.Tick(0.05);

        cycle.Suspended = true;
        var held = cycle.Current;

        for (var i = 0; i < 20_000; i++) cycle.Tick(0.05);   // a thousand seconds

        Assert.Equal(held, cycle.Current);
        Assert.Null(cycle.Outgoing);
    }

    [Fact]
    public void A_transition_already_running_when_an_emission_starts_is_allowed_to_finish()
    {
        // Freezing it half way would leave two states live for the whole Emission.
        var cycle = Cycle();
        var was = cycle.Current;

        while (cycle.Current == was) cycle.Tick(0.05);

        cycle.Suspended = true;
        for (var i = 0; i < 400; i++) cycle.Tick(0.05);

        Assert.Null(cycle.Outgoing);
        Assert.Equal(1, cycle.Progress);
    }

    [Fact]
    public void The_cycle_picks_up_again_when_the_emission_ends()
    {
        var cycle = Cycle();
        cycle.Suspended = true;
        for (var i = 0; i < 2_000; i++) cycle.Tick(0.05);

        var held = cycle.Current;
        cycle.Suspended = false;

        var guard = 0;
        while (cycle.Current == held && guard++ < 100_000) cycle.Tick(0.05);

        Assert.NotEqual(held, cycle.Current);
    }

    [Fact]
    public void Switching_weather_back_on_starts_from_a_settled_state()
    {
        // Not from whatever half-finished transition it was switched off during.
        var cycle = Cycle();
        var was = cycle.Current;
        while (cycle.Current == was) cycle.Tick(0.05);

        cycle.Restart();

        Assert.Null(cycle.Outgoing);
        Assert.Equal(1, cycle.Progress);
    }

    [Fact]
    public void A_zero_or_negative_delta_does_nothing()
    {
        // A stalled frame or a clock that went backwards must not advance the weather.
        var cycle = Cycle();
        var was = cycle.Current;

        cycle.Tick(0);
        cycle.Tick(-5);

        Assert.Equal(was, cycle.Current);
        Assert.Null(cycle.Outgoing);
    }

    [Fact]
    public void The_sequence_is_reproducible_from_a_seed()
    {
        Assert.Equal(Sequence(Cycle(42), 30), Sequence(Cycle(42), 30));
    }
}
