using Bubbles.Session;

namespace Bubbles.Tests;

/// <summary>The application can hold itself off, for reasons the machine is not being asked
/// about. There is one such reason today: an open settings window.
///
/// Reading a settings window without touching the keyboard is exactly what the idle timer
/// misreads as absence, and drawing the screensaver over the window you are configuring it in
/// would be the most conspicuous possible instance of that failure.</summary>
public sealed class AppHoldTests
{
    private static readonly HoldOff WindowOpen = HoldOff.Everything("the settings window is open");

    [Fact]
    public void An_open_window_suppresses_both_stages()
    {
        var held = HoldOff.None.And(WindowOpen);

        Assert.True(held.Artifacts);
        Assert.True(held.Blackout);
        Assert.Equal("the settings window is open", held.Reason);
    }

    [Fact]
    public void Neither_stage_is_reached_while_it_holds()
    {
        var held = HoldOff.None.And(WindowOpen);

        // Both delays have passed, and neither stage arrives.
        Assert.Equal(IdleController.Stage.Active,
                     IdleController.Resolve(wantsBubbles: true, wantsBlackout: true, held));
    }

    [Fact]
    public void It_composes_with_a_reason_from_the_machine()
    {
        // Music alone would let the screen reach black. The window must not.
        var music = HoldOff.ArtifactsOnly("music is playing in Spotify");
        var held = music.And(WindowOpen);

        Assert.True(held.Artifacts);
        Assert.True(held.Blackout);
        Assert.Equal(IdleController.Stage.Active,
                     IdleController.Resolve(wantsBubbles: true, wantsBlackout: true, held));
    }

    [Fact]
    public void The_stricter_reason_is_the_one_reported()
    {
        // The window suppresses more than music does, so it is what explains the black screen
        // the user is not seeing.
        var held = HoldOff.ArtifactsOnly("music is playing in Spotify").And(WindowOpen);

        Assert.Equal("the settings window is open", held.Reason);
    }

    [Fact]
    public void Closing_the_window_lets_the_idle_timer_govern_again()
    {
        var held = HoldOff.None.And(HoldOff.None);

        Assert.False(held.Any);
        Assert.Equal(IdleController.Stage.Blackout,
                     IdleController.Resolve(wantsBubbles: true, wantsBlackout: true, held));
    }

    [Fact]
    public void A_deliberate_request_is_not_idleness()
    {
        // IdleController discards all hold-off when a force is armed, so an explicit Start now
        // from the tray reaches the artifacts even with the window open. Resolve sees the
        // hold-off the forced path substitutes, which is none.
        Assert.Equal(IdleController.Stage.Bubbles,
                     IdleController.Resolve(wantsBubbles: true, wantsBlackout: false, HoldOff.None));
    }
}
