using System.IO;

using Bubbles.Displays;
using Bubbles.Zone;

namespace Bubbles.Keyboard;

/// <summary>Carries an Emission onto the keyboard, and gives the keyboard back afterwards.
///
/// Three rules shape everything here.
///
/// *Nothing on screen ever waits for a keyboard.* Every write to the hardware happens on one
/// background thread that exists to be blocked. The render loop hands over a colour and
/// returns; if the worker is busy, the colour is simply the one that gets skipped. A keyboard
/// that has stopped answering costs the Emission nothing at all, which is the only acceptable
/// price for a decoration.
///
/// *Failure is decided once.* Most machines running this application have no keyboard this can
/// talk to, so the failure path is the common path. One attempt, one line in the log, and then
/// silence for the rest of the session -- no retry timer, no dialog, and no second attempt in
/// the middle of the next Emission. That governs the *search*: a keyboard found once and given
/// back at the end of a blackout is opened again for the next one, which is a fresh loan rather
/// than a second guess.
///
/// *What is borrowed is written down first.* A note goes to disk before a single colour is
/// sent. A monitor left dim is visible and can be fixed from its own buttons; a keyboard left
/// black by a process that is no longer running looks like broken hardware, and there is
/// nothing on it to say otherwise.</summary>
internal sealed class KeyboardLighting : IDisposable
{
    /// <summary>How long to wait for the keyboard to be handed back at exit before giving up
    /// and letting the process go. The record on disk is the backstop if this runs out.</summary>
    private static readonly TimeSpan ShutdownGrace = TimeSpan.FromSeconds(3);

    /// <summary>How soon black is said again just after something might have disturbed the keys.
    ///
    /// The floor of the ramp, and where the ramp is sent back to whenever the machine passes
    /// through one of the transitions <see cref="MachineEvents"/> reports.</summary>
    private static readonly TimeSpan Floor = TimeSpan.FromSeconds(2);

    /// <summary>The slowest the re-assert ever gets, once nothing has happened for a while.
    ///
    /// Not chosen for symmetry: it is what `DisplayBlackout._whileDark` already uses against the
    /// same class of problem on the same machine, so there is one number to reason about rather
    /// than two. It is also the worst case for how long a repaint can be left on the keys, which
    /// is the number to lower if a green is ever seen with no disturbance logged near it.</summary>
    private static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(20);

    /// <summary>How fast the ramp relaxes. Seven steps from the floor to the ceiling, which is
    /// about a minute of attention after each disturbance.
    ///
    /// A ramp measured from the *start of the blackout* would be worth almost nothing: it reaches
    /// the ceiling inside the first minute and a blackout is hours, so every wakeup but a handful
    /// happens at the ceiling either way. Measured from the last disturbance it earns its keep,
    /// because the attentive phase happens again every time there is a reason for it.</summary>
    private const double Growth = 1.5;

    private static string StateFile => Path.Combine(Settings.Directory, "keyboard-state.json");

    /// <summary>Its own file, not a corner of the keyboard's. The two debts are settled
    /// independently: a run can die owing Dynamic Lighting and no keyboard, or the reverse.</summary>
    private static string DynamicLightingFile =>
        Path.Combine(Settings.Directory, "dynamic-lighting-state.json");

    private enum Chore { None, Open, Dark, Restore, Recover }

    private readonly Settings _settings;
    private readonly Func<IKeyboardDevice> _open;
    private readonly PendingRestore<KeyboardRecord> _owed;

    /// <summary>Windows' Dynamic Lighting, borrowed alongside the keyboard so that the writes
    /// this layer makes are the ones the keys actually show. Never null in the application; the
    /// tests that have no interest in it pass one over a fake toggle.</summary>
    private readonly DynamicLightingLoan _standDown;

    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);

    /// <summary>The ends of the re-assert ramp. Fields rather than the constants directly so the
    /// tests can drive the loop in milliseconds instead of waiting out a minute of it.</summary>
    private readonly TimeSpan _floor;
    private readonly TimeSpan _ceiling;

    private Thread? _worker;
    private bool _stopping;

    private Chore _chore;
    private KeyColor? _queued;
    private bool _queuedUrgent;

    // Worker thread only, past construction.

    /// <summary>The keyboard, once one has been found. Kept across a hand-back, because a
    /// device that has been given back is an empty object rather than an absent one, and
    /// opening it again is how the next Emission gets its keyboard. Null means there is none
    /// to be had: either the search found nothing, or the one that was found has gone.</summary>
    private IKeyboardDevice? _device;

    /// <summary>Whether the one search of the session has been made. It says nothing about
    /// whether the device is in hand -- that question goes to the device, which is the only
    /// thing that knows.</summary>
    private bool _searched;

    /// <summary>Render thread only. Owned there, never read from the worker.</summary>
    private readonly SendPolicy _policy = SendPolicy.ForEmission();

    /// <summary>The weather's own policy, with its own floor. Separate from the Emission's so
    /// that neither one's last frame can ration the other's first -- and because a state that
    /// held for a minute must not suppress the Emission that interrupts it.</summary>
    private readonly SendPolicy _weatherPolicy = SendPolicy.ForWeather();

    /// <summary>The weather's clock. Its own, and not the Emission's: the two are rationed at
    /// different rates and neither is the wall.</summary>
    private double _weatherClock;

    /// <summary>Whether an Emission is running, so ambient frames arriving alongside one can be
    /// dropped rather than queued. Set from the Emission's own events, on the same thread they
    /// are raised on.</summary>
    private bool _emitting;

    /// <summary>Worker thread only. Whether the screen is black and the keys are being held that
    /// way, and when they were taken there.
    ///
    /// One `GoDark()` is not enough. The device is opened with shared access -- the lighting is
    /// not ours exclusively -- so another owner can repaint the keys at any moment, and the
    /// protocol has no read, so there is no way to notice that it has. The only answer available
    /// from in here is to say it again, often enough that being wrong is brief.</summary>
    private bool _holdingDark;

    private long _darkAt;

    /// <summary>Worker thread only. How long to wait before saying black again, ramping from the
    /// floor to the ceiling while nothing happens and sent back to the floor when something
    /// does.</summary>
    private TimeSpan _cadence;

    /// <summary>Set from <see cref="MachineEvents"/>, on its thread, under the gate. Says the
    /// machine passed through something that may have taken the keys, so the ramp starts again
    /// and black is said now rather than at the end of the current wait.</summary>
    private bool _disturbed;

    private string? _disturbedBy;

    public KeyboardLighting(Settings settings)
        : this(settings, () => new AuraKeyboard(), StateFile,
               new DynamicLightingLoan(DynamicLightingFile))
    {
    }

    /// <param name="open">How to reach a keyboard. Replaced in tests, because everything
    /// worth checking in this class -- when it opens, what it sends, what it gives back --
    /// is decided above the hardware.</param>
    /// <param name="standDown">The Dynamic Lighting loan. Passed in rather than defaulted,
    /// because a default would be one over the real registry, and a test that reached it would
    /// change the personalization settings of the machine running it.</param>
    /// <param name="floor">How soon the re-assert repeats just after a disturbance.</param>
    /// <param name="ceiling">The slowest it gets once nothing has happened for a while.</param>
    internal KeyboardLighting(
        Settings settings, Func<IKeyboardDevice> open, string stateFile,
        DynamicLightingLoan standDown, TimeSpan? floor = null, TimeSpan? ceiling = null)
    {
        _settings = settings;
        _open = open;
        _standDown = standDown;
        _floor = floor ?? Floor;
        _ceiling = ceiling ?? Ceiling;
        _owed = new PendingRestore<KeyboardRecord>(
            stateFile,
            record => record.Key,
            "keyboard lighting",
            record => record.Name);
    }

    /// <summary>Whether anything has been borrowed and not yet given back. For tests and for
    /// the log; nothing behaves differently on it.</summary>
    internal int Owed => _owed.Count;

    /// <summary>Whether Dynamic Lighting is currently stood down. For tests and for the log.</summary>
    internal int DynamicLightingOwed => _standDown.Owed;

    /// <summary>Whether standing Dynamic Lighting down is asked for right now.
    ///
    /// Both settings, because the second is subordinate to the first: somebody who asked for an
    /// Emission on the keys did not thereby ask for a Windows setting to be edited, and somebody
    /// who asked for that while the keys are not being driven asked for nothing at all.</summary>
    private bool StandingDown => _settings.KeyboardLighting && _settings.StandDynamicLightingDown;

    // ---- what the overlay tells it ------------------------------------------------------

    /// <summary>Anything a previous run left the keyboard in.
    ///
    /// Reads the record before deciding to do anything at all, so a machine that has never
    /// enabled this -- which is to say nearly all of them -- touches no hardware here. There
    /// is no file, so there is nothing owed, so nothing happens.
    ///
    /// Deliberately not gated on the setting. Somebody who turned this off after a crash is
    /// still owed their keyboard back.</summary>
    public void RecoverFromCrash()
    {
        _owed.Load();
        _standDown.Load();

        if (_owed.Count == 0 && _standDown.Owed == 0) return;

        if (_owed.Count > 0)
            Diagnostics.Log($"keyboard lighting: {_owed.Count} keyboard(s) owed by a previous run");

        Queue(Chore.Recover);
    }

    /// <summary>An Emission has started. Nothing is sent yet -- this is only the moment to go
    /// looking for a keyboard, so the search is behind us by the time the sky is worth
    /// following.</summary>
    public void EmissionBegan()
    {
        if (!_settings.KeyboardLighting) return;

        _emitting = true;
        _policy.Reset();
        Queue(Chore.Open);
    }

    /// <summary>One frame of an Emission: how far in it is, and whether a bolt is on screen
    /// this frame.
    ///
    /// The strike is passed in rather than asked for. The overlay has already put that
    /// question to the lightning while drawing it, and a second caller could get a different
    /// answer -- a keyboard flashing on a frame with no bolt on it.</summary>
    public void Frame(double emissionTime, bool striking)
    {
        if (!_settings.KeyboardLighting) return;

        // After a failed attempt there is nothing to send to, and the ramp should not spend
        // the rest of the Emission computing colours for nobody.
        if (Abandoned) return;

        var decision = _policy.Decide(emissionTime, striking);

        if (!decision.Send) return;

        Queue(decision.Colour, decision.Urgent);
    }

    /// <summary>One frame of ambient weather: the sky as it stands, the family tinting it, and
    /// whether a distant bolt is on screen.
    ///
    /// Silently ignored while an Emission is running. The overlay does not raise this then, and
    /// this checks anyway, because "the Emission owns the keyboard" is the kind of rule that
    /// should not depend on one caller remembering it.</summary>
    public void Weather(SkyState sky, Anomaly family, bool striking)
    {
        if (!_settings.KeyboardLighting || !_settings.KeyboardWeather) return;

        if (_emitting || Abandoned) return;

        // A frame's worth, at the rate the overlay draws. The cycle is advanced by the overlay;
        // this only needs a clock to ration against.
        _weatherClock += 1.0 / 30;

        var decision = _weatherPolicy.DecideWeather(
            _weatherClock, WeatherLight.For(sky, family, _weatherClock), striking);

        if (!decision.Send) return;

        Queue(decision.Colour, decision.Urgent);
    }

    /// <summary>The screen has reached black. So does the keyboard: a lit keyboard beside a
    /// screen deliberately taken to black is the one thing that would give the black away.</summary>
    public void WentDark()
    {
        if (!_settings.KeyboardLighting) return;

        _emitting = false;
        _policy.Reset();
        _weatherPolicy.Reset();
        Queue(Chore.Dark);
    }

    /// <summary>Awake. Whatever the keyboard was doing before goes back, whether or not the
    /// setting has been turned off in the meantime -- the debt is not conditional.</summary>
    public void LeftDark()
    {
        _emitting = false;
        _policy.Reset();
        _weatherPolicy.Reset();
        _weatherClock = 0;

        // Queued whether or not anything is on the books yet: the worker may still be opening
        // the keyboard, and by the time it gets here the debt will exist. GiveBack is a no-op
        // if it does not.
        Queue(Chore.Restore);
    }

    /// <summary>The machine passed through something that may have disturbed the keys -- a lock,
    /// a resume, a display being reconfigured.
    ///
    /// Not a chore: it does not replace whatever else has been asked for, and it does not start a
    /// worker. A machine that has never taken a keyboard has nothing to reassert and nothing to
    /// wake up for, which is the common case and must stay free.
    ///
    /// Called from the <c>SystemEvents</c> thread.</summary>
    public void Disturbed(string what)
    {
        lock (_gate)
        {
            // No worker means no loan, ever, on this machine. Nothing to say black to.
            if (_stopping || _worker is null) return;

            _disturbed = true;
            _disturbedBy = what;
        }

        _wake.Set();
    }

    public void Dispose()
    {
        Thread? worker;

        lock (_gate)
        {
            _stopping = true;
            if (_owed.Count > 0 || _standDown.Owed > 0) _chore = Chore.Restore;
            worker = _worker;
        }

        if (worker is null)
        {
            _wake.Dispose();
            return;
        }

        _wake.Set();

        // Bounded, because this runs on the way out of the process. A keyboard that will not
        // answer must not be the reason the application refuses to close; the record on disk
        // means the next run picks the debt up.
        if (!worker.Join(ShutdownGrace))
            Diagnostics.Log("keyboard lighting: gave up waiting for the keyboard at exit");

        _wake.Dispose();
    }

    // ---- the once-per-session decision ----------------------------------------------------

    /// <summary>Whether there is no keyboard to send to for the rest of this session.
    ///
    /// Two ways to get here, and they are the same afterwards: the one search found nothing, or
    /// a keyboard that was found stopped accepting writes. Either way the ramp stops computing
    /// colours for nobody.</summary>
    private bool Abandoned
    {
        get { lock (_gate) return _searched && _device is null; }
    }

    // ---- the worker ---------------------------------------------------------------------

    private void Queue(KeyColor colour, bool urgent)
    {
        lock (_gate)
        {
            if (_stopping) return;

            // An urgent colour that has not gone out yet is not replaced by an ordinary one.
            // Without this the flare could be overwritten by the next frame of the ramp while
            // the worker was still busy, which is exactly the coalescing it is exempt from.
            if (_queued is not null && _queuedUrgent && !urgent) return;

            _queued = colour;
            _queuedUrgent = urgent;

            Start();
        }

        _wake.Set();
    }

    private void Queue(Chore chore)
    {
        lock (_gate)
        {
            if (_stopping) return;

            _chore = chore;

            // Going dark and handing the keyboard back are both the end of whatever a waiting
            // colour was part of, so they discard it. Opening does not: it is the step before
            // a colour, not instead of one.
            if (chore is Chore.Dark or Chore.Restore or Chore.Recover) _queued = null;

            Start();
        }

        _wake.Set();
    }

    /// <summary>Starts the worker on first use. Called under the lock.</summary>
    private void Start()
    {
        if (_worker is not null) return;

        _worker = new Thread(Work)
        {
            IsBackground = true,
            Name = "keyboard lighting",
            // Below normal: this is a decoration, and the thread it must never take time from
            // is the one drawing the Emission.
            Priority = ThreadPriority.BelowNormal,
        };

        _worker.Start();
    }

    private void Work()
    {
        while (true)
        {
            // Unbounded unless the screen is black. A blackout bounds the wait, and the timeout
            // is the whole of the re-assert: nobody has asked for anything, and the keys are
            // still meant to be off, so say so again in case somebody else has said otherwise.
            if (!_wake.WaitOne(_holdingDark ? _cadence : Timeout.InfiniteTimeSpan))
            {
                try
                {
                    Reassert();
                }
                catch (Exception ex)
                {
                    Diagnostics.Log($"keyboard lighting: {ex}");
                }

                continue;
            }

            Chore chore;
            KeyColor? colour;
            bool stopping;
            bool disturbed;
            string? disturbedBy;

            lock (_gate)
            {
                chore = _chore;
                colour = _queued;
                stopping = _stopping;
                disturbed = _disturbed;
                disturbedBy = _disturbedBy;

                _chore = Chore.None;
                _queued = null;
                _queuedUrgent = false;
                _disturbed = false;
                _disturbedBy = null;
            }

            try
            {
                // Before the chore, and never instead of one. A disturbance says the keys may
                // have been repainted since they were last told; it says nothing about what was
                // asked for, so it is answered and then the wake is handled as it would be.
                if (disturbed && _holdingDark)
                {
                    Diagnostics.Log($"keyboard lighting: {disturbedBy} may have taken the keys; " +
                                    "saying black again");

                    _cadence = _floor;
                    Reassert();
                }

                switch (chore)
                {
                    case Chore.Restore:
                    case Chore.Recover:
                        _holdingDark = false;
                        GiveBack(chore == Chore.Recover ? "a previous run" : "awake");
                        break;

                    // What Show and GoDark return is acted on rather than dropped. A refused
                    // write means the device has let go of itself, and a layer that ignores
                    // that goes on sending colours nobody will ever see.
                    case Chore.Dark:
                        if (!Ensure()) break;   // nothing to send to is nothing to hold

                        _holdingDark = true;
                        _darkAt = Environment.TickCount64;

                        // Reaching black is itself the most recent disturbance: it is the moment
                        // the blackout's work on the displays begins, and that work has been seen
                        // to provoke the repaint. So the ramp starts at the floor.
                        _cadence = _floor;

                        if (!_device!.GoDark()) Lost("the blackout");
                        break;

                    // Opening is not an alternative to showing a colour, it is what has to
                    // happen first. One wake can carry both -- the Emission's start and its
                    // first frame are a frame apart -- and handling only one of them dropped
                    // the opening colour of every Emission.
                    default:
                        // A bare wake -- a disturbance, already dealt with above -- asks for
                        // nothing and must not cancel the blackout it woke us during.
                        if (chore != Chore.Open && colour is null) break;

                        // A colour, or an Emission about to send one, means something is on
                        // screen again and there is no blackout left to hold.
                        _holdingDark = false;

                        if (Ensure() && colour is { } wanted && !_device!.Show(wanted))
                            Lost("a colour");
                        break;
                }
            }
            catch (Exception ex)
            {
                // Nothing above this thread is in a position to handle anything, and a
                // decoration must not be able to take the process down from a background
                // thread it owns.
                Diagnostics.Log($"keyboard lighting: {ex}");
            }

            if (stopping)
            {
                _device?.Dispose();
                _device = null;
                return;
            }
        }
    }

    /// <summary>How long to wait before saying black again.
    ///
    /// Two cadences, because the repaint is not random. The blackout's other half runs on the
    /// displays and is over within a few seconds of the screen going black, and that is when the
    /// keys have been seen to be taken; the hours after it are when they are least likely to be.
    /// So: closely for the first half-minute, then at the interval the monitor backlight's own
    /// re-assert already uses against the same class of problem.</summary>
    /// <summary>The next wait, one step further from the last disturbance. Never past the
    /// ceiling, which is both the slowest this gets and the longest a repaint can sit on the
    /// keys unanswered.</summary>
    internal TimeSpan Relax(TimeSpan cadence)
    {
        var next = TimeSpan.FromTicks((long)(cadence.Ticks * Growth));

        return next > _ceiling ? _ceiling : next;
    }

    /// <summary>Says black again, to a keyboard that may have been repainted since it was last
    /// told.
    ///
    /// Deliberately not through <see cref="SendPolicy"/>. That exists to suppress a colour which
    /// has not moved, and this colour has not moved -- that is the entire point of sending it.
    ///
    /// Through <see cref="Ensure"/> rather than around it, so a device that has let go of its
    /// handle mid-blackout is opened again and gets its black, while a session that never found a
    /// keyboard costs a branch and sends nothing.</summary>
    private void Reassert()
    {
        if (!Ensure())
        {
            // Either there was never a keyboard, or the one there was has gone. Nothing to hold.
            _holdingDark = false;
            return;
        }

        if (!_device!.GoDark())
        {
            Lost("holding the blackout");
            return;
        }

        // How long it has been black and how relaxed the ramp has become, because the open
        // question about this feature is whether the ceiling is low enough, and that is only
        // answerable from the log of a real blackout beside somebody watching the keys. Free when
        // BUBBLES_LOG is off, like everything else here.
        Diagnostics.Log($"keyboard lighting: black again, " +
                        $"{(Environment.TickCount64 - _darkAt) / 1000}s into the blackout, " +
                        $"next in {_cadence.TotalSeconds:0.#}s");

        _cadence = Relax(_cadence);
    }

    /// <summary>A keyboard in hand, or nothing to be done.
    ///
    /// The once-per-session decision is the *search*, not the holding. Those were one field
    /// once, and a keyboard given back at the end of a blackout left this answering yes for the
    /// rest of the process while every write went to a closed handle -- so the second blackout
    /// of a session left the keys lit beside a black screen, in silence. What is cached now is
    /// only that the looking has been done:
    ///
    /// <code>
    /// _searched  _device              meaning                            here
    /// ---------  -------------------  ---------------------------------  ----------
    /// false      null                 not looked yet                     search
    /// true       { IsOpen: true }     in hand                            true
    /// true       { IsOpen: false }    found once, handed back            open again
    /// true       null                 looked and found nothing, or lost  false
    /// </code>
    ///
    /// Opening a keyboard that has already been found is not a retry of a decision made
    /// against. The search succeeded; the device is known to be there; it was handed back
    /// because the screen woke up, which is the feature working.
    ///
    /// The record of what is owed is written before this returns true -- on every loan, not
    /// just the first -- so there is no window in which the keyboard has been taken, is about
    /// to be changed, and nothing on disk says so.</summary>
    private bool Ensure()
    {
        if (_device is { IsOpen: true }) return true;

        if (_searched && _device is null) return false;

        // Either nothing has been looked for yet, or something was found and has since been
        // given back. The same object serves both: opening it is how it comes back.
        var device = _device ?? _open();

        var record = device.Open();

        if (record is null)
        {
            device.Dispose();

            lock (_gate)
            {
                _searched = true;
                _device = null;
            }

            Diagnostics.Log("keyboard lighting: no keyboard this session; staying quiet");
            return false;
        }

        _owed.Remember([record]);

        // Taken here, on the worker thread, at the moment the device comes into hand -- which is
        // EmissionBegan, six and a half seconds of buildup before the first colour matters. The
        // other owner needs a moment to let go, and this is the earliest moment there is to give
        // it. Nothing on screen is waiting on the registry: this thread exists to be blocked.
        if (StandingDown) _standDown.Take();

        lock (_gate)
        {
            _searched = true;
            _device = device;
        }

        return true;
    }

    /// <summary>The keyboard has stopped answering. Said once, and then dropped for the rest of
    /// the session.
    ///
    /// Nulling the device makes <see cref="Abandoned"/> true, which stops the ramp computing
    /// colours for something that is gone. Deliberately not a reopen: a device that accepts a
    /// handle and refuses every write would otherwise loop open-fail-open-fail once per
    /// rationed colour. Failure is decided once here, as everywhere else in this class.</summary>
    private void Lost(string what)
    {
        // Whatever the screen is doing, there is nothing left to hold it against. Without this a
        // lost keyboard leaves the worker waking every cadence for the rest of the blackout to
        // find out again that there is nothing there.
        _holdingDark = false;

        IKeyboardDevice? gone;

        lock (_gate)
        {
            gone = _device;
            if (gone is null) return;

            _device = null;
        }

        gone.Dispose();

        Diagnostics.Log($"keyboard lighting: {what} did not reach the keyboard; quiet for the rest of this session");
    }

    /// <summary>Hands back everything owed, and forgets only what the keyboard confirmed.
    ///
    /// Will open a device of its own if there is not one, which is not a retry of a decision
    /// already made against: there is only something owed here because a keyboard was found and
    /// changed earlier. A session that never reached one owes nothing and does nothing.
    ///
    /// A device that is held but has already let go of its handle -- one that stopped answering
    /// mid-Emission -- is restored through the object that is already here rather than a new
    /// one, because there is nothing to open: restoring is letting go, and it has let go.
    ///
    /// This closes the device it restores. Nothing here says so afterwards, and nothing needs
    /// to: the next <see cref="Ensure"/> asks the device rather than this method.</summary>
    private void GiveBack(string why)
    {
        // First, and unconditionally. It is what the recovery path exists for -- a previous run
        // that died with Dynamic Lighting off owes it back whether or not it also owes a
        // keyboard, and whether or not either setting is still on.
        _standDown.Settle(why);

        if (_owed.Count == 0) return;

        var device = _device;
        var borrowed = device is null;

        device ??= _open();

        try
        {
            _owed.Settle(
                owed => owed.Where(device.Restore).Select(record => record.Key).ToList(),
                why);
        }
        finally
        {
            if (borrowed) device.Dispose();
        }
    }
}
