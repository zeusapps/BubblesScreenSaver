using Windows.Media.Control;

namespace Bubbles.Interop;

/// <summary>What kind of thing a player says it is playing.</summary>
internal enum MediaKind
{
    /// <summary>Nothing playing, or nothing that says what it is.</summary>
    None,

    /// <summary>Somebody is watching. Silent footage counts, which is the entire point.</summary>
    Video,

    /// <summary>Somebody is listening, which is not the same thing.</summary>
    Music,
}

/// <summary>What is playing, according to Windows rather than according to the loudspeaker.
///
/// The audio meter was introduced to catch a video playing in a window, and reasons that sound
/// coming out means somebody is listening to it. True in that direction. The implementation
/// depends on the converse -- that no sound means nobody is watching -- and silent drone footage
/// is the counterexample, along with muted playback, a clip with no audio track at all, and
/// anything routed to an endpoint other than the metered one.
///
/// These are the records behind the media flyout: every player that registers a session reports
/// whether it is playing and whether it is video, music or an image. None of that depends on an
/// audio track existing, on the volume, or on which device the sound goes to.
///
/// Not every player registers one -- mpv and some games do not -- so this is an additional
/// signal rather than a replacement for the meter.</summary>
internal static class MediaSessions
{
    // RequestAsync is the only asynchronous part of the API; once held, the session list and
    // each session's playback info are synchronous property reads, which is what the hold-off
    // needs on the dispatcher thread. Requested once in the background and cached.
    private static GlobalSystemMediaTransportControlsSessionManager? _manager;
    private static Task<GlobalSystemMediaTransportControlsSessionManager>? _pending;
    private static readonly object Gate = new();

    /// <summary>The strongest thing currently playing: video if anything is playing video,
    /// otherwise music if anything is playing music, otherwise nothing.
    ///
    /// Never throws, and reports <see cref="MediaKind.None"/> when the state cannot be read.
    /// Holding off on a failure to read would be unbounded -- a permanently failing call would
    /// keep the overlay from ever running, which is worse than a screensaver arriving during a
    /// film. The same reasoning as an unreadable audio peak, which is not silence but is not a
    /// reason to hold off either.</summary>
    public static MediaKind Playing(out string? app)
    {
        app = null;

        try
        {
            var manager = Manager();
            if (manager is null) return MediaKind.None;

            var kind = MediaKind.None;

            foreach (var session in manager.GetSessions())
            {
                var found = Classify(session, out var owner);
                if (found == MediaKind.None) continue;

                // Video wins outright: if anything at all is being watched, nothing else about
                // the mix matters.
                if (found == MediaKind.Video)
                {
                    app = owner;
                    return MediaKind.Video;
                }

                if (kind == MediaKind.None)
                {
                    kind = found;
                    app = owner;
                }
            }

            return kind;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"could not read the media sessions: {ex.Message}");
            Drop();
            return MediaKind.None;
        }
    }

    /// <summary>Every session and what it says about itself, for --media.</summary>
    public static List<string> Describe()
    {
        var lines = new List<string>();

        try
        {
            var manager = Manager();

            if (manager is null)
            {
                lines.Add("  the session manager is not available yet");
                return lines;
            }

            var sessions = manager.GetSessions();

            if (sessions.Count == 0)
            {
                lines.Add("  no media session is registered");
                return lines;
            }

            foreach (var session in sessions)
            {
                string status, type;

                try
                {
                    var info = session.GetPlaybackInfo();
                    status = info.PlaybackStatus.ToString();
                    type = info.PlaybackType?.ToString() ?? "(none)";
                }
                catch (Exception ex)
                {
                    status = $"unreadable ({ex.Message})";
                    type = "(none)";
                }

                var kind = Classify(session, out _);

                lines.Add($"  {session.SourceAppUserModelId,-40} {status,-10} {type,-8} -> {kind}");
            }
        }
        catch (Exception ex)
        {
            lines.Add($"  could not read the media sessions: {ex.Message}");
        }

        return lines;
    }

    /// <summary>A session is a reason only while it reports Playing. The existence of a session
    /// is not: a player left open overnight would otherwise hold the overlay off for ever, which
    /// is the QUNS_BUSY mistake -- a signal that sounds right, reads true nearly always, and
    /// stops the screensaver running at all.</summary>
    private static MediaKind Classify(
        GlobalSystemMediaTransportControlsSession session, out string? app)
    {
        app = null;

        try
        {
            var info = session.GetPlaybackInfo();

            if (info.PlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                return MediaKind.None;

            app = Pretty(session.SourceAppUserModelId);
            return KindOf(info.PlaybackStatus, info.PlaybackType);
        }
        catch
        {
            return MediaKind.None;
        }
    }

    /// <summary>The whole decision, separated from the session object so it can be tested.
    ///
    /// Only Playing counts. A session that merely exists does not, and neither does one that is
    /// Paused, Stopped, Changing or Closed: a player left open overnight would otherwise hold
    /// the overlay off for ever, which is the QUNS_BUSY mistake -- a signal that sounds right,
    /// reads true nearly always, and stops the screensaver running at all.
    ///
    /// Image is a slideshow or album art, and is also the shape a stale session takes, so it is
    /// not a reason either. Nor is a session that declines to say what it is playing.</summary>
    internal static MediaKind KindOf(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus status,
        Windows.Media.MediaPlaybackType? type)
    {
        if (status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            return MediaKind.None;

        return type switch
        {
            Windows.Media.MediaPlaybackType.Video => MediaKind.Video,
            Windows.Media.MediaPlaybackType.Music => MediaKind.Music,
            _ => MediaKind.None,
        };
    }

    /// <summary>The cached manager, or null until the first request completes.
    ///
    /// Deliberately not blocking on the task: this is called from the dispatcher thread, which
    /// also renders the overlay. Reporting nothing for the first moments of the process costs a
    /// hold-off that would have started a second later anyway.</summary>
    private static GlobalSystemMediaTransportControlsSessionManager? Manager()
    {
        lock (Gate)
        {
            if (_manager is not null) return _manager;

            if (_pending is null)
            {
                _pending = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
            }
            else if (_pending.IsCompletedSuccessfully)
            {
                _manager = _pending.Result;
                _pending = null;
                return _manager;
            }
            else if (_pending.IsFaulted || _pending.IsCanceled)
            {
                Diagnostics.Log($"media session manager request failed: {_pending.Exception?.Message}");
                _pending = null;
            }

            return null;
        }
    }

    /// <summary>Forgets the cached manager so the next call re-requests it.
    ///
    /// AudioActivity caches its meter and only drops it when a call returns a failing HRESULT --
    /// so a meter on a superseded endpoint can go on returning success with a peak of zero, and
    /// report silence for ever. This must not repeat that: the manager is dropped on any failure
    /// and the session list is re-read every time rather than cached.</summary>
    private static void Drop()
    {
        lock (Gate)
        {
            _manager = null;
            _pending = null;
        }
    }

    /// <summary>An AUMID is either a packaged family name or a path. Either way the last
    /// component is the part worth reading.</summary>
    private static string Pretty(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return "an unnamed player";

        var trimmed = id.Split('!')[0];
        var tail = trimmed.Split('\\', '/').Last();

        return string.IsNullOrWhiteSpace(tail) ? id : tail;
    }
}
