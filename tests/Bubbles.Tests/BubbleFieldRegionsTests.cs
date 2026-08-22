using System.Windows;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>The field's half of "the same density on every screen". The arithmetic is asserted
/// in <see cref="MonitorRegionsTests"/>; this is about the field actually applying it, and about
/// bubbles never being left on a screen they no longer belong to.</summary>
public sealed class BubbleFieldRegionsTests
{
    private const double Width = 1920;
    private const double Height = 1080;

    private static Rect Screen(double x, double w, double h) => new(x, 0, w, h);

    private static BubbleField Field(int density, params Rect[] regions)
    {
        var field = new BubbleField(new Settings { BubbleCount = density }.Clamped());

        var union = new Rect();
        foreach (var r in regions) union.Union(r);

        field.Resize(new Size(Math.Max(Width, union.Right), Math.Max(Height, union.Bottom)));
        field.SetRegions(regions);
        return field;
    }

    private static int[] PerRegion(BubbleField field, int regions)
    {
        var counts = new int[regions];
        foreach (var b in field.Bubbles) counts[b.Region]++;
        return counts;
    }

    [Fact]
    public void One_baseline_screen_carries_the_configured_density()
    {
        var field = Field(22, Screen(0, Width, Height));

        Assert.Equal(22, field.Bubbles.Count);
    }

    [Fact]
    public void A_laptop_beside_a_larger_external_gets_the_smaller_share()
    {
        var field = Field(22, Screen(0, Width, Height), Screen(Width, 3840, 2160));
        var counts = PerRegion(field, 2);

        Assert.Equal(110, field.Bubbles.Count);
        Assert.Equal(22, counts[0]);
        Assert.Equal(88, counts[1]);
    }

    [Fact]
    public void Connecting_a_second_screen_leaves_the_first_ones_count_alone()
    {
        // The defect this change exists to fix: a fixed total meant the screen you were already
        // looking at lost half its bubbles the moment you docked.
        var one = Screen(0, Width, Height);
        var two = Screen(Width, Width, Height);

        var field = Field(22, one);
        Assert.Equal(22, field.Bubbles.Count);

        field.Resize(new Size(Width * 2, Height));
        field.SetRegions(new[] { one, two });

        Assert.Equal(44, field.Bubbles.Count);
        Assert.Equal(new[] { 22, 22 }, PerRegion(field, 2));
    }

    [Fact]
    public void Disconnecting_a_screen_leaves_nobody_stranded_on_it()
    {
        var one = Screen(0, Width, Height);
        var two = Screen(Width, Width, Height);

        var field = Field(22, one, two);
        Assert.Equal(44, field.Bubbles.Count);

        field.Resize(new Size(Width, Height));
        field.SetRegions(new[] { one });

        Assert.All(field.Bubbles, b => Assert.Equal(0, b.Region));
        Assert.All(field.Bubbles, b => Assert.InRange(b.X, one.Left, one.Right));
    }

    [Fact]
    public void Every_bubble_sits_inside_the_screen_it_was_dealt_to()
    {
        // A bubble moved between screens and not re-placed is off on a monitor it no longer
        // belongs to, and the clamp drags it back across the desktop in a straight line.
        var one = Screen(0, Width, Height);
        var two = Screen(Width, 3840, 2160);
        var field = Field(22, one, two);

        foreach (var b in field.Bubbles)
        {
            var region = b.Region == 0 ? one : two;

            Assert.InRange(b.X, region.Left, region.Right);
            Assert.InRange(b.Y, region.Top, region.Bottom);
        }
    }

    [Fact]
    public void Density_per_unit_area_is_even_across_mismatched_screens()
    {
        var one = Screen(0, Width, Height);
        var two = Screen(Width, 3440, 1440);
        var field = Field(30, one, two);
        var counts = PerRegion(field, 2);

        var first = counts[0] / (one.Width * one.Height);
        var second = counts[1] / (two.Width * two.Height);

        // Within one bubble's worth on the smaller screen: the split is integers, not fractions.
        Assert.True(Math.Abs(first - second) < 1.0 / (one.Width * one.Height),
            $"densities differ: {counts[0]} on {one.Width}x{one.Height}, {counts[1]} on {two.Width}x{two.Height}");
    }

    [Fact]
    public void A_display_event_that_changes_nothing_leaves_the_field_alone()
    {
        var regions = new[] { Screen(0, Width, Height), Screen(Width, Width, Height) };
        var field = Field(22, regions);

        var before = field.Bubbles.Select(b => (b.X, b.Y, b.Region)).ToArray();
        field.SetRegions(new[] { regions[0], regions[1] });
        var after = field.Bubbles.Select(b => (b.X, b.Y, b.Region)).ToArray();

        Assert.Equal(before, after);
    }
}
