namespace Bubbles.Session;

/// <summary>How long you have actually been away, which is not the same as how long it has
/// been since the last keypress.
///
/// Windows keeps one counter of the time since the last keyboard or mouse event. It is
/// machine-wide, it knows nothing about this application, and taken at face value it answers a
/// question nobody asked. Two things are true of it that are not true of "time this screensaver
/// could have been running", and both are corrected here.
///
/// *Time spent held off is not time spent away.* Sitting on a call produces no input, so the
/// system counter climbs for the whole call while the hold-off keeps the screensaver away.
/// Hanging up would otherwise leave that counter far past every threshold at once, and the
/// bubbles would arrive the instant the call ended -- possibly straight to a black screen,
/// skipping the bubbles entirely -- while you are still sitting in front of the machine.
///
/// That time is subtracted rather than used to reset the clock, which matters more than it
/// sounds: an app that grabs the microphone for a moment every few minutes -- a voice
/// assistant, a conferencing client checking presence, a browser tab -- would otherwise reset
/// the countdown more often than it could ever elapse, and the screensaver would simply never
/// start. Subtracting costs such a blip the fraction of a second it actually lasted.
///
/// *Time before this run began is not time spent away either.* The counter outlives the
/// process, so a run that starts while the machine has already been sitting starts with the
/// thresholds already passed. Measured with a two-minute artifacts stage configured, it lasted
/// 1.2 seconds:
///
/// <code>
/// 13:04:38      the application starts
/// 13:04:39.227  tick idle=149,0s cur=Active  next=Bubbles   idleCfg=30 blackCfg=150
/// 13:04:40.454  tick idle=150,3s cur=Bubbles next=Blackout
/// </code>
///
/// That is the ordinary experience of restarting: after an update, after a crash, at login on a
/// machine that sat at the lock screen -- every one of them a moment somebody is watching to
/// see whether it works. So the answer is bounded by the life of the run. You cannot have been
/// away from a screensaver that did not exist.
///
/// The bound is applied to the *result*, after the subtraction, not to the system counter
/// before it. Clamping the input instead would make the hold-off tally meaningless for the
/// first minutes of every run.</summary>
public sealed class IdleClock
{
    private double _heldOffSeconds;
    private long? _lastTick;

    /// <summary>When this run's countdown began, taken from the first tick rather than from
    /// construction: the class is handed a clock rather than reading one, and an origin taken
    /// from a clock it does not otherwise touch would be the one place it reached outside its
    /// inputs. The difference is a fraction of a second, in the safe direction.</summary>
    private long? _startedAt;

    /// <param name="systemIdle">Seconds since the last keyboard or mouse input.</param>
    /// <param name="heldOff">Whether something is suppressing the screensaver right now.</param>
    /// <param name="now">A monotonic millisecond clock, i.e. Environment.TickCount64.</param>
    /// <returns>The idle time that should count towards the thresholds.</returns>
    public double Elapsed(double systemIdle, bool heldOff, long now)
    {
        // First value wins, and it is never moved again. Input arriving restarts the countdown
        // through systemIdle going to zero; it does not restart the run.
        _startedAt ??= now;

        if (heldOff && _lastTick is { } previous)
            _heldOffSeconds += Math.Max(0, (now - previous) / 1000.0);

        _lastTick = now;

        // You cannot have been held off for longer than you have been without input, and this
        // is also what clears the tally: a keypress takes systemIdle to zero and takes the
        // accumulated hold-off with it.
        _heldOffSeconds = Math.Min(_heldOffSeconds, systemIdle);

        // And you cannot have been away for longer than there has been something to be away
        // from. Once the run outlives the system counter this stops applying and never binds
        // again, which is the ordinary case within a minute or two of starting.
        var sinceStart = (now - _startedAt.Value) / 1000.0;

        return Math.Min(systemIdle - _heldOffSeconds, sinceStart);
    }
}
