namespace Bubbles.Session;

/// <summary>How long you have actually been away, which is not the same as how long it has
/// been since the last keypress.
///
/// Sitting on a call produces no keyboard or mouse input, so the system idle timer climbs for
/// the whole call while the hold-off keeps the screensaver away. Hanging up then leaves that
/// timer far past every threshold at once, and the bubbles arrive the moment the call ends --
/// possibly straight to a black screen, skipping the bubbles entirely -- while you are still
/// sitting in front of the machine.
///
/// So the countdown restarts when the hold-off lifts. Until you touch something, "idle" is
/// measured from the end of the call rather than from the last keypress before it.</summary>
public sealed class IdleClock
{
    private long? _released;
    private bool _heldOff;

    /// <param name="systemIdle">Seconds since the last keyboard or mouse input.</param>
    /// <param name="heldOff">Whether something is suppressing the screensaver right now.</param>
    /// <param name="now">A monotonic millisecond clock, i.e. Environment.TickCount64.</param>
    /// <returns>The idle time that should count towards the thresholds.</returns>
    public double Elapsed(double systemIdle, bool heldOff, long now)
    {
        if (heldOff)
        {
            _heldOff = true;
            return 0;
        }

        if (_heldOff)
        {
            _heldOff = false;
            _released = now;
        }

        if (_released is not { } released) return systemIdle;

        var sinceReleased = (now - released) / 1000.0;

        // Once there has been input since the hold-off lifted, the system timer is the smaller
        // and more accurate of the two, and the release point stops mattering.
        if (systemIdle <= sinceReleased)
        {
            _released = null;
            return systemIdle;
        }

        return sinceReleased;
    }
}
