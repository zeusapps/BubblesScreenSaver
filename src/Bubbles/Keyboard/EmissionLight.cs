using Bubbles.Overlay;

namespace Bubbles.Keyboard;

/// <summary>One colour, as the keyboard wants it: three bytes, no dependency on WPF, no
/// dependency on a device.</summary>
internal readonly record struct KeyColor(byte R, byte G, byte B)
{
    public static readonly KeyColor Black = new(0, 0, 0);

    public bool IsBlack => R == 0 && G == 0 && B == 0;

    /// <summary>How far apart two colours are, as the largest single-channel difference. Used
    /// to decide whether a change is worth a packet; a per-channel maximum is the right
    /// measure because it is the channel that moved furthest that would be seen.</summary>
    public int DistanceTo(KeyColor other) => Math.Max(
        Math.Abs(R - other.R),
        Math.Max(Math.Abs(G - other.G), Math.Abs(B - other.B)));
}

/// <summary>What the keyboard shows at a given moment of an Emission.
///
/// A pure function of elapsed time, written against the same constants the screen is animated
/// with. That is the whole point: the keyboard cannot drift out of step with the screen,
/// because it is not following the screen -- both are reading the same clock. It also means
/// the interesting half of this feature is testable on any machine, with no keyboard at all,
/// by asking it for a time and looking at the colour.</summary>
internal static class EmissionLight
{
    /// <summary>A lightning strike, on the keys. Near-white and cold, because a bolt is not the
    /// sky it crosses.</summary>
    public static readonly KeyColor Strike = new(0xFF, 0xF4, 0xE0);

    /// <summary>The other end of the range a strike is picked from -- the same brightness, a
    /// colder cast. Bolts are not all the same colour, and on a board that can only show one
    /// at a time a run of identical white flashes reads as the backlight being stuck.</summary>
    private static readonly KeyColor StrikeCold = new(0xE4, 0xEE, 0xFF);

    /// <summary>How long a strike takes to fall back to the sky behind it.
    ///
    /// A bolt stays on screen for a good fraction of a second and the dense part of a storm has
    /// several overlapping, so this cannot be tied to whether a bolt is *present* -- that was
    /// the first version, and it left the keyboard solid white for the whole of the storm while
    /// the screen was showing thin bright lines over red. A strike is an event: it fires, and
    /// then it is over, whatever the sky is still doing.</summary>
    public const double StrikeFlash = 0.18;

    /// <summary>Deepest red the buildup reaches, at the moment the sky has fully taken over.
    /// Taken from the top stop of the Emission sky, so the keys and the screen are the same
    /// crimson rather than two people's idea of red.</summary>
    private static readonly KeyColor DeepRed = new(0xC4, 0x30, 0x18);

    /// <summary>The total brightness of <see cref="DeepRed"/> -- the level the buildup climbs
    /// to and the wavefront departs from.
    ///
    /// Exposed because the ambient weather is budgeted as a fraction of it. Quoting a fraction
    /// of a real colour is the only way the cap means anything: "a fifth as bright as the
    /// Emission" is checkable, where "dim" is not.</summary>
    public static int PeakBrightness => DeepRed.R + DeepRed.G + DeepRed.B;

    /// <summary>The wavefront itself: the deep red pushed toward white.</summary>
    private static readonly KeyColor Flare = new(0xFF, 0xE8, 0xD0);

    /// <summary>How long the flare takes to arrive, and how long to leave. Short both ways --
    /// the screen's flash is 0.3s in and gone by <see cref="EmissionTimeline.WaveEnds"/>, and a
    /// keyboard that lingers after it turns a shockwave into a glow.</summary>
    private const double FlareRise = 0.3;
    private const double FlareFall = 0.9;

    /// <summary>Whether <paramref name="time"/> falls in the wavefront's flash: the rise, and
    /// the moment at the top of it.
    ///
    /// The one stretch of the timeline that must not be rationed. It is what the whole
    /// Emission has been building toward, it is over in a third of a second, and a policy
    /// thinking in visible steps and minimum intervals would skip straight across it.
    ///
    /// Deliberately narrower than the flare itself. The fall back to red afterwards takes
    /// nearly a second and is a fade like any other -- exempting that as well would mean
    /// sending at frame rate for a second to describe something the ordinary rationing renders
    /// perfectly well.</summary>
    public static bool IsFlare(double time) =>
        time > EmissionTimeline.FlarePeak - FlareRise &&
        time < EmissionTimeline.FlarePeak + 0.2;

    /// <summary>How much of a strike is left, <paramref name="since"/> seconds after it fired.
    /// Falls off fast and squarely: a bolt is over before the eye has finished with it.</summary>
    public static double FlashAmount(double since)
    {
        if (double.IsNaN(since) || since < 0 || since >= StrikeFlash) return 0;

        var left = 1 - since / StrikeFlash;
        return left * left;
    }

    /// <summary>The colour of one particular bolt.
    ///
    /// Varied per strike, so a burst is a burst rather than one long white. Deterministic in
    /// the strike's own start time rather than random, because the whole timeline is a pure
    /// function of time and a keyboard that cannot be asked what it did last is a poor place to
    /// start keeping hidden state.</summary>
    public static KeyColor StrikeColour(double startedAt)
    {
        var hash = Math.Abs(Math.Sin(startedAt * 12.9898) * 43758.5453);

        return Mix(Strike, StrikeCold, hash - Math.Floor(hash));
    }

    /// <summary>The sky at <paramref name="time"/>, with a bolt over it.
    ///
    /// Mixed over the ramp rather than replacing it, so what the keys show during a storm is
    /// the storm with lightning in it -- which is what the screen shows.</summary>
    public static KeyColor WithStrike(double time, double since, double startedAt) =>
        Mix(At(time), StrikeColour(startedAt), FlashAmount(since));

    /// <summary>The colour at <paramref name="time"/> seconds into the Emission.</summary>
    public static KeyColor At(double time)
    {
        if (double.IsNaN(time) || time <= 0) return KeyColor.Black;

        if (time < EmissionTimeline.BuildupEnds) return Buildup(time);

        if (time < EmissionTimeline.WaveEnds) return Wavefront(time);

        return Fading(time);
    }

    /// <summary>Nothing, rising to deep red. Eased the same way the sky's opacity is -- slow to
    /// start, gathering -- so the keys fill at the rate the screen does rather than linearly
    /// while the screen accelerates.</summary>
    private static KeyColor Buildup(double time)
    {
        var progress = Math.Clamp(time / EmissionTimeline.BuildupEnds, 0, 1);
        return Scale(DeepRed, Math.Pow(progress, 1.6));
    }

    /// <summary>The flare arrives, peaks at <see cref="EmissionTimeline.FlarePeak"/>, and
    /// falls back to the deep red the buildup left behind.</summary>
    private static KeyColor Wavefront(double time)
    {
        if (time < EmissionTimeline.FlarePeak)
        {
            var rising = Math.Clamp((time - (EmissionTimeline.FlarePeak - FlareRise)) / FlareRise, 0, 1);
            return Mix(DeepRed, Flare, rising);
        }

        var falling = Math.Clamp((time - EmissionTimeline.FlarePeak) / FlareFall, 0, 1);
        return Mix(Flare, DeepRed, falling);
    }

    /// <summary>Everything drains away, arriving at black exactly when the screen does.</summary>
    private static KeyColor Fading(double time)
    {
        if (time >= EmissionTimeline.DarknessAt) return KeyColor.Black;

        var left = 1 - (time - EmissionTimeline.WaveEnds) /
                       (EmissionTimeline.DarknessAt - EmissionTimeline.WaveEnds);

        return Scale(DeepRed, Math.Clamp(left, 0, 1));
    }

    private static KeyColor Scale(KeyColor colour, double by) => new(
        Byte(colour.R * by),
        Byte(colour.G * by),
        Byte(colour.B * by));

    private static KeyColor Mix(KeyColor from, KeyColor to, double amount) => new(
        Byte(from.R + (to.R - from.R) * amount),
        Byte(from.G + (to.G - from.G) * amount),
        Byte(from.B + (to.B - from.B) * amount));

    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
