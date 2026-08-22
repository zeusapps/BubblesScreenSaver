namespace Bubbles.Overlay;

/// <summary>What the overlay is showing. Deliberately not IdleController.Stage: the overlay
/// knows nothing about the idle timer that drives it, and that separation is what keeps Zone,
/// Overlay and Session from depending on each other.</summary>
public enum OverlayStage
{
    /// <summary>Nothing drawn.</summary>
    Active,

    /// <summary>Artifacts drifting over the live, dimmed desktop.</summary>
    Artifacts,

    /// <summary>Solid black.</summary>
    Blackout,
}

/// <summary>The opacity every layer rests at once a transition has finished.
///
/// These values used to exist only as the endpoints of five <c>Animate</c> calls, four keyframe
/// timelines and two direct assignments, so nothing stated what the layers *should* be and
/// nothing could check it. <see cref="OverlayWindow.Apply"/> proved the drift: it reset the
/// scrim and the artifacts on a settings change and left the sky and the flash alone.
///
/// Pure, and free of any window, so the invariant can be asserted directly -- in particular
/// that every sequence of stage changes ending at <see cref="OverlayStage.Artifacts"/> rests
/// with the scrim at Dim and the sky at zero. An overlay that comes back from a blackout with
/// the scrim still held at 1 is opaque, which is the one thing this application must never
/// be.</summary>
public readonly record struct LayerRest(
    double Root,
    double Scrim,
    double Sky,
    double Flash,
    double Artifacts,
    double Detector)
{
    /// <param name="detectorWanted">Whether the theme has a detector at all.</param>
    public static LayerRest For(OverlayStage stage, Settings settings, bool detectorWanted) => stage switch
    {
        // Hidden. The layers beneath still hold their artifacts-stage values so that showing
        // is a fade of the root alone rather than a rebuild of everything under it.
        OverlayStage.Active => new(
            Root: 0,
            Scrim: settings.Dim,
            Sky: 0,
            Flash: 0,
            Artifacts: settings.Opacity,
            Detector: 0),

        OverlayStage.Artifacts => new(
            Root: 1,
            Scrim: settings.Dim,
            Sky: 0,
            Flash: 0,
            Artifacts: settings.Opacity,
            Detector: detectorWanted ? 1 : 0),

        // The artifacts emit light, so a real blackout hides them too.
        OverlayStage.Blackout => new(
            Root: 1,
            Scrim: 1,
            Sky: 0,
            Flash: 0,
            Artifacts: 0,
            Detector: 0),

        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };
}
