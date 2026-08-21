namespace Bubbles.Session;

/// <summary>Turns a stream of instantaneous audio peaks into "somebody is watching something".
///
/// A peak reading is a single moment, and a film is full of moments that are silent: a pause in
/// the dialogue, a cut between scenes, the gap between tracks. Holding off only while the meter
/// is above zero would let the screensaver in during any quiet passage, which is exactly when
/// it is least welcome.
///
/// So sound heard recently counts as sound. The grace period is deliberately longer than any
/// ordinary silence in speech or music and shorter than the idle delay, and because a hold-off
/// is *discounted* rather than treated as a reset -- see <see cref="IdleClock"/> -- a stray
/// notification chime costs the countdown its own length and nothing more.</summary>
public sealed class SoundWatch
{
    /// <summary>Below this is silence. Not zero: idle output paths report a trickle of noise,
    /// and treating that as playback would hold the screensaver off for ever.</summary>
    public const float Silence = 0.005f;

    private readonly double _graceSeconds;
    private long? _lastHeard;

    /// <param name="graceSeconds">How long after the last sound to keep counting it.</param>
    public SoundWatch(double graceSeconds = 30) => _graceSeconds = graceSeconds;

    /// <param name="peak">Peak output level 0..1, or null if it could not be read -- which is
    /// not silence, and must not be treated as a reason to hold off.</param>
    /// <param name="now">A monotonic millisecond clock, i.e. Environment.TickCount64.</param>
    public bool Playing(float? peak, long now)
    {
        if (peak is not { } level) return false;

        if (level > Silence)
        {
            _lastHeard = now;
            return true;
        }

        if (_lastHeard is not { } heard) return false;

        if ((now - heard) / 1000.0 <= _graceSeconds) return true;

        _lastHeard = null;
        return false;
    }
}
