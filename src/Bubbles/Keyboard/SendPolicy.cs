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
internal sealed class SendPolicy
{
    /// <summary>How far a colour must move before it is worth a packet. Keyboard LEDs are
    /// diffused through plastic; steps finer than this are not something anybody sees.</summary>
    private const int VisibleStep = 6;

    /// <summary>The floor between ordinary sends, in Emission seconds. About eight a second,
    /// which is smooth for a twelve-second ramp.
    ///
    /// Measured on the Emission's clock rather than the wall's, because the Emission's clock is
    /// what the colour is a function of. A frame that arrives late has not earned a send by
    /// being late, and one that arrives early has not been denied one for being early.</summary>
    private const double Floor = 0.12;

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

    public SendDecision Decide(double emissionTime, bool striking)
    {
        // The rising edge only. A bolt that is merely still on screen has already had its
        // flash, and treating presence as the trigger is what pinned the keys at white for the
        // whole of the storm.
        if (striking && !_striking) _strikeAt = emissionTime;
        _striking = striking;

        var since = emissionTime - _strikeAt;
        var flashing = since >= 0 && since < EmissionLight.StrikeFlash;

        var colour = flashing
            ? EmissionLight.WithStrike(emissionTime, since, _strikeAt)
            : EmissionLight.At(emissionTime);

        // A flash lasts about five frames and every one of them is on the way down. Rationing
        // those is what would leave the keys bright after the bolt had gone.
        var urgent = flashing || EmissionLight.IsFlare(emissionTime);

        if (!urgent && !Worth(colour, emissionTime)) return default;

        _last = colour;
        _any = true;
        _at = emissionTime;

        return new SendDecision(true, colour, urgent);
    }

    private bool Worth(KeyColor colour, double emissionTime)
    {
        if (!_any) return true;

        // Arriving at black is the end of the Emission and has to actually land, however
        // gently the last of the red faded out.
        if (colour.IsBlack) return !_last.IsBlack;

        if (colour.DistanceTo(_last) < VisibleStep) return false;

        return emissionTime - _at >= Floor;
    }
}
