using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

using Bubbles.Displays;

namespace Bubbles.Zone;

/// <summary>Fog and rain, in front of the artifacts.
///
/// In front deliberately. Fog rendered behind them fogs nothing -- the artifacts stay sharp over
/// the top of it and the effect reads as a haze on the desktop instead. Rain belongs in front for
/// the same reason. The storm's lightning is the exception and stays on
/// <see cref="LightningLayer"/> behind them, on the reasoning that layer already records: it is
/// the sky, so it silhouettes the artifacts rather than covering them.
///
/// A Canvas of scrolling sheets rather than a layer that draws in OnRender. The artifacts get
/// away with drawing live because they are small and staggered across frames, and the lightning
/// because a strike is 0.42s inside a 12.5s Emission -- but rain covers the whole desktop and
/// never stops, so its motion is handed to the compositor and costs nothing per frame.</summary>
internal sealed class WeatherLayer : Canvas
{
    /// <summary>One scrolling sheet: a rectangle over one screen, filled with a tiled brush that
    /// slides. Density comes from the tile, which is a fixed size in DIP, so a larger screen
    /// carries proportionally more of it without anything being counted.</summary>
    private sealed class Sheet
    {
        public required Rectangle Element;
        public required int Scale;
        public int Level = -2;
    }

    private readonly List<Sheet> _rain = new();
    private readonly List<Sheet> _fog = new();

    private IReadOnlyList<Rect> _regions = Array.Empty<Rect>();
    private bool _scrolling;

    /// <summary>How much of the fog survives, 0 to 1.
    ///
    /// Driven down over an Emission's buildup. A full-desktop haze in front of the artifacts
    /// flattens exactly the contrast the Emission spends six seconds building, so the fog gets
    /// out of the way -- while the rain stays and is lit by the strikes.</summary>
    public double FogDamping { get; set; } = 1;

    public WeatherLayer()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
    }

    /// <summary>The monitors, in field coordinates. Weather is one state across the whole
    /// desktop -- it is one sky -- but it is drawn per screen so that its density follows each
    /// screen's own area.</summary>
    public IReadOnlyList<Rect> Regions
    {
        get => _regions;
        set
        {
            var next = value ?? Array.Empty<Rect>();
            if (MonitorRegions.Same(_regions, next)) return;

            _regions = next.Count == 0 ? Array.Empty<Rect>() : next.ToArray();
            Rebuild();
        }
    }

    /// <summary>Puts the layer into the state the cycle is describing.
    ///
    /// Both states are live only while a cross-fade is running; outside one the outgoing slot is
    /// empty and exactly one state is drawn.</summary>
    public void Show(WeatherCycle cycle) =>
        Show(cycle.IntensityOf(Weather.Fog),
             cycle.IntensityOf(Weather.Rain) + cycle.IntensityOf(Weather.Storm));

    /// <summary>Shows one state, or a point part way between two.
    ///
    /// The explicit form, for the offline renderers and for asking the layer directly what a
    /// given moment looks like. The cycle's own state is one caller of this, not the only one.</summary>
    public void Show(Weather current, Weather? outgoing = null, double progress = 1)
    {
        Show(IntensityOf(Weather.Fog, current, outgoing, progress),
             IntensityOf(Weather.Rain, current, outgoing, progress)
             + IntensityOf(Weather.Storm, current, outgoing, progress));
    }

    private static double IntensityOf(Weather state, Weather current, Weather? outgoing, double progress)
    {
        var intensity = 0.0;
        if (current == state) intensity += progress;
        if (outgoing == state) intensity += 1 - progress;
        return Math.Clamp(intensity, 0, 1);
    }

    private void Show(double fogIntensity, double rainIntensity)
    {
        var fog = fogIntensity * Math.Clamp(FogDamping, 0, 1);

        // Rain and storm both drive the rain sheets, added rather than taken separately: a rain
        // to storm change should not dip through a gap where neither is raining.
        var rain = rainIntensity;

        Apply(_fog, fog);
        Apply(_rain, Math.Min(1, rain));

        // Nothing on screen means nothing to animate. Rain and fog are the only things in this
        // app that would otherwise keep the compositor busy while the Zone is calm.
        Scrolling(fog > 0 || rain > 0);
    }

    /// <summary>Stops everything and empties the layer. Used at blackout, where nothing is drawn
    /// at all -- the same rule the lightning already follows.</summary>
    public void Stop()
    {
        Apply(_fog, 0);
        Apply(_rain, 0);
        Scrolling(false);
    }

    private static void Apply(List<Sheet> sheets, double intensity)
    {
        var level = WeatherBrushes.LevelFor(intensity);

        foreach (var sheet in sheets)
        {
            if (sheet.Level == level) continue;
            sheet.Level = level;

            if (level < 0)
            {
                sheet.Element.Visibility = Visibility.Collapsed;
                sheet.Element.Fill = null;
                continue;
            }

            sheet.Element.Visibility = Visibility.Visible;
            sheet.Element.Fill = sheet.Scale < 0
                ? WeatherBrushes.FogAt(level)
                : WeatherBrushes.RainAt(sheet.Scale, level);
        }
    }

    /// <summary>Starts or stops the tile scrolls. One animation per sheet for the whole desktop,
    /// not one per screen: the sheets share their brushes, so they share their motion too.</summary>
    private void Scrolling(bool on)
    {
        if (_scrolling == on) return;
        _scrolling = on;

        for (var scale = 0; scale < WeatherBrushes.Scales; scale++)
        {
            var scroll = WeatherBrushes.RainScroll(scale);

            if (!on)
            {
                scroll.BeginAnimation(TranslateTransform.XProperty, null);
                scroll.BeginAnimation(TranslateTransform.YProperty, null);
                continue;
            }

            var tile = WeatherBrushes.RainTile(scale);
            var seconds = WeatherBrushes.RainPeriods[scale];

            // Each axis wraps on its own tile dimension, so both loops are seamless. The sideways
            // one used to travel 0.22 of the tile's *height*, which is no whole number of tile
            // widths, so every cycle ended by snapping the rain back a fraction of a tile -- the
            // jump that made it look like it was stuttering rather than falling.
            //
            // The slant survives as the ratio of the two speeds: X crosses a tile width in
            // whatever time makes its speed 0.22 of the fall.
            var across = seconds * tile.Width / (WeatherBrushes.RainSlant * tile.Height);

            scroll.BeginAnimation(TranslateTransform.YProperty, Loop(tile.Height, TimeSpan.FromSeconds(seconds)));
            scroll.BeginAnimation(TranslateTransform.XProperty, Loop(tile.Width, TimeSpan.FromSeconds(across)));
        }

        var drift = WeatherBrushes.FogDrift();

        if (!on)
        {
            drift.BeginAnimation(TranslateTransform.XProperty, null);
            return;
        }

        drift.BeginAnimation(
            TranslateTransform.XProperty,
            Loop(WeatherBrushes.FogTile().Width, TimeSpan.FromSeconds(WeatherBrushes.FogPeriod)));
    }

    private static DoubleAnimation Loop(double distance, TimeSpan period) =>
        new(0, distance, new Duration(period))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };

    /// <summary>Rebuilds the sheets for the current screens. One rain sheet per screen per
    /// parallax scale, and one fog sheet per screen.</summary>
    private void Rebuild()
    {
        Children.Clear();
        _rain.Clear();
        _fog.Clear();

        foreach (var region in ScreensToCover())
        {
            if (region.Width <= 0 || region.Height <= 0) continue;

            for (var scale = 0; scale < WeatherBrushes.Scales; scale++)
                _rain.Add(Add(region, scale));

            _fog.Add(Add(region, scale: -1));
        }

        // Whatever was showing has just been thrown away with the old sheets, so the layer is
        // blank until the next Show. The caller ticks every frame, so that is the next one.
        _scrolling = false;
    }

    /// <summary>The screens to draw over: the real monitors, or this element's own bounds when
    /// there are none. The fallback is how the offline renderers get weather without a display
    /// layout, and how the layer behaves before UpdateRegions has first run.</summary>
    private IReadOnlyList<Rect> ScreensToCover()
    {
        if (_regions.Count > 0) return _regions;

        return ActualWidth > 0 && ActualHeight > 0
            ? new[] { new Rect(0, 0, ActualWidth, ActualHeight) }
            : Array.Empty<Rect>();
    }

    /// <summary>Rebuilds on a resize, so a layer running on the fallback follows the window
    /// rather than staying the size it had before the window was stretched over the desktop.</summary>
    protected override void OnRenderSizeChanged(SizeChangedInfo info)
    {
        base.OnRenderSizeChanged(info);
        if (_regions.Count == 0) Rebuild();
    }

    private Sheet Add(Rect region, int scale)
    {
        var element = new Rectangle
        {
            Width = region.Width,
            Height = region.Height,
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };

        SetLeft(element, region.Left);
        SetTop(element, region.Top);
        Children.Add(element);

        return new Sheet { Element = element, Scale = scale };
    }
}
