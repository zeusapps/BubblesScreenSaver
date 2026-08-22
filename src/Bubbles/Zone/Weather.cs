namespace Bubbles.Zone;

/// <summary>The Zone's ambient weather.
///
/// Between the artifacts arriving and the screen going black the sky had exactly one state, so
/// every night looked the same. These are the four it cycles through.</summary>
public enum Weather
{
    /// <summary>Nothing drawn. The Zone as it was before weather existed.</summary>
    Clear,

    /// <summary>A haze in front of the artifacts, softening them without hiding them.</summary>
    Fog,

    /// <summary>Falling precipitation.</summary>
    Rain,

    /// <summary>Rain, with distant strikes behind the artifacts.</summary>
    Storm,
}

/// <summary>Which weather is showing, what it is turning into, and when it next changes.
///
/// Pure, and free of any window or clock, so the whole cycle can be asserted directly: that a
/// change is always visible, that the dwell never falls on a beat, that a transition ends with
/// exactly one state live. Advanced by <see cref="Tick"/> with a delta in seconds, the same way
/// the field is.</summary>
public sealed class WeatherCycle
{
    /// <summary>How long a state holds before the next roll, and how far that varies.
    ///
    /// A minute reads as weather rather than as a slideshow. The jitter is what stops the
    /// changes landing on a beat, which is what makes a fixed interval feel mechanical.</summary>
    private const double Dwell = 60;
    private const double Jitter = 0.25;

    /// <summary>Long enough that fog lifting reads as the weather changing rather than as a
    /// dissolve.</summary>
    public const double CrossFade = 6;

    /// <summary>How often each state comes up.
    ///
    /// Weighted rather than dealt from a <see cref="ShuffledDeck"/>: a deck of four would make
    /// storms exactly a quarter of all weather and the sequence predictable in fours, and the
    /// Zone should be able to stay fair for a while. A storm is meant to be an event.</summary>
    private static readonly (Weather State, int Weight)[] Weights =
    [
        (Weather.Clear, 35),
        (Weather.Fog, 25),
        (Weather.Rain, 25),
        (Weather.Storm, 15),
    ];

    private readonly Random _rng;

    private double _holdFor;
    private double _held;
    private double _fading;

    /// <param name="random">Supply one to make the sequence reproducible; the app leaves it be.</param>
    public WeatherCycle(Random? random = null)
    {
        _rng = random ?? new Random();
        Current = Roll(from: null);
        _holdFor = NextDwell();
    }

    /// <summary>The state coming in, which outside a transition is simply the weather.</summary>
    public Weather Current { get; private set; }

    /// <summary>The state going out, or null when nothing is in transition.</summary>
    public Weather? Outgoing { get; private set; }

    /// <summary>How far the incoming state has come, 0 to 1. Always 1 when settled, so a caller
    /// can render <see cref="Current"/> at this and <see cref="Outgoing"/> at 1 minus it without
    /// a special case.</summary>
    public double Progress => Outgoing is null ? 1 : 1 - _fading / CrossFade;

    /// <summary>How strongly one state is showing, counting both sides of a cross-fade.
    ///
    /// One place to ask, because two things need the answer and they must agree: the layer that
    /// draws the rain, and the lightning that has to know whether a storm is overhead.</summary>
    public double IntensityOf(Weather state)
    {
        var intensity = 0.0;
        if (Current == state) intensity += Progress;
        if (Outgoing == state) intensity += 1 - Progress;
        return Math.Clamp(intensity, 0, 1);
    }

    /// <summary>True while the cycle is held for an Emission. The sky is already the show; a
    /// weather change underneath it would be a second one.</summary>
    public bool Suspended { get; set; }

    /// <summary>Advances the cycle. A transition already running when the cycle is suspended is
    /// allowed to finish -- stopping it half way would leave two states live indefinitely.</summary>
    public void Tick(double seconds)
    {
        if (seconds <= 0) return;

        if (Outgoing is not null)
        {
            _fading -= seconds;
            if (_fading <= 0)
            {
                _fading = 0;
                Outgoing = null;
            }

            return;
        }

        if (Suspended) return;

        _held += seconds;
        if (_held < _holdFor) return;

        Outgoing = Current;
        Current = Roll(from: Current);
        _fading = CrossFade;
        _held = 0;
        _holdFor = NextDwell();
    }

    /// <summary>Starts again from a freshly rolled state. Used when weather is switched back on,
    /// so it does not resume mid-transition from whenever it was switched off.</summary>
    public void Restart()
    {
        Outgoing = null;
        _fading = 0;
        _held = 0;
        Current = Roll(from: null);
        _holdFor = NextDwell();
    }

    private double NextDwell() => Dwell * (1 + (_rng.NextDouble() * 2 - 1) * Jitter);

    /// <summary>Deals the next state, never the one already showing.
    ///
    /// Excluding the current state is what makes a roll worth doing: rolling the same weather
    /// again is a minute in which nothing happens, and the cycle exists to make something.</summary>
    private Weather Roll(Weather? from)
    {
        var total = 0;
        foreach (var (state, weight) in Weights)
            if (state != from)
                total += weight;

        var pick = _rng.Next(total);

        foreach (var (state, weight) in Weights)
        {
            if (state == from) continue;

            pick -= weight;
            if (pick < 0) return state;
        }

        return Weather.Clear;
    }
}
