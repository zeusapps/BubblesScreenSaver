using System.Windows;

using Bubbles.Displays;

namespace Bubbles.Tests;

/// <summary>The arithmetic behind "the same density on every screen". None of this needs a
/// window, which is the point: the defect it fixes is only visible on a second monitor, and
/// nobody has one attached while they are running tests.</summary>
public sealed class MonitorRegionsTests
{
    private static Rect Screen(double w, double h) => new(0, 0, w, h);

    private static readonly Rect Baseline = Screen(1920, 1080);

    [Fact]
    public void One_baseline_screen_carries_exactly_the_configured_density()
    {
        Assert.Equal(22, MonitorRegions.DerivedTotal(22, new[] { Baseline }));
    }

    [Fact]
    public void A_second_screen_adds_bubbles_rather_than_dividing_the_ones_already_there()
    {
        // The whole reason BubbleCount changed meaning: plugging in a monitor used to halve
        // the density on the screen you were already looking at.
        var total = MonitorRegions.DerivedTotal(22, new[] { Baseline, Baseline });

        Assert.Equal(44, total);
        Assert.Equal(new[] { 22, 22 }, MonitorRegions.Split(total, new[] { Baseline, Baseline }));
    }

    [Fact]
    public void A_bigger_screen_carries_proportionally_more()
    {
        var quadruple = Screen(3840, 2160);

        Assert.Equal(88, MonitorRegions.DerivedTotal(22, new[] { quadruple }));
    }

    [Fact]
    public void A_laptop_beside_a_larger_external_ends_up_at_the_same_density_on_both()
    {
        var laptop = Baseline;
        var external = Screen(3840, 2160);   // four times the area

        var regions = new[] { laptop, external };
        var counts = MonitorRegions.Split(MonitorRegions.DerivedTotal(22, regions), regions);

        Assert.Equal(22, counts[0]);
        Assert.Equal(88, counts[1]);

        var laptopDensity = counts[0] / MonitorRegions.AreaOf(laptop);
        var externalDensity = counts[1] / MonitorRegions.AreaOf(external);

        Assert.Equal(laptopDensity, externalDensity, 6);
    }

    [Fact]
    public void The_parts_always_sum_to_the_total()
    {
        // Independent rounding does not, which is a bubble that spawns on no screen.
        var regions = new[] { Screen(1366, 768), Screen(3440, 1440), Screen(1080, 1920) };

        foreach (var density in new[] { 1, 7, 22, 50, 137 })
        {
            var total = MonitorRegions.DerivedTotal(density, regions);
            var counts = MonitorRegions.Split(total, regions);

            Assert.Equal(total, counts.Sum());
        }
    }

    [Fact]
    public void A_screen_too_small_for_a_share_still_gets_one()
    {
        // A tiny secondary display rounds to zero on its own merits; leaving it empty reads as
        // the app having failed to notice it.
        var regions = new[] { Screen(3840, 2160), Screen(100, 60) };
        var counts = MonitorRegions.Split(20, regions);

        Assert.Equal(20, counts.Sum());
        Assert.True(counts[1] >= 1);
    }

    [Fact]
    public void Every_screen_with_area_gets_something_when_there_is_enough_to_go_round()
    {
        var regions = new[] { Screen(3840, 2160), Screen(1920, 1080), Screen(800, 600), Screen(640, 480) };
        var counts = MonitorRegions.Split(MonitorRegions.DerivedTotal(22, regions), regions);

        Assert.All(counts, c => Assert.True(c >= 1));
    }

    [Fact]
    public void More_screens_than_bubbles_serves_the_biggest_and_does_not_hang()
    {
        var regions = new[] { Screen(1920, 1080), Screen(1920, 1080), Screen(1920, 1080) };
        var counts = MonitorRegions.Split(2, regions);

        Assert.Equal(2, counts.Sum());
    }

    [Fact]
    public void A_desktop_large_enough_to_trip_the_ceiling_is_held_at_it()
    {
        var wall = Enumerable.Repeat(Screen(3840, 2160), 8).ToArray();

        Assert.Equal(400, MonitorRegions.DerivedTotal(22, wall));
    }

    [Fact]
    public void No_layout_yet_is_treated_as_one_baseline_screen()
    {
        // UpdateRegions has not run, or the window is collapsed to 1x1. Rendering nothing would
        // be worse than rendering a single screen's worth.
        Assert.Equal(22, MonitorRegions.DerivedTotal(22, Array.Empty<Rect>()));
        Assert.Equal(22, MonitorRegions.DerivedTotal(22, new[] { new Rect(0, 0, 0, 0) }));
    }

    [Fact]
    public void An_empty_rectangle_neither_adds_area_nor_subtracts_it()
    {
        // Rect.Empty carries negative-infinity extents, so an unguarded multiplication makes the
        // whole desktop's area negative and every count collapses to the floor.
        Assert.Equal(0, MonitorRegions.AreaOf(Rect.Empty));
        Assert.Equal(1920 * 1080, MonitorRegions.TotalArea(new[] { Baseline, Rect.Empty }));
    }

    [Fact]
    public void Converting_a_stored_total_lands_as_close_to_it_as_an_integer_density_allows()
    {
        // The upgrade promise, and its limit. On a desktop five baseline screens in area, one
        // step of density is five bubbles, so 22 is simply not a reachable total -- 20 is the
        // nearest. What must hold is that nothing closer was available.
        foreach (var regions in new[]
                 {
                     new[] { Baseline },
                     new[] { Baseline, Baseline },
                     new[] { Baseline, Screen(3840, 2160) },
                     new[] { Screen(3440, 1440) },
                 })
        {
            var density = MonitorRegions.DensityFor(22, regions);
            var reached = MonitorRegions.DerivedTotal(density, regions);

            foreach (var other in new[] { density - 1, density + 1 })
            {
                if (other < 1) continue;

                var alternative = MonitorRegions.DerivedTotal(other, regions);
                Assert.True(Math.Abs(alternative - 22) >= Math.Abs(reached - 22),
                    $"density {other} reaches {alternative}, closer to 22 than {density} reaching {reached}");
            }
        }
    }

    [Fact]
    public void Converting_is_exact_whenever_the_desktop_is_a_whole_number_of_baseline_screens()
    {
        // The common desks: one screen, or two of the same size.
        foreach (var regions in new[] { new[] { Baseline }, new[] { Baseline, Baseline } })
        {
            var density = MonitorRegions.DensityFor(22, regions);

            Assert.Equal(22, MonitorRegions.DerivedTotal(density, regions));
        }
    }

    [Fact]
    public void Converting_with_no_layout_leaves_the_number_alone()
    {
        Assert.Equal(22, MonitorRegions.DensityFor(22, Array.Empty<Rect>()));
    }
}
