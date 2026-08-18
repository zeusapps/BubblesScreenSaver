using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bubbles;

/// <summary>One artifact: a static halo bitmap with a live-drawn body on top.
///
/// The split matters for cost. WPF re-rasterises vector content on every composition pass,
/// and the halo is a full-element radial gradient -- by far the largest area here. Keeping it
/// as a bitmap means only the body is ever re-rendered, and the body is cached too, so it is
/// re-rasterised on a rota rather than on every frame.
///
/// Everything is drawn in a fixed 200x200 space and scaled to the artifact's real size by the
/// owner's transform, so the geometry stays resolution independent.</summary>
public sealed class ArtifactVisual : Grid
{
    public const double UnitSize = 200;

    private const double Centre = UnitSize / 2;
    private const double BaseRadius = UnitSize * 0.38;

    /// <summary>Levels are pre-baked alpha variants. Varying intensity with PushOpacity
    /// instead would be far dearer than it looks: each push makes WPF render that subtree
    /// into an intermediate surface, and a thermic artifact wanted fourteen of them per draw.</summary>
    private const int Levels = 8;

    private sealed record Palette(
        Brush Body, Brush[] Core, Pen RimPen, Pen SeamPen, Brush Spec,
        Brush Particle, Brush[] Ember, Pen[][] CrackPens, Pen[] ArcPen, Pen LensPen, Brush ChipBrush);

    private static readonly Palette?[] Palettes = new Palette?[Artifacts.Count];
    private static readonly BitmapSource?[] Halos = new BitmapSource?[Artifacts.Count];

    private readonly Image _halo = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Body _body = new();

    public ArtifactVisual()
    {
        IsHitTestVisible = false;
        Width = UnitSize;
        Height = UnitSize;

        RenderOptions.SetBitmapScalingMode(_halo, BitmapScalingMode.Linear);
        Children.Add(_halo);
        Children.Add(_body);
    }

    public int Skin
    {
        set
        {
            if (_body.Skin == value) return;
            _body.Skin = value;
            _halo.Source = HaloFor(value);
        }
    }

    /// <summary>Seconds. Every animation here is a function of it.</summary>
    public double Time
    {
        set
        {
            _body.Time = value;

            // The halo breathes by opacity alone, which is a composition-only change and so
            // costs nothing to animate on every frame.
            _halo.Opacity = 0.72 + 0.28 * Math.Sin(value * 0.9 + _body.Skin);
        }
    }

    /// <summary>Raised during an Emission, when the Zone stops being calm.</summary>
    public double Agitation
    {
        set => _body.Agitation = value;
    }

    public void SetRenderScale(double scale) => _body.SetRenderScale(scale);

    /// <summary>Re-renders the body. The owner calls this on a rota rather than every frame.</summary>
    public void InvalidateInterior() => _body.InvalidateVisual();

    private static readonly BitmapSource?[] StaticSprites = new BitmapSource?[Artifacts.Count];

    /// <summary>The same artifact, drawn once and frozen. Drawing these live is what costs;
    /// as bitmaps they composite for about the same as the old soap bubbles did.</summary>
    public static BitmapSource StaticSprite(int skin)
    {
        if (StaticSprites[skin] is { } cached) return cached;

        const int size = 256;
        var visual = new ArtifactVisual { Skin = skin, Time = skin * 1.7 };
        visual.Measure(new Size(UnitSize, UnitSize));
        visual.Arrange(new Rect(0, 0, UnitSize, UnitSize));
        visual.UpdateLayout();

        // Scaling by dpi is what makes the 200-unit visual fill a 256px bitmap.
        var dpi = 96.0 * size / UnitSize;
        var bmp = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return StaticSprites[skin] = bmp;
    }

    // A cheap deterministic hash, so per-particle randomness needs no allocation and is
    // stable from frame to frame.
    private static double Hash(int a, int b)
    {
        var x = Math.Sin(a * 127.1 + b * 311.7) * 43758.5453;
        return x - Math.Floor(x);
    }

    private static Color A(byte alpha, Color c) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Brush Radial(params (Color Colour, double Offset)[] stops)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };

        foreach (var (colour, offset) in stops)
            brush.GradientStops.Add(new GradientStop(colour, offset));

        brush.Freeze();
        return brush;
    }

    /// <summary>The glow an artifact throws into the air, baked once per kind.</summary>
    private static BitmapSource HaloFor(int skin)
    {
        if (Halos[skin] is { } cached) return cached;

        const int size = 128;
        var art = Artifacts.All[skin];
        var brush = Radial((A(0, art.Halo), 0.55), (A(58, art.Halo), 0.78), (A(0, art.Halo), 1.0));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawEllipse(brush, null, new Point(size / 2.0, size / 2.0), size / 2.0, size / 2.0);

        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return Halos[skin] = bmp;
    }

    private static Palette PaletteFor(int skin)
    {
        if (Palettes[skin] is { } cached) return cached;

        var art = Artifacts.All[skin];
        var dark = art.Family == Anomaly.Gravitational;

        var body = dark
            ? Radial((A(232, art.Core), 0.0), (A(206, art.Core), 0.55), (A(120, art.Shell), 0.92), (A(60, art.Shell), 1.0))
            : art.Family == Anomaly.Thermic
                ? Radial((A(150, art.Shell), 0.0), (A(186, art.Shell), 0.6), (A(215, art.Shell), 1.0))
                : Radial((A(46, art.Shell), 0.0), (A(88, art.Shell), 0.6), (A(165, art.Shell), 1.0));

        var core = new Brush[Levels];
        for (var i = 0; i < Levels; i++)
        {
            var f = (i + 1.0) / Levels;
            core[i] = Radial(
                (A((byte)(235 * f), art.Core), 0.0),
                (A((byte)(140 * f), art.Core), 0.35),
                (A(0, art.Core), 1.0));
        }

        var rim = new SolidColorBrush(A(230, art.Shell));
        rim.Freeze();
        var rimPen = new Pen(rim, UnitSize * 0.012);
        rimPen.Freeze();

        // Shapes built from several parts get their internal seams stroked too, so those use
        // a softer pen -- at full strength the mass reads as wireframe rather than solid.
        var seam = new SolidColorBrush(A(110, art.Shell));
        seam.Freeze();
        var seamPen = new Pen(seam, UnitSize * 0.008);
        seamPen.Freeze();

        var spec = Radial((A(180, Colors.White), 0.0), (A(0, Colors.White), 1.0));

        // Everything below was once built per particle per frame, which came to roughly a
        // thousand brush and pen allocations every frame across the field. Varying intensity
        // is done with PushOpacity instead, which costs nothing.
        var particle = Radial((A(120, Colors.White), 0.0), (A(40, art.Core), 0.55), (A(0, art.Core), 1.0));
        var ember = new Brush[Levels];
        for (var i = 0; i < Levels; i++)
        {
            var f = (i + 1.0) / Levels;
            ember[i] = Radial((A((byte)(200 * f), art.Core), 0.0), (A(0, art.Core), 1.0));
        }

        var crackPens = new Pen[4][];
        for (var step = 0; step < crackPens.Length; step++)
        {
            crackPens[step] = new Pen[Levels];
            for (var i = 0; i < Levels; i++)
            {
                var f = (i + 1.0) / Levels;
                var brush = new SolidColorBrush(A((byte)((190 - step * 42) * f), art.Core));
                brush.Freeze();
                crackPens[step][i] = new Pen(brush, UnitSize * 0.021 * Math.Pow(0.72, step));
                crackPens[step][i].Freeze();
            }
        }

        var arcPen = new Pen[Levels];
        for (var i = 0; i < Levels; i++)
        {
            var f = (i + 1.0) / Levels;
            var brush = new SolidColorBrush(A((byte)(220 * f), art.Core));
            brush.Freeze();
            arcPen[i] = new Pen(brush, UnitSize * 0.011);
            arcPen[i].Freeze();
        }

        var lensBrush = new SolidColorBrush(A(90, art.Shell));
        lensBrush.Freeze();
        var lensPen = new Pen(lensBrush, UnitSize * 0.009);
        lensPen.Freeze();

        var chip = new SolidColorBrush(A(180, art.Shell));
        chip.Freeze();

        return Palettes[skin] = new Palette(
            body, core, rimPen, seamPen, spec, particle, ember, crackPens, arcPen, lensPen, chip);
    }

    /// <summary>The artifact itself, minus its halo.</summary>
    private sealed class Body : FrameworkElement
    {
        private readonly BitmapCache _cache = new() { SnapsToDevicePixels = false, RenderAtScale = 1 };
        private double _renderScale = 1;

        public int Skin { get; set; } = -1;
        public double Time { get; set; }
        public double Agitation { get; set; } = 1;

        public Body()
        {
            IsHitTestVisible = false;
            Width = UnitSize;
            Height = UnitSize;

            // Without this WPF re-rasterises the whole vector body on every composition pass,
            // whether or not anything changed. Cached, the render thread composites a texture
            // and only re-rasterises when the body is invalidated.
            CacheMode = _cache;
        }

        /// <summary>Keeps the cached rasterisation matched to how big this artifact is on
        /// screen. Changing it forces a re-render, so it only moves in noticeable steps.</summary>
        public void SetRenderScale(double scale)
        {
            scale = Math.Clamp(scale, 0.35, 2.6);
            if (Math.Abs(scale - _renderScale) < 0.12) return;

            _renderScale = scale;
            _cache.RenderAtScale = scale;
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (Skin < 0) return;

            var art = Artifacts.All[Skin];
            var palette = PaletteFor(Skin);
            var t = Time;

            var silhouette = BuildSilhouette(art.Shape, t);

            // Filling the geometry directly is cheaper than clipping to it and filling a
            // rectangle: a per-frame clip geometry pushes WPF onto an intermediate surface.
            dc.DrawGeometry(palette.Body, null, silhouette);

            switch (art.Family)
            {
                case Anomaly.Chemical: DrawChemical(dc, palette, t); break;
                case Anomaly.Electrical: DrawElectrical(dc, palette, t); break;
                case Anomaly.Thermic: DrawThermic(dc, palette, t); break;
                default: DrawGravitational(dc, palette, t); break;
            }

            // A specular pin, so it reads as a physical object rather than a light.
            dc.DrawEllipse(palette.Spec, null,
                new Point(Centre - BaseRadius * 0.42, Centre - BaseRadius * 0.46),
                UnitSize * 0.055, UnitSize * 0.042);

            var fused = art.Shape is ArtifactShape.Cluster or ArtifactShape.Beads;
            dc.DrawGeometry(null, fused ? palette.SeamPen : palette.RimPen, silhouette);
        }

        /// <summary>Quantises an intensity in 0..1 onto the pre-baked alpha levels.</summary>
        private static int Level(double intensity) =>
            (int)Math.Clamp(Math.Round(intensity * (Levels - 1)), 0, Levels - 1);

        /// <summary>Agitation makes the whole outline shiver during an Emission.</summary>
        private double Shiver(double t, int i) => 1 + (Agitation - 1) * 0.045 * Math.Sin(t * 9 + i);

        /// <summary>The outline. Most shapes are a sum of slowly rotating harmonics, so they
        /// are never circles and never quite the same twice; the rest are built from parts.</summary>
        private Geometry BuildSilhouette(ArtifactShape shape, double t)
        {
            var spin = t * 0.11 * (1 + Skin % 3 * 0.35) * (Skin % 2 == 0 ? 1 : -1);

            switch (shape)
            {
                case ArtifactShape.Crescent: return BuildCrescent(t, spin);
                case ArtifactShape.Cluster: return BuildCluster(t);
                case ArtifactShape.Beads: return BuildBeads(t, spin);
                case ArtifactShape.Starburst: return BuildStarburst(t, spin);
            }

            var (points, harmonics) = shape switch
            {
                ArtifactShape.Blob => (40, new[] { (2, 0.15, 0.33), (3, 0.09, 0.51) }),
                ArtifactShape.Chunky => (40, new[] { (3, 0.17, 0.29), (5, 0.10, 0.44), (2, 0.07, 0.19) }),
                ArtifactShape.Spiky => (52, new[] { (8, 0.20, 0.24), (3, 0.07, 0.40) }),
                ArtifactShape.Shard => (9, new[] { (2, 0.16, 0.21), (3, 0.12, 0.35) }),
                _ => (44, new[] { (4, 0.12, 0.27), (2, 0.06, 0.42) }),   // Coil
            };

            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                for (var i = 0; i < points; i++)
                {
                    var theta = i * Math.Tau / points;
                    var r = 1.0;

                    foreach (var (k, amplitude, speed) in harmonics)
                    {
                        var phase = Hash(Skin, k) * Math.Tau;
                        r += amplitude * Math.Sin(k * (theta + spin) + phase + t * speed);
                    }

                    r *= Shiver(t, i);

                    var at = new Point(
                        Centre + Math.Cos(theta) * BaseRadius * r,
                        Centre + Math.Sin(theta) * BaseRadius * r);

                    if (i == 0) ctx.BeginFigure(at, isFilled: true, isClosed: true);
                    else ctx.LineTo(at, isStroked: true, isSmoothJoin: shape != ArtifactShape.Shard);
                }
            }

            if (shape != ArtifactShape.Coil)
            {
                geometry.Freeze();
                return geometry;
            }

            // A coil is matter wrapped around a hole, so punch one out of the middle.
            var hole = new EllipseGeometry(
                new Point(Centre, Centre),
                BaseRadius * (0.34 + 0.04 * Math.Sin(t * 0.6)),
                BaseRadius * (0.30 + 0.05 * Math.Cos(t * 0.5)));

            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, geometry, hole);
            combined.Freeze();
            return combined;
        }

        /// <summary>A small body throwing out long needles, which lengthen and shorten.</summary>
        private Geometry BuildStarburst(double t, double spin)
        {
            const int points = 104;
            var spines = 6 + Skin % 4;
            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                for (var i = 0; i < points; i++)
                {
                    var theta = i * Math.Tau / points;
                    var needle = Math.Pow(Math.Abs(Math.Sin(spines * (theta + spin) / 2)), 9);
                    var reach = 0.40 + 0.72 * needle * (0.75 + 0.25 * Math.Sin(t * 0.8 + Skin));

                    var r = reach * Shiver(t, i);
                    var at = new Point(
                        Centre + Math.Cos(theta) * BaseRadius * r * 1.35,
                        Centre + Math.Sin(theta) * BaseRadius * r * 1.35);

                    if (i == 0) ctx.BeginFigure(at, isFilled: true, isClosed: true);
                    else ctx.LineTo(at, isStroked: true, isSmoothJoin: false);
                }
            }

            geometry.Freeze();
            return geometry;
        }

        /// <summary>A C, curled around empty space, opening and closing as it turns.</summary>
        private Geometry BuildCrescent(double t, double spin)
        {
            var outer = new EllipseGeometry(
                new Point(Centre, Centre),
                BaseRadius * 1.02,
                BaseRadius * (0.9 + 0.06 * Math.Sin(t * 0.5)));

            var bite = BaseRadius * (0.42 + 0.06 * Math.Sin(t * 0.42));
            var inner = new EllipseGeometry(
                new Point(Centre + Math.Cos(spin * 2.4) * bite, Centre + Math.Sin(spin * 2.4) * bite),
                BaseRadius * 0.78,
                BaseRadius * 0.7);

            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            combined.Freeze();
            return combined;
        }

        /// <summary>Several masses fused together, breathing slightly out of step.</summary>
        private Geometry BuildCluster(double t)
        {
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };

            for (var i = 0; i < 5; i++)
            {
                var orbit = BaseRadius * (i == 0 ? 0 : 0.42 + Hash(Skin, i + 700) * 0.16);
                var angle = Hash(Skin, i + 740) * Math.Tau + t * 0.16 * (i % 2 == 0 ? 1 : -1);
                var size = BaseRadius * (i == 0 ? 0.62 : 0.34 + Hash(Skin, i + 780) * 0.2);
                var breathe = 1 + 0.07 * Math.Sin(t * 0.9 + i);

                group.Children.Add(new EllipseGeometry(
                    new Point(Centre + Math.Cos(angle) * orbit, Centre + Math.Sin(angle) * orbit),
                    size * breathe,
                    size * breathe * (0.85 + 0.15 * Math.Cos(t * 0.7 + i))));
            }

            group.Freeze();
            return group;
        }

        /// <summary>Small bodies strung along stems, swaying.</summary>
        private Geometry BuildBeads(double t, double spin)
        {
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            group.Children.Add(new EllipseGeometry(new Point(Centre, Centre), BaseRadius * 0.26, BaseRadius * 0.26));

            for (var i = 0; i < 7; i++)
            {
                var angle = i * Math.Tau / 7 + spin * 1.6;
                var sway = Math.Sin(t * 1.1 + i) * 0.12;
                var reach = BaseRadius * (0.62 + Hash(Skin, i + 820) * 0.3);
                var at = new Point(
                    Centre + Math.Cos(angle + sway) * reach,
                    Centre + Math.Sin(angle + sway) * reach);

                var stem = new StreamGeometry();
                using (var ctx = stem.Open())
                {
                    var nx = -Math.Sin(angle + sway) * BaseRadius * 0.045;
                    var ny = Math.Cos(angle + sway) * BaseRadius * 0.045;
                    ctx.BeginFigure(new Point(Centre + nx, Centre + ny), true, true);
                    ctx.LineTo(new Point(Centre - nx, Centre - ny), true, false);
                    ctx.LineTo(new Point(at.X - nx * 0.4, at.Y - ny * 0.4), true, false);
                    ctx.LineTo(new Point(at.X + nx * 0.4, at.Y + ny * 0.4), true, false);
                }
                stem.Freeze();
                group.Children.Add(stem);

                var bead = BaseRadius * (0.16 + Hash(Skin, i + 860) * 0.09) * (1 + 0.1 * Math.Sin(t * 1.4 + i));
                group.Children.Add(new EllipseGeometry(at, bead, bead));
            }

            group.Freeze();
            return group;
        }

        /// <summary>Reagent bubbles, drifting and rising inside the shell.</summary>
        private void DrawChemical(DrawingContext dc, Palette palette, double t)
        {
            dc.DrawEllipse(palette.Core[Level(0.55 + 0.15 * Math.Sin(t * 1.1))], null,
                new Point(Centre, Centre), BaseRadius * 0.72, BaseRadius * 0.72);

            for (var i = 0; i < 9; i++)
            {
                var speed = 0.25 + Hash(Skin, i) * 0.5;
                var orbit = BaseRadius * (0.15 + Hash(Skin, i + 40) * 0.55);
                var angle = Hash(Skin, i + 80) * Math.Tau + t * speed * (i % 2 == 0 ? 1 : -1) * Agitation;

                // A slow vertical bob on top of the orbit, so they look suspended in fluid.
                var bob = Math.Sin(t * (0.6 + Hash(Skin, i + 120) * 0.7) + i) * BaseRadius * 0.09;
                var at = new Point(
                    Centre + Math.Cos(angle) * orbit,
                    Centre + Math.Sin(angle) * orbit + bob);

                var size = BaseRadius * (0.045 + Hash(Skin, i + 160) * 0.075);
                dc.DrawEllipse(palette.Particle, null, at, size, size);
            }
        }

        /// <summary>A hot core that discharges: the arcs are rebuilt on each strike.</summary>
        private void DrawElectrical(DrawingContext dc, Palette palette, double t)
        {
            var interval = 0.42 / Agitation;
            var strike = (int)Math.Floor(t / interval);
            var life = t / interval - strike;            // 0..1 through the current strike
            var brightness = Math.Pow(1 - life, 1.7);

            dc.DrawEllipse(palette.Core[Level(0.55 + 0.45 * brightness)], null,
                new Point(Centre, Centre), BaseRadius * 0.6, BaseRadius * 0.6);

            if (brightness < 0.05) return;

            var arcPen = palette.ArcPen[Level(brightness)];

            for (var arc = 0; arc < 4; arc++)
            {
                var angle = Hash(strike + Skin * 31, arc) * Math.Tau;
                var at = new Point(Centre, Centre);

                for (var step = 0; step < 5; step++)
                {
                    angle += (Hash(strike + Skin * 31, arc * 16 + step) - 0.5) * 1.6;
                    var length = BaseRadius * (0.13 + Hash(strike, arc * 8 + step) * 0.12);
                    var next = new Point(at.X + Math.Cos(angle) * length, at.Y + Math.Sin(angle) * length);
                    dc.DrawLine(arcPen, at, next);
                    at = next;
                }
            }
        }

        /// <summary>Fissures that breathe, and embers lifting off the crust.</summary>
        private void DrawThermic(DrawingContext dc, Palette palette, double t)
        {
            var glow = 0.6 + 0.4 * Math.Sin(t * 1.6 * Agitation + Skin);

            dc.DrawEllipse(palette.Core[Level(0.5 + 0.5 * glow)], null,
                new Point(Centre, Centre), BaseRadius * 0.6, BaseRadius * 0.6);

            for (var crack = 0; crack < 7; crack++)
            {
                var pulse = 0.45 + 0.55 * Math.Sin(t * 1.9 + crack * 1.3);
                var angle = Hash(Skin, crack + 200) * Math.Tau + t * 0.05;
                var at = new Point(
                    Centre + Math.Cos(angle) * BaseRadius * 0.14,
                    Centre + Math.Sin(angle) * BaseRadius * 0.14);

                var level = Level(pulse);
                for (var step = 0; step < 4; step++)
                {
                    angle += (Hash(Skin, crack * 16 + step) - 0.5) * 1.0;
                    var next = new Point(
                        at.X + Math.Cos(angle) * BaseRadius * 0.17,
                        at.Y + Math.Sin(angle) * BaseRadius * 0.17);

                    dc.DrawLine(palette.CrackPens[step][level], at, next);
                    at = next;
                }
            }

            for (var ember = 0; ember < 6; ember++)
            {
                var speed = 0.18 + Hash(Skin, ember + 300) * 0.22;
                var progress = (t * speed + Hash(Skin, ember + 340)) % 1.0;
                var drift = Math.Sin(progress * 6 + ember) * BaseRadius * 0.12;

                var at = new Point(
                    Centre + (Hash(Skin, ember + 380) - 0.5) * BaseRadius * 1.1 + drift,
                    Centre + BaseRadius * (0.7 - progress * 1.5));

                dc.DrawEllipse(palette.Ember[Level((1 - progress) * glow)], null,
                    at, BaseRadius * 0.05, BaseRadius * 0.05);
            }
        }

        /// <summary>Heavy enough to bend what is around it, with debris caught in orbit.</summary>
        private void DrawGravitational(DrawingContext dc, Palette palette, double t)
        {
            var spin = t * 0.5 * Agitation;

            dc.PushTransform(new RotateTransform(spin * 40, Centre, Centre));
            dc.DrawEllipse(null, palette.LensPen, new Point(Centre, Centre),
                BaseRadius * (0.66 + 0.05 * Math.Sin(t * 0.7)),
                BaseRadius * (0.44 + 0.06 * Math.Cos(t * 0.6)));
            dc.Pop();

            for (var i = 0; i < 5; i++)
            {
                var orbit = BaseRadius * (0.5 + Hash(Skin, i + 500) * 0.3);
                var angle = Hash(Skin, i + 540) * Math.Tau + t * (0.3 + Hash(Skin, i + 580) * 0.4) * Agitation;
                var squash = 0.55 + 0.3 * Math.Sin(t * 0.4 + i);

                var at = new Point(
                    Centre + Math.Cos(angle) * orbit,
                    Centre + Math.Sin(angle) * orbit * squash);

                var size = BaseRadius * (0.035 + Hash(Skin, i + 620) * 0.05);

                // Debris, drawn as small angular chips rather than dots.
                var chip = new StreamGeometry();
                using (var ctx = chip.Open())
                {
                    ctx.BeginFigure(new Point(at.X - size, at.Y), true, true);
                    ctx.LineTo(new Point(at.X, at.Y - size * 1.4), true, false);
                    ctx.LineTo(new Point(at.X + size, at.Y), true, false);
                    ctx.LineTo(new Point(at.X, at.Y + size * 0.8), true, false);
                }
                chip.Freeze();

                dc.DrawGeometry(palette.ChipBrush, null, chip);
            }
        }
    }
}
