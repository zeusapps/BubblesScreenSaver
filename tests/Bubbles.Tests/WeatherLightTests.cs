using Bubbles.Keyboard;
using Bubbles.Overlay;
using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>The keyboard's ambient colour: what it does for the hours between the artifacts
/// arriving and the screen going black.
///
/// The test that matters most here is the ceiling. `keyboard-lighting` ruled ambient light out
/// on the grounds that it would dilute the Emission, and this change only answers that by
/// keeping the weather demonstrably quieter than the thing it must not compete with.</summary>
public class WeatherLightTests
{
    private static readonly Weather[] Lit = [Weather.Fog, Weather.Rain, Weather.Storm];

    private static int Brightness(KeyColor colour) => colour.R + colour.G + colour.B;

    /// <summary>The brightest the weather can ever be, over every state and every family.</summary>
    private static KeyColor Brightest() =>
        (from state in Lit
         from family in Enum.GetValues<Anomaly>()
         select WeatherLight.At(state, 1, family))
        .MaxBy(Brightness);

    // ---- the colour ----------------------------------------------------------------------

    [Fact]
    public void AClearSkyLeavesTheKeysUnlit()
    {
        foreach (var family in Enum.GetValues<Anomaly>())
            Assert.Equal(KeyColor.Black, WeatherLight.At(Weather.Clear, 1, family));
    }

    [Fact]
    public void TheKeysTakeTheColourTheSkyIsActuallyDrawnIn()
    {
        foreach (var family in Enum.GetValues<Anomaly>())
        {
            var fog = WeatherTint.Fog(family);
            var rain = WeatherTint.Rain(family);

            // Not equal -- scaled well down -- but the same channel has to lead, or the keys
            // and the screen are simply different colours.
            var onFog = WeatherLight.At(Weather.Fog, 1, family);
            var onRain = WeatherLight.At(Weather.Rain, 1, family);

            Assert.Equal(Leader(fog.R, fog.G, fog.B), Leader(onFog.R, onFog.G, onFog.B));
            Assert.Equal(Leader(rain.R, rain.G, rain.B), Leader(onRain.R, onRain.G, onRain.B));
        }
    }

    [Fact]
    public void TheKeysKeepTheSkysHueAndExaggerateIt()
    {
        // The sheets are pale by design -- drawn at low alpha so the weather does not become a
        // filter over the desktop. Copied faithfully onto one zone they arrive as four barely
        // different greys, which is what "I cannot see how the keys relate to the screen" looks
        // like. So the keys keep the sheet's hue and push it away from grey.
        foreach (var family in Enum.GetValues<Anomaly>())
        {
            var sheet = WeatherTint.Rain(family);
            var keys = WeatherLight.At(Weather.Rain, 1, family);

            Assert.Equal(Leader(sheet.R, sheet.G, sheet.B), Leader(keys.R, keys.G, keys.B));
            Assert.True(Spread(keys) > Spread(new KeyColor(sheet.R, sheet.G, sheet.B)),
                        $"{family}: the keys were no more saturated than the pale sheet");
        }
    }

    /// <summary>How far a colour sits from its own grey, as a share of its brightness. The
    /// measure of whether a hue reads at all.</summary>
    private static double Spread(KeyColor colour)
    {
        var mean = (colour.R + colour.G + colour.B) / 3.0;
        if (mean <= 0) return 0;

        return (Math.Abs(colour.R - mean) + Math.Abs(colour.G - mean) + Math.Abs(colour.B - mean)) / mean;
    }

    [Fact]
    public void TheSheetTintAndTheArtifactsTintAreNearlyTheSameHue()
    {
        // Worth recording, because it means the choice between them barely affects the hue --
        // the family already pulls the sheet 85% of the way to its own colour. What separates
        // the keys from the artifacts is the exaggeration above, not which source is read.
        foreach (var family in Enum.GetValues<Anomaly>())
        {
            var sheet = WeatherTint.Rain(family);
            var artifacts = AnomalyTint.Of(family);

            Assert.Equal(Leader(sheet.R, sheet.G, sheet.B), Leader(artifacts.R, artifacts.G, artifacts.B));
        }
    }

    [Fact]
    public void FogAndRainAreDifferentSheets()
    {
        // Untinted they are plainly different -- a grey-green haze against a cold grey-blue
        // drop.
        Assert.NotEqual(Normalised(WeatherTint.Fog(null)), Normalised(WeatherTint.Rain(null)));
    }

    [Fact]
    public void OnceAFamilyIsDominantFogAndRainAreToldApartByLevelNotHue()
    {
        // Worth stating rather than discovering. The family pulls both sheets 85% of the way to
        // its own colour, so with an anomaly dominant the two arrive at nearly the same hue and
        // it is the weights -- fog quieter than rain -- that separate them on the keys.
        var fog = WeatherLight.At(Weather.Fog, 1, Anomaly.Chemical);
        var rain = WeatherLight.At(Weather.Rain, 1, Anomaly.Chemical);

        Assert.True(Brightness(rain) > Brightness(fog), "rain was no stronger than fog");
    }

    /// <summary>A colour at a fixed brightness, so two can be compared on hue alone.</summary>
    private static KeyColor Normalised(System.Windows.Media.Color colour)
    {
        var sum = colour.R + colour.G + colour.B;
        if (sum == 0) return KeyColor.Black;

        var scale = 120.0 / sum;

        return new KeyColor(
            (byte)Math.Clamp(Math.Round(colour.R * scale), 0, 255),
            (byte)Math.Clamp(Math.Round(colour.G * scale), 0, 255),
            (byte)Math.Clamp(Math.Round(colour.B * scale), 0, 255));
    }

    private static int Distance(KeyColor a, KeyColor b)
    {
        var an = Normalised(System.Windows.Media.Color.FromRgb(a.R, a.G, a.B));
        return Math.Abs(an.R - b.R) + Math.Abs(an.G - b.G) + Math.Abs(an.B - b.B);
    }

    private static int Leader(int r, int g, int b) => r >= g && r >= b ? 0 : g >= b ? 1 : 2;

    [Fact]
    public void IntensityScalesTheResult()
    {
        var full = WeatherLight.At(Weather.Rain, 1, Anomaly.Chemical);
        var half = WeatherLight.At(Weather.Rain, 0.5, Anomaly.Chemical);

        Assert.True(Brightness(half) < Brightness(full));
        Assert.Equal(KeyColor.Black, WeatherLight.At(Weather.Rain, 0, Anomaly.Chemical));
    }

    [Fact]
    public void TheSameInputsGiveTheSameColour()
    {
        Assert.Equal(WeatherLight.At(Weather.Storm, 0.4, Anomaly.Thermic),
                     WeatherLight.At(Weather.Storm, 0.4, Anomaly.Thermic));
    }

    [Fact]
    public void AStormIsRainWithLightningInIt()
    {
        // On screen a storm is the same precipitation with bolts behind it, so the keys agree:
        // near enough the same colour, told apart by the flashes rather than by a hue nobody
        // is looking at. An earlier version pulled the storm toward cold and it was the
        // keyboard inventing weather the screen was not showing.
        var rain = WeatherLight.At(Weather.Rain, 1, Anomaly.Chemical);
        var storm = WeatherLight.At(Weather.Storm, 1, Anomaly.Chemical);

        Assert.Equal(Leader(rain.R, rain.G, rain.B), Leader(storm.R, storm.G, storm.B));
        Assert.True(Brightness(storm) >= Brightness(rain), "the storm was lighter rain");
    }

    [Fact]
    public void NonsenseIntensitiesAreNotAnError()
    {
        Assert.Equal(KeyColor.Black, WeatherLight.At(Weather.Rain, double.NaN, Anomaly.Chemical));
        Assert.Equal(KeyColor.Black, WeatherLight.At(Weather.Rain, -5, Anomaly.Chemical));

        // Clamped rather than overflowing into a brighter colour than the ceiling allows.
        Assert.Equal(WeatherLight.At(Weather.Rain, 1, Anomaly.Chemical),
                     WeatherLight.At(Weather.Rain, 40, Anomaly.Chemical));
    }

    // ---- the ceiling ---------------------------------------------------------------------

    /// <summary>The load-bearing test of this change. If it is failing, ambient weather has
    /// started competing with the Emission, and the Emission is the only thing here that is
    /// supposed to be startling.</summary>
    [Fact]
    public void TheBrightestWeatherIsFarDimmerThanTheEmissionsOwnRed()
    {
        var weather = Brightness(Brightest());

        // The colour the buildup climbs to and the wavefront departs from.
        var emission = Brightness(EmissionLight.At(EmissionTimeline.BuildupEnds - 0.01));

        Assert.True(weather * 2 <= emission,
                    $"ambient weather reached {weather} against the Emission's {emission}");
    }

    [Fact]
    public void TheEmissionOvertakesTheWeatherWellBeforeItsWavefront()
    {
        var weather = Brightness(Brightest());
        var late = Brightness(EmissionLight.At(EmissionTimeline.BuildupEnds * 0.75));

        Assert.True(late > weather,
                    $"three quarters through the buildup the Emission was only {late} against {weather}");
    }

    [Fact]
    public void TheWavefrontDwarfsAnythingTheWeatherCanDo()
    {
        // The moment that has to land as an event, whatever the weather was doing before it.
        var flare = Brightness(EmissionLight.At(EmissionTimeline.FlarePeak));

        Assert.True(flare > Brightness(Brightest()) * 4,
                    $"the flare was only {flare} against weather at {Brightness(Brightest())}");
    }

    [Fact]
    public void TheEmissionStartsDimmerThanTheWeatherAndThatIsCorrect()
    {
        // Stated as a test so nobody "fixes" it. An Emission opens from black; if it began
        // brighter than the weather it interrupts there would be no build to it.
        Assert.True(Brightness(EmissionLight.At(0.5)) < Brightness(Brightest()));
    }

    [Fact]
    public void ABoltIsABoltWhicheverSkyItCrosses()
    {
        // Dimmed to half at first, and that was wrong. Lightning is the one part of ambient
        // weather legibly tied to something visible on screen, so it gets the same treatment
        // an Emission's does.
        Assert.Equal(EmissionLight.Strike, WeatherLight.Strike);
    }

    [Fact]
    public void PrecipitationShimmersAndFogDoesNot()
    {
        // One zone cannot scroll three sheets of rain past each other, so it wobbles instead.
        // Fog is the one weather that genuinely sits still.
        var rain = Enumerable.Range(0, 60)
            .Select(i => WeatherLight.At(Weather.Rain, 1, Anomaly.Chemical, i * 0.1))
            .Distinct().Count();

        var fog = Enumerable.Range(0, 60)
            .Select(i => WeatherLight.At(Weather.Fog, 1, Anomaly.Chemical, i * 0.1))
            .Distinct().Count();

        Assert.True(rain > 3, $"rain produced only {rain} distinct colours over six seconds");
        Assert.Equal(1, fog);
    }

    [Fact]
    public void AnAmbientStrikeStillReadsAsLightning()
    {
        // Brighter than any weather it lands on, or it is not a strike.
        Assert.True(Brightness(WeatherLight.Strike) > Brightness(Brightest()) * 2);
    }

    // ---- cross-fades -----------------------------------------------------------------------

    [Fact]
    public void SettledWeatherIsExactlyThatStatesColour()
    {
        var cycle = new WeatherCycle(new Random(1));

        var summed = WeatherLight.For(cycle.Sky, Anomaly.Chemical, 0);
        var single = WeatherLight.At(cycle.Current, 1, Anomaly.Chemical);

        Assert.Equal(single, summed);
    }

    [Fact]
    public void ATransitionLandsBetweenItsTwoEnds()
    {
        var cycle = new WeatherCycle(new Random(7));

        // Run until a cross-fade is genuinely under way.
        var guard = 0;
        while (cycle.Outgoing is null && guard++ < 100_000) cycle.Tick(0.05);

        Assert.True(cycle.Outgoing is not null, "the cycle never crossed over");

        var blended = Brightness(WeatherLight.For(cycle.Sky, Anomaly.Chemical, 0));
        var incoming = Brightness(WeatherLight.At(cycle.Current, 1, Anomaly.Chemical));
        var outgoing = Brightness(WeatherLight.At(cycle.Outgoing!.Value, 1, Anomaly.Chemical));

        Assert.InRange(blended, Math.Min(incoming, outgoing), Math.Max(incoming, outgoing));
    }

    [Fact]
    public void NoBlendEverExceedsTheCeiling()
    {
        // Both sides of a cross-fade are summed, so this is where a naive add would overflow
        // into something brighter than any single state is allowed to be.
        var cycle = new WeatherCycle(new Random(3));
        var brightest = Brightness(Brightest());

        for (var i = 0; i < 20_000; i++)
        {
            cycle.Tick(0.05);
            Assert.True(Brightness(WeatherLight.For(cycle.Sky, Anomaly.Electrical, 0)) <= brightest);
        }
    }
}
