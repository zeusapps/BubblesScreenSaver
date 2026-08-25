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

        /// <param name="alreadyIdle">How long the machine had been sitting before this clock
        /// existed. The system counter outlives the process, so this is what a run started
        /// after an update, a crash, or a login onto a machine left at the lock screen is
        /// handed on its very first tick.</param>
        public Ticker(double alreadyIdle = 0) => _sinceInput = (long)(alreadyIdle * 1000);

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

    // ---- a partial hold-off must not stop the clock ---------------------------------------

    /// <summary>Music holds the artifacts off but deliberately lets the screen reach black. The
    /// controller therefore passes <c>held.Total</c> here rather than "anything is held off" --
    /// pass true for a partial hold-off and the countdown freezes short of BlackoutSeconds, so
    /// the blackout that reason allowed never arrives and an album keeps an OLED lit for hours.
    /// Which is the failure the whole arrangement exists to fix.</summary>
    [Fact]
    public void A_partial_hold_off_lets_the_countdown_reach_blackout()
    {
        const double blackoutAfter = 10 * Minute;

        // Music playing from the moment the user walks away: held.Total is false throughout.
        var idle = new Ticker().Run(12 * Minute, heldOff: false).Idle;

        Assert.True(idle >= blackoutAfter,
            $"the countdown reached only {idle:N0}s and would never black out");
    }

    [Fact]
    public void A_total_hold_off_still_stops_it()
    {
        const double blackoutAfter = 10 * Minute;

        var idle = new Ticker().Run(12 * Minute, heldOff: true).Idle;

        Assert.True(idle < blackoutAfter);
    }

    // ---- a run cannot have been away before it started ------------------------------------

    /// <summary>The bug this bound exists for. Restarting the application on a machine that had
    /// been sitting for two and a half minutes gave the artifacts stage 1.2 seconds of its
    /// configured two minutes, and the blackout swallowed it -- with nothing held off at all.</summary>
    [Fact]
    public void A_run_that_starts_into_an_idle_machine_begins_its_countdown_at_zero()
    {
        AssertSeconds(0, new Ticker(alreadyIdle: 40 * Minute).Run(0.4).Idle);
    }

    [Fact]
    public void The_countdown_then_runs_from_the_start_of_the_run()
    {
        var ticker = new Ticker(alreadyIdle: 40 * Minute);

        AssertSeconds(2 * Minute, ticker.Run(2 * Minute).Idle);
    }

    /// <summary>The stage the fault actually cost. With a two-minute artifacts stage and a
    /// blackout at two and a half, a run starting one second short of the blackout must still
    /// owe the whole of both.</summary>
    [Fact]
    public void The_artifacts_stage_is_not_skipped_by_a_restart()
    {
        const double artifactsAfter = 2 * Minute;
        const double blackoutAfter = 2.5 * Minute;

        var ticker = new Ticker(alreadyIdle: blackoutAfter - 1);

        Assert.True(ticker.Run(1).Idle < artifactsAfter,
            "the run began past its own artifacts threshold");

        AssertSeconds(artifactsAfter, ticker.Run(artifactsAfter - 1).Idle);
        Assert.True(ticker.Idle < blackoutAfter, "the blackout arrived with the artifacts");

        Assert.True(ticker.Run(Minute).Idle >= blackoutAfter);
    }

    /// <summary>The bound is temporary by construction. Once the run has been going for longer
    /// than the machine has been quiet, it stops binding and never binds again.</summary>
    [Fact]
    public void Once_the_run_outlives_the_system_timer_the_bound_stops_applying()
    {
        // Started into an idle machine; somebody came back, touched the mouse, and left again.
        var ticker = new Ticker(alreadyIdle: 40 * Minute).Run(Minute).Input().Run(3 * Minute);

        AssertSeconds(3 * Minute, ticker.Idle);
    }

    /// <summary>It is a ceiling, not a floor. A run far older than the last keypress reports the
    /// keypress, which is what would break if the two were ever combined the other way round.</summary>
    [Fact]
    public void The_bound_never_reports_more_than_the_time_since_the_last_input()
    {
        var ticker = new Ticker().Run(10 * Minute).Input().Run(Minute);

        AssertSeconds(Minute, ticker.Idle);
    }

    /// <summary>The two corrections compose. Running past the point where the bound binds, the
    /// hold-off subtraction has to behave exactly as it does without it -- a call discounted in
    /// full, and the whole delay owed again afterwards.</summary>
    [Fact]
    public void The_hold_off_discount_is_unchanged_once_the_bound_is_out_of_the_way()
    {
        var ticker = new Ticker(alreadyIdle: 40 * Minute).Run(Minute).Input();

        ticker.Run(40 * Minute, heldOff: true);
        AssertSeconds(0, ticker.Idle);

        AssertSeconds(5 * Minute, ticker.Run(5 * Minute).Idle);
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
