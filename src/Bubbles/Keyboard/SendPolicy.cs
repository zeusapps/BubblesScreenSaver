namespace Bubbles.Keyboard;

/// <summary>What the policy decided about one frame.</summary>
/// <param name="Send">Whether this frame is worth a packet at all.</param>
/// <param name="Colour">What to show, if it is.</param>
/// <param name="Urgent">Whether it must not be dropped in favour of a later colour. True for
/// lightning and for the wavefront's flare -- the two things in an Emission that are over
/// before a policy thinking in visible steps would have noticed them.</param>
internal readonly record struct SendDecision(bool Send, KeyColor Colour, bool Urgent);

/// <summary>How often the keyboard is actually written to.
///
/// A frame is 33ms at the default frame rate, and an Emission is twelve seconds: a keyboard
/// fed every frame would take something over three hundred packets to do what a few dozen do
/// indistinguishably. This application has an explicit stance on what it costs while the
/// overlay is up -- there is a setting with a warning on it about exactly this -- and hardware
/// written to at frame rate for a decoration nobody can see the steps of is the same mistake in
/// a different place.
///
/// So: send when the colour has visibly moved, and not more often than the floor allows. With
/// two exemptions, because coalescing the flare or a lightning strike would remove the only two
/// moments of the Emission that are supposed to be sudden.
///
/// Kept apart from the hardware, the thread and the settings so that the rule itself can be
/// checked by handing it a series of frames and counting.</summary>
/// <param name="floor">The minimum interval between ordinary sends, in the seconds of whatever
/// clock this policy is being driven by.</param>
internal sealed class SendPolicy(double floor)
{
    /// <summary>How far a colour must move before it is worth a packet. Keyboard LEDs are
    /// diffused through plastic; steps finer than this are not something anybody sees.</summary>
    private const int VisibleStep = 6;

    /// <summary>An Emission's floor: about eight sends a second, which is smooth for a colour
    /// that travels from black to white and back in twelve seconds.
    ///
    /// Measured on the Emission's clock rather than the wall's, because the Emission's clock is
    /// what the colour is a function of. A frame that arrives late has not earned a send by
    /// being late, and one that arrives early has not been denied one for being early.</summary>
    public static SendPolicy ForEmission() => new(0.12);

    /// <summary>The weather's floor.
    ///
    /// It was a second and a half, on the reasoning that a state holds for about a minute and a
    /// cross-fade takes six seconds. Precipitation shimmers, though, and a shimmer rationed to
    /// one step every second and a half is not a shimmer -- so the floor is short enough to
    /// carry it and the visible-step rule does the saving instead: a still sky moves too little
    /// to send at all, however often it is asked.</summary>
    public static SendPolicy ForWeather() => new(0.2);

    private KeyColor _last;
    private bool _any;
    private double _at;

    /// <summary>When the bolt currently fading fired. Negative infinity means none has.</summary>
    private double _strikeAt = double.NegativeInfinity;

    /// <summary>Whether a bolt was on screen last frame, so that a strike can be recognised by
    /// its beginning rather than by its presence. HasStrike stays true for as long as the bolt
    /// is drawn, and a storm keeps several in the air at once.</summary>
    private bool _striking;

    /// <summary>Back to the beginning: nothing sent, nothing owed to the clock. Called at the
    /// start of an Emission and at each end of a blackout, so one Emission's last frame cannot
    /// suppress the next one's first.</summary>
    public void Reset()
    {
        _any = false;
        _last = KeyColor.Black;
        _at = 0;
        _strikeAt = double.NegativeInfinity;
        _striking = false;
    }

    /// <summary>One frame of an Emission.</summary>
    public SendDecision Decide(double emissionTime, bool striking)
    {
        var flash = Flash(emissionTime, striking, EmissionLight.StrikeFlash);

        var colour = flash > 0
            ? EmissionLight.WithStrike(emissionTime, emissionTime - _strikeAt, _strikeAt)
            : EmissionLight.At(emissionTime);

        // A flash lasts about five frames and every one of them is on the way down. Rationing
        // those is what would leave the keys bright after the bolt had gone.
        return Resolve(emissionTime, colour, flash > 0 || EmissionLight.IsFlare(emissionTime));
    }

    /// <summary>One frame of ambient weather.
    ///
    /// The sky is handed in already computed, rather than being derived here as the Emission's
    /// is, because it depends on the whole weather cycle and the field's dominant anomaly --
    /// neither of which this class has any business knowing about.</summary>
    public SendDecision DecideWeather(double clock, KeyColor sky, bool striking)
    {
        var flash = Flash(clock, striking, EmissionLight.StrikeFlash);

        var colour = flash > 0 ? Blend(sky, WeatherLight.Strike, flash) : sky;

        return Resolve(clock, colour, flash > 0);
    }

    /// <summary>How much of a strike is showing, on the same edge-triggered, decaying rule both
    /// paths use. Zero when no bolt is fading.</summary>
    private double Flash(double clock, bool striking, double length)
    {
        // The rising edge only. A bolt that is merely still on screen has already had its
        // flash, and treating presence as the trigger is what pinned the keys at white for the
        // whole of the storm.
        if (striking && !_striking) _strikeAt = clock;
        _striking = striking;

        var since = clock - _strikeAt;

        return since >= 0 && since < length ? EmissionLight.FlashAmount(since) : 0;
    }

    private SendDecision Resolve(double clock, KeyColor colour, bool urgent)
    {
        if (!urgent && !Worth(colour, clock)) return default;

        _last = colour;
        _any = true;
        _at = clock;

        return new SendDecision(true, colour, urgent);
    }

    private static KeyColor Blend(KeyColor from, KeyColor to, double amount) => new(
        Byte(from.R + (to.R - from.R) * amount),
        Byte(from.G + (to.G - from.G) * amount),
        Byte(from.B + (to.B - from.B) * amount));

    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private bool Worth(KeyColor colour, double clock)
    {
        if (!_any) return true;

        // Arriving at black has to actually land, however gently the last of the colour faded
        // out -- it is the end of an Emission, or the sky going clear.
        if (colour.IsBlack) return !_last.IsBlack;

        if (colour.DistanceTo(_last) < VisibleStep) return false;

        return clock - _at >= floor;
    }
}
