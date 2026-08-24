using Bubbles.Zone;

namespace Bubbles.Keyboard;

/// <summary>What the keyboard shows while the weather is on screen and nothing louder is
/// happening.
///
/// The counterpart to <see cref="EmissionLight"/>, and deliberately its opposite in character.
/// An Emission is twelve seconds and is meant to be startling; the weather runs for as long as
/// the screensaver does and is meant to be barely noticed -- the room going faintly green when
/// fog rolls in, the way the screen already does.
///
/// It reads the weather's own state rather than the picture of it, and takes its colour from
/// the same place the picture does: <see cref="WeatherTint.Rain"/> and
/// <see cref="WeatherTint.Fog"/>, which are the pale drop and haze colours pulled toward
/// whichever anomaly family dominates the field.
///
/// Not <see cref="AnomalyTint"/> directly, which was the first attempt. That is the colour the
/// rain is *derived from*, not the colour it *is* -- the keys came out the artifacts' colour
/// while the screen showed cold pale rain, which is a near miss and reads worse than an obvious
/// one.</summary>
internal static class WeatherLight
{
    /// <summary>The fraction of the Emission's deepest red, by total brightness, that ambient
    /// weather may reach.
    ///
    /// This is the number the whole feature turns on. `keyboard-lighting` ruled ambient light
    /// out on the grounds that it would dilute the Emission, and the only thing that answers
    /// that objection is a ceiling low enough that the brightest possible storm is still
    /// obviously quieter than the Emission's own colour.
    ///
    /// It is asserted against <see cref="EmissionLight"/> in the tests rather than left to
    /// judgement, so raising it fails something rather than quietly spending the Emission's
    /// impact.
    ///
    /// Set by looking at the keyboard, which is the only way it could be set. It began at 0.22
    /// and the answer was that the backlight "reads as turned off most of the time" -- keys are
    /// diffused through plastic and a fifth of a dark red is nothing at all on them. Half the
    /// Emission's red is still plainly quieter than an Emission, whose wavefront is three times
    /// that red again.</summary>
    public const double AmbientCeiling = 0.50;

    /// <summary>How much of the ceiling each state is worth.
    ///
    /// Fog is the quietest because on screen it is a haze in front of the artifacts rather than
    /// a sky behind them. A storm is barely brighter than rain, because on screen a storm *is*
    /// rain -- the same precipitation with bolts behind it. The lightning is what makes it read
    /// as a storm, and inventing a colour difference on top of that would be the keyboard
    /// disagreeing with the screen about what is happening.</summary>
    private static double Weight(Weather state) => state switch
    {
        Weather.Fog => 0.65,
        Weather.Rain => 0.85,
        Weather.Storm => 0.95,
        _ => 0,
    };

    /// <summary>A bolt, on the keys. The same one an Emission throws.
    ///
    /// It was dimmed to half at first, on the theory that a distant strike should be a flicker
    /// rather than a slap. Looking at it says otherwise: lightning is the one part of ambient
    /// weather that is legibly connected to something visible on screen, and halving it threw
    /// away the clearest signal the feature has. A bolt is a bolt.</summary>
    public static KeyColor Strike => EmissionLight.Strike;

    /// <summary>How much precipitation flickers, and how fast.
    ///
    /// Rain on screen is three scrolling sheets at different speeds, which reads as movement;
    /// one zone of backlight cannot scroll, so it shimmers instead. Fog does not -- a haze is
    /// the one weather that genuinely sits still.</summary>
    private const double FlickerDepth = 0.22;

    /// <summary>A steady but unrhythmic wobble, from two frequencies that do not divide into
    /// one another. Deterministic, because the whole timeline is.</summary>
    private static double Flicker(double clock)
    {
        var wave = 0.5 + 0.5 * Math.Sin(clock * 6.1) * Math.Cos(clock * 2.7);
        return 1 - FlickerDepth * wave;
    }

    /// <summary>The whole sky at once, cross-fades included.
    ///
    /// Summed over every state rather than switched on the current one, because
    /// <see cref="SkyState.IntensityOf"/> reports both sides of a transition and they add
    /// to one. Blending them is therefore the same arithmetic the screen does, which is why the
    /// keys cannot fall out of step during a fade.
    ///
    /// Summed before rounding, not after. Rounding each state and adding the results lets a
    /// cross-fade come out brighter than either state on its own, which is a ceiling that does
    /// not hold for the one moment anybody is looking at it.</summary>
    public static KeyColor For(SkyState sky, Anomaly family, double clock)
    {
        double r = 0, g = 0, b = 0;

        foreach (var state in States)
        {
            var (pr, pg, pb) = Contribution(state, sky.IntensityOf(state), family, clock);
            r += pr;
            g += pg;
            b += pb;
        }

        return new KeyColor(Byte(r), Byte(g), Byte(b));
    }

    /// <summary>How far a sheet's colour is pushed away from grey before it reaches the keys.
    ///
    /// The sheets are drawn over the desktop at low alpha, so their colours are pale by
    /// design -- that is what stops the weather becoming a filter over everything. Copied
    /// faithfully onto one zone of diffused plastic they arrive as four barely different
    /// greys, and the first person to look at it could not tell what the keys had to do with
    /// the screen.
    ///
    /// So the keyboard leans on the hue a little. Not enough to become a different colour --
    /// the keys are meant to be the colour the rain is, and the rain's colour comes from the
    /// artifacts drifting in it -- but enough that a pale sheet is not delivered as grey.</summary>
    private const double Saturation = 1.5;

    private static readonly Weather[] States = [Weather.Clear, Weather.Fog, Weather.Rain, Weather.Storm];

    /// <summary>One state's contribution, at the intensity it is showing.</summary>
    public static KeyColor At(Weather state, double intensity, Anomaly family, double clock = 0)
    {
        var (r, g, b) = Contribution(state, intensity, family, clock);
        return new KeyColor(Byte(r), Byte(g), Byte(b));
    }

    /// <summary>One state's contribution, unrounded.
    ///
    /// Budgeted by total brightness rather than by scaling each channel, because the four
    /// anomaly tints are averaged colours whose channel sums differ -- scaling them all by the
    /// same factor would make some families twice as bright on the keys as others, and the
    /// family is supposed to change the hue, not the level.</summary>
    private static (double R, double G, double B) Contribution(
        Weather state, double intensity, Anomaly family, double clock)
    {
        var weight = Weight(state);

        if (weight <= 0 || double.IsNaN(intensity) || intensity <= 0) return (0, 0, 0);

        // The colour the sky is actually drawn in, not the tint behind it.
        var tint = state == Weather.Fog
            ? WeatherTint.Fog(family)
            : WeatherTint.Rain(family);

        var colour = Saturated(new KeyColor(tint.R, tint.G, tint.B));

        var sum = colour.R + colour.G + colour.B;
        if (sum <= 0) return (0, 0, 0);

        // Rain and storms shimmer; fog holds still.
        var shimmer = state == Weather.Fog ? 1 : Flicker(clock);

        var budget = AmbientCeiling * weight * Math.Clamp(intensity, 0, 1)
                     * shimmer * EmissionLight.PeakBrightness;
        var scale = budget / sum;

        return (colour.R * scale, colour.G * scale, colour.B * scale);
    }

    /// <summary>Pushes a colour away from its own grey, keeping its hue and losing its
    /// washed-out-ness.</summary>
    private static KeyColor Saturated(KeyColor colour)
    {
        var mean = (colour.R + colour.G + colour.B) / 3.0;

        return new KeyColor(
            Byte(mean + (colour.R - mean) * Saturation),
            Byte(mean + (colour.G - mean) * Saturation),
            Byte(mean + (colour.B - mean) * Saturation));
    }

    private static KeyColor Scale(KeyColor colour, double by) =>
        new(Byte(colour.R * by), Byte(colour.G * by), Byte(colour.B * by));

    private static byte Byte(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);
}
