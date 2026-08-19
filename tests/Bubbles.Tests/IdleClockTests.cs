using Bubbles.Session;

namespace Bubbles.Tests;

/// <summary>Hanging up must not be the same thing as walking away.</summary>
public sealed class IdleClockTests
{
    private const double Minute = 60;

    [Fact]
    public void Without_a_hold_off_the_system_timer_is_used_as_is()
    {
        var clock = new IdleClock();

        Assert.Equal(90, clock.Elapsed(90, heldOff: false, now: 0));
        Assert.Equal(120, clock.Elapsed(120, heldOff: false, now: 30_000));
    }

    [Fact]
    public void A_long_call_does_not_leave_the_countdown_already_finished()
    {
        var clock = new IdleClock();

        // Forty minutes on a call, touching nothing.
        clock.Elapsed(5 * Minute, heldOff: true, now: 0);
        clock.Elapsed(40 * Minute, heldOff: true, now: 40 * 60_000);

        // Hang up. The input timer says forty-five minutes; you are sitting right there.
        Assert.Equal(0, clock.Elapsed(45 * Minute, heldOff: false, now: 40 * 60_000));
    }

    [Fact]
    public void After_a_call_the_countdown_runs_from_the_hangup()
    {
        var clock = new IdleClock();

        clock.Elapsed(40 * Minute, heldOff: true, now: 40 * 60_000);
        clock.Elapsed(40 * Minute, heldOff: false, now: 40 * 60_000);

        // Five more minutes of not touching anything: five minutes idle, not forty-five.
        Assert.Equal(5 * Minute, clock.Elapsed(45 * Minute, heldOff: false, now: 45 * 60_000));
    }

    [Fact]
    public void The_full_delay_is_owed_again_after_a_call_rather_than_a_remainder()
    {
        var clock = new IdleClock();
        const double idleThreshold = 5 * Minute;

        clock.Elapsed(40 * Minute, heldOff: true, now: 40 * 60_000);
        clock.Elapsed(40 * Minute, heldOff: false, now: 40 * 60_000);

        Assert.True(clock.Elapsed(44 * Minute, heldOff: false, now: 44 * 60_000) < idleThreshold);
        Assert.True(clock.Elapsed(45 * Minute, heldOff: false, now: 45 * 60_000) >= idleThreshold);
    }

    [Fact]
    public void Real_input_after_a_call_takes_over_from_the_hangup()
    {
        var clock = new IdleClock();

        clock.Elapsed(40 * Minute, heldOff: true, now: 40 * 60_000);
        clock.Elapsed(40 * Minute, heldOff: false, now: 40 * 60_000);

        // Two minutes after hanging up you touch the mouse, so the system timer resets and is
        // now the smaller of the two. It must win, or a keypress would not wake the machine.
        Assert.Equal(0, clock.Elapsed(0, heldOff: false, now: 42 * 60_000));
        Assert.Equal(10, clock.Elapsed(10, heldOff: false, now: 42 * 60_000 + 10_000));
    }

    [Fact]
    public void Nothing_counts_as_idle_while_the_hold_off_is_on()
    {
        var clock = new IdleClock();

        Assert.Equal(0, clock.Elapsed(40 * Minute, heldOff: true, now: 40 * 60_000));
    }

    [Fact]
    public void A_second_call_restarts_the_countdown_again()
    {
        var clock = new IdleClock();

        clock.Elapsed(10 * Minute, heldOff: true, now: 10 * 60_000);
        clock.Elapsed(10 * Minute, heldOff: false, now: 10 * 60_000);
        Assert.Equal(Minute, clock.Elapsed(11 * Minute, heldOff: false, now: 11 * 60_000));

        clock.Elapsed(11 * Minute, heldOff: true, now: 11 * 60_000);
        clock.Elapsed(30 * Minute, heldOff: true, now: 30 * 60_000);

        Assert.Equal(0, clock.Elapsed(30 * Minute, heldOff: false, now: 30 * 60_000));
    }

    [Fact]
    public void A_hold_off_that_never_fires_leaves_the_timer_untouched()
    {
        var clock = new IdleClock();

        // The common case: no call all day. Nothing should be special about it.
        Assert.Equal(Minute, clock.Elapsed(Minute, heldOff: false, now: 60_000));
        Assert.Equal(2 * Minute, clock.Elapsed(2 * Minute, heldOff: false, now: 120_000));
        Assert.Equal(3 * Minute, clock.Elapsed(3 * Minute, heldOff: false, now: 180_000));
    }

    [Fact]
    public void A_hold_off_lasting_a_single_tick_still_restarts_the_countdown()
    {
        var clock = new IdleClock();

        // The microphone flickers in use for one 400 ms tick -- a notification sound, say.
        clock.Elapsed(30 * Minute, heldOff: false, now: 30 * 60_000);
        clock.Elapsed(30 * Minute, heldOff: true, now: 30 * 60_000 + 400);

        Assert.Equal(0, clock.Elapsed(30 * Minute, heldOff: false, now: 30 * 60_000 + 800));
    }
}
