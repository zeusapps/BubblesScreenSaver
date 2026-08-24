using System.Text.Json;

namespace Bubbles.Tests;

/// <summary>settings.json is hand-edited, so Clamped() is the only thing standing between a
/// typo and an overlay that cannot be dismissed or never appears at all.</summary>
public sealed class SettingsTests
{
    [Fact]
    public void A_nonsense_bubble_count_is_pulled_back_into_range()
    {
        Assert.Equal(1, new Settings { BubbleCount = 0 }.Clamped().BubbleCount);
        Assert.Equal(400, new Settings { BubbleCount = 100_000 }.Clamped().BubbleCount);
    }

    [Fact]
    public void The_overlay_can_never_be_clamped_to_invisible()
    {
        // Zero opacity would leave the thing running, eating input events, showing nothing.
        Assert.True(new Settings { Opacity = 0 }.Clamped().Opacity > 0);
    }

    [Fact]
    public void The_radius_range_cannot_be_inverted()
    {
        var settings = new Settings { MinRadius = 300, MaxRadius = 10 }.Clamped();

        Assert.True(settings.MaxRadius >= settings.MinRadius);
    }

    [Fact]
    public void The_radius_range_is_clamped_against_the_corrected_minimum_not_the_raw_one()
    {
        // MinRadius is floored at 4 first; MaxRadius then has to respect the corrected value.
        var settings = new Settings { MinRadius = -50, MaxRadius = -100 }.Clamped();

        Assert.Equal(4, settings.MinRadius);
        Assert.True(settings.MaxRadius >= settings.MinRadius);
    }

    [Fact]
    public void Blackout_never_lands_before_the_bubbles_do()
    {
        // Blacking out earlier than the fade-in would skip the screensaver entirely.
        var settings = new Settings { IdleSeconds = 600, BlackoutSeconds = 60 }.Clamped();

        Assert.True(settings.BlackoutSeconds >= settings.IdleSeconds);
    }

    [Fact]
    public void Zero_blackout_stays_zero_because_it_means_never()
    {
        Assert.Equal(0, new Settings { IdleSeconds = 600, BlackoutSeconds = 0 }.Clamped().BlackoutSeconds);
    }

    [Fact]
    public void The_idle_delay_can_never_be_zero()
    {
        // A zero would fire the screensaver into the middle of typing.
        Assert.True(new Settings { IdleSeconds = 0 }.Clamped().IdleSeconds >= 1);
    }

    [Fact]
    public void Clamping_a_default_configuration_changes_nothing()
    {
        var defaults = new Settings();
        var clamped = new Settings().Clamped();

        Assert.Equal(JsonSerializer.Serialize(defaults), JsonSerializer.Serialize(clamped));
    }

    [Fact]
    public void A_file_that_names_only_one_setting_leaves_the_rest_at_their_defaults()
    {
        var settings = JsonSerializer.Deserialize<Settings>("""{ "BubbleCount": 7 }""");

        Assert.NotNull(settings);
        Assert.Equal(7, settings.BubbleCount);
        Assert.Equal(new Settings().IdleSeconds, settings.IdleSeconds);
    }

    [Fact]
    public void Asking_for_a_PIN_is_off_unless_it_is_asked_for()
    {
        // Locking somebody's machine is not a thing to start doing to them because they
        // upgraded, so the default and the absent-from-file case must both be false.
        Assert.False(new Settings().LockAfterBlackout);
        Assert.False(JsonSerializer.Deserialize<Settings>("{}")!.LockAfterBlackout);
        Assert.False(new Settings { LockAfterBlackout = false }.Clamped().LockAfterBlackout);
    }

    [Fact]
    public void Asking_for_a_PIN_survives_a_round_trip_and_a_clamp()
    {
        // A setting that quietly reverted would leave an unlocked laptop in a hotel room.
        var saved = JsonSerializer.Serialize(new Settings { LockAfterBlackout = true });
        var restored = JsonSerializer.Deserialize<Settings>(saved);

        Assert.True(restored!.LockAfterBlackout);
        Assert.True(restored.Clamped().LockAfterBlackout);
    }

    [Fact]
    public void Watching_for_media_is_on_by_default_including_for_an_existing_settings_file()
    {
        // Anybody upgrading has a settings.json written before this existed, and the case this
        // fixes -- silent video, which nothing else catches -- is exactly what they are hitting.
        Assert.True(new Settings().PauseWhileMediaPlaying);
        Assert.True(JsonSerializer.Deserialize<Settings>("{}")!.PauseWhileMediaPlaying);
        Assert.True(JsonSerializer.Deserialize<Settings>("""{"IdleSeconds":30}""")!.PauseWhileMediaPlaying);
    }

    [Fact]
    public void Keyboard_lighting_is_off_by_default()
    {
        // It needs a server almost nobody is running, and turning it on takes the keyboard
        // away from whatever vendor software owns it. Both are reasons it must be asked for.
        Assert.False(new Settings().KeyboardLighting);
        Assert.False(JsonSerializer.Deserialize<Settings>("{}")!.KeyboardLighting);
    }

    [Fact]
    public void Keyboard_weather_is_off_by_default()
    {
        // It holds the keyboard for as long as the screensaver is up, not for an Emission, so
        // it is a second deliberate act on top of a setting that is itself off by default.
        Assert.False(new Settings().KeyboardWeather);
        Assert.False(JsonSerializer.Deserialize<Settings>("{}")!.KeyboardWeather);
    }

    [Fact]
    public void Turning_keyboard_lighting_on_survives_a_round_trip_and_a_clamp()
    {
        var saved = JsonSerializer.Serialize(new Settings { KeyboardLighting = true });
        var restored = JsonSerializer.Deserialize<Settings>(saved);

        Assert.True(restored!.KeyboardLighting);
        Assert.True(restored.Clamped().KeyboardLighting);
    }

    [Fact]
    public void Turning_the_media_signal_off_survives_a_round_trip_and_a_clamp()
    {
        // The escape hatch for a player that misreports itself as permanently playing. If it
        // silently reverted, the overlay would be held off for ever with no way back.
        var saved = JsonSerializer.Serialize(new Settings { PauseWhileMediaPlaying = false });
        var restored = JsonSerializer.Deserialize<Settings>(saved);

        Assert.False(restored!.PauseWhileMediaPlaying);
        Assert.False(restored.Clamped().PauseWhileMediaPlaying);
    }

    [Fact]
    public void A_settings_file_written_before_the_density_change_asks_to_be_converted()
    {
        // Anybody upgrading has a settings.json with no version key in it, holding a BubbleCount
        // that means a total. Missing the case would silently reinterpret it as a density and
        // multiply their bubbles by the size of their desktop.
        var old = JsonSerializer.Deserialize<Settings>("""{ "BubbleCount": 22 }""");

        Assert.NotNull(old);
        Assert.True(old.NeedsDensityMigration);
    }

    [Fact]
    public void A_converted_file_is_never_converted_again()
    {
        // Without the stamp the conversion re-applies on every launch and compounds, and the
        // bubbles dwindle a little each time the app starts.
        var converted = JsonSerializer.Deserialize<Settings>(
            $$"""{ "BubbleCount": 4, "SettingsVersion": {{Settings.DensityVersion}} }""");

        Assert.NotNull(converted);
        Assert.False(converted.NeedsDensityMigration);
    }

    [Fact]
    public void The_version_stamp_survives_a_round_trip_and_a_clamp()
    {
        var saved = JsonSerializer.Serialize(new Settings { SettingsVersion = Settings.DensityVersion });
        var restored = JsonSerializer.Deserialize<Settings>(saved);

        Assert.False(restored!.NeedsDensityMigration);
        Assert.False(restored.Clamped().NeedsDensityMigration);
    }

    [Fact]
    public void Settings_survive_a_round_trip()
    {
        var original = new Settings { BubbleCount = 31, Theme = OverlayTheme.Soap, Emission = false };

        var restored = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(original));

        Assert.NotNull(restored);
        Assert.Equal(31, restored.BubbleCount);
        Assert.Equal(OverlayTheme.Soap, restored.Theme);
        Assert.False(restored.Emission);
    }
}
