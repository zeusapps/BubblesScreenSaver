using System.Windows;
using System.Windows.Media;

using Bubbles.Displays;

namespace Bubbles.Overlay;

/// <summary>A vertical gradient painted once per monitor: the Emission's burning sky, and the
/// wavefront's flare.
///
/// These were plain Rectangles filled with a LinearGradientBrush. A brush ramps over the element
/// it fills, and the element is the whole virtual desktop, so on a desktop taller than one
/// monitor each screen showed a different slice of the sky -- a monitor sitting in the lower half
/// of the union never saw the crimson at the top, and there was no horizon on it at all. Drawing
/// the same brush once per region puts the full ramp on every screen.
///
/// The brushes themselves stay where they were, so the palette is still defined in one place and
/// the offline renderers keep calling for it.</summary>
internal sealed class SkyLayer : RegionLayer
{
    /// <summary>The gradient to ramp over each screen. Expected to use relative coordinates, so
    /// that it maps onto whichever rectangle it is drawn into.</summary>
    public Brush? Fill { get; set; }

    protected override void OnRender(DrawingContext dc)
    {
        if (Fill is null) return;

        foreach (var region in RegionsToDraw())
        {
            if (region.Width <= 0 || region.Height <= 0) continue;

            dc.DrawRectangle(Fill, null, region);
        }
    }
}
