using System.Windows;

namespace Bubbles.Displays;

/// <summary>A full-desktop layer that knows the desktop is several monitors.
///
/// The overlay is one window over the union of the screens, so a layer that draws against its
/// own bounds is drawing against a rectangle no monitor matches -- gradients ramp over the
/// tallest screen and geometry is scaled by it. Subclasses draw once per region instead.
///
/// A layer with no regions falls back to a single region covering its own bounds. That is not a
/// degenerate case to be tolerated: it is how <see cref="Export"/> builds these layers inside a
/// fixed-size container with no display layout at all, and it is what the overlay renders before
/// <c>UpdateRegions</c> has first run.</summary>
internal abstract class RegionLayer : FrameworkElement
{
    private static readonly Rect[] None = Array.Empty<Rect>();

    private IReadOnlyList<Rect> _regions = None;

    // The fallback is cached rather than allocated per frame: OnRender runs every frame while
    // anything is on screen, and these layers cover the whole desktop.
    private Rect[] _fallback = None;
    private double _fallbackWidth = -1;
    private double _fallbackHeight = -1;

    /// <summary>The monitors, in field coordinates. Empty means "use my own bounds".</summary>
    public IReadOnlyList<Rect> Regions
    {
        get => _regions;
        set
        {
            var next = value ?? None;
            if (MonitorRegions.Same(_regions, next)) return;

            // Copied, because the caller rebuilds its list on every display change and a layer
            // holding the live one would see the desktop change under it mid-render.
            _regions = next.Count == 0 ? None : next.ToArray();

            OnRegionsChanged();
            InvalidateVisual();
        }
    }

    /// <summary>Called when the desktop layout has genuinely changed, for subclasses holding
    /// anything derived from it -- a per-region schedule, a per-region brush.</summary>
    protected virtual void OnRegionsChanged()
    {
    }

    /// <summary>Redraws when the element is resized. A filled Rectangle got this for free; a
    /// layer that draws its own regions does not, and without it a layer running on the fallback
    /// keeps painting the size it had before the window was stretched over the desktop.</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        InvalidateVisual();
    }

    /// <summary>The regions to draw into: the real monitors, or this element's own bounds when
    /// there are none. Empty only when the element has no size to draw into either.</summary>
    protected IReadOnlyList<Rect> RegionsToDraw()
    {
        if (_regions.Count > 0) return _regions;

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return None;

        if (_fallbackWidth != width || _fallbackHeight != height)
        {
            _fallback = new[] { new Rect(0, 0, width, height) };
            _fallbackWidth = width;
            _fallbackHeight = height;
        }

        return _fallback;
    }
}
