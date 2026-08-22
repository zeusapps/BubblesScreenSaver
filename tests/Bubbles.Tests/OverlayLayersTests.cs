using Bubbles.Overlay;

namespace Bubbles.Tests;

/// <summary>The overlay must show the live desktop through itself. Drawing over a dead image is
/// what the screensaver Windows ships does, and not doing that is the whole reason this exists.
///
/// The layer opacities used to live only as the endpoints of animations, so nothing said what
/// they should be and nothing could check it. These assert the table.</summary>
public sealed class OverlayLayersTests
{
    private static Settings Defaults() => new();

    private static readonly OverlayStage[] AllStages =
    [
        OverlayStage.Active,
        OverlayStage.Artifacts,
        OverlayStage.Blackout,
    ];

    [Fact]
    public void ArtifactsRestOverTheDesktop()
    {
        var settings = Defaults();
        var rest = LayerRest.For(OverlayStage.Artifacts, settings, detectorWanted: true);

        Assert.Equal(1, rest.Root);
        Assert.Equal(settings.Dim, rest.Scrim);
        Assert.Equal(0, rest.Sky);
        Assert.Equal(0, rest.Flash);
        Assert.Equal(settings.Opacity, rest.Artifacts);
        Assert.Equal(1, rest.Detector);
    }

    [Fact]
    public void BlackoutIsSolidBlackWithNothingDrawnOnIt()
    {
        var rest = LayerRest.For(OverlayStage.Blackout, Defaults(), detectorWanted: true);

        Assert.Equal(1, rest.Scrim);
        Assert.Equal(0, rest.Artifacts);
        Assert.Equal(0, rest.Detector);
        Assert.Equal(0, rest.Sky);
        Assert.Equal(0, rest.Flash);
    }

    [Fact]
    public void ActiveDrawsNothing()
    {
        var rest = LayerRest.For(OverlayStage.Active, Defaults(), detectorWanted: true);

        Assert.Equal(0, rest.Root);
        Assert.Equal(0, rest.Detector);
    }

    /// <summary>The bug this whole change exists for. An interrupted blackout used to be able to
    /// leave the scrim held at 1, and the artifacts then faded in over solid black.</summary>
    [Fact]
    public void TheArtifactsStageIsNeverOpaque()
    {
        var settings = Defaults();
        Assert.True(settings.Dim < 1, "the default Dim must leave the desktop visible");

        var rest = LayerRest.For(OverlayStage.Artifacts, settings, detectorWanted: true);

        Assert.True(rest.Scrim < 1, $"scrim {rest.Scrim} would hide the desktop entirely");
        Assert.True(rest.Sky < 1, $"the emission sky {rest.Sky} is near-black and opaque");
    }

    /// <summary>Only the blackout stage may be opaque. Anything else covering the desktop is the
    /// regression, whatever produced it.</summary>
    [Theory]
    [InlineData(OverlayStage.Active)]
    [InlineData(OverlayStage.Artifacts)]
    public void OnlyBlackoutHidesTheDesktop(OverlayStage stage)
    {
        var rest = LayerRest.For(stage, Defaults(), detectorWanted: true);

        Assert.True(rest.Scrim < 1);
        Assert.Equal(0, rest.Sky);
        Assert.Equal(0, rest.Flash);
    }

    /// <summary>Dim of 1 is a user asking for an opaque scrim, and must be honoured. The test
    /// above is about the default, not about clamping what somebody chose.</summary>
    [Fact]
    public void AFullyDimmedSettingIsRespected()
    {
        var settings = new Settings { Dim = 1 };
        Assert.Equal(1, LayerRest.For(OverlayStage.Artifacts, settings, detectorWanted: false).Scrim);
    }

    [Fact]
    public void TheSettingsAreWhatDrivesTheRestingValues()
    {
        var settings = new Settings { Dim = 0.2, Opacity = 0.4 };
        var rest = LayerRest.For(OverlayStage.Artifacts, settings, detectorWanted: false);

        Assert.Equal(0.2, rest.Scrim);
        Assert.Equal(0.4, rest.Artifacts);
    }

    [Fact]
    public void ThemesWithoutADetectorRestWithItHidden()
    {
        var rest = LayerRest.For(OverlayStage.Artifacts, Defaults(), detectorWanted: false);
        Assert.Equal(0, rest.Detector);
    }

    /// <summary>The property that matters, stated directly: where the overlay has been has no
    /// bearing on where it rests. A blackout, an interrupted Emission and a cold start must all
    /// arrive at the same artifacts stage.
    ///
    /// This is true by construction, because the resting state is a function of the stage alone.
    /// That is exactly the design decision worth pinning -- the previous arrangement accumulated
    /// it across five animations, four keyframe timelines and two assignments, and one path
    /// through them left the scrim held at black.</summary>
    [Fact]
    public void HistoryDoesNotChangeWhereTheOverlayRests()
    {
        var settings = Defaults();
        var expected = LayerRest.For(OverlayStage.Artifacts, settings, detectorWanted: true);

        foreach (var first in AllStages)
        {
            foreach (var second in AllStages)
            {
                foreach (var third in AllStages)
                {
                    // Walk the sequence, then arrive at the artifacts stage.
                    _ = LayerRest.For(first, settings, detectorWanted: true);
                    _ = LayerRest.For(second, settings, detectorWanted: true);
                    _ = LayerRest.For(third, settings, detectorWanted: true);

                    var arrived = LayerRest.For(OverlayStage.Artifacts, settings, detectorWanted: true);

                    Assert.Equal(expected, arrived);
                }
            }
        }
    }

    [Fact]
    public void AnUnknownStageIsAnError()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => LayerRest.For((OverlayStage)99, Defaults(), detectorWanted: false));
    }

    [Fact]
    public void The_density_conversion_waits_for_the_window_to_be_laid_out()
    {
        // UpdateRegions gets the field-coordinate ratio by dividing the virtual desktop's width
        // by ActualWidth, and ShowBubbles calls it before WPF has caught up with SetWindowPos.
        // On that first call the regions come out a fraction of their real size. Every other
        // consumer is corrected by the next layout pass; the conversion writes to disk, and this
        // is what stopped it turning a stored 26 into 53 where the answer was 30.
        Assert.False(OverlayWindow.LayoutSettled(pixelsPerDip: 2.0, windowScale: 1.5));
        Assert.True(OverlayWindow.LayoutSettled(pixelsPerDip: 1.5, windowScale: 1.5));
    }

    [Fact]
    public void An_unknown_window_scale_is_never_settled()
    {
        Assert.False(OverlayWindow.LayoutSettled(pixelsPerDip: 1, windowScale: 0));
    }

    [Fact]
    public void Floating_point_noise_still_counts_as_settled()
    {
        // The ratio is a division, so it will not land on the scale exactly.
        Assert.True(OverlayWindow.LayoutSettled(pixelsPerDip: 1.5000001, windowScale: 1.5));
    }
}
