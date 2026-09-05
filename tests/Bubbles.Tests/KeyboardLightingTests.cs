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

    private readonly string _lightingFile = Path.Combine(
        Path.GetTempPath(), $"bubbles-dynamic-lighting-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_stateFile)) File.Delete(_stateFile);
        if (File.Exists(_lightingFile)) File.Delete(_lightingFile);
        GC.SuppressFinalize(this);
    }

    /// <summary>A keyboard that records what it was asked to do and never fails, unless it is
    /// built to.
    ///
    /// As unforgiving as the real one about the two things that matter. Restoring closes it,
    /// because on the hardware giving the keyboard back *is* letting go of the handle -- a fake
    /// that stayed open through a hand-back hid a defect for as long as it existed, since no
    /// test could reach the state the application spent every second blackout in. And writes
    /// can be made to fail, because they do.</summary>
    private sealed class FakeKeyboard(bool present = true, bool restores = true) : IKeyboardDevice
    {
        private readonly object _gate = new();
        private readonly List<KeyColor> _shown = [];

        /// <summary>When each black landed. The interval between re-asserts is a requirement in
        /// its own right now that it no longer ramps, and the only way to check it from out here
        /// is to time the arrivals.</summary>
        private readonly List<long> _darkTimes = [];

        // Read under the lock, not just written under it. A plain int field polled from the
        // test thread can be hoisted out of the wait loop, and the test then waits forever for
        // a change it has already been told about.
        private int _opens;
        private int _darks;
        private int _restores;
        private bool _disposed;
        private bool _open;
        private bool _refusing;

        public int Opens { get { lock (_gate) return _opens; } }
        public int Darks { get { lock (_gate) return _darks; } }
        public int Restores { get { lock (_gate) return _restores; } }
        public bool Disposed { get { lock (_gate) return _disposed; } }
        public bool IsOpen { get { lock (_gate) return _open; } }

        public IReadOnlyList<KeyColor> Shown
        {
            get { lock (_gate) return _shown.ToList(); }
        }

        /// <summary>The gaps between successive blacks, in milliseconds.</summary>
        public IReadOnlyList<long> DarkGaps
        {
            get
            {
                lock (_gate)
                    return _darkTimes.Zip(_darkTimes.Skip(1), (a, b) => b - a).ToList();
            }
        }

        /// <summary>Makes every write from here on fail, as a keyboard that has been unplugged
        /// does. Like the real device, refusing a write also lets go of it.</summary>
        public void RefuseWrites() { lock (_gate) _refusing = true; }

        /// <summary>Lets go of the handle without being asked and without failing anything, as a
        /// device does when something below it takes it away -- a suspend, a re-enumeration. It
        /// will open again; it just is not open now.</summary>
        public void LetGo() { lock (_gate) _open = false; }

        public KeyboardRecord? Open()
        {
            lock (_gate)
            {
                _opens++;
                if (!present) return null;

                _open = true;

                return new KeyboardRecord { Key = "0B05:19B6", Name = "Fake Keyboard" };
            }
        }

        public bool Show(KeyColor colour)
        {
            lock (_gate)
            {
                if (_refusing || !_open) { _open = false; return false; }

                _shown.Add(colour);
                return true;
            }
        }

        public bool GoDark()
        {
            lock (_gate)
            {
                if (_refusing || !_open) { _open = false; return false; }

                _darks++;
                _darkTimes.Add(Environment.TickCount64);
                return true;
            }
        }

        public bool Restore(KeyboardRecord record)
        {
            lock (_gate)
            {
                _restores++;
                _open = false;
                return restores;
            }
        }

        public void Dispose() { lock (_gate) { _disposed = true; _open = false; } }
    }

    /// <summary>Windows' Dynamic Lighting toggle, with the registry taken out.
    ///
    /// Counts its reads as well as its writes, because half of what is claimed here is that a
    /// machine which has not asked for this is never even asked the question.</summary>
    private sealed class FakeAmbientLighting(bool? enabled = true, bool writable = true) : IAmbientLighting
    {
        private readonly object _gate = new();

        private bool? _enabled = enabled;
        private int _reads;
        private int _writes;
        private bool? _recordedWhenChanged;

        public int Reads { get { lock (_gate) return _reads; } }
        public int Writes { get { lock (_gate) return _writes; } }
        public bool? Enabled { get { lock (_gate) return _enabled; } }

        /// <summary>Something to look at the instant the value is first changed, so the order
        /// of "write it down" and "change it" can be asserted rather than assumed.</summary>
        public Func<bool>? Watch { get; set; }

        /// <summary>What <see cref="Watch"/> saw at the moment of the first write.</summary>
        public bool? RecordedWhenChanged { get { lock (_gate) return _recordedWhenChanged; } }

        public bool? Read()
        {
            lock (_gate)
            {
                _reads++;
                return _enabled;
            }
        }

        public bool Write(bool value)
        {
            lock (_gate)
            {
                _writes++;
                _recordedWhenChanged ??= Watch?.Invoke();

                if (!writable) return false;

                _enabled = value;
                return true;
            }
        }
    }

    /// <param name="cadence">How often the blackout says black again. Long enough by default that
    /// no test sees a second one unless it asked for it -- a re-assert arriving in the middle of a
    /// test about something else would make the counts it asserts a race.</param>
    private KeyboardLighting Layer(
        FakeKeyboard keyboard, bool on = true, bool weather = false, Action<int>? onConnect = null,
        FakeAmbientLighting? lighting = null, bool standDown = false, TimeSpan? cadence = null)
    {
        var connects = 0;

        return new KeyboardLighting(
            new Settings
            {
                KeyboardLighting = on,
                KeyboardWeather = weather,
                StandDynamicLightingDown = standDown,
            },
            () =>
            {
                onConnect?.Invoke(++connects);
                return keyboard;
            },
            _stateFile,
            new DynamicLightingLoan(lighting ?? new FakeAmbientLighting(), _lightingFile),
            cadence: cadence ?? TimeSpan.FromMinutes(10));
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

    /// <summary>The test that used to live here counted opens and asserted there had been
    /// exactly one across three blackouts. That is the symptom, not the property: the reason
    /// there was one open was that the layer went on believing it held a keyboard it had given
    /// back, and every colour after the first blackout went to a closed handle in silence. What
    /// was wanted was that the keys go dark with the screen. So that is what is asserted.</summary>
    [Fact]
    public void EveryBlackoutTakesTheKeyboardDark()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        for (var emission = 1; emission <= 3; emission++)
        {
            var before = keyboard.Shown.Count;

            layer.EmissionBegan();
            for (var t = 0.0; t < 12; t += 0.5) layer.Frame(t, striking: false);

            Until(() => keyboard.Shown.Count > before, $"Emission {emission} to reach the keys");

            layer.WentDark();
            Until(() => keyboard.Darks == emission, $"blackout {emission}");

            layer.LeftDark();
            Until(() => keyboard.Restores == emission, $"hand-back {emission}");
        }

        Assert.Equal(3, keyboard.Darks);
        Assert.Equal(3, keyboard.Restores);
    }

    /// <summary>The debt is a fresh one on each loan. It was written down before the first
    /// colour of the first Emission and cleared on waking; the second Emission borrows the
    /// keyboard again, and a crash during it has to leave a record behind just as the first
    /// would have.</summary>
    [Fact]
    public void ASecondLoanIsWrittenDownLikeTheFirst()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => layer.Owed == 1, "the first loan on the books");

        layer.WentDark();
        layer.LeftDark();
        Until(() => layer.Owed == 0, "the first hand-back");
        Assert.False(File.Exists(_stateFile), "the record outlived the hand-back");

        var before = keyboard.Shown.Count;

        layer.EmissionBegan();
        layer.Frame(2, striking: false);

        Until(() => keyboard.Shown.Count > before, "the second Emission to reach the keys");

        Assert.Equal(1, layer.Owed);
        Assert.True(File.Exists(_stateFile), "the second loan was taken without being recorded");
    }

    /// <summary>A keyboard that stops answering is given up on, not reopened. The alternative
    /// is a device that accepts a handle and refuses every write, reopened once per rationed
    /// colour for the rest of the session.</summary>
    [Fact]
    public void AKeyboardThatStopsAnsweringIsDroppedForTheSession()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => keyboard.Shown.Count > 0, "the first colour");

        keyboard.RefuseWrites();

        var opens = keyboard.Opens;
        var shown = keyboard.Shown.Count;

        for (var t = 3.0; t < 12; t += 0.25) layer.Frame(t, striking: true);
        layer.WentDark();

        Thread.Sleep(150);

        Assert.Equal(shown, keyboard.Shown.Count);
        Assert.Equal(opens, keyboard.Opens);
        Assert.Equal(0, keyboard.Darks);
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

    // ---- standing Dynamic Lighting down ---------------------------------------------------

    /// <summary>The whole point of the borrow: what was there is written down, and only then is
    /// it changed. The fake looks at the disk from inside its own write, so this is the real
    /// order rather than an inference from the end state.</summary>
    [Fact]
    public void TheValueIsWrittenDownBeforeDynamicLightingIsChanged()
    {
        var keyboard = new FakeKeyboard();
        var lighting = new FakeAmbientLighting(enabled: true)
        {
            Watch = () => File.Exists(_lightingFile),
        };

        using var layer = Layer(keyboard, standDown: true, lighting: lighting);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);

        Until(() => lighting.Writes > 0, "Dynamic Lighting to be stood down");

        Assert.Equal(false, lighting.Enabled);
        Assert.Equal(1, layer.DynamicLightingOwed);
        Assert.True(lighting.RecordedWhenChanged, "the value was changed before it was recorded");
        Assert.Contains("AmbientLightingEnabled", File.ReadAllText(_lightingFile));
    }

    [Fact]
    public void WakingPutsDynamicLightingBackAndClearsTheRecord()
    {
        var keyboard = new FakeKeyboard();
        var lighting = new FakeAmbientLighting(enabled: true);

        using var layer = Layer(keyboard, standDown: true, lighting: lighting);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => lighting.Enabled == false, "Dynamic Lighting to be stood down");

        layer.WentDark();
        layer.LeftDark();

        Until(() => layer.DynamicLightingOwed == 0, "the Dynamic Lighting debt to be settled");

        Assert.Equal(true, lighting.Enabled);
        Assert.False(File.Exists(_lightingFile), "the record outlived the restore");
    }

    /// <summary>The reason the recorded value is restored rather than a fixed one. Handing back
    /// "on" here would switch on a feature this machine had deliberately turned off -- giving
    /// back something that was never taken.</summary>
    [Fact]
    public void AMachineAlreadyOffIsLeftOff()
    {
        var keyboard = new FakeKeyboard();
        var lighting = new FakeAmbientLighting(enabled: false);

        using var layer = Layer(keyboard, standDown: true, lighting: lighting);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => layer.DynamicLightingOwed == 1, "the loan on the books");

        Assert.Contains("\"Enabled\":false", File.ReadAllText(_lightingFile));

        layer.LeftDark();
        Until(() => layer.DynamicLightingOwed == 0, "the debt to be settled");

        Assert.Equal(false, lighting.Enabled);
    }

    /// <summary>Keyboard lighting on, this setting off: the keys are driven and no Windows
    /// setting is touched. Not even read -- somebody who did not ask for this should not have
    /// their personalization settings inspected on their behalf.</summary>
    [Fact]
    public void WithTheSettingOffDynamicLightingIsNotEvenRead()
    {
        var keyboard = new FakeKeyboard();
        var lighting = new FakeAmbientLighting(enabled: true);

        using var layer = Layer(keyboard, standDown: false, lighting: lighting);

        layer.EmissionBegan();
        for (var t = 0.0; t < 12; t += 0.5) layer.Frame(t, striking: false);
        Until(() => keyboard.Shown.Count > 0, "the keys to be driven at all");

        layer.WentDark();
        layer.LeftDark();
        Until(() => layer.Owed == 0, "the keyboard hand-back");

        Assert.Equal(0, lighting.Reads);
        Assert.Equal(0, lighting.Writes);
        Assert.Equal(0, layer.DynamicLightingOwed);
        Assert.False(File.Exists(_lightingFile));
    }

    /// <summary>This setting on, keyboard lighting off: it is subordinate, so it does nothing on
    /// its own account.</summary>
    [Fact]
    public void WithKeyboardLightingOffDynamicLightingIsNotEvenRead()
    {
        var keyboard = new FakeKeyboard();
        var lighting = new FakeAmbientLighting(enabled: true);

        using var layer = Layer(keyboard, on: false, standDown: true, lighting: lighting);

        layer.EmissionBegan();
        for (var t = 0.0; t < 12; t += 0.5) layer.Frame(t, striking: false);
        layer.WentDark();

        Thread.Sleep(100);

        Assert.Equal(0, lighting.Reads);
        Assert.Equal(0, lighting.Writes);
        Assert.Equal(0, layer.DynamicLightingOwed);
        Assert.False(File.Exists(_lightingFile));
    }

    /// <summary>A run that died with Dynamic Lighting switched off. The record outlives it, and
    /// the next start puts the setting back -- with both settings off, because the debt is not
    /// conditional on the setting that incurred it.</summary>
    [Fact]
    public void ADynamicLightingRecordLeftByAPreviousRunIsSettledAtStartup()
    {
        File.WriteAllText(_lightingFile,
            """
            [{"Key":"AmbientLightingEnabled","Enabled":true}]
            """);

        var keyboard = new FakeKeyboard();
        var lighting = new FakeAmbientLighting(enabled: false);

        using var layer = Layer(keyboard, on: false, standDown: false, lighting: lighting);

        layer.RecoverFromCrash();

        Until(() => lighting.Enabled == true, "the previous run's Dynamic Lighting debt");
        Until(() => layer.DynamicLightingOwed == 0, "the record to be cleared");

        Assert.False(File.Exists(_lightingFile));

        // And nothing was sent to a keyboard on the way: the two debts are settled separately.
        Assert.Equal(0, keyboard.Opens);
        Assert.Empty(keyboard.Shown);
    }

    /// <summary>A toggle that cannot be read is not a loan. Recording a value that was never
    /// observed would mean writing a guess back to somebody's registry on the next wake.</summary>
    [Fact]
    public void AToggleThatCannotBeReadIsNotBorrowed()
    {
        var keyboard = new FakeKeyboard();
        var lighting = new FakeAmbientLighting(enabled: null);

        using var layer = Layer(keyboard, standDown: true, lighting: lighting);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => lighting.Reads > 0, "the attempt to read it");

        Thread.Sleep(50);

        Assert.Equal(0, lighting.Writes);
        Assert.Equal(0, layer.DynamicLightingOwed);
        Assert.False(File.Exists(_lightingFile));
    }

    /// <summary>Recorded ahead of a change that was then refused. Nothing moved, so nothing is
    /// owed -- and the next run must not find a record telling it to switch Dynamic Lighting
    /// back on.</summary>
    [Fact]
    public void AChangeThatWasRefusedLeavesNothingOwed()
    {
        var keyboard = new FakeKeyboard();
        var lighting = new FakeAmbientLighting(enabled: true, writable: false);

        using var layer = Layer(keyboard, standDown: true, lighting: lighting);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => lighting.Writes > 0, "the attempt to stand it down");
        Until(() => layer.DynamicLightingOwed == 0, "the record to be dropped");

        Assert.Equal(true, lighting.Enabled);
        Assert.False(File.Exists(_lightingFile));
    }

    [Fact]
    public void ExitPutsDynamicLightingBack()
    {
        var keyboard = new FakeKeyboard();
        var lighting = new FakeAmbientLighting(enabled: true);

        var layer = Layer(keyboard, standDown: true, lighting: lighting);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => lighting.Enabled == false, "Dynamic Lighting to be stood down");

        layer.Dispose();

        Assert.Equal(true, lighting.Enabled);
        Assert.Equal(0, layer.DynamicLightingOwed);
    }

    /// <summary>One loan, not one per rationed colour. Every frame passes through the same
    /// path, and a registry write per frame would be both pointless and visible.</summary>
    [Fact]
    public void TheLoanIsTakenOnceForAnEmission()
    {
        var keyboard = new FakeKeyboard();
        var lighting = new FakeAmbientLighting(enabled: true);

        using var layer = Layer(keyboard, standDown: true, lighting: lighting);

        layer.EmissionBegan();
        for (var t = 0.0; t < 12; t += 1.0 / 30) layer.Frame(t, striking: false);

        Until(() => lighting.Writes > 0, "Dynamic Lighting to be stood down");
        Thread.Sleep(100);

        Assert.Equal(1, lighting.Writes);
        Assert.Equal(1, lighting.Reads);
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

    // ---- holding the blackout ------------------------------------------------------------

    [Fact]
    public void TheKeysAreHeldDarkForAsLongAsTheScreenIs()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, cadence: TimeSpan.FromMilliseconds(15));

        layer.WentDark();

        // One GoDark is what used to happen, and five to ten seconds later the vendor's software
        // had the keys back. Black is a request, not a lock: it has to be made again.
        Until(() => keyboard.Darks > 1, "the blackout to be held");
        Assert.True(keyboard.IsOpen, "the device is still held, not released between re-asserts");
    }

    [Fact]
    public void NothingIsReassertedOnceTheBlackoutEnds()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, cadence: TimeSpan.FromMilliseconds(15));

        layer.WentDark();
        Until(() => keyboard.Darks > 1, "the blackout to be held");

        layer.LeftDark();
        Until(() => layer.Owed == 0, "the hand-back");

        var after = keyboard.Darks;
        Thread.Sleep(100);   // many cadences' worth

        Assert.Equal(after, keyboard.Darks);
    }

    [Fact]
    public void AReassertThatIsRefusedEndsTheSessionsLighting()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, cadence: TimeSpan.FromMilliseconds(15));

        layer.WentDark();
        Until(() => keyboard.Darks == 1, "the blackout");

        keyboard.RefuseWrites();

        // The refusal is noticed on the next re-assert, and that is the end of it: no reopening,
        // no loop of open-fail-open-fail once every cadence for the rest of the blackout.
        Until(() => !keyboard.IsOpen, "the refusal to be noticed");

        var opens = keyboard.Opens;
        Thread.Sleep(100);

        Assert.Equal(opens, keyboard.Opens);
    }

    [Fact]
    public void ADeviceThatLetGoDuringTheBlackoutIsOpenedAgain()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, cadence: TimeSpan.FromMilliseconds(15));

        layer.WentDark();
        Until(() => keyboard.Darks == 1, "the blackout");

        var darks = keyboard.Darks;
        keyboard.LetGo();

        // Handed back is not the same as refused. The search succeeded and the keyboard is still
        // there, so the black lands again rather than the session giving up on it.
        Until(() => keyboard.Darks > darks, "the black to land again");
        Assert.True(keyboard.Opens > 1, "the device was opened again");
    }

    [Fact]
    public void TheReassertIntervalDoesNotDrift()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, cadence: TimeSpan.FromMilliseconds(30));

        layer.WentDark();

        // Seven blacks is six gaps. Under the ramp this replaced -- half again each time -- the
        // sixth gap would be seven and a half times the first, which is what this rules out.
        Until(() => keyboard.Darks >= 7, "several re-asserts");

        var gaps = keyboard.DarkGaps;
        var min = gaps.Min();
        var max = gaps.Max();

        // Measured against the smallest gap rather than the first. The first is the noisiest --
        // it carries the device open and the state file write -- and anchoring the tolerance to
        // it was slack enough to let a real ramp through.
        //
        // Generous all the same, because this is a BelowNormal thread on a machine running a
        // test suite. It is the *shape* that is the requirement, and it survives the noise: the
        // ramp this replaced would put the sixth gap at seven and a half times the first.
        Assert.True(
            max <= min * 3 + 120,
            $"gaps stay flat, but {string.Join("ms, ", gaps)}ms");
    }

    [Fact]
    public void ADisturbanceSaysBlackAgainWithoutWaitingOutTheInterval()
    {
        var keyboard = new FakeKeyboard();

        // An interval far longer than the test can wait for: the only thing that can produce a
        // second black here is the disturbance itself.
        using var layer = Layer(keyboard, cadence: TimeSpan.FromMinutes(10));

        layer.WentDark();
        Until(() => keyboard.Darks == 1, "the blackout");

        layer.Disturbed("a test");

        Until(() => keyboard.Darks > 1, "the disturbance to reach the keys");
    }

    [Fact]
    public void ADisturbanceDoesNotChangeTheIntervalThatFollowsIt()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, cadence: TimeSpan.FromMilliseconds(30));

        layer.WentDark();
        Until(() => keyboard.Darks >= 4, "the interval before");

        layer.Disturbed("a test");

        Until(() => keyboard.Darks >= 9, "the interval after");

        // The disturbance itself lands early by design -- that is the whole of what it does now --
        // so what is checked is the interval either side of it, not the gap it sits in. Under the
        // ramp this replaced, a disturbance also sent the interval back to its floor, and the two
        // sides would differ.
        var gaps = keyboard.DarkGaps;
        var before = gaps.Take(3).Min();

        Assert.All(gaps.Skip(5), gap => Assert.True(
            gap <= before * 3 + 120,
            $"the interval is unchanged by a disturbance, but {string.Join("ms, ", gaps)}ms"));
    }

    [Fact]
    public void ADisturbanceDoesNotEndTheBlackout()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, cadence: TimeSpan.FromMilliseconds(15));

        layer.WentDark();
        Until(() => keyboard.Darks > 1, "the blackout to be held");

        layer.Disturbed("a test");
        Thread.Sleep(50);

        // A bare wake asks for nothing. It must not be mistaken for a colour arriving, which is
        // what would cancel the hold and leave the keys to whatever painted them next.
        var darks = keyboard.Darks;
        Until(() => keyboard.Darks > darks, "the blackout to still be held afterwards");
        Assert.Empty(keyboard.Shown);
    }

    [Fact]
    public void ADisturbanceOnAMachineThatNeverTookAKeyboardCostsNothing()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, cadence: TimeSpan.FromMilliseconds(15));

        layer.Disturbed("a test");
        Thread.Sleep(100);

        // No worker, no loan, nothing to reassert. The overwhelming majority of machines spend
        // their whole lives here and must not start a thread for a session lock.
        Assert.Equal(0, keyboard.Opens);
        Assert.Equal(0, keyboard.Darks);
    }

    [Fact]
    public void WithTheSettingOffTheBlackoutIsNeverHeld()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard, on: false, cadence: TimeSpan.FromMilliseconds(15));

        layer.WentDark();
        Thread.Sleep(100);   // many cadences' worth, on a machine that asked for none of this

        Assert.Equal(0, keyboard.Opens);
        Assert.Equal(0, keyboard.Darks);
    }

    [Fact]
    public void TheArtifactsStageGivesTheKeyboardBackWithoutABlackout()
    {
        var keyboard = new FakeKeyboard();
        var lighting = new FakeAmbientLighting(enabled: true);
        using var layer = Layer(keyboard, weather: true, lighting: lighting, standDown: true);

        Weather(layer, Settled(Zone.Weather.Fog));
        Until(() => keyboard.Shown.Count > 0, "the weather to reach the keys");
        Assert.Equal(false, lighting.Enabled);

        // The screensaver leaves the screen without ever having reached black -- you came back
        // to your desk before the blackout. That is the end of the loan just as much as waking
        // from a blackout is, and it used to end nothing at all.
        layer.LeftDark();

        Until(() => layer.Owed == 0, "the keyboard to be given back");
        Until(() => layer.DynamicLightingOwed == 0, "Dynamic Lighting to be given back");
        Assert.Equal(true, lighting.Enabled);
        Assert.False(keyboard.IsOpen);
    }

    [Fact]
    public void TheSecondHandBackOfAWakeFindsNothingOwed()
    {
        var keyboard = new FakeKeyboard();
        using var layer = Layer(keyboard);

        layer.EmissionBegan();
        layer.Frame(2, striking: false);
        Until(() => keyboard.Shown.Count > 0, "the first colour");

        layer.WentDark();
        Until(() => keyboard.Darks == 1, "the blackout");

        // A blackout that ends normally hands back twice: once on leaving the black, and again a
        // moment later when the overlay leaves the screen. The second finds an empty ledger.
        layer.LeftDark();
        Until(() => layer.Owed == 0, "the hand-back");

        layer.LeftDark();
        Thread.Sleep(50);

        Assert.Equal(1, keyboard.Restores);
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
