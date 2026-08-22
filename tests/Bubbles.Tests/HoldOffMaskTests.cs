using Bubbles.Session;

namespace Bubbles.Tests;

/// <summary>Watching and listening are not the same thing, and one veto could not express both.
///
/// A film must hold everything off. An album must hold the artifacts off and still let the
/// screen reach black, because three hours of music over a static desktop is exactly the
/// burn-in this application exists to prevent.</summary>
public sealed class HoldOffMaskTests
{
    [Fact]
    public void NothingHeldOffPermitsEverything()
    {
        Assert.False(HoldOff.None.Artifacts);
        Assert.False(HoldOff.None.Blackout);
        Assert.False(HoldOff.None.Any);
        Assert.False(HoldOff.None.Total);
        Assert.Null(HoldOff.None.Reason);
    }

    [Fact]
    public void SomebodyPresentSuppressesBothStages()
    {
        var held = HoldOff.Everything("microphone in use by Teams");

        Assert.True(held.Artifacts);
        Assert.True(held.Blackout);
        Assert.True(held.Total);
        Assert.Equal("microphone in use by Teams", held.Reason);
    }

    [Fact]
    public void MusicSuppressesTheArtifactsAndPermitsBlack()
    {
        var held = HoldOff.ArtifactsOnly("music is playing in Spotify");

        Assert.True(held.Artifacts);
        Assert.False(held.Blackout);
        Assert.True(held.Any);
        Assert.False(held.Total);
    }

    /// <summary>The subtlest consequence of a partial hold-off. Only a total one may stop the
    /// countdown: discount time while blackout is still permitted and the clock freezes short of
    /// BlackoutSeconds, so the black screen the reason allowed never arrives.</summary>
    [Fact]
    public void OnlyATotalHoldOffStopsTheCountdown()
    {
        Assert.True(HoldOff.Everything("on a call").Total);
        Assert.False(HoldOff.ArtifactsOnly("music is playing").Total);
        Assert.False(HoldOff.None.Total);
    }

    // ---- composition ---------------------------------------------------------------------

    [Fact]
    public void AStrictReasonBeatsAPermissiveOne()
    {
        var music = HoldOff.ArtifactsOnly("music is playing");
        var call = HoldOff.Everything("microphone in use by Teams");

        var combined = music.And(call);

        Assert.True(combined.Artifacts);
        Assert.True(combined.Blackout);
        Assert.Equal("microphone in use by Teams", combined.Reason);
    }

    [Fact]
    public void CombiningIsTheSameEitherWayRound()
    {
        var music = HoldOff.ArtifactsOnly("music is playing");
        var call = HoldOff.Everything("microphone in use by Teams");

        var forwards = music.And(call);
        var backwards = call.And(music);

        Assert.Equal(forwards.Artifacts, backwards.Artifacts);
        Assert.Equal(forwards.Blackout, backwards.Blackout);
        Assert.Equal("microphone in use by Teams", forwards.Reason);
        Assert.Equal("microphone in use by Teams", backwards.Reason);
    }

    [Fact]
    public void CombiningWithNothingChangesNothing()
    {
        var music = HoldOff.ArtifactsOnly("music is playing");

        Assert.Equal(music, music.And(HoldOff.None));
        Assert.Equal(music, HoldOff.None.And(music));
    }

    [Fact]
    public void TwoPermissiveReasonsStayPermissive()
    {
        var combined = HoldOff.ArtifactsOnly("music is playing")
            .And(HoldOff.ArtifactsOnly("a podcast is playing"));

        Assert.True(combined.Artifacts);
        Assert.False(combined.Blackout);
        Assert.False(combined.Total);
    }

    // ---- what the controller does with the mask -------------------------------------------

    /// <summary>The real resolution out of IdleController, not a copy of it: a duplicated
    /// expression here would keep passing while the controller drifted away from it.</summary>
    private static IdleController.Stage Resolve(bool wantsBubbles, bool wantsBlackout, HoldOff held) =>
        IdleController.Resolve(wantsBubbles, wantsBlackout, held);

    [Fact]
    public void NothingHeldFollowsTheIdleTimer()
    {
        Assert.Equal(IdleController.Stage.Active, Resolve(false, false, HoldOff.None));
        Assert.Equal(IdleController.Stage.Bubbles, Resolve(true, false, HoldOff.None));
        Assert.Equal(IdleController.Stage.Blackout, Resolve(true, true, HoldOff.None));
    }

    [Fact]
    public void APresentUserGetsNothingAtAnyThreshold()
    {
        var held = HoldOff.Everything("on a call");

        Assert.Equal(IdleController.Stage.Active, Resolve(false, false, held));
        Assert.Equal(IdleController.Stage.Active, Resolve(true, false, held));
        Assert.Equal(IdleController.Stage.Active, Resolve(true, true, held));
    }

    /// <summary>The album, start to finish. Past IdleSeconds nothing is drawn, and past
    /// BlackoutSeconds the screen goes black without an artifact ever appearing.</summary>
    [Fact]
    public void MusicSkipsTheArtifactsAndStillReachesBlack()
    {
        var held = HoldOff.ArtifactsOnly("music is playing in Spotify");

        Assert.Equal(IdleController.Stage.Active, Resolve(false, false, held));
        Assert.Equal(IdleController.Stage.Active, Resolve(true, false, held));
        Assert.Equal(IdleController.Stage.Blackout, Resolve(true, true, held));
    }

    /// <summary>Not a ceiling. A ceiling on an ordered chain cannot express this, because
    /// Blackout is the further stage and it is the one that is allowed.</summary>
    [Fact]
    public void ThePermittedStageIsNotAnOrderedBound()
    {
        var held = HoldOff.ArtifactsOnly("music is playing");

        Assert.Equal(IdleController.Stage.Active, Resolve(true, false, held));
        Assert.Equal(IdleController.Stage.Blackout, Resolve(true, true, held));
    }
}
