using System.Reflection;

using Bubbles.Keyboard;
using Bubbles.Overlay;

namespace Bubbles.Tests;

/// <summary>The keyboard's half of an Emission, which is a pure function of elapsed time and
/// therefore the half that can be checked without a keyboard.</summary>
public class EmissionLightTests
{
    [Fact]
    public void StartsBlack()
    {
        Assert.Equal(KeyColor.Black, EmissionLight.At(0));
        Assert.Equal(KeyColor.Black, EmissionLight.At(-1));
    }

    [Fact]
    public void ArrivesAtBlackWhenTheScreenDoes()
    {
        Assert.Equal(KeyColor.Black, EmissionLight.At(EmissionTimeline.DarknessAt));

        // And stays there. A blackout outlives the Emission that reached it.
        Assert.Equal(KeyColor.Black, EmissionLight.At(EmissionTimeline.DarknessAt + 30));
    }

    [Fact]
    public void RedRisesThroughTheBuildup()
    {
        var previous = -1;

        for (var t = 0.0; t < EmissionTimeline.BuildupEnds; t += 0.1)
        {
            var colour = EmissionLight.At(t);
            Assert.True(colour.R >= previous, $"red fell back at {t:N1}s");
            previous = colour.R;
        }

        Assert.True(previous > 0x80, "the buildup never got anywhere near its deepest red");
    }

    [Fact]
    public void TheBuildupIsRed()
    {
        for (var t = 0.5; t < EmissionTimeline.BuildupEnds; t += 0.25)
        {
            var colour = EmissionLight.At(t);
            Assert.True(colour.R > colour.G, $"green caught red at {t:N1}s");
            Assert.True(colour.R > colour.B, $"blue caught red at {t:N1}s");
        }
    }

    [Fact]
    public void TheFlareIsTheBrightestMomentAndTheWhitest()
    {
        var flare = EmissionLight.At(EmissionTimeline.FlarePeak);

        for (var t = 0.0; t < EmissionTimeline.BuildupEnds; t += 0.1)
        {
            var buildup = EmissionLight.At(t);
            Assert.True(Brightness(flare) > Brightness(buildup), $"the buildup at {t:N1}s outshone the flare");
            Assert.True(flare.G >= buildup.G, $"green was higher at {t:N1}s than at the flare");
            Assert.True(flare.B >= buildup.B, $"blue was higher at {t:N1}s than at the flare");
        }

        // Toward white, not merely brighter red.
        Assert.True(flare.G > 0xC0);
        Assert.True(flare.B > 0xA0);
    }

    [Fact]
    public void TheFlareIsOverBeforeTheWavefrontIs()
    {
        var flare = EmissionLight.At(EmissionTimeline.FlarePeak);
        var after = EmissionLight.At(EmissionTimeline.WaveEnds - 0.05);

        Assert.True(Brightness(after) < Brightness(flare));
        Assert.True(after.R > after.G, "the keys were still washed out when the wavefront ended");
    }

    [Fact]
    public void FadesAwayThroughTheDarkness()
    {
        var previous = int.MaxValue;

        for (var t = EmissionTimeline.WaveEnds; t <= EmissionTimeline.DarknessAt; t += 0.1)
        {
            var brightness = Brightness(EmissionLight.At(t));
            Assert.True(brightness <= previous, $"the keys brightened again at {t:N1}s");
            previous = brightness;
        }
    }

    [Fact]
    public void TheSameTimeGivesTheSameColour()
    {
        foreach (var t in new[] { 0.0, 1.75, EmissionTimeline.BuildupEnds, EmissionTimeline.FlarePeak, 10.0 })
            Assert.Equal(EmissionLight.At(t), EmissionLight.At(t));
    }

    [Fact]
    public void NeverThrowsOnATimeItCannotHave()
    {
        Assert.Equal(KeyColor.Black, EmissionLight.At(double.NaN));
        Assert.Equal(KeyColor.Black, EmissionLight.At(double.PositiveInfinity));
        Assert.Equal(KeyColor.Black, EmissionLight.At(double.NegativeInfinity));
    }

    /// <summary>The claim the whole design rests on: the keys and the screen read one clock.
    ///
    /// Reflection rather than a comparison of literals, because what has to be true is that
    /// the overlay's own constants *are* these -- somebody re-hardcoding 6.5 into
    /// OverlayWindow would pass any test written against the numbers themselves.</summary>
    [Theory]
    [InlineData("BuildupEnds", EmissionTimeline.BuildupEnds)]
    [InlineData("WaveEnds", EmissionTimeline.WaveEnds)]
    [InlineData("DarknessAt", EmissionTimeline.DarknessAt)]
    public void TheOverlayAnimatesAgainstTheSameConstants(string name, double expected)
    {
        var field = typeof(OverlayWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.Equal(expected, Assert.IsType<double>(field!.GetRawConstantValue()));
    }

    private static int Brightness(KeyColor colour) => colour.R + colour.G + colour.B;
}
