using Bubbles.Session;

namespace Bubbles.Tests;

/// <summary>Watching a video produces no keyboard or mouse input, so the idle timer decides you
/// have left — the same failure as sitting on a call. These are the two signals that catch it,
/// and the ways each could go wrong.</summary>
public sealed class HoldOffTests
{
    // ---- fullscreen, by geometry ---------------------------------------------------------

    // The monitor these numbers were measured on.
    private const int Left = 0, Top = 0, Right = 2560, Bottom = 1600;

    private static bool Fills(int l, int t, int r, int b) =>
        UserBusy.FillsMonitor(l, t, r, b, Left, Top, Right, Bottom);

    [Fact]
    public void A_window_on_the_monitor_bounds_is_fullscreen()
    {
        Assert.True(Fills(0, 0, 2560, 1600));
    }

    [Fact]
    public void A_maximised_window_is_not_fullscreen()
    {
        // Measured from a maximised Windows Terminal: it overshoots the monitor by the width
        // of its invisible resize border and stops short at the bottom for the taskbar.
        Assert.False(Fills(-11, -11, 2571, 1539));
    }

    [Fact]
    public void A_maximised_window_is_still_not_fullscreen_with_the_taskbar_hidden()
    {
        // With the taskbar auto-hidden the work area becomes the whole monitor, so the bottom
        // edge no longer gives the game away. The overshoot on the other three still does --
        // which is the entire reason this compares all four edges rather than coverage.
        Assert.False(Fills(-11, -11, 2571, 1611));
    }

    [Fact]
    public void A_window_a_pixel_out_is_still_fullscreen()
    {
        // Players are occasionally a hair off; being strict to the pixel would miss them.
        Assert.True(Fills(1, 0, 2559, 1601));
    }

    [Fact]
    public void A_window_covering_only_part_of_the_screen_is_not_fullscreen()
    {
        Assert.False(Fills(100, 100, 1200, 900));
    }

    [Fact]
    public void A_window_larger_than_the_monitor_is_not_fullscreen()
    {
        // Spanning two monitors is not the same as filling this one, and neither is a window
        // dragged mostly off-screen.
        Assert.False(Fills(0, 0, 5120, 1600));
        Assert.False(Fills(-2000, 0, 2560, 1600));
    }

    [Fact]
    public void A_monitor_that_is_not_at_the_origin_works_the_same()
    {
        // A second screen sits at an offset, and its windows carry that offset too.
        Assert.True(UserBusy.FillsMonitor(2560, 0, 5120, 1440, 2560, 0, 5120, 1440));
        Assert.False(UserBusy.FillsMonitor(2549, -11, 5131, 1379, 2560, 0, 5120, 1440));
    }

    // ---- sound ----------------------------------------------------------------------------

    private const long Second = 1000;

    [Fact]
    public void Sound_playing_counts_as_somebody_watching()
    {
        Assert.True(new SoundWatch().Playing(0.4f, 0));
    }

    [Fact]
    public void Silence_does_not()
    {
        Assert.False(new SoundWatch().Playing(0f, 0));
    }

    [Fact]
    public void A_quiet_passage_does_not_let_the_screensaver_in()
    {
        var watch = new SoundWatch(graceSeconds: 30);
        watch.Playing(0.6f, 0);

        // A pause in the dialogue, a cut between scenes, the gap between tracks.
        Assert.True(watch.Playing(0f, 10 * Second));
        Assert.True(watch.Playing(0f, 25 * Second));
    }

    [Fact]
    public void Silence_for_longer_than_the_grace_period_releases_it()
    {
        var watch = new SoundWatch(graceSeconds: 30);
        watch.Playing(0.6f, 0);

        Assert.False(watch.Playing(0f, 31 * Second));
    }

    [Fact]
    public void The_grace_period_runs_from_the_last_sound_not_the_first()
    {
        var watch = new SoundWatch(graceSeconds: 30);

        watch.Playing(0.6f, 0);
        watch.Playing(0.6f, 60 * Second);   // still going an hour in

        Assert.True(watch.Playing(0f, 80 * Second));
        Assert.False(watch.Playing(0f, 95 * Second));
    }

    [Fact]
    public void A_trickle_of_noise_is_silence()
    {
        // An endpoint that idles just above zero would otherwise hold the screensaver off for
        // ever, which is the one failure this check must not have. Measured at a flat 0.0000
        // on the machine this was built for, but not every device is so tidy.
        Assert.False(new SoundWatch().Playing(SoundWatch.Silence, 0));
        Assert.False(new SoundWatch().Playing(SoundWatch.Silence / 2, 0));
    }

    [Fact]
    public void No_reading_at_all_is_not_a_reason_to_hold_off()
    {
        // A machine with no output device must not be a machine whose screensaver never runs.
        var watch = new SoundWatch();

        Assert.False(watch.Playing(null, 0));
        Assert.False(watch.Playing(null, 10 * Second));
    }

    [Fact]
    public void A_lost_audio_device_does_not_freeze_an_earlier_hold_off()
    {
        var watch = new SoundWatch(graceSeconds: 30);
        watch.Playing(0.6f, 0);

        // The reading goes away mid-grace. It must not be read as continuing sound.
        Assert.False(watch.Playing(null, 5 * Second));
    }
}
