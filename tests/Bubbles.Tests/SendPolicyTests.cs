using Bubbles.Keyboard;
using Bubbles.Overlay;

namespace Bubbles.Tests;

/// <summary>How often the keyboard is written to, and which frames are exempt from the
/// rationing.
///
/// Separate from the layer because the layer's worker keeps only the latest colour -- which is
/// right, since a stale colour is worth nothing to a keyboard -- and that makes counting sends
/// through it a measurement of thread scheduling rather than of the rule. The rule is here,
/// where a series of frames goes in and a count comes out.</summary>
public class SendPolicyTests
{
    /// <summary>An Emission at the default frame rate, as the render loop would deliver it.</summary>
    private static List<SendDecision> Run(double from, double to, Func<double, bool> striking)
    {
        var policy = new SendPolicy();
        var sent = new List<SendDecision>();

        for (var t = from; t <= to; t += 1.0 / 30)
        {
            var decision = policy.Decide(t, striking(t));
            if (decision.Send) sent.Add(decision);
        }

        return sent;
    }

    [Fact]
    public void TheBuildupIsNotSentEveryFrame()
    {
        var frames = (int)(EmissionTimeline.BuildupEnds * 30);
        var sent = Run(0, EmissionTimeline.BuildupEnds, _ => false);

        Assert.True(sent.Count < frames / 3,
                    $"sent {sent.Count} of {frames} frames; the point was to send far fewer");
        Assert.True(sent.Count > 5, $"sent only {sent.Count} colours, which is not a ramp");
    }

    [Fact]
    public void AWholeEmissionCostsATrivialNumberOfPackets()
    {
        var frames = (int)(EmissionTimeline.DarknessAt * 30);
        var sent = Run(0, EmissionTimeline.DarknessAt, _ => false);

        // The floor's own budget over twelve and a half seconds is about 104. Landing near
        // that would mean the exemptions had quietly stopped being exemptions -- which is what
        // happened when the flare's whole one-second decay was treated as urgent.
        Assert.True(sent.Count < 90, $"a twelve-second Emission wanted {sent.Count} packets");
        Assert.True(sent.Count < frames / 4, $"sent {sent.Count} of {frames} frames");
    }

    [Fact]
    public void StillFramesAreNotSentAtAll()
    {
        // The very start of the buildup, where the eased ramp barely moves.
        var sent = Run(0, 0.5, _ => false);

        Assert.Single(sent);
    }

    [Fact]
    public void TheFirstFrameAlwaysGoesOut()
    {
        var policy = new SendPolicy();

        Assert.True(policy.Decide(0, striking: false).Send);
    }

    [Fact]
    public void AStrikeIsSentTheMomentItFires()
    {
        var policy = new SendPolicy();

        policy.Decide(1.0, striking: false);

        // Inside the floor, which would have swallowed an ordinary colour arriving here.
        var decision = policy.Decide(1.03, striking: true);

        Assert.True(decision.Send, "the strike was rationed away");
        Assert.True(decision.Urgent, "the strike was droppable");
        Assert.True(Brightness(decision.Colour) > 600, "the strike barely brightened the keys");
    }

    /// <summary>The bug the first hardware run found.
    ///
    /// `HasStrike` is true for every frame a bolt is drawn, and the dense part of a storm keeps
    /// several overlapping. Treating that as "show white" left the keyboard solid white for the
    /// whole storm, while the screen was showing thin bright lines over a red sky.</summary>
    [Fact]
    public void ABoltHeldOnScreenDoesNotHoldTheKeysWhite()
    {
        var policy = new SendPolicy();
        var last = default(SendDecision);

        // One bolt, on screen for a second and a half without interruption.
        for (var t = 1.0; t < 2.5; t += 1.0 / 30)
        {
            var decision = policy.Decide(t, striking: true);
            if (decision.Send) last = decision;
        }

        Assert.True(last.Colour.R > last.Colour.G,
                    $"the keys were left washed out at {last.Colour}");
        Assert.True(Brightness(last.Colour) < 400,
                    $"the keys were still glaring at {last.Colour}");
    }

    [Fact]
    public void EachBoltFlashesAgain()
    {
        var policy = new SendPolicy();
        var flashes = 0;

        // Three separate bolts, each preceded by a clear frame so the edge is real.
        for (var i = 0; i < 3; i++)
        {
            var t = 1.0 + i * 0.5;
            policy.Decide(t, striking: false);

            var decision = policy.Decide(t + 1.0 / 30, striking: true);
            if (decision.Send && Brightness(decision.Colour) > 600) flashes++;
        }

        Assert.Equal(3, flashes);
    }

    [Fact]
    public void AStrikeFallsBackToTheSkyBehindIt()
    {
        var policy = new SendPolicy();

        policy.Decide(3.0, striking: true);

        // Well after the flash is over, with the bolt still notionally on screen.
        var after = policy.Decide(3.0 + EmissionLight.StrikeFlash + 0.05, striking: true);

        Assert.Equal(EmissionLight.At(3.0 + EmissionLight.StrikeFlash + 0.05), after.Colour);
    }

    [Fact]
    public void BoltsAreNotAllTheSameWhite()
    {
        // A run of identical flashes reads as a stuck backlight rather than as lightning.
        var colours = new[] { 1.0, 2.0, 3.0, 4.0 }
            .Select(EmissionLight.StrikeColour)
            .ToList();

        Assert.True(colours.Distinct().Count() > 1, "every bolt was the same colour");

        // ...but all of them are still lightning: bright, and near white.
        Assert.All(colours, c => Assert.True(Brightness(c) > 600, $"{c} is not a bolt"));
    }

    [Fact]
    public void AStrikeColourIsTheSameEveryTimeItIsAskedFor()
    {
        Assert.Equal(EmissionLight.StrikeColour(2.5), EmissionLight.StrikeColour(2.5));
    }

    [Fact]
    public void TheFlashDecaysToNothing()
    {
        Assert.Equal(1, EmissionLight.FlashAmount(0));
        Assert.True(EmissionLight.FlashAmount(EmissionLight.StrikeFlash / 2) < 0.5);
        Assert.Equal(0, EmissionLight.FlashAmount(EmissionLight.StrikeFlash));
        Assert.Equal(0, EmissionLight.FlashAmount(10));
        Assert.Equal(0, EmissionLight.FlashAmount(double.NaN));
    }

    private static int Brightness(KeyColor colour) => colour.R + colour.G + colour.B;

    [Fact]
    public void TheFlareIsSentAndIsNotDroppable()
    {
        var policy = new SendPolicy();

        // A frame immediately before it, so the floor is freshly against us.
        policy.Decide(EmissionTimeline.FlarePeak - 0.02, striking: false);

        var decision = policy.Decide(EmissionTimeline.FlarePeak, striking: false);

        Assert.True(decision.Send, "the brightest moment of the Emission was rationed away");
        Assert.True(decision.Urgent);
    }

    [Fact]
    public void TheFlashIsUrgentFromItsRiseToItsPeak()
    {
        var policy = new SendPolicy();

        for (var t = EmissionTimeline.FlarePeak - 0.2; t <= EmissionTimeline.FlarePeak; t += 1.0 / 30)
            Assert.True(policy.Decide(t, striking: false).Urgent, $"the flash at {t:N2}s was droppable");
    }

    [Fact]
    public void TheFlaresDecayIsRationedLikeAnyOtherFade()
    {
        var policy = new SendPolicy();

        // A second after the peak the wavefront is on its way back to red, and there is
        // nothing sudden left to protect.
        Assert.False(policy.Decide(EmissionTimeline.FlarePeak + 0.8, striking: false).Urgent);
    }

    [Fact]
    public void TheRampIsDroppable()
    {
        var policy = new SendPolicy();

        var decision = policy.Decide(3.0, striking: false);

        Assert.True(decision.Send);
        Assert.False(decision.Urgent, "an ordinary ramp colour must be replaceable by a newer one");
    }

    [Fact]
    public void ArrivingAtBlackIsAlwaysSent()
    {
        var sent = Run(0, EmissionTimeline.DarknessAt, _ => false);

        Assert.True(sent[^1].Colour.IsBlack, "the Emission did not end on black");
    }

    [Fact]
    public void BlackIsSentOnceAndNotRepeated()
    {
        var policy = new SendPolicy();

        policy.Decide(EmissionTimeline.DarknessAt, striking: false);

        Assert.False(policy.Decide(EmissionTimeline.DarknessAt + 1, striking: false).Send);
    }

    [Fact]
    public void ResetLetsTheNextEmissionStartFromNothing()
    {
        var policy = new SendPolicy();

        policy.Decide(EmissionTimeline.DarknessAt, striking: false);
        policy.Reset();

        // Without the reset, the previous Emission's black would suppress this one's, and the
        // keys would sit on whatever they happened to be showing.
        Assert.True(policy.Decide(0, striking: false).Send);
    }
}
