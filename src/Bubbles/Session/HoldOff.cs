namespace Bubbles.Session;

/// <summary>Which stages a reason suppresses, and why.
///
/// Hold-off used to be a single <c>string?</c>: a reason, or nothing, and any reason at all
/// collapsed the whole machine to Active. That was right while every signal meant "somebody is
/// here". A media session does not — it says what is playing, and watching a film and listening
/// to an album call for opposite treatment:
///
///   watching  -> draw nothing at all, the screen is in use
///   listening -> do not throw artifacts over the desktop, but do let it reach black,
///                because an album must not keep an OLED lit for three hours
///
/// The two flags are independent rather than an ordered ceiling. A ceiling cannot express
/// "artifacts no, blackout yes", because blackout is the *further* stage.</summary>
/// <param name="Artifacts">Whether the artifacts stage is suppressed.</param>
/// <param name="Blackout">Whether the blackout stage is suppressed.</param>
/// <param name="Reason">What is suppressing, for the log and the tray. Null when nothing is.</param>
public readonly record struct HoldOff(bool Artifacts, bool Blackout, string? Reason)
{
    /// <summary>Nothing is holding anything off.</summary>
    public static readonly HoldOff None = new(false, false, null);

    /// <summary>Somebody is here: draw nothing.</summary>
    public static HoldOff Everything(string reason) => new(true, true, reason);

    /// <summary>Somebody is listening but not looking: no artifacts, but black is welcome.</summary>
    public static HoldOff ArtifactsOnly(string reason) => new(true, false, reason);

    /// <summary>Whether every stage is suppressed. Only then does the idle countdown stop:
    /// discounting time under a partial hold-off would freeze the clock short of
    /// BlackoutSeconds, and the blackout the reason deliberately permitted would never
    /// arrive.</summary>
    public bool Total => Artifacts && Blackout;

    /// <summary>Whether anything at all is suppressed.</summary>
    public bool Any => Artifacts || Blackout;

    /// <summary>Combines two reasons: every stage either of them suppresses is suppressed.
    ///
    /// The reported reason is the stricter one, so the log names what is actually withholding
    /// the stage the user is not seeing. With music playing and the microphone open, "microphone
    /// in use" is the useful line.</summary>
    public HoldOff And(HoldOff other)
    {
        if (!other.Any) return this;
        if (!Any) return other;

        // Whichever suppresses more explains more. A tie keeps the one already held, which is
        // the earlier and therefore higher-priority signal in the order they are evaluated.
        var stricter = other.Total && !Total;

        return new HoldOff(
            Artifacts || other.Artifacts,
            Blackout || other.Blackout,
            stricter ? other.Reason : Reason);
    }
}
