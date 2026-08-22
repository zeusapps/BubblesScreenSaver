namespace Bubbles.Zone;

/// <summary>Which anomaly family the weather takes its colour from.
///
/// Pure, and free of any window or clock, so the whole decision can be asserted directly --
/// the same shape <see cref="WeatherCycle"/> is in, and for the same reason: watching the sky
/// for twenty minutes to find out whether a tint ever flickers is not a test.
///
/// The decision needs hysteresis or it will flap. Twenty-odd artifacts spread over four
/// families leaves two of them within an artifact of each other most of the time, and a single
/// collection would otherwise flip the whole sky. So a challenger has to lead the incumbent by
/// <see cref="Margin"/> before it takes over, and whatever is showing holds for
/// <see cref="Dwell"/> regardless. Weather itself changes about once a minute; a tint changing
/// faster than that reads as flickering rather than as a shift.</summary>
public sealed class FamilyCensus
{
    /// <summary>How far ahead a challenger has to be. Three artifacts out of a couple of dozen
    /// is past the point where the lead is one respawn's worth of noise.</summary>
    public const int Margin = 3;

    /// <summary>The least time a tint holds, in seconds, however the counts move. Short of a
    /// weather dwell, because the tint is the quieter of the two and should be allowed to
    /// answer the field sooner -- but long enough that a run of collections cannot walk it
    /// through three families in a minute.</summary>
    public const double Dwell = 25;

    private static readonly Anomaly[] Families = Enum.GetValues<Anomaly>();

    private double _held = Dwell;

    /// <summary>The family the weather is currently coloured by.
    ///
    /// Starts on a family rather than on nothing: there is always some weather and it always
    /// has a colour, and the first census replaces this within a frame of the field existing.
    /// </summary>
    public Anomaly Dominant { get; private set; } = Anomaly.Chemical;

    /// <summary>Ages the dwell. Called per frame, which costs a subtraction -- the counting
    /// itself is <see cref="Take"/>, and that happens on change.</summary>
    public void Tick(double seconds)
    {
        if (seconds > 0) _held += seconds;
    }

    /// <summary>Takes the census and decides.
    ///
    /// <paramref name="counts"/> is indexed by <see cref="Anomaly"/>. Returns true when the
    /// dominant family changed, so the caller can cross-fade rather than compare.</summary>
    public bool Take(IReadOnlyList<int> counts)
    {
        if (_held < Dwell) return false;

        var incumbent = counts.Count > (int)Dominant ? counts[(int)Dominant] : 0;

        var challenger = Dominant;
        var best = incumbent;

        foreach (var family in Families)
        {
            var count = counts.Count > (int)family ? counts[(int)family] : 0;
            if (count > best)
            {
                best = count;
                challenger = family;
            }
        }

        // An empty field, or a lead too slim to be anything but noise, leaves the sky where it
        // is. Nothing here resets to a default: there is no such thing as no colour.
        if (challenger == Dominant || best - incumbent < Margin) return false;

        Dominant = challenger;
        _held = 0;
        return true;
    }
}
