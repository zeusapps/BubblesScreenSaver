using Bubbles.Session;

namespace Bubbles.Tests;

/// <summary>The settings window offers each control the range in <see cref="Settings.Range"/>,
/// and <see cref="Settings.Clamped"/> enforces the same constants. These hold the two together:
/// a bound that drifted would let a slider reach a value the clamp then quietly moved, which
/// reads as the app losing the setting rather than refusing it.</summary>
public sealed class SettingsRangeTests
{
    /// <summary>Every simple bound: the property, its floor, and its ceiling. MaxRadius and
    /// BlackoutSeconds are absent because both are floored against another setting rather than
    /// against a constant, and are covered separately below.</summary>
    public static TheoryData<string, double, double> Bounds() => new()
    {
        { nameof(Settings.BubbleCount), Settings.Range.BubbleCountMin, Settings.Range.BubbleCountMax },
        { nameof(Settings.MinRadius), Settings.Range.MinRadiusMin, Settings.Range.MinRadiusMax },
        { nameof(Settings.Speed), Settings.Range.SpeedMin, Settings.Range.SpeedMax },
        { nameof(Settings.SpeedVariance), Settings.Range.SpeedVarianceMin, Settings.Range.SpeedVarianceMax },
        { nameof(Settings.Opacity), Settings.Range.OpacityMin, Settings.Range.OpacityMax },
        { nameof(Settings.Buoyancy), Settings.Range.BuoyancyMin, Settings.Range.BuoyancyMax },
        { nameof(Settings.Wobble), Settings.Range.WobbleMin, Settings.Range.WobbleMax },
        { nameof(Settings.MaxFps), Settings.Range.MaxFpsMin, Settings.Range.MaxFpsMax },
        { nameof(Settings.IdleSeconds), Settings.Range.IdleSecondsMin, Settings.Range.IdleSecondsMax },
        { nameof(Settings.Dim), Settings.Range.DimMin, Settings.Range.DimMax },
        { nameof(Settings.CollectRadius), Settings.Range.CollectRadiusMin, Settings.Range.CollectRadiusMax },
        { nameof(Settings.UpdateCheckHours), Settings.Range.UpdateCheckHoursMin, Settings.Range.UpdateCheckHoursMax },
        { nameof(Settings.FadeInSeconds), Settings.Range.FadeInSecondsMin, Settings.Range.FadeInSecondsMax },
    };

    private static double Read(Settings settings, string property) =>
        Convert.ToDouble(typeof(Settings).GetProperty(property)!.GetValue(settings));

    private static void Write(Settings settings, string property, double value)
    {
        var target = typeof(Settings).GetProperty(property)!;
        target.SetValue(settings, Convert.ChangeType(value, target.PropertyType));
    }

    [Theory]
    [MemberData(nameof(Bounds))]
    public void A_value_at_either_bound_survives_the_clamp(string property, double min, double max)
    {
        // The value a slider produces at each end of its travel. If the clamp moved it, the
        // control would be offering something the app does not accept.
        var atFloor = new Settings();
        Write(atFloor, property, min);
        Assert.Equal(min, Read(atFloor.Clamped(), property), 6);

        var atCeiling = new Settings();
        Write(atCeiling, property, max);
        Assert.Equal(max, Read(atCeiling.Clamped(), property), 6);
    }

    [Theory]
    [MemberData(nameof(Bounds))]
    public void A_value_outside_the_bounds_is_brought_to_them(string property, double min, double max)
    {
        var below = new Settings();
        Write(below, property, min - 1);
        Assert.Equal(min, Read(below.Clamped(), property), 6);

        var above = new Settings();
        Write(above, property, max + 1);
        Assert.Equal(max, Read(above.Clamped(), property), 6);
    }

    [Fact]
    public void The_largest_radius_reaches_its_own_ceiling()
    {
        // MaxRadius is floored at MinRadius rather than at a constant, so it is not in the table
        // above -- but its ceiling is still a bound the window offers.
        var settings = new Settings { MaxRadius = Settings.Range.MaxRadiusMax }.Clamped();

        Assert.Equal(Settings.Range.MaxRadiusMax, settings.MaxRadius);
    }

    [Fact]
    public void The_blackout_delay_reaches_its_own_ceiling()
    {
        var settings = new Settings { BlackoutSeconds = Settings.Range.BlackoutSecondsMax }.Clamped();

        Assert.Equal(Settings.Range.BlackoutSecondsMax, settings.BlackoutSeconds);
    }

    [Fact]
    public void The_blackout_delay_cannot_precede_the_start_delay()
    {
        // The reason the window presents this delay as time measured from when the screensaver
        // appears: a smaller number does not mean what it says.
        var settings = new Settings { IdleSeconds = 300, BlackoutSeconds = 60 }.Clamped();

        Assert.Equal(300, settings.BlackoutSeconds);
    }

    [Fact]
    public void Never_survives_the_clamp_as_never()
    {
        Assert.Equal(0, new Settings { BlackoutSeconds = 0 }.Clamped().BlackoutSeconds);
    }

    // ---- carrying settings from one instance to another ----------------------------------

    [Fact]
    public void Copying_carries_every_persisted_value()
    {
        var source = new Settings
        {
            BubbleCount = 77,
            Dim = 0.42,
            Theme = OverlayTheme.Soap,
            LockAfterBlackout = true,
            MonitorStandby = true,
            SettingsVersion = Settings.DensityVersion,
        };

        var target = new Settings();
        source.CopyTo(target);

        Assert.Equal(77, target.BubbleCount);
        Assert.Equal(0.42, target.Dim);
        Assert.Equal(OverlayTheme.Soap, target.Theme);
        Assert.True(target.LockAfterBlackout);
        Assert.True(target.MonitorStandby);
        Assert.Equal(Settings.DensityVersion, target.SettingsVersion);
    }

    [Fact]
    public void A_snapshot_is_detached_from_the_settings_it_was_taken_from()
    {
        var host = new SettingsHost(new Settings { Dim = 0.3 });
        var snapshot = host.Snapshot();

        host.Edit(s => s.Dim = 0.9);

        Assert.Equal(0.3, snapshot.Dim);
        Assert.Equal(0.9, host.Current.Dim);
    }

    [Fact]
    public void Restoring_a_snapshot_puts_the_values_back_without_swapping_the_instance()
    {
        var settings = new Settings { Dim = 0.3, BubbleCount = 10 };
        var host = new SettingsHost(settings);
        var snapshot = host.Snapshot();

        host.Edit(s =>
        {
            s.Dim = 0.9;
            s.BubbleCount = 200;
        });
        host.Restore(snapshot);

        Assert.Equal(0.3, host.Current.Dim);
        Assert.Equal(10, host.Current.BubbleCount);

        // The same object throughout: anything holding a reference from before the restore must
        // see the restored values, not the ones it was left with.
        Assert.Same(settings, host.Current);
    }

    [Fact]
    public void Every_listener_hears_about_an_edit()
    {
        var host = new SettingsHost(new Settings());
        var heard = 0;
        Settings? seen = null;

        host.Listen(_ => heard++);
        host.Listen(s => seen = s);
        host.Edit(s => s.Dim = 0.5);

        Assert.Equal(1, heard);
        Assert.Same(host.Current, seen);
    }

    [Fact]
    public void An_edit_is_clamped_before_anyone_is_told()
    {
        var host = new SettingsHost(new Settings());
        double? seen = null;

        host.Listen(s => seen = s.Opacity);
        host.Edit(s => s.Opacity = 99);

        Assert.Equal(Settings.Range.OpacityMax, seen);
    }
}
