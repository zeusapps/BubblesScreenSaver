using System.Text.Json;

namespace Bubbles.Tests;

/// <summary>A settings.json written before the settings window existed must still be read the
/// same way afterwards.
///
/// The window changed where settings are edited, not what they are. Nothing here is a schema
/// migration, so the guard is simply that a real file from before the change survives a
/// round-trip unaltered -- including SettingsVersion, which decides whether BubbleCount is read
/// as a density or as a total.</summary>
public sealed class SettingsCompatibilityTests
{
    /// <summary>A settings.json as written by the version before the settings window, with
    /// values deliberately away from the defaults.</summary>
    private const string BeforeTheChange = """
        {
          "SettingsVersion": 1,
          "BubbleCount": 14,
          "MinRadius": 33.333333333333336,
          "MaxRadius": 125,
          "Speed": 42,
          "SpeedVariance": 0.65,
          "Opacity": 0.85,
          "Buoyancy": 0,
          "Wobble": 0.045,
          "HideCursor": true,
          "ClickThrough": true,
          "MaxFps": 30,
          "PauseWhileMicrophoneInUse": true,
          "PauseWhileCameraInUse": true,
          "PauseInFullScreen": true,
          "PauseWhileAudioPlaying": true,
          "PauseWhileMediaPlaying": true,
          "IdleSeconds": 120,
          "BlackoutSeconds": 120,
          "Dim": 0.55,
          "FadeInSeconds": 2,
          "DimMonitorBacklight": true,
          "DisableHdrDuringBlackout": true,
          "MonitorStandby": false,
          "AutoUpdate": true,
          "UpdateCheckHours": 24,
          "Theme": "Zone",
          "Animated": true,
          "CollectRadius": 60,
          "ShowDetector": true,
          "Lightning": true,
          "Weather": true,
          "Emission": true,
          "LockAfterBlackout": true
        }
        """;

    private static Settings Read() =>
        JsonSerializer.Deserialize<Settings>(BeforeTheChange, Settings.JsonOptions)!;

    [Fact]
    public void An_older_file_is_read_with_every_value_intact()
    {
        var settings = Read();

        Assert.Equal(1, settings.SettingsVersion);
        Assert.Equal(14, settings.BubbleCount);
        Assert.Equal(120, settings.IdleSeconds);
        Assert.Equal(120, settings.BlackoutSeconds);
        Assert.Equal(0.55, settings.Dim);
        Assert.Equal(OverlayTheme.Zone, settings.Theme);
        Assert.True(settings.LockAfterBlackout);
        Assert.False(settings.MonitorStandby);
    }

    [Fact]
    public void A_file_written_before_the_keyboard_lighting_setting_reads_it_as_off()
    {
        // The one thing that must be true of every new setting, and the one that matters most
        // for this one: an existing installation cannot acquire it by upgrading. Enabling it
        // takes the keyboard away from whatever vendor software is managing it, which is not
        // something to do to somebody who never asked.
        Assert.False(Read().KeyboardLighting);
        Assert.False(Read().Clamped().KeyboardLighting);
    }

    [Fact]
    public void Clamping_an_older_file_changes_nothing()
    {
        // Every value in it is already legal, so the clamp must be a no-op. If a bound moved
        // when it was lifted into Settings.Range, this is where it shows.
        var settings = Read();
        var before = JsonSerializer.Serialize(settings, Settings.JsonOptions);

        settings.Clamped();

        Assert.Equal(before, JsonSerializer.Serialize(settings, Settings.JsonOptions));
    }

    [Fact]
    public void A_blackout_delay_equal_to_the_start_delay_is_left_alone()
    {
        // The real file has both at 120, which is the boundary of the cross-field clamp: legal,
        // and one second lower would not be.
        var settings = Read().Clamped();

        Assert.Equal(settings.IdleSeconds, settings.BlackoutSeconds);
    }

    [Fact]
    public void The_written_shape_still_carries_every_key_the_older_file_had()
    {
        var written = JsonSerializer.Serialize(Read(), Settings.JsonOptions);

        using var before = JsonDocument.Parse(BeforeTheChange);
        using var after = JsonDocument.Parse(written);

        var keys = after.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

        foreach (var property in before.RootElement.EnumerateObject())
            Assert.Contains(property.Name, keys);
    }

    // ---- the blackout delay, as the settings window presents it -------------------------

    // The window offers this delay as time *after* the screensaver appears, because that is what
    // the clamp enforces. That makes zero a real setting -- black the moment the artifacts would
    // have shown -- so "never" needs a value of its own rather than sharing zero with it.
    private const double Never = -1;

    private static double Gap(Settings s) =>
        s.BlackoutSeconds <= 0 ? Never : Math.Max(0, s.BlackoutSeconds - s.IdleSeconds);

    private static void SetGap(Settings s, double gap) =>
        s.BlackoutSeconds = gap < 0 ? 0 : s.IdleSeconds + gap;

    [Fact]
    public void A_blackout_at_the_moment_the_screensaver_starts_is_not_never()
    {
        // The real file: both delays at 120, so the gap is zero and the blackout is enabled.
        // Reading that back as "never" would switch it off when the window closed -- silently
        // disabling the thing that keeps an OLED from being held lit.
        var settings = Read();

        Assert.Equal(0, Gap(settings));
        Assert.NotEqual(Never, Gap(settings));
    }

    [Theory]
    [InlineData(120, 120)]   // black the moment the artifacts appear
    [InlineData(120, 180)]   // a minute later
    [InlineData(60, 660)]    // the shipped default
    [InlineData(120, 0)]     // never
    public void The_blackout_delay_survives_a_round_trip_through_the_window(
        double idle, double blackout)
    {
        var settings = new Settings { IdleSeconds = idle, BlackoutSeconds = blackout }.Clamped();
        var shown = Gap(settings);

        SetGap(settings, shown);
        settings.Clamped();

        Assert.Equal(blackout, settings.BlackoutSeconds);
    }

    /// <summary>What the window does when the start delay moves: the gap goes with it.</summary>
    private static void MoveStartDelay(Settings s, double seconds)
    {
        var gap = Gap(s);
        s.IdleSeconds = seconds;
        if (gap >= 0) s.BlackoutSeconds = seconds + gap;
    }

    [Theory]
    [InlineData(120, 180, 300, 360)]   // a one-minute gap, start delay raised to five
    [InlineData(120, 180, 60, 120)]    // ...and lowered to one
    [InlineData(60, 660, 120, 720)]    // the shipped default, start delay doubled
    [InlineData(120, 120, 600, 600)]   // black as soon as it starts, and it stays that way
    public void The_gap_follows_the_start_delay(
        double idle, double blackout, double newIdle, double expected)
    {
        var settings = new Settings { IdleSeconds = idle, BlackoutSeconds = blackout }.Clamped();
        var gap = Gap(settings);

        MoveStartDelay(settings, newIdle);
        settings.Clamped();

        Assert.Equal(newIdle, settings.IdleSeconds);
        Assert.Equal(expected, settings.BlackoutSeconds);
        Assert.Equal(gap, Gap(settings));
    }

    [Fact]
    public void Raising_the_start_delay_does_not_quietly_close_the_gap()
    {
        // The defect this guards: the clamp floors BlackoutSeconds at IdleSeconds, so setting the
        // start delay alone used to leave a screen told to go black a minute after the artifacts
        // going black with them instead.
        var settings = new Settings { IdleSeconds = 120, BlackoutSeconds = 180 }.Clamped();

        MoveStartDelay(settings, 300);
        settings.Clamped();

        Assert.Equal(60, settings.BlackoutSeconds - settings.IdleSeconds);
    }

    [Fact]
    public void Never_stays_never_when_the_start_delay_moves()
    {
        var settings = new Settings { IdleSeconds = 120, BlackoutSeconds = 0 }.Clamped();

        MoveStartDelay(settings, 600);
        settings.Clamped();

        Assert.Equal(0, settings.BlackoutSeconds);
    }

    [Fact]
    public void Choosing_never_disables_the_blackout()
    {
        var settings = new Settings { IdleSeconds = 120, BlackoutSeconds = 600 };

        SetGap(settings, Never);

        Assert.Equal(0, settings.Clamped().BlackoutSeconds);
    }

    [Fact]
    public void A_file_with_no_version_still_asks_for_the_density_migration()
    {
        // The pre-density case, which the settings window must not disturb: an absent
        // SettingsVersion leaves the initializer at zero, and that is what marks the file as
        // holding a total rather than a density.
        var settings = JsonSerializer.Deserialize<Settings>(
            """{ "BubbleCount": 40 }""", Settings.JsonOptions)!;

        Assert.True(settings.NeedsDensityMigration);
    }
}
