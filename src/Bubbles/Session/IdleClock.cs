namespace Bubbles.Session;

/// <summary>How long you have actually been away, which is not the same as how long it has
/// been since the last keypress.
///
/// Sitting on a call produces no keyboard or mouse input, so the system idle timer climbs for
/// the whole call while the hold-off keeps the screensaver away. Hanging up then leaves that
/// timer far past every threshold at once, and the bubbles arrive the instant the call ends --
/// possibly straight to a black screen, skipping the bubbles entirely -- while you are still
/// sitting in front of the machine.
///
/// So time spent held off does not count as time spent away. It is subtracted rather than
/// used to reset the clock, which matters more than it sounds: an app that grabs the
/// microphone for a moment every few minutes -- a voice assistant, a conferencing client
/// checking presence, a browser tab -- would otherwise reset the countdown more often than it
/// could ever elapse, and the screensaver would simply never start. Subtracting costs such a
/// blip the fraction of a second it actually lasted.</summary>
public sealed class IdleClock
{
    private double _heldOffSeconds;
    private long? _lastTick;

    /// <param name="systemIdle">Seconds since the last keyboard or mouse input.</param>
    /// <param name="heldOff">Whether something is suppressing the screensaver right now.</param>
    /// <param name="now">A monotonic millisecond clock, i.e. Environment.TickCount64.</param>
    /// <returns>The idle time that should count towards the thresholds.</returns>
    public double Elapsed(double systemIdle, bool heldOff, long now)
    {
        if (heldOff && _lastTick is { } previous)
            _heldOffSeconds += Math.Max(0, (now - previous) / 1000.0);

        _lastTick = now;

        // You cannot have been held off for longer than you have been without input, and this
        // is also what clears the tally: a keypress takes systemIdle to zero and takes the
        // accumulated hold-off with it.
        _heldOffSeconds = Math.Min(_heldOffSeconds, systemIdle);

        return systemIdle - _heldOffSeconds;
    }
}
