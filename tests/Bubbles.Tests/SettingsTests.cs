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
