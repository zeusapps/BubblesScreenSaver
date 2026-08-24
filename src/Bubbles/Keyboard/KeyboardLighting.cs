using System.IO;

using Bubbles.Displays;

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
/// the middle of the next Emission.
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

    private static string StateFile => Path.Combine(Settings.Directory, "keyboard-state.json");

    private enum Chore { None, Open, Dark, Restore, Recover }

    private readonly Settings _settings;
    private readonly Func<IKeyboardDevice> _open;
    private readonly PendingRestore<KeyboardRecord> _owed;

    private readonly object _gate = new();
    private readonly AutoResetEvent _wake = new(false);

    private Thread? _worker;
    private bool _stopping;

    private Chore _chore;
    private KeyColor? _queued;
    private bool _queuedUrgent;

    // Worker thread only, past construction.
    private IKeyboardDevice? _device;
    private bool _decided;

    /// <summary>Render thread only. Owned there, never read from the worker.</summary>
    private readonly SendPolicy _policy = new();

    public KeyboardLighting(Settings settings)
        : this(settings, () => new AuraKeyboard(), StateFile)
    {
    }

    /// <param name="open">How to reach a keyboard. Replaced in tests, because everything
    /// worth checking in this class -- when it opens, what it sends, what it gives back --
    /// is decided above the hardware.</param>
    internal KeyboardLighting(Settings settings, Func<IKeyboardDevice> open, string stateFile)
    {
        _settings = settings;
        _open = open;
        _owed = new PendingRestore<KeyboardRecord>(
            stateFile,
            record => record.Key,
            "keyboard lighting",
            record => record.Name);
    }

    /// <summary>Whether anything has been borrowed and not yet given back. For tests and for
    /// the log; nothing behaves differently on it.</summary>
    internal int Owed => _owed.Count;

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

        if (_owed.Count == 0) return;

        Diagnostics.Log($"keyboard lighting: {_owed.Count} keyboard(s) owed by a previous run");
        Queue(Chore.Recover);
    }

    /// <summary>An Emission has started. Nothing is sent yet -- this is only the moment to go
    /// looking for a keyboard, so the search is behind us by the time the sky is worth
    /// following.</summary>
    public void EmissionBegan()
    {
        if (!_settings.KeyboardLighting) return;

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

    /// <summary>The screen has reached black. So does the keyboard: a lit keyboard beside a
    /// screen deliberately taken to black is the one thing that would give the black away.</summary>
    public void WentDark()
    {
        if (!_settings.KeyboardLighting) return;

        _policy.Reset();
        Queue(Chore.Dark);
    }

    /// <summary>Awake. Whatever the keyboard was doing before goes back, whether or not the
    /// setting has been turned off in the meantime -- the debt is not conditional.</summary>
    public void LeftDark()
    {
        _policy.Reset();

        // Queued whether or not anything is on the books yet: the worker may still be opening
        // the keyboard, and by the time it gets here the debt will exist. GiveBack is a no-op
        // if it does not.
        Queue(Chore.Restore);
    }

    public void Dispose()
    {
        Thread? worker;

        lock (_gate)
        {
            _stopping = true;
            if (_owed.Count > 0) _chore = Chore.Restore;
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

    /// <summary>Whether the one attempt to find a keyboard has been made and came to
    /// nothing.</summary>
    private bool Abandoned
    {
        get { lock (_gate) return _decided && _device is null; }
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
            _wake.WaitOne();

            Chore chore;
            KeyColor? colour;
            bool stopping;

            lock (_gate)
            {
                chore = _chore;
                colour = _queued;
                stopping = _stopping;

                _chore = Chore.None;
                _queued = null;
                _queuedUrgent = false;
            }

            try
            {
                switch (chore)
                {
                    case Chore.Restore:
                    case Chore.Recover:
                        GiveBack(chore == Chore.Recover ? "a previous run" : "awake");
                        break;

                    case Chore.Dark:
                        if (Ensure()) _device!.GoDark();
                        break;

                    // Opening is not an alternative to showing a colour, it is what has to
                    // happen first. One wake can carry both -- the Emission's start and its
                    // first frame are a frame apart -- and handling only one of them dropped
                    // the opening colour of every Emission.
                    default:
                        if ((chore == Chore.Open || colour is not null) && Ensure() && colour is { } wanted)
                            _device!.Show(wanted);
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

    /// <summary>The once-per-session decision.
    ///
    /// The record of what is owed is written before this returns true, so there is no window
    /// in which the keyboard has been found, is about to be changed, and nothing on disk says
    /// what it was.</summary>
    private bool Ensure()
    {
        if (_decided) return _device is not null;

        lock (_gate) _decided = true;

        var device = _open();
        var record = device.Open();

        if (record is null)
        {
            device.Dispose();
            Diagnostics.Log("keyboard lighting: no keyboard this session; staying quiet");
            return false;
        }

        _owed.Remember([record]);

        lock (_gate) _device = device;

        return true;
    }

    /// <summary>Hands back everything owed, and forgets only what the keyboard confirmed.
    ///
    /// Will open a device of its own if there is not one, which is not a retry of a decision
    /// already made against: there is only something owed here because a keyboard was found and
    /// changed earlier. A session that never reached one owes nothing and does nothing.</summary>
    private void GiveBack(string why)
    {
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
