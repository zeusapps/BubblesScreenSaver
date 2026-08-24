namespace Bubbles.Overlay;

/// <summary>The Emission's clock, in seconds from the first tremor.
///
/// These used to be private to <see cref="OverlayWindow"/>, which was right while the screen
/// was the only thing animated against them. The keyboard lighting is animated against them
/// too, and a copy of three numbers is a copy that can be edited on one side only -- so they
/// live here, where both readers see the same values by construction rather than by anybody
/// remembering to change two places.</summary>
internal static class EmissionTimeline
{
    /// <summary>The buildup is over: the sky is fully taken over and the tremors peak.</summary>
    public const double BuildupEnds = 6.5;

    /// <summary>The wavefront has passed.</summary>
    public const double WaveEnds = 8.4;

    /// <summary>Everything has arrived at black.</summary>
    public const double DarknessAt = 12.5;

    /// <summary>When the wavefront's hard flare peaks. The screen's flash layer is keyed to
    /// this exact moment, and so is the keyboard's.</summary>
    public const double FlarePeak = BuildupEnds + 0.3;
}
