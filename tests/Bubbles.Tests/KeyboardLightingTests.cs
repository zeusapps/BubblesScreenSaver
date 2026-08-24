using System.IO;

using Bubbles.Keyboard;
using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>The keyboard layer, with the hardware taken out.
///
/// Everything worth checking about this feature that does not need a keyboard is checked here:
/// that it opens one once and never again, that a machine without one pays nothing, that what
/// is borrowed is written down before it is taken, and that it is given back on every path that
/// ends an Emission.</summary>
public class KeyboardLightingTests : IDisposable
{
    private readonly string _stateFile = Path.Combine(
        Path.GetTempPath(), $"bubbles-keyboard-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_stateFile)) File.Delete(_stateFile);
        GC.SuppressFinalize(this);
    }

    /// <summary>A keyboard that records what it was asked to do and never fails, unless it is
    /// built to.</summary>
    private sealed class FakeKeyboard(bool present = true, bool restores = true) : IKeyboardDevice
    {
        private readonly object _gate = new();
        private readonly List<KeyColor> _shown = [];

        // Read under the lock, not just written under it. A plain int field polled from the
        // test thread can be hoisted out of the wait loop, and the test then waits forever for
        // a change it has already been told about.
        private int _opens;
        private int _darks;
        private int _restores;
        private bool _disposed;

        public int Opens { get { lock (_gate) return _opens; } }
        public int Darks { get { lock (_gate) return _darks; } }
        public int Restores { get { lock (_gate) return _restores; } }
        public bool Disposed { get { lock (_gate) return _disposed; } }

        public IReadOnlyList<KeyColor> Shown
        {
            get { lock (_gate) return _shown.ToList(); }
        }

        public KeyboardRecord? Open()
        {
            lock (_gate)
            {
                _opens++;
                if (!present) return null;

                return new KeyboardRecord { Key = "0B05:19B6", Name = "Fake Keyboard" };
            }
        }

        public bool Show(KeyColor colour)
        {
            lock (_gate) _shown.Add(colour);
            return true;
        }

        public bool GoDark()
        {
            lock (_gate) _darks++;
            return true;
        }

        public bool Restore(KeyboardRecord record)
        {
            lock (_gate) _restores++;
            return restores;
        }

        public void Dispose() { lock (_gate) _disposed = true; }
    }

    private KeyboardLighting Layer(
        FakeKeyboard keyboard, bool on = true, bool weather = false, Action<int>? onConnect = null)
    {
        var connects = 0;

        return new KeyboardLighting(
            new Settings { KeyboardLighting = on, KeyboardWeather = weather },
            () =>
            {
                onConnect?.Invoke(++connects);
                return keyboard;
            },
            _stateFile);
    }

    /// <summary>The worker is a background thread, so the assertions have to wait for it. Short
    /// and polled, so a passing test costs milliseconds and a failing one still fails.</summary>
    private static void Until(Func<bool> done, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (done()) return;
            Thread.Sleep(5);
        }

        Assert.Fail($"timed out waiting for {what}");
    }

    // ---- connecting ---------------------------------------------------------------------

    [Fact]
    public void WithTheSettingOffNothingIsEverReachedFor()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, on: false);

        layer.EmissionBegan();
        for (var t = 0.0; t < 12; t += 0.1) layer.Frame(t, striking: false);
        layer.WentDark();

        // Nothing to wait for: with the setting off, not one of those calls may have started
        // a thread, opened a socket, or asked anything of a keyboard.
        Thread.Sleep(100);

        Assert.Equal(0, keyboard.Opens);
        Assert.Empty(keyboard.Shown);
        Assert.Equal(0, keyboard.Darks);
    }

    [Fact]
    public void TheKeyboardIsOpenedOnceAcrossManyEmissions()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        for (var emission = 0; emission < 3; emission++)
        {
            layer.EmissionBegan();
            for (var t = 0.0; t < 12; t += 0.5) layer.Frame(t, striking: false);
            layer.WentDark();
            layer.LeftDark();
        }

        Until(() => keyboard.Restores > 0, "the keyboard to be handed back");

        Assert.Equal(1, keyboard.Opens);
    }

    [Fact]
    public void AMachineWithNoKeyboardIsAskedExactlyOnce()
    {
        var keyboard = new FakeKeyboard(present: false);
        using var layer = Layer(keyboard);

        layer.EmissionBegan();
        layer.Frame(1, striking: false);

        Until(() => keyboard.Opens == 1, "the one attempt");

        // The rest of this Emission, and every Emission after it, in silence.
        for (var t = 1.0; t < 12; t += 0.25) layer.Frame(t, striking: true);
        layer.WentDark();
        layer.LeftDark();

        layer.EmissionBegan();
        for (var t = 0.0; t < 12; t += 0.25) layer.Frame(t, striking: false);

        Thread.Sleep(150);

        Assert.Equal(1, keyboard.Opens);
        Assert.Empty(keyboard.Shown);
        Assert.Equal(0, keyboard.Darks);
        Assert.True(keyboard.Disposed, "the client was left open after it came to nothing");
    }

    [Fact]
    public void NothingIsOwedWhenNoKeyboardWasFound()
    {
        var keyboard = new FakeKeyboard(present: false);
        using var layer = Layer(keyboard);

        layer.EmissionBegan();
        layer.Frame(1, striking: false);

        Until(() => keyboard.Opens == 1, "the one attempt");

        Assert.Equal(0, layer.Owed);
        Assert.False(File.Exists(_stateFile), "a debt was recorded against a keyboard that was never found");
    }

    // ---- what is owed -------------------------------------------------------------------

    [Fact]
    public void TheModeIsOnDiskBeforeTheFirstColourIsSent()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);

        Until(() => keyboard.Shown.Count > 0, "the first colour");

        // Recorded, and recorded first: by the time anything had been shown, the file was
        // already there.
        Assert.True(File.Exists(_stateFile));
        Assert.Equal(1, layer.Owed);
        Assert.Contains("Fake Keyboard", File.ReadAllText(_stateFile));
    }

    [Fact]
    public void WakingHandsTheKeyboardBack()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => keyboard.Shown.Count > 0, "the first colour");

        layer.WentDark();
        Until(() => keyboard.Darks == 1, "the blackout");

        layer.LeftDark();
        Until(() => layer.Owed == 0, "the debt to be settled");

        Assert.Equal(1, keyboard.Restores);
        Assert.False(File.Exists(_stateFile), "the record outlived the restore");
    }

    [Fact]
    public void AKeyboardThatWillNotConfirmStaysOwed()
    {
        var keyboard = new FakeKeyboard(restores: false);
        using var layer = Layer(keyboard);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => keyboard.Shown.Count > 0, "the first colour");

        layer.LeftDark();
        Until(() => keyboard.Restores == 1, "the attempt to hand it back");

        // Attempted is not restored. The record has to survive, or nothing will ever put this
        // keyboard right.
        Assert.Equal(1, layer.Owed);
        Assert.True(File.Exists(_stateFile));
    }

    [Fact]
    public void ExitHandsTheKeyboardBack()
    {
        var keyboard = new FakeKeyboard();
        var layer = Layer(keyboard);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => keyboard.Shown.Count > 0, "the first colour");

        layer.Dispose();

        Assert.Equal(1, keyboard.Restores);
        Assert.Equal(0, layer.Owed);
    }

    [Fact]
    public void ARecordLeftByAPreviousRunIsSettledAtStartup()
    {
        File.WriteAllText(_stateFile,
            """
            [{"Key":"0B05:19B6","Name":"Fake Keyboard"}]
            """);

        var keyboard = new FakeKeyboard();

        // Off, deliberately: somebody who turned this off after a crash is still owed their
        // keyboard back.
        using var layer = Layer(keyboard, on: false);

        layer.RecoverFromCrash();

        Until(() => keyboard.Restores == 1, "the previous run's debt");
        Until(() => layer.Owed == 0, "the record to be cleared");

        Assert.Equal(0, keyboard.Opens);
        Assert.Empty(keyboard.Shown);
    }

    [Fact]
    public void AStartupWithNothingOwedTouchesNothing()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        layer.RecoverFromCrash();
        Thread.Sleep(100);

        Assert.Equal(0, keyboard.Opens);
        Assert.Equal(0, keyboard.Restores);
    }

    // ---- the send policy ----------------------------------------------------------------

    [Fact]
    public void TheEmissionStillEndsOnBlackThroughTheWholeStack()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        layer.EmissionBegan();
        for (var t = 0.0; t <= 12.5; t += 1.0 / 30) layer.Frame(t, striking: false);

        Until(() => keyboard.Shown.LastOrDefault().IsBlack, "the keys to arrive at black");
    }

    [Fact]
    public void TheArtifactsStageIsLeftAlone()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        // Hours of artifacts drifting about. The overlay raises nothing at the keyboard for
        // any of it -- EmissionFrame is raised from inside the Emission's own branch of the
        // render loop -- so the layer hears nothing, opens nothing, and sends nothing.
        Thread.Sleep(100);

        Assert.Equal(0, keyboard.Opens);
        Assert.Empty(keyboard.Shown);
        Assert.Equal(0, layer.Owed);
        Assert.False(File.Exists(_stateFile));
    }

    [Fact]
    public void APlainFadeStillTakesTheKeyboardDark()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        // A blackout that was not an Emission: no build-up, no frames, straight to black. A
        // lit keyboard beside it would give the black away just as surely.
        layer.WentDark();

        Until(() => keyboard.Darks == 1, "the blackout");

        Assert.Empty(keyboard.Shown);
    }

    // ---- the weather ---------------------------------------------------------------------

    /// <summary>A cycle settled on one state, which is what the overlay hands over most of the
    /// time.</summary>
    private static WeatherCycle Settled(Weather wanted)
    {
        for (var seed = 0; seed < 500; seed++)
        {
            var cycle = new WeatherCycle(new Random(seed));
            if (cycle.Current == wanted && cycle.Outgoing is null) return cycle;
        }

        throw new InvalidOperationException($"no seed produced settled {wanted}");
    }

    private static void Weather(KeyboardLighting layer, WeatherCycle cycle, int frames = 120)
    {
        for (var i = 0; i < frames; i++) layer.Weather(cycle.Sky, Anomaly.Chemical, striking: false);
    }

    [Fact]
    public void WithTheWeatherSettingOffTheSkyReachesNothing()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, on: true, weather: false);

        Weather(layer, Settled(Zone.Weather.Rain));
        Thread.Sleep(100);

        Assert.Equal(0, keyboard.Opens);
        Assert.Empty(keyboard.Shown);
    }

    [Fact]
    public void TheWeatherNeedsTheMasterSwitchToo()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, on: false, weather: true);

        Weather(layer, Settled(Zone.Weather.Rain));
        Thread.Sleep(100);

        Assert.Equal(0, keyboard.Opens);
        Assert.Empty(keyboard.Shown);
    }

    [Fact]
    public void RainLightsTheKeys()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, weather: true);

        Weather(layer, Settled(Zone.Weather.Rain));

        Until(() => keyboard.Shown.Any(c => !c.IsBlack), "the weather to reach the keys");
    }

    [Fact]
    public void AClearSkyLeavesTheKeysDarkButTheDeviceHeld()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, weather: true);

        Weather(layer, Settled(Zone.Weather.Clear));

        Until(() => keyboard.Opens == 1, "the keyboard to be opened");

        // Opened and owed, so it is genuinely held -- releasing it on every clear spell would
        // hand it back to the vendor's software, which would light it.
        Assert.Equal(1, layer.Owed);
        Assert.All(keyboard.Shown, colour => Assert.True(colour.IsBlack));
    }

    [Fact]
    public void AnEmissionTakesTheKeyboardFromTheWeather()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, weather: true);

        var sky = Settled(Zone.Weather.Rain);
        Weather(layer, sky);
        Until(() => keyboard.Shown.Any(c => !c.IsBlack), "the weather to reach the keys");

        layer.EmissionBegan();

        // Weather frames arriving alongside an Emission are dropped, not queued behind it.
        var before = keyboard.Shown.Count;
        Weather(layer, sky);
        Thread.Sleep(100);

        Assert.Equal(before, keyboard.Shown.Count);
    }

    [Fact]
    public void TheWeatherComesBackAfterABlackout()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, weather: true);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => keyboard.Shown.Count > 0, "the Emission");

        layer.WentDark();
        Until(() => keyboard.Darks == 1, "the blackout");

        layer.LeftDark();
        Until(() => layer.Owed == 0, "the hand-back");

        var after = keyboard.Shown.Count;
        Weather(layer, Settled(Zone.Weather.Fog));

        Until(() => keyboard.Shown.Count > after, "the weather to resume");
    }

    [Fact]
    public void GoingDarkIsNotAColour()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => keyboard.Shown.Count > 0, "the first colour");

        layer.WentDark();

        // Asked to go dark, not merely shown black: on a keyboard that can unpower its
        // backlight those are different, and the darker one is the point.
        Until(() => keyboard.Darks == 1, "the blackout");
    }
}
