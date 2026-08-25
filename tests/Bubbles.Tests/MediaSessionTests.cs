using Windows.Media;
using Windows.Media.Control;

using Bubbles.Interop;
using Bubbles.Session;

namespace Bubbles.Tests;

/// <summary>What a media session means. The audio meter cannot tell silent footage from an
/// empty room; this is the signal that can, so what it counts and what it ignores both matter.
///
/// The dangerous direction is counting too much. An unbounded hold-off is the QUNS_BUSY
/// mistake — a signal that reads true nearly always and stops the screensaver running at all —
/// and a media player left open overnight is exactly that shape.</summary>
public sealed class MediaSessionTests
{
    private const GlobalSystemMediaTransportControlsSessionPlaybackStatus Playing =
        GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

    // ---- what counts -----------------------------------------------------------------------

    /// <summary>The bug this change exists for: drone footage with no audio track at all.
    /// Nothing about this depends on the machine making a sound.</summary>
    [Fact]
    public void Playing_video_is_watched()
    {
        Assert.Equal(MediaKind.Video, MediaSessions.KindOf(Playing, MediaPlaybackType.Video));
    }

    [Fact]
    public void Playing_music_is_listened_to_which_is_not_the_same_thing()
    {
        Assert.Equal(MediaKind.Music, MediaSessions.KindOf(Playing, MediaPlaybackType.Music));
    }

    // ---- and what that means for the stages -------------------------------------------------

    /// <summary>Watching stops everything. Silent footage is the case this exists for, so
    /// nothing about it may depend on the machine making a sound.</summary>
    [Fact]
    public void Something_watched_holds_off_both_stages()
    {
        var kind = MediaSessions.KindOf(Playing, MediaPlaybackType.Video);
        var held = UserBusy.FromMedia(kind, "a player");

        Assert.True(held.Artifacts);
        Assert.True(held.Blackout);
        Assert.True(held.Total);
        Assert.Contains("video", held.Reason);
    }

    /// <summary>And listening still reaches black, which is the whole reason the two are
    /// distinguished: an album must not keep an OLED lit for three hours.</summary>
    [Fact]
    public void An_album_still_reaches_black()
    {
        var kind = MediaSessions.KindOf(Playing, MediaPlaybackType.Music);
        var held = UserBusy.FromMedia(kind, "Spotify.exe");

        Assert.True(held.Artifacts);
        Assert.False(held.Blackout);
        Assert.Contains("music", held.Reason);
    }

    [Fact]
    public void Nothing_playing_holds_nothing_off()
    {
        Assert.False(UserBusy.FromMedia(MediaKind.None, null).Any);
    }

    // ---- what does not -----------------------------------------------------------------------

    [Theory]
    [InlineData(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)]
    [InlineData(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped)]
    [InlineData(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed)]
    [InlineData(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing)]
    [InlineData(GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened)]
    public void A_session_that_is_not_playing_holds_nothing_off(
        GlobalSystemMediaTransportControlsSessionPlaybackStatus status)
    {
        // A film paused at lunchtime, or a player left open overnight. Treating the existence
        // of a session as a reason would hold the overlay off indefinitely.
        Assert.Equal(MediaKind.None, MediaSessions.KindOf(status, MediaPlaybackType.Video));
        Assert.Equal(MediaKind.None, MediaSessions.KindOf(status, MediaPlaybackType.Music));
    }

    [Fact]
    public void A_session_that_will_not_say_what_it_is_playing_holds_nothing_off()
    {
        Assert.Equal(MediaKind.None, MediaSessions.KindOf(Playing, null));
    }

    [Fact]
    public void A_slideshow_is_not_a_reason()
    {
        // Image is album art as often as it is a photo viewer, and it is also the shape a stale
        // session takes.
        Assert.Equal(MediaKind.None, MediaSessions.KindOf(Playing, MediaPlaybackType.Image));
    }

    [Fact]
    public void Unknown_is_not_a_reason()
    {
        Assert.Equal(MediaKind.None, MediaSessions.KindOf(Playing, MediaPlaybackType.Unknown));
    }

    // ---- reading the live state must never throw ---------------------------------------------

    /// <summary>Whatever the machine happens to be doing, asking must not throw and must not
    /// hold anything off by accident. A permanently failing call that held the overlay off
    /// would be worse than a screensaver arriving during a film.</summary>
    [Fact]
    public void Asking_what_is_playing_is_always_safe()
    {
        var kind = MediaSessions.Playing(out var app);

        Assert.True(Enum.IsDefined(kind));
        if (kind == MediaKind.None) Assert.Null(app);
    }

    [Fact]
    public void Describing_the_sessions_always_says_something()
    {
        Assert.NotEmpty(MediaSessions.Describe());
    }
}
