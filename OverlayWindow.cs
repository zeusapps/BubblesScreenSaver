using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Bubbles;

/// <summary>A borderless, focus-less, click-through window stretched over the whole virtual
/// desktop. The bubbles live in it; everything else shows straight through.</summary>
public sealed class OverlayWindow : Window
{
    private const double SpriteSize = 512;   // must match BubbleArt's sprite resolution

    /// <summary>Frames between interior redraws of any one artifact. See OnRendering.</summary>
    private const int ArtifactRedrawInterval = 4;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;

    private sealed class Visual
    {
        public required FrameworkElement Element;
        public ArtifactVisual? Artifact;   // Zone theme: drawn live every frame
        public Image? Sprite;              // Soap theme: a pre-rendered bitmap
        public int Skin = -1;
        public double Alpha = -1;
    }

    private readonly Grid _root = new() { Opacity = 0 };
    private readonly Rectangle _scrim = new() { Fill = Brushes.Black };
    private readonly Canvas _canvas = new() { ClipToBounds = false };
    private readonly Rectangle _emission = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly Rectangle _flash = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly Canvas _detectorLayer = new() { ClipToBounds = false, Opacity = 0 };
    private readonly Detector _detector = new();
    private Rect _detectorScreen = new(0, 0, 1920, 1080);
    private readonly List<Visual> _visuals = new();
    private readonly BubbleField _field;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly DispatcherTimer _topmostGuard;

    private Settings _settings;
    private TimeSpan _lastFrame;
    private double _frameBudget;
    private double _sinceDraw;
    private IntPtr _hwnd;
    private bool _shown;
    private bool _collapsed;
    private bool _blackout;
    private bool _emitting;
    private double _emissionTime;
    private double _animationTime;
    private int _frameCounter;
    private double _pixelsPerDip = 1;
    private OverlayTheme? _visualTheme;
    private bool? _visualAnimated;

    /// <summary>User-facing pause, from the tray menu.</summary>
    public bool Paused { get; set; }

    /// <summary>Internal render suspension: hidden, or drawing into a powered-down panel.</summary>
    public bool Suspended { get; set; } = true;

    /// <summary>True once the bubbles are on screen (or on their way in).</summary>
    public bool IsShowing => _shown;

    private bool IsZone => _settings.Theme == OverlayTheme.Zone;

    private bool DetectorWanted => IsZone && _settings.ShowDetector;

    /// <summary>Never hide the pointer in AlwaysOn -- there the overlay is up while you work.</summary>
    private bool CursorHidingWanted => _settings.HideCursor && !_settings.AlwaysOn;

    private int SkinCount => IsZone ? Artifacts.Count : BubbleArt.SkinCount;

    /// <summary>The burning sky of an Emission: crimson overhead, falling away to nothing.</summary>
    internal static LinearGradientBrush EmissionSkyBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xC4, 0x30, 0x18), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x8A, 0x1C, 0x0A), 0.32));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x33, 0x0C, 0x06), 0.68));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x08, 0x03, 0x02), 1.00));
        brush.Freeze();
        return brush;
    }

    /// <summary>The wavefront itself -- a hot flare from above.</summary>
    internal static LinearGradientBrush ShockwaveLightBrush()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0xFF, 0xF1, 0xCC), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0xF0, 0xFF, 0xA8, 0x45), 0.28));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x80, 0xB8, 0x3E, 0x10), 0.70));
        brush.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x40, 0x10, 0x04), 1.00));
        brush.Freeze();
        return brush;
    }

    public OverlayWindow(Settings settings)
    {
        _settings = settings;
        _field = new BubbleField(settings);
        _field.PopulationChanged += RebuildVisuals;

        Title = "Bubbles";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = false;      // DWM does the transparency; this would kill GPU rendering
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        IsHitTestVisible = false;
        UseLayoutRounding = false;
        SnapsToDevicePixels = false;

        _canvas.Opacity = _settings.Opacity;
        _scrim.Opacity = _settings.Dim;
        _emission.Fill = EmissionSkyBrush();
        _flash.Fill = ShockwaveLightBrush();

        // Back to front: dimming sheet, burning sky, artifacts, shockwave, detector.
        // The artifacts sit above the sky so they still glow through an Emission rather
        // than being washed flat by it.
        _root.Children.Add(_scrim);
        _root.Children.Add(_emission);
        _root.Children.Add(_canvas);
        _root.Children.Add(_flash);
        _detectorLayer.Children.Add(_detector);
        _root.Children.Add(_detectorLayer);
        Content = _root;

        _field.SkinCount = SkinCount;

        _frameBudget = _settings.MaxFps > 0 ? 1.0 / _settings.MaxFps : 0;

        _topmostGuard = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _topmostGuard.Tick += (_, _) =>
        {
            ReassertTopmost();

            // Something else may have set a pointer while we were idle. Only re-hide once
            // the machine has genuinely been still for a moment, so this can never fight
            // a user who is actively moving the mouse.
            if (_shown && CursorHidingWanted && NativeInput.IdleSeconds() > 2)
                NativeCursor.Hide();
        };

        // While collapsed the window is 1x1; that size must not reach the simulation.
        SizeChanged += (_, _) =>
        {
            if (_collapsed) return;
            UpdateRegions();
            _field.Resize(new Size(ActualWidth, ActualHeight));
        };
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;

        var source = HwndSource.FromHwnd(_hwnd);
        if (source?.CompositionTarget is not null)
            source.CompositionTarget.BackgroundColor = Colors.Transparent;

        Native.MakeGlass(_hwnd);
        Native.MakeClickThrough(_hwnd, _settings.ClickThrough);
        StretchOverVirtualDesktop();

        _lastFrame = _clock.Elapsed;
        CompositionTarget.Rendering += OnRendering;
        _topmostGuard.Start();

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUBBLES_SNAP")))
        {
            var snap = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
            snap.Tick += (_, _) => { snap.Stop(); SnapshotVisualTree(); };
            snap.Start();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        CompositionTarget.Rendering -= OnRendering;
        _topmostGuard.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        base.OnClosed(e);
    }

    public void Apply(Settings settings)
    {
        _settings = settings;
        if (!_blackout)
        {
            _canvas.BeginAnimation(UIElement.OpacityProperty, null);
            _scrim.BeginAnimation(UIElement.OpacityProperty, null);
            _canvas.Opacity = settings.Opacity;
            _scrim.Opacity = settings.Dim;
        }
        _frameBudget = settings.MaxFps > 0 ? 1.0 / settings.MaxFps : 0;
        if (_hwnd != IntPtr.Zero) Native.MakeClickThrough(_hwnd, settings.ClickThrough);
        _field.SkinCount = SkinCount;
        _field.Apply(settings);

        // Force every sprite to be re-fetched next frame, in case the theme changed.
        foreach (var v in _visuals) v.Skin = -1;
        if (!_blackout) _detectorLayer.Opacity = DetectorWanted ? 1 : 0;

        settings.Save();
    }

    /// <summary>Fades the bubbles in. Cheap to call when already shown.</summary>
    public void ShowBubbles()
    {
        Diagnostics.Log($"ShowBubbles shown={_shown} scrim={_scrim.Opacity:N2} canvas={_canvas.Opacity:N2}");
        Suspended = false;
        if (_shown) return;
        _shown = true;

        _collapsed = false;
        Visibility = Visibility.Visible;
        _lastFrame = _clock.Elapsed;
        _sinceDraw = 0;
        StretchOverVirtualDesktop();
        UpdateRegions();

        _detectorLayer.Opacity = DetectorWanted ? 1 : 0;
        if (CursorHidingWanted) NativeCursor.Hide();

        Fade(to: 1, seconds: _settings.FadeInSeconds, thenHide: false);
    }

    /// <summary>Fades to (or back from) a solid black screen. The bubbles themselves emit
    /// light, so a real blackout hides them too and stops rendering entirely.</summary>
    public void SetBlackout(bool on)
    {
        Diagnostics.Log($"SetBlackout({on}) was={_blackout} scrim={_scrim.Opacity:N2} " +
                        $"canvas={_canvas.Opacity:N2} root={_root.Opacity:N2} shown={_shown}");
        if (_blackout == on) return;
        _blackout = on;

        if (on)
        {
            if (IsZone && _settings.Emission) BeginEmission();
            else BeginPlainFade();

            if (CursorHidingWanted) NativeCursor.Hide();
        }
        else
        {
            EndBlackout();
        }
    }

    // Emission timeline, in seconds from the first tremor.
    private const double BuildupEnds = 6.5;
    private const double WaveEnds = 8.4;
    private const double DarknessAt = 12.5;

    /// <summary>The plain version: everything simply dims away to black.</summary>
    private void BeginPlainFade()
    {
        Animate(_detectorLayer, 0, 0.6);
        Animate(_canvas, 0, 2.5);
        Animate(_scrim, 1, 2.5).Completed += (_, _) => { if (_blackout) Suspended = true; };
    }

    /// <summary>An Emission. The sky burns, the artifacts go frantic, the wavefront hits,
    /// and then the Zone is dark. Ends on solid black exactly like the plain fade does.</summary>
    private void BeginEmission()
    {
        _emitting = true;
        _emissionTime = 0;

        var pda = DetectorWanted ? 1.0 : 0.0;

        // The detector loses the signal shortly before the wave arrives.
        Keys(_detectorLayer, (pda, 0), (pda, BuildupEnds - 1.6), (0, BuildupEnds - 0.2));

        // The world recedes as the sky takes over, then everything is swallowed.
        Keys(_scrim, (_settings.Dim, 0), (0.86, BuildupEnds), (0.86, WaveEnds), (1, DarknessAt));
        Keys(_canvas, (_settings.Opacity, 0), (1, BuildupEnds), (1, WaveEnds), (0, DarknessAt));
        Keys(_emission, (0, 0), (0.94, BuildupEnds), (0.78, WaveEnds), (0, DarknessAt));

        // The wavefront itself: a hard flare, gone almost as fast as it came.
        Keys(_flash, (0, 0), (0, BuildupEnds), (0.85, BuildupEnds + 0.3), (0, WaveEnds));

        Keys(_root, (1, 0), (1, DarknessAt)).Completed += (_, _) =>
        {
            if (!_blackout) return;
            _emitting = false;
            _field.Agitation = 1;
            Suspended = true;
        };
    }

    private void EndBlackout()
    {
        _emitting = false;
        _emissionTime = 0;
        _field.Agitation = 1;
        Suspended = false;

        if (CursorHidingWanted) NativeCursor.Restore();

        Animate(_scrim, _settings.Dim, 0.25);
        Animate(_canvas, _settings.Opacity, 0.25);
        Animate(_emission, 0, 0.25);
        Animate(_flash, 0, 0.25);
        Animate(_detectorLayer, DetectorWanted ? 1 : 0, 0.25);
    }

    /// <summary>How hard the Zone is shaking at this point in the Emission.</summary>
    private static double EmissionAgitation(double t) => t switch
    {
        < BuildupEnds => 1 + 2.8 * Math.Pow(t / BuildupEnds, 1.6),
        < WaveEnds => 3.8,
        _ => Math.Max(1, 3.8 - 2.8 * (t - WaveEnds) / (DarknessAt - WaveEnds)),
    };

    private static DoubleAnimation Animate(UIElement target, double to, double seconds)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromSeconds(Math.Max(0.01, seconds)))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd,
        };
        target.BeginAnimation(UIElement.OpacityProperty, animation);
        return animation;
    }

    /// <summary>Builds one opacity timeline from (value, at-second) pairs.</summary>
    private static DoubleAnimationUsingKeyFrames Keys(UIElement target, params (double Value, double At)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };

        foreach (var (value, at) in frames)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(
                value,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        }

        target.BeginAnimation(UIElement.OpacityProperty, animation);
        return animation;
    }

    /// <summary>Fades the bubbles out and stops the render loop once they're gone.</summary>
    public void HideBubbles(bool immediate = false)
    {
        if (!_shown && !immediate) return;
        _shown = false;

        // Leaving means you touched something, so the pointer is coming back anyway --
        // but restore it explicitly rather than relying on the window underneath.
        if (CursorHidingWanted) NativeCursor.Restore();

        if (immediate)
        {
            _root.BeginAnimation(UIElement.OpacityProperty, null);
            _root.Opacity = 0;
            Visibility = Visibility.Hidden;
            Suspended = true;
            Collapse();
            return;
        }

        Fade(to: 0, seconds: 0.35, thenHide: true);
    }

    private void Fade(double to, double seconds, bool thenHide)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromSeconds(Math.Max(0.01, seconds)))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd,
        };

        if (thenHide)
        {
            animation.Completed += (_, _) =>
            {
                if (_shown) return;   // shown again mid-fade -- leave it alone
                Visibility = Visibility.Hidden;
                Suspended = true;
                Collapse();
            };
        }

        _root.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(StretchOverVirtualDesktop);

    private void StretchOverVirtualDesktop()
    {
        if (_hwnd == IntPtr.Zero || _collapsed) return;
        var (x, y, w, h) = Native.VirtualScreen();
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, x, y, w, h, Native.SWP_NOACTIVATE);
    }

    /// <summary>Hands the simulation one rectangle per physical monitor, in field
    /// coordinates. Without this the field is one big rectangle spanning every screen --
    /// which both lets bubbles clump on one monitor and lets them drift into the region
    /// a shorter monitor leaves behind, where nothing is actually displayed.</summary>
    private void UpdateRegions()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;

        var (vx, vy, vw, _) = Native.VirtualScreen();

        // The window spans the virtual desktop in physical pixels while WPF lays it out in
        // DIPs, so this ratio converts Win32 screen rectangles into field coordinates.
        var pixelsPerDip = vw / ActualWidth;
        if (pixelsPerDip <= 0) return;
        _pixelsPerDip = pixelsPerDip;

        var regions = new List<Rect>();
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            var b = screen.Bounds;
            regions.Add(new Rect(
                (b.X - vx) / pixelsPerDip,
                (b.Y - vy) / pixelsPerDip,
                b.Width / pixelsPerDip,
                b.Height / pixelsPerDip));
        }

        _field.SetRegions(regions);

        // Keep the detector on one screen -- preferably the primary -- so it can never
        // drift into the part of the virtual desktop that no monitor actually covers.
        var primary = System.Windows.Forms.Screen.PrimaryScreen;
        var index = primary is null
            ? 0
            : Math.Max(0, Array.FindIndex(System.Windows.Forms.Screen.AllScreens, s => s.DeviceName == primary.DeviceName));
        _detectorScreen = regions[Math.Min(index, regions.Count - 1)];
    }

    /// <summary>Shrinks the hidden window to a single pixel. A window stretched over a
    /// 5120x1600 desktop keeps a render surface that size alive even while hidden, which is
    /// a lot of memory to hold for an app that spends most of its life doing nothing.</summary>
    private void Collapse()
    {
        if (_hwnd == IntPtr.Zero || _collapsed) return;
        _collapsed = true;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 1, 1, Native.SWP_NOACTIVATE);
    }

    /// <summary>Renders what WPF believes it is drawing, straight to a file. Separates
    /// "the element was never drawn" from "it was drawn but not composited to the screen".</summary>
    private void SnapshotVisualTree()
    {
        try
        {
            var w = (int)ActualWidth;
            var h = (int)ActualHeight;
            Diagnostics.Log($"snapshot root={_root.ActualWidth:F0}x{_root.ActualHeight:F0} " +
                            $"window={w}x{h} rootClip={_root.Clip} pdaMargin={_detector.Margin} " +
                            $"pdaVisible={_detector.IsVisible} pdaOpacity={_detector.Opacity:F2}");

            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(_root);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));

            using var file = System.IO.File.Create(System.IO.Path.Combine(Settings.Directory, "snap.png"));
            encoder.Save(file);
            Diagnostics.Log("snapshot written");
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"snapshot failed: {ex.Message}");
        }
    }

    private void ReassertTopmost()
    {
        if (_hwnd == IntPtr.Zero) return;
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    /// <summary>Unit box the visuals draw into. The per-bubble transform scales it to the
    /// real size, so this is not a resolution.</summary>
    private double UnitSize => IsZone && _settings.Animated ? ArtifactVisual.UnitSize : SpriteSize;

    private void RebuildVisuals()
    {
        // A theme switch changes the element type outright, so start over.
        if (_visualTheme != _settings.Theme || _visualAnimated != _settings.Animated)
        {
            _visualTheme = _settings.Theme;
            _visualAnimated = _settings.Animated;
            _canvas.Children.Clear();
            _visuals.Clear();
        }

        while (_visuals.Count > _field.Bubbles.Count)
        {
            _canvas.Children.Remove(_visuals[^1].Element);
            _visuals.RemoveAt(_visuals.Count - 1);
        }

        while (_visuals.Count < _field.Bubbles.Count)
        {
            Visual visual;

            if (IsZone && _settings.Animated)
            {
                var artifact = new ArtifactVisual();
                visual = new Visual { Element = artifact, Artifact = artifact };
            }
            else
            {
                var sprite = new Image
                {
                    Width = SpriteSize,
                    Height = SpriteSize,
                    Stretch = Stretch.Fill,
                    IsHitTestVisible = false,
                };
                RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.Linear);
                visual = new Visual { Element = sprite, Sprite = sprite };
            }

            visual.Element.RenderTransformOrigin = new Point(0.5, 0.5);
            visual.Element.RenderTransform = new MatrixTransform();
            Canvas.SetLeft(visual.Element, 0);
            Canvas.SetTop(visual.Element, 0);

            _canvas.Children.Add(visual.Element);
            _visuals.Add(visual);
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var dt = (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        if (Paused || Suspended || dt <= 0) return;

        // Frame cap: accumulate real time, only step + redraw once a whole frame is due.
        _sinceDraw += dt;
        if (_frameBudget > 0 && _sinceDraw < _frameBudget) return;

        var step = _sinceDraw;
        _sinceDraw = 0;

        if (_emitting)
        {
            _emissionTime += step;
            _field.Agitation = EmissionAgitation(_emissionTime);
        }

        _field.Update(step);

        _animationTime += step;
        _frameCounter++;

        var bubbles = _field.Bubbles;

        if (DetectorWanted && !_blackout)
        {
            _detector.Tick(step, _field, _detectorScreen);

            // Artifacts that reach the detector are collected, and the Zone sends in a
            // replacement from one edge.
            _field.CollectPoint = _detector.SensorPoint;
        }
        else
        {
            _field.CollectPoint = null;
        }
        var count = Math.Min(bubbles.Count, _visuals.Count);
        var unit = UnitSize;
        var wobble = _settings.Wobble;

        for (var i = 0; i < count; i++)
        {
            var b = bubbles[i];
            var v = _visuals[i];

            if (v.Skin != b.Skin)
            {
                v.Skin = b.Skin;
                if (v.Artifact is not null) v.Artifact.Skin = b.Skin;
                else v.Sprite!.Source = IsZone ? ArtifactVisual.StaticSprite(b.Skin) : BubbleArt.Skins[b.Skin];
            }

            if (Math.Abs(v.Alpha - b.Alpha) > 0.001)
            {
                v.Alpha = b.Alpha;
                v.Element.Opacity = b.Alpha;
            }

            if (v.Artifact is { } artifact)
            {
                // Offset by the bubble's own phase so no two are ever in step.
                artifact.Time = _animationTime + b.Phase * 3;
                artifact.SetRenderScale(b.Radius * 2 / ArtifactVisual.UnitSize * _pixelsPerDip);
                artifact.Agitation = _field.Agitation;

                // Redrawing vector content is far dearer than moving it: WPF can composite a
                // cached rasterisation under a new transform almost for free, but must
                // re-rasterise whenever the content is invalidated. The drift therefore
                // updates every frame while the interior is refreshed on a rota, which is
                // invisible at these speeds and cuts the cost by two thirds.
                if ((_frameCounter + i) % ArtifactRedrawInterval == 0)
                    artifact.InvalidateInterior();
            }

            // Squash and stretch on top of whatever the silhouette is doing.
            var sx = 1 + wobble * Math.Sin(b.Phase);
            var sy = 1 - wobble * Math.Sin(b.Phase + 0.7);
            var k = b.Radius * 2 / unit;

            var m = new Matrix();
            m.Scale(k * sx, k * sy);
            m.Translate(b.X - unit / 2, b.Y - unit / 2);
            ((MatrixTransform)v.Element.RenderTransform).Matrix = m;
        }
    }
}
