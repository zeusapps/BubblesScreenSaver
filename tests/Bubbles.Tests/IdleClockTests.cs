using Bubbles.Session;

namespace Bubbles.Tests;

/// <summary>Hanging up must not be the same thing as walking away -- and a microphone blip
/// must not be the same thing as hanging up.</summary>
public sealed class IdleClockTests
{
    private const double Minute = 60;

    /// <summary>The controller samples every 400 ms, so anything derived from how long a
    /// hold-off lasted is accurate to one tick and no better. Asserting exact equality would
    /// be asserting the sampling rate, not the behaviour.</summary>
    private static void AssertSeconds(double expected, double actual) =>
        Assert.InRange(actual, expected - 0.5, expected + 0.5);

    /// <summary>Runs the clock the way the controller does, at 400 ms ticks, and hands back
    /// the last value it produced.</summary>
    private sealed class Ticker
    {
        private readonly IdleClock _clock = new();

        private const long TickMs = 400;

        // Counted in whole ticks rather than accumulated as seconds: adding 0.4 fifteen hundred
        // times drifts far enough to gain or lose a tick, which then shows up as a test failure
        // that has nothing to do with the clock.
        private long _now;
        private long _sinceInput;
        private double _last;

        public double Idle => _last;

        /// <param name="seconds">Wall-clock seconds to advance.</param>
        /// <param name="heldOff">Whether the hold-off is on throughout.</param>
        public Ticker Run(double seconds, bool heldOff = false)
        {
            for (var tick = 0; tick < (long)Math.Round(seconds * 1000 / TickMs); tick++)
            {
                _now += TickMs;
                _sinceInput += TickMs;
                _last = _clock.Elapsed(_sinceInput / 1000.0, heldOff, _now);
            }

            return this;
        }

        /// <summary>A keypress or mouse movement: the system idle timer goes back to zero.</summary>
        public Ticker Input()
        {
            _now += TickMs;
            _sinceInput = 0;
            _last = _clock.Elapsed(0, heldOff: false, _now);
            return this;
        }
    }

    [Fact]
    public void Without_a_hold_off_the_system_timer_is_used_as_is()
    {
        AssertSeconds(3 * Minute, new Ticker().Run(3 * Minute).Idle);
    }

    // ---- the bug: hanging up started the animation immediately -------------------------

    [Fact]
    public void A_long_call_does_not_leave_the_countdown_already_finished()
    {
        // Stop typing, join a call, sit on it for forty minutes, hang up.
        var idle = new Ticker().Run(40 * Minute, heldOff: true).Idle;

        AssertSeconds(0, idle);
    }

    [Fact]
    public void After_a_call_the_countdown_runs_from_the_hangup()
    {
        var ticker = new Ticker().Run(40 * Minute, heldOff: true);

        AssertSeconds(5 * Minute, ticker.Run(5 * Minute).Idle);
    }

    [Fact]
    public void The_full_delay_is_owed_again_after_a_call_rather_than_a_remainder()
    {
        const double threshold = 5 * Minute;
        var ticker = new Ticker().Run(40 * Minute, heldOff: true);

        Assert.True(ticker.Run(4 * Minute).Idle < threshold);
        Assert.True(ticker.Run(1 * Minute).Idle >= threshold);
    }

    // ---- and the other way: a blip must not hold it off forever -------------------------

    [Fact]
    public void A_momentary_hold_off_costs_only_the_moment_it_lasted()
    {
        // Half an hour idle, then something grabs the microphone for one 400 ms tick.
        var ticker = new Ticker().Run(30 * Minute).Run(0.4, heldOff: true);

        // Not reset to zero: a notification sound is not a call.
        Assert.True(ticker.Idle > 29 * Minute);
    }

    [Fact]
    public void Repeated_blips_do_not_stop_the_screensaver_from_ever_starting()
    {
        // A presence check that grabs the microphone for a moment every minute, for an hour.
        // Resetting the countdown on each one would mean it never elapses at all.
        var ticker = new Ticker();

        for (var i = 0; i < 60; i++) ticker.Run(Minute).Run(0.4, heldOff: true);

        Assert.True(ticker.Idle > 55 * Minute);
    }

    // ---- input always wins ---------------------------------------------------------------

    [Fact]
    public void A_keypress_takes_the_accumulated_hold_off_with_it()
    {
        var ticker = new Ticker().Run(40 * Minute, heldOff: true).Input();

        AssertSeconds(0, ticker.Idle);

        // ...and the clock then runs normally, with nothing owed from the call.
        AssertSeconds(2 * Minute, ticker.Run(2 * Minute).Idle);
    }

    [Fact]
    public void Typing_during_a_call_does_not_bank_idle_time_for_afterwards()
    {
        var ticker = new Ticker()
            .Run(20 * Minute, heldOff: true)
            .Input()                          // you type something mid-call
            .Run(10 * Minute, heldOff: true); // then go quiet again until the end

        AssertSeconds(0, ticker.Idle);
    }

    // ---- several calls --------------------------------------------------------------------

    [Fact]
    public void A_second_call_is_discounted_as_well_as_the_first()
    {
        var ticker = new Ticker()
            .Run(10 * Minute, heldOff: true)
            .Run(1 * Minute)
            .Run(20 * Minute, heldOff: true);

        AssertSeconds(Minute, ticker.Idle);
    }

    [Fact]
    public void Idle_never_reads_as_negative()
    {
        var ticker = new Ticker().Run(10 * Minute, heldOff: true);

        Assert.True(ticker.Idle >= 0);
    }
}
