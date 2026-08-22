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

        /// <summary>Which half of the tint cross-fade this sheet is. Slot 0 carries the tint
        /// coming in, slot 1 the one going out; outside a tint change slot 1 is empty and one
        /// sheet is drawn per screen per scale, exactly as before there were tints.</summary>
        public required int Slot;

        public int Level = -2;

        /// <summary>The tint currently on this sheet. Held alongside the rung because a sheet
        /// has to be refilled when either one moves, and neither on its own says whether the
        /// brush hanging on it is still the right brush.
        ///
        /// Starts on <see cref="Unset"/> rather than on null, which is itself a tint a sheet can
        /// legitimately be showing -- a fresh sheet has to be filled whatever it is asked
        /// for.</summary>
        public Anomaly? Family = Unset;

        private const Anomaly Unset = (Anomaly)(-1);
    }

    /// <summary>How many tints can be on screen at once. Two, which is the limit the weather
    /// cross-fade already holds itself to and for the same reason: a third sheet is a third
    /// desktop-sized fill for a distinction nobody watching could name.</summary>
    private const int Slots = 2;

    private readonly List<Sheet> _rain = new();
    private readonly List<Sheet> _fog = new();

    private IReadOnlyList<Rect> _regions = Array.Empty<Rect>();
    private bool _scrolling;

    private Anomaly? _family;
    private Anomaly? _outgoingFamily;
    private bool _tinting;
    private double _tintFade;

    private Rectangle? _flourish;

    /// <summary>How much of the fog survives, 0 to 1.
    ///
    /// Driven down over an Emission's buildup. A full-desktop haze in front of the artifacts
    /// flattens exactly the contrast the Emission spends six seconds building, so the fog gets
    /// out of the way -- while the rain stays and is lit by the strikes.</summary>
    public double FogDamping { get; set; } = 1;

    /// <summary>Whether a bolt is on screen. Precipitation renders a couple of rungs brighter
    /// while it is.
    ///
    /// This is the claim the README has been making since weather arrived and the layer has
    /// never made good on: lightning draws below the artifacts and weather above them, so a bolt
    /// passed two layers behind the rain without touching it. The layer is told rather than
    /// asked, because the render loop already consults HasStrike every frame and a second
    /// question would be the same answer twice.
    ///
    /// It needs no clock of its own. The lift is on for exactly the frames the strike is, which
    /// is what makes the rain look lit by the sky rather than lit on a timer of its own.</summary>
    public bool Lit { get; set; }

    /// <summary>The anomaly family the weather takes its colour from, or null for the untinted
    /// sheets.
    ///
    /// Assigned, not asked for: the census that decides this runs when the field changes, and
    /// the layer is told the answer. Assigning a new one starts a cross-fade of exactly the
    /// length and shape a weather change uses -- what is being faded is the pair (state,
    /// family) rather than the state alone, so as far as this layer is concerned a tint change
    /// and a weather change are the same event.
    ///
    /// The brushes for a family are built the first time one is named here, and kept for the
    /// rest of the run.</summary>
    public Anomaly? Family
    {
        get => _family;
        set
        {
            if (value == _family) return;

            // A second change arriving mid-fade drops whatever was already on its way out. It
            // is the one of the three least on screen, and two live sheets is the limit.
            _outgoingFamily = _family;
            _family = value;
            _tinting = true;
            _tintFade = WeatherCycle.CrossFade;
        }
    }

    /// <summary>How far the incoming tint has come, 0 to 1. Always 1 when settled, so the
    /// outgoing tint can be drawn at one minus this without a special case -- the same shape
    /// <see cref="WeatherCycle.Progress"/> is in.</summary>
    private double TintProgress => _tinting ? 1 - _tintFade / WeatherCycle.CrossFade : 1;

    /// <summary>Advances the tint cross-fade. Called per frame alongside the cycle's own tick;
    /// outside a tint change it does nothing at all.</summary>
    public void Tick(double seconds)
    {
        if (!_tinting || seconds <= 0) return;

        _tintFade -= seconds;
        if (_tintFade > 0) return;

        _tintFade = 0;
        _tinting = false;
        _outgoingFamily = null;
    }

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

        Apply(_fog, fog, precipitation: false);
        Apply(_rain, Math.Min(1, rain), precipitation: true);

        // Nothing on screen means nothing to animate. Rain and fog are the only things in this
        // app that would otherwise keep the compositor busy while the Zone is calm.
        Scrolling(fog > 0 || rain > 0);
    }

    /// <summary>Stops everything and empties the layer. Used at blackout, where nothing is drawn
    /// at all -- the same rule the lightning already follows.</summary>
    public void Stop()
    {
        // The fade goes with the sheets. Coming back from a blackout mid-tint would otherwise
        // resume a cross-fade between two colours nobody has seen for hours.
        _tinting = false;
        _tintFade = 0;
        _outgoingFamily = null;

        Clear(_flourish);
        _flourish = null;

        Apply(_fog, 0, precipitation: false);
        Apply(_rain, 0, precipitation: true);
        Scrolling(false);
    }

    /// <summary>Fills one kind of sheet at one intensity, split across the tint cross-fade.
    ///
    /// The intensity the state asks for is shared between the two tints rather than given to
    /// each: a sky halfway between two colours is not twice as much weather.</summary>
    private void Apply(List<Sheet> sheets, double intensity, bool precipitation)
    {
        var progress = TintProgress;

        var incoming = Rung(intensity * progress, precipitation);
        var outgoing = _tinting ? Rung(intensity * (1 - progress), precipitation) : -1;

        foreach (var sheet in sheets)
        {
            var family = sheet.Slot == 0 ? _family : _outgoingFamily;
            var level = sheet.Slot == 0 ? incoming : outgoing;

            if (sheet.Level == level && sheet.Family == family) continue;
            sheet.Level = level;
            sheet.Family = family;

            if (level < 0)
            {
                sheet.Element.Visibility = Visibility.Collapsed;
                sheet.Element.Fill = null;
                continue;
            }

            sheet.Element.Visibility = Visibility.Visible;
            sheet.Element.Fill = sheet.Scale < 0
                ? WeatherBrushes.FogAt(family, level)
                : WeatherBrushes.RainAt(family, sheet.Scale, level);
        }
    }

    // -- the collection flourish ----------------------------------------------------------

    /// <summary>How long a flourish lives, in seconds.
    ///
    /// Short of the detector's 1.6s collection cooldown, which is what bounds how often one can
    /// start -- so at most one is ever on screen. The layer keeps only one slot regardless, so
    /// that a change to the cooldown cannot quietly turn this into a pile.</summary>
    internal const double FlourishLife = 1.1;

    /// <summary>How wide a flourish is at full spread, in DIP. Larger than the detector's own
    /// flash and much dimmer, so it reads as the sky answering rather than as a second
    /// readout.</summary>
    private const double FlourishSize = 360;

    /// <summary>Disturbs the sky where an artifact was picked up.
    ///
    /// One short-lived element rather than anything done to the sheets. A sheet is a
    /// desktop-wide tile, and it cannot be disturbed in one place without being repainted --
    /// which is the cost this whole layer is built to avoid. A single small element, alive for
    /// about a second, is cheap in exactly the way a mask over the whole desktop is not.</summary>
    public void Flourish(Point at, Anomaly family)
    {
        var element = AddFlourish(at, family);
        var life = TimeSpan.FromSeconds(FlourishLife);

        // Up fast and down slowly: a pickup is an event, and the sky taking a full second to
        // notice it would read as unrelated.
        var opacity = new DoubleAnimationUsingKeyFrames { Duration = new Duration(life) };
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromPercent(Peak)));
        opacity.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)));

        // The element is taken out at the end rather than left at zero opacity: WPF still
        // composites a transparent child, and one of these arrives every second or so.
        opacity.Completed += (_, _) =>
        {
            if (!ReferenceEquals(_flourish, element)) return;

            Clear(element);
            _flourish = null;
        };

        var spread = new DoubleAnimation(Seed, 1, new Duration(life))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        element.BeginAnimation(OpacityProperty, opacity);

        var scale = (ScaleTransform)element.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, spread);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, spread);
    }

    /// <summary>The same burst, held still part way through its life.
    ///
    /// For the offline renderers, which have no clock to run an animation against: an element
    /// that has only just been added is at the start of its fade, which is nothing at all, and a
    /// panel of nothing is not a panel of a collection.</summary>
    internal void FlourishAt(Point at, Anomaly family, double phase)
    {
        var element = AddFlourish(at, family);

        element.Opacity = phase <= Peak
            ? phase / Peak
            : Math.Max(0, 1 - (phase - Peak) / (1 - Peak));

        var spread = Seed + (1 - Seed) * (1 - Math.Pow(1 - Math.Clamp(phase, 0, 1), 3));

        var scale = (ScaleTransform)element.RenderTransform;
        scale.ScaleX = spread;
        scale.ScaleY = spread;
    }

    /// <summary>Where in its life the burst is at its brightest, and how wide it starts.</summary>
    private const double Peak = 0.12;
    private const double Seed = 0.35;

    private Rectangle AddFlourish(Point at, Anomaly family)
    {
        Clear(_flourish);

        var tint = AnomalyTint.Of(family);

        var glow = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };

        // Bright in the middle and gone well before the edge, so it has no findable rim -- the
        // same falloff the fog patches use, for the same reason.
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, tint.R, tint.G, tint.B), 0));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x5E, tint.R, tint.G, tint.B), 0.34));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x18, tint.R, tint.G, tint.B), 0.66));
        glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, tint.R, tint.G, tint.B), 1));
        glow.Freeze();

        var element = new Rectangle
        {
            Width = FlourishSize,
            Height = FlourishSize,
            Fill = glow,
            IsHitTestVisible = false,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(Seed, Seed),
        };

        SetLeft(element, at.X - FlourishSize / 2);
        SetTop(element, at.Y - FlourishSize / 2);

        // On top of the sheets, so it reads as something in the air rather than behind the
        // rain. Still below the detector, which is a layer of its own above this one.
        Children.Add(element);
        _flourish = element;

        return element;
    }

    /// <summary>How many flourishes are on screen. Never more than one -- asserted, because the
    /// bound comes from a cooldown that lives in another file.</summary>
    internal int Flourishes => _flourish is null ? 0 : 1;

    private void Clear(Rectangle? element)
    {
        if (element is null) return;

        element.BeginAnimation(OpacityProperty, null);

        if (element.RenderTransform is ScaleTransform scale)
        {
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        }

        Children.Remove(element);
    }

    /// <summary>The rung one sheet renders at.
    ///
    /// Precipitation runs on a shorter ladder with headroom above it, and a strike spends that
    /// headroom -- which is a fill swapped for another brush that was built at startup, not a
    /// tile repainted brighter.</summary>
    private int Rung(double intensity, bool precipitation)
    {
        if (!precipitation) return WeatherBrushes.LevelFor(intensity);

        var level = WeatherBrushes.RainLevelFor(intensity);
        if (level < 0 || !Lit) return level;

        return Math.Min(WeatherBrushes.Levels - 1, level + WeatherBrushes.StrikeLift);
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

        // Cleared with everything else. Holding a reference to an element no longer in the tree
        // would leave the one slot occupied for ever, and no flourish would show again.
        _flourish = null;

        // Slot 1 first, so the tint on its way out sits underneath the one coming in.
        foreach (var region in ScreensToCover())
        {
            if (region.Width <= 0 || region.Height <= 0) continue;

            for (var slot = Slots - 1; slot >= 0; slot--)
            {
                for (var scale = 0; scale < WeatherBrushes.Scales; scale++)
                    _rain.Add(Add(region, scale, slot));

                _fog.Add(Add(region, scale: -1, slot));
            }
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

    private Sheet Add(Rect region, int scale, int slot)
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

        return new Sheet { Element = element, Scale = scale, Slot = slot };
    }
}
