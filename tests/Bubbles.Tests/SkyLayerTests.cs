using System.Windows;
using System.Windows.Media;

using Bubbles.Overlay;

namespace Bubbles.Tests;

/// <summary>The sky and the shockwave are vertical gradients. Filling one Rectangle stretched
/// over the union of the screens ramped them across the whole desktop, so a monitor sitting in
/// the lower half of that union showed only the bottom of the gradient and had no horizon on it
/// at all. They are drawn once per screen now.
///
/// Asserted through the drawing the layer produces rather than through pixels: the question is
/// which rectangles it fills, and a rendered bitmap would answer it far less directly.</summary>
public sealed class SkyLayerTests
{
    private static Rect[] Drawn(SkyLayer layer, Size size)
    {
        layer.Measure(size);
        layer.Arrange(new Rect(size));
        layer.UpdateLayout();

        var drawing = VisualTreeHelper.GetDrawing(layer);
        if (drawing is null) return Array.Empty<Rect>();

        var rects = new List<Rect>();
        Collect(drawing, rects);
        return rects.ToArray();
    }

    private static void Collect(DrawingGroup group, List<Rect> into)
    {
        foreach (var child in group.Children)
        {
            switch (child)
            {
                case DrawingGroup nested:
                    Collect(nested, into);
                    break;
                case GeometryDrawing { Geometry: RectangleGeometry rect }:
                    into.Add(rect.Rect);
                    break;
            }
        }
    }

    private static SkyLayer Sky() => new() { Fill = OverlayWindow.EmissionSkyBrush() };

    [Fact]
    public void Each_screen_gets_the_whole_gradient() => Sta.Run(() =>
    {
        // A tall screen beside a short one, which is the case that used to put the horizon in a
        // different place on each.
        var tall = new Rect(0, 0, 2560, 1440);
        var short_ = new Rect(2560, 360, 1920, 1080);

        var layer = Sky();
        layer.Regions = new[] { tall, short_ };

        var drawn = Drawn(layer, new Size(4480, 1440));

        Assert.Equal(2, drawn.Length);
        Assert.Contains(tall, drawn);
        Assert.Contains(short_, drawn);
    });

    [Fact]
    public void With_no_regions_it_fills_its_own_bounds() => Sta.Run(() =>
    {
        // How Export builds these: a fixed-size container and no display layout at all.
        var layer = Sky();
        var drawn = Drawn(layer, new Size(460, 300));

        Assert.Equal(new[] { new Rect(0, 0, 460, 300) }, drawn);
    });

    [Fact]
    public void A_region_with_no_area_is_not_drawn() => Sta.Run(() =>
    {
        var layer = Sky();
        layer.Regions = new[] { new Rect(0, 0, 1920, 1080), Rect.Empty };

        var drawn = Drawn(layer, new Size(1920, 1080));

        Assert.Equal(new[] { new Rect(0, 0, 1920, 1080) }, drawn);
    });

    [Fact]
    public void Nothing_is_drawn_without_a_fill() => Sta.Run(() =>
    {
        var layer = new SkyLayer { Regions = new[] { new Rect(0, 0, 1920, 1080) } };

        Assert.Empty(Drawn(layer, new Size(1920, 1080)));
    });
}
