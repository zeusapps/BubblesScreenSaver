using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

using Bubbles.Displays;
using Bubbles.Interop;
using Bubbles.Zone;

namespace Bubbles.Overlay;

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
    private readonly SkyLayer _emission = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly SkyLayer _flash = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly LightningLayer _lightning = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly WeatherLayer _weather = new() { Opacity = 0 };
    private readonly WeatherCycle _cycle = new();

    /// <summary>Which anomaly family the weather is coloured by. Fed from the field, on the two
    /// events that can change what is drifting up there -- never per frame.</summary>
    private readonly FamilyCensus _census = new();
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
    private double _ambientTime;
    private bool _ambientLightning;

    /// <summary>Whether a bolt is on screen this frame, from either sky. Read by the weather,
    /// which brightens the precipitation for exactly as long as it is true -- so it is written
    /// wherever HasStrike is already being asked, rather than asked a second time.</summary>
    private bool _strikeOnScreen;
    private bool _lightningDrawn;
    private double _animationTime;
    private int _frameCounter;
    private double _pixelsPerDip = 1;
    private OverlayTheme? _visualTheme;
    private bool? _visualAnimated;

    /// <summary>User-facing pause, from the tray menu.</summary>
    public bool Paused { get; set; }

    /// <summary>Internal render suspension: hidden, or drawing into a powered-down panel.
    ///
    /// Stops the weather on the way in. Everything else in this window is driven from
    /// OnRendering, which returns immediately while suspended -- but the weather's motion is
    /// animations on the compositor, and those keep running whether or not anything is asking
    /// them to. Leaving them going is the compositor working on a panel nobody can see.</summary>
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;

            if (value) _weather.Stop();
        }
    }

    private bool _suspended = true;

    /// <summary>Whether the artifacts are wanted on screen at all.
    ///
    /// False while something is holding the artifacts off but still permitting a blackout --
    /// music playing, say. Reaching black by way of an Emission would be twelve seconds of
    /// artifacts, lightning and a burning sky, which contradicts the reason that allowed the
    /// blackout in the first place, so the plain fade is used instead.</summary>
    public bool ArtifactsWelcome { get; set; } = true;

    /// <summary>True once the bubbles are on screen (or on their way in).</summary>
    public bool IsShowing => _shown;

    /// <summary>Raised once the screen has actually reached black -- not when the blackout
    /// begins, since an Emission spends twelve seconds getting there and dimming the monitors
    /// early would cut the show short.</summary>
    public event Action? WentDark;

    /// <summary>Raised the moment the blackout ends.</summary>
    public event Action? LeftDark;

    private bool IsZone => _settings.Theme == OverlayTheme.Zone;

    private bool DetectorWanted => IsZone && _settings.ShowDetector;

    /// <summary>Whether weather should be running at all. A different place is a different sky,
    /// so the Soap theme has none.</summary>
    private bool WeatherWanted => IsZone && _settings.Weather;

    private bool CursorHidingWanted => _settings.HideCursor;

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
        _field.PopulationChanged += TakeCensus;
        _field.ArtifactCollected += OnCollected;

        // The sky has a colour before it has any artifacts to take one from. Without this the
        // first census would find its own opening family already leading and decide there was
        // nothing to do, and the weather would run untinted until the field happened to swing.
        _weather.Family = _census.Dominant;

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

        // One source of truth for when the sky goes dark, so the strike schedule follows the
        // timeline instead of a count someone has to re-derive by hand whenever it is retuned.
        _lightning.EmissionEnds = DarknessAt;

        // Back to front: dimming sheet, burning sky, artifacts, shockwave, detector.
        // The artifacts sit above the sky so they still glow through an Emission rather
        // than being washed flat by it.
        _root.Children.Add(_scrim);
        _root.Children.Add(_emission);

        // Lightning belongs to the sky, so it sits behind the artifacts and silhouettes them.
        _root.Children.Add(_lightning);

        _root.Children.Add(_canvas);

        // Weather goes in front of the artifacts. Behind them fog fogs nothing -- the artifacts
        // stay sharp over the top of it and it reads as a haze on the desktop instead. The
        // shockwave still comes over everything, because it is the wavefront arriving.
        _root.Children.Add(_weather);

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

        ApplyGlass();
        Native.MakeClickThrough(_hwnd, _settings.ClickThrough);
        StretchOverVirtualDesktop();

        _lastFrame = _clock.Elapsed;
        CompositionTarget.Rendering += OnRendering;
        _topmostGuard.Start();

        WarmWeatherTiles();

        // BUBBLES_SNAP=7 writes one snapshot seven seconds in; BUBBLES_SNAP=4,20,36 writes
        // three. A filmstrip is what a cross-fade needs -- a single frame cannot show one
        // landing, which is the half of weather worth reviewing.
        var moments = SnapshotMoments(Environment.GetEnvironmentVariable("BUBBLES_SNAP"));

        foreach (var at in moments)
        {
            var when = at;
            var snap = new DispatcherTimer { Interval = TimeSpan.FromSeconds(when) };
            snap.Tick += (_, _) => { snap.Stop(); SnapshotVisualTree($"snap-{when:0.##}"); };
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
        var weatherWas = WeatherWanted;
        _settings = settings;

        // Switched back on, so start from a freshly rolled state rather than resuming whatever
        // half-finished cross-fade it was switched off during.
        if (WeatherWanted && !weatherWas) _cycle.Restart();

        if (!WeatherWanted)
        {
            _weather.Stop();
            StopAmbientLightning();
        }

        // The whole resting state, not a subset. This used to reset the scrim and the artifacts
        // and leave the sky and the flash wherever an interrupted Emission had put them.
        if (!_blackout) SettleLayers(_shown ? OverlayStage.Artifacts : OverlayStage.Active);

        _frameBudget = settings.MaxFps > 0 ? 1.0 / settings.MaxFps : 0;
        if (_hwnd != IntPtr.Zero) Native.MakeClickThrough(_hwnd, settings.ClickThrough);
        _field.SkinCount = SkinCount;
        _field.Apply(settings);

        // Switching theme swaps the element type outright -- live-drawn artifacts for bitmap
        // sprites -- so the visuals have to be rebuilt, not just re-pointed. Without this the
        // old elements survived and kept drawing artifacts, at the wrong scale, because the
        // unit box the transform divides by belongs to the other theme.
        RebuildVisuals();
        foreach (var v in _visuals) v.Skin = -1;

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

        // Everything underneath the root goes to its resting value before the root fades in.
        // Without this the artifacts arrive over whatever an interrupted blackout left behind --
        // in the worst case a scrim still held at 1, which is a solid black screen.
        SettleLayers(OverlayStage.Artifacts);

        if (CursorHidingWanted) NativeCursor.Hide();

        Fade(to: 1, seconds: _settings.FadeInSeconds, thenHide: false);
    }

    /// <summary>Assigns an opacity so that it actually takes.
    ///
    /// The animation has to be cleared first. Once a property has been animated with
    /// FillBehavior.HoldEnd, the held value outranks anything assigned directly -- so after a
    /// single blackout, assigning Opacity did nothing and the detector stayed on screen in a
    /// theme that has no detector, frozen, because nothing was ticking it any more.
    ///
    /// The scrim, the sky and the flash are animated exactly the same way and carry exactly the
    /// same hazard, so every layer goes through here rather than only the one where it was
    /// noticed first.</summary>
    private static void Settle(UIElement target, double opacity)
    {
        target.BeginAnimation(UIElement.OpacityProperty, null);
        target.Opacity = opacity;
    }

    /// <summary>Puts every layer beneath the root at its resting value for a stage. The root
    /// itself is left alone: it is what the show and hide fades drive.</summary>
    private void SettleLayers(OverlayStage stage)
    {
        var rest = LayerRest.For(stage, _settings, DetectorWanted, WeatherWanted);

        Settle(_scrim, rest.Scrim);
        Settle(_emission, rest.Sky);
        Settle(_flash, rest.Flash);
        Settle(_canvas, rest.Artifacts);
        Settle(_detectorLayer, rest.Detector);
        Settle(_weather, rest.Weather);
        Settle(_lightning, 0);

        // Nothing is drawn once the screen is dark, and the scrolls stop with it.
        if (rest.Weather <= 0) _weather.Stop();
    }

    /// <summary>Shows or hides the detector.</summary>
    private void SetDetectorVisible(bool visible) => Settle(_detectorLayer, visible ? 1 : 0);

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
            if (IsZone && _settings.Emission && ArtifactsWelcome) BeginEmission();
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
        Animate(_scrim, 1, 2.5, ReachedBlack);
    }

    /// <summary>An Emission. The sky burns, the artifacts go frantic, the wavefront hits,
    /// and then the Zone is dark. Ends on solid black exactly like the plain fade does.</summary>
    private void BeginEmission()
    {
        _emitting = true;
        _emissionTime = 0;
        _lightningDrawn = false;

        _lightning.BeginAnimation(UIElement.OpacityProperty, null);
        _lightning.Opacity = _settings.Lightning ? 1 : 0;

        var pda = DetectorWanted ? 1.0 : 0.0;

        // The detector loses the signal shortly before the wave arrives.
        Keys(_detectorLayer, null, (pda, 0), (pda, BuildupEnds - 1.6), (0, BuildupEnds - 0.2));

        // The world recedes as the sky takes over, then everything is swallowed.
        Keys(_scrim, null, (_settings.Dim, 0), (0.86, BuildupEnds), (0.86, WaveEnds), (1, DarknessAt));
        Keys(_canvas, null, (_settings.Opacity, 0), (1, BuildupEnds), (1, WaveEnds), (0, DarknessAt));
        Keys(_emission, null, (0, 0), (0.94, BuildupEnds), (0.78, WaveEnds), (0, DarknessAt));

        // The wavefront itself: a hard flare, gone almost as fast as it came.
        Keys(_flash, null, (0, 0), (0, BuildupEnds), (0.85, BuildupEnds + 0.3), (0, WaveEnds));

        // A flat timeline on the root, purely to time the end of the Emission.
        Keys(_root, ReachedBlack, (1, 0), (1, DarknessAt));
    }

    /// <summary>The screen has actually arrived at black. Rendering stops here -- there is
    /// nothing left to draw -- and anything that should react to real darkness happens now
    /// rather than when the blackout merely started.</summary>
    private void ReachedBlack()
    {
        if (!_blackout) return;

        HideLightning();
        _emitting = false;
        _emissionTime = 0;
        _field.Agitation = 1;
        Suspended = true;
        Raise(WentDark, nameof(WentDark));
    }

    /// <summary>Comes back from black.
    ///
    /// The overlay puts its own state back *before* it tells anybody. LeftDark restores monitor
    /// backlights over DDC/CI, changes HDR mode and may request a workstation lock -- the most
    /// failure-prone work in the application, and slow even when it succeeds, since a mode
    /// change costs a re-sync on every display.
    ///
    /// It used to be raised first, and a throw from it skipped every restore below. By that
    /// point _blackout is already false, so SetBlackout(false) early-returns from then on and
    /// FillBehavior.HoldEnd pins the scrim at full black: the overlay is opaque for the rest of
    /// the process's life, with nothing on screen to say why. Nothing the overlay owns may
    /// depend on foreign work succeeding.</summary>
    private void EndBlackout()
    {
        HideLightning();
        _emitting = false;
        _emissionTime = 0;
        _field.Agitation = 1;
        Suspended = false;

        if (CursorHidingWanted) NativeCursor.Restore();

        var rest = LayerRest.For(OverlayStage.Artifacts, _settings, DetectorWanted, WeatherWanted);

        Animate(_scrim, rest.Scrim, 0.25);
        Animate(_canvas, rest.Artifacts, 0.25);
        Animate(_emission, rest.Sky, 0.25);
        Animate(_flash, rest.Flash, 0.25);
        Animate(_detectorLayer, rest.Detector, 0.25);

        Raise(LeftDark, nameof(LeftDark));
    }

    /// <summary>Raises one of the blackout events without letting a subscriber's failure become
    /// the overlay's. The displays have their own recovery -- every change is recorded before it
    /// is made and replayed at the next launch -- whereas an overlay stuck opaque has none.</summary>
    private static void Raise(Action? handler, string name)
    {
        try
        {
            handler?.Invoke();
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"{name} subscriber threw: {ex}");
        }
    }

    private void HideLightning()
    {
        _lightning.BeginAnimation(UIElement.OpacityProperty, null);
        _lightning.Opacity = 0;
        _lightningDrawn = false;
    }

    /// <summary>How hard the Zone is shaking at this point in the Emission.</summary>
    private static double EmissionAgitation(double t) => t switch
    {
        < BuildupEnds => 1 + 2.8 * Math.Pow(t / BuildupEnds, 1.6),
        < WaveEnds => 3.8,
        _ => Math.Max(1, 3.8 - 2.8 * (t - WaveEnds) / (DarknessAt - WaveEnds)),
    };

    /// <summary>Starts an opacity animation.
    ///
    /// The completion callback is a parameter rather than something the caller attaches to the
    /// returned animation, because attaching afterwards silently does nothing: BeginAnimation
    /// creates the clock from the timeline there and then, and a handler added later is never
    /// invoked. That mistake cost a blackout that neither dimmed the monitors nor stopped the
    /// render loop, with no error anywhere to show for it.</summary>
    private static void Animate(UIElement target, double to, double seconds, Action? onCompleted = null)
    {
        var animation = new DoubleAnimation(to, TimeSpan.FromSeconds(Math.Max(0.01, seconds)))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd,
        };

        if (onCompleted is not null) animation.Completed += (_, _) => onCompleted();

        target.BeginAnimation(UIElement.OpacityProperty, animation);
    }

    /// <summary>Builds one opacity timeline from (value, at-second) pairs. The callback comes
    /// first for the same reason as in <see cref="Animate"/>.</summary>
    private static void Keys(UIElement target, Action? onCompleted, params (double Value, double At)[] frames)
    {
        var animation = new DoubleAnimationUsingKeyFrames { FillBehavior = FillBehavior.HoldEnd };

        foreach (var (value, at) in frames)
        {
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(
                value,
                KeyTime.FromTimeSpan(TimeSpan.FromSeconds(at)),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        }

        if (onCompleted is not null) animation.Completed += (_, _) => onCompleted();

        target.BeginAnimation(UIElement.OpacityProperty, animation);
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
        ApplyGlass();
    }

    /// <summary>Asks DWM to extend the frame over the whole client area, which is where the
    /// transparency comes from.
    ///
    /// Re-asserted rather than set once. Everything else volatile in this window already is --
    /// topmost every three seconds, the window bounds on every display change -- and elsewhere
    /// the same rule holds for monitor brightness and for HDR. This was the last set-once Win32
    /// call in the file, and a blackout now performs two display mode changes per cycle
    /// (HDR off going in, HDR on coming out). If the extension does not survive one of those,
    /// the window paints opaque and every artifact arrives on a black screen.
    ///
    /// Called where the window is already being repositioned, so it costs nothing on the render
    /// path.</summary>
    private void ApplyGlass()
    {
        if (_hwnd == IntPtr.Zero) return;

        // A discarded return value is how an opaque overlay becomes a silent failure.
        if (!Native.MakeGlass(_hwnd))
            Diagnostics.Log("MakeGlass failed: the overlay will render opaque");
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

        // The first layout is the first chance to convert a BubbleCount written when it meant a
        // total rather than a density. It needs the real regions in field coordinates, which is
        // why this cannot happen in Settings.Load.
        MigrateDensity(regions);

        // Every full-desktop layer draws per screen, not against the union. Without this the
        // sky ramps over the tallest monitor and the bolts are scaled by it.
        _field.SetRegions(regions);
        _lightning.Regions = regions;
        _weather.Regions = regions;
        _emission.Regions = regions;
        _flash.Regions = regions;

        // Keep the detector on one screen -- preferably the primary -- so it can never
        // drift into the part of the virtual desktop that no monitor actually covers.
        var primary = System.Windows.Forms.Screen.PrimaryScreen;
        var index = primary is null
            ? 0
            : Math.Max(0, Array.FindIndex(System.Windows.Forms.Screen.AllScreens, s => s.DeviceName == primary.DeviceName));
        _detectorScreen = regions[Math.Min(index, regions.Count - 1)];
    }

    /// <summary>Whether the field-coordinate ratio agrees with the window's DPI scale, which it
    /// only does once WPF has laid the window out at its stretched size.
    ///
    /// Its own method so the condition can be asserted: everything else that reads stale regions
    /// is corrected by the next layout pass, but the density conversion writes to disk.</summary>
    internal static bool LayoutSettled(double pixelsPerDip, double windowScale) =>
        windowScale > 0 && Math.Abs(pixelsPerDip - windowScale) <= 0.01;

    /// <summary>Converts a stored artifact count from the total it used to mean into the density
    /// it means now, once, against the layout in front of the user at the time.
    ///
    /// Somebody who tuned the count on their own desk keeps the picture they tuned; the new
    /// meaning only shows itself the next time their layout changes. Stamped and saved
    /// immediately, because a conversion that re-ran on every launch would compound and their
    /// artifacts would dwindle a few at a time.</summary>
    private void MigrateDensity(IReadOnlyList<Rect> regions)
    {
        if (!_settings.NeedsDensityMigration || regions.Count == 0) return;

        // Only once WPF has actually laid the window out at its stretched size. UpdateRegions
        // divides the virtual desktop's width by ActualWidth to get field coordinates, and
        // ShowBubbles calls it immediately after SetWindowPos, before WPF has caught up -- so on
        // that first call ActualWidth is still the unstretched size, the ratio is wrong by that
        // factor, and every region comes out a fraction of its real area. The other consumers
        // are corrected by the next layout pass; this one writes to disk and is permanent. It
        // converted 26 into 53 where the right answer was 30.
        //
        // The window's own DPI scale is what the ratio should equal once the layout is settled.
        var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (!LayoutSettled(_pixelsPerDip, scale))
        {
            Diagnostics.Log($"density migration deferred: ratio {_pixelsPerDip:N3} " +
                            $"has not settled to the window scale {scale:N3}");
            return;
        }

        var before = _settings.BubbleCount;
        _settings.BubbleCount = MonitorRegions.DensityFor(before, regions);
        _settings.SettingsVersion = Settings.DensityVersion;
        _settings.Save();

        Diagnostics.Log($"BubbleCount {before} converted to density {_settings.BubbleCount} " +
                        $"across {regions.Count} screen(s)");
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
    /// <summary>Rasterises every family's weather tiles before any of them is needed.
    ///
    /// One family per callback, at idle priority, so the dispatcher gets a turn between them and
    /// none of this ever lands inside a frame. Done eagerly rather than when a family first wins
    /// the census: the brushes are wanted on the very first frame of the tint cross-fade, so
    /// there is no moment between deciding and drawing in which to build them lazily.
    ///
    /// The whole set is a few hundred milliseconds and some twelve megabytes, spent once at
    /// startup while the screen is still empty. That is the trade -- memory for a freeze, made
    /// deliberately, because the freeze was visible and the memory is not.</summary>
    private void WarmWeatherTiles()
    {
        var pending = new Queue<Anomaly?>();
        pending.Enqueue(null);
        foreach (var family in Enum.GetValues<Anomaly>()) pending.Enqueue(family);

        Next();

        void Next()
        {
            if (pending.Count == 0) return;

            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                WeatherBrushes.Warm(pending.Dequeue());
                Next();
            });
        }
    }

    private static List<double> SnapshotMoments(string? spec)
    {
        var moments = new List<double>();
        if (string.IsNullOrWhiteSpace(spec)) return moments;

        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries))
            if (double.TryParse(part.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var at) && at > 0)
                moments.Add(at);

        return moments;
    }

    private void SnapshotVisualTree(string name = "snap")
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

            using var file = System.IO.File.Create(
                System.IO.Path.Combine(Settings.Directory, $"{name}.png"));
            encoder.Save(file);
            Diagnostics.Log($"snapshot written: {name}.png");
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"snapshot failed: {ex.Message}");
        }
    }

    /// <summary>Advances the weather and tells the layer what to show.
    ///
    /// Fog is pulled out over an Emission's buildup: a full-desktop haze in front of the
    /// artifacts flattens exactly the contrast the Emission spends six seconds building. Rain is
    /// left running and is lit by the strikes.</summary>
    private void TickWeather(double step)
    {
        if (!WeatherWanted || _blackout)
        {
            _weather.Stop();
            StopAmbientLightning();

            // Dropped rather than left to decay. Frozen here it would still be running when
            // weather came back, and the fog would thin for a pickup made hours ago.
            _fogDipLeft = 0;
            return;
        }

        // Both clocks, every frame: the cycle's, and the tint's. Neither counts anything --
        // the census that decides the tint runs when the field changes, and this is the
        // subtraction that walks the cross-fade it started.
        _census.Tick(step);
        _weather.Tick(step);

        if (_fogDipLeft > 0) _fogDipLeft = Math.Max(0, _fogDipLeft - step);

        if (_pinned is not null)
        {
            _weather.FogDamping = (_emitting ? FogDampingAt(_emissionTime) : 1) * ThermicDamping();

            // Struck before shown, both here and below: the lift lasts exactly the strike, and a
            // sheet filled before this frame's bolt is known about would lag it by one.
            TickAmbientLightning(step, _pinned.Value.StormIntensity);
            _weather.Lit = _strikeOnScreen;

            _weather.Show(_pinned.Value.To, _pinned.Value.From, _pinned.Value.Progress);
            return;
        }

        // The burning sky is the show. A weather change underneath it would be a second one, so
        // the cycle is held -- though a cross-fade already in flight is allowed to finish, since
        // freezing it half way would leave two states live for the whole Emission.
        _cycle.Suspended = _emitting;
        _cycle.Tick(step);

        _weather.FogDamping = (_emitting ? FogDampingAt(_emissionTime) : 1) * ThermicDamping();

        TickAmbientLightning(step, _cycle.IntensityOf(Weather.Storm));
        _weather.Lit = _strikeOnScreen;

        _weather.Show(_cycle);
    }

    /// <summary>Holds the weather at one state, or part way between two, instead of letting the
    /// cycle choose. Only <c>--weather-demo</c> uses this: waiting a minute per change is no way
    /// to look at four states.</summary>
    internal void PinWeather(Weather to, Weather? from, double progress)
    {
        var storm = 0.0;
        if (to == Weather.Storm) storm += progress;
        if (from == Weather.Storm) storm += 1 - progress;

        _pinned = (to, from, progress, Math.Clamp(storm, 0, 1));
    }

    /// <summary>Holds the weather's tint at one family instead of letting the census choose.
    ///
    /// Only <c>--weather-demo</c> uses this, for the same reason it pins the state: a tint holds
    /// for twenty-five seconds and only moves when the field swings by three artifacts, so
    /// waiting for one is no way to look at four of them. The change itself is the real one --
    /// this assigns the property the census assigns, and the layer cross-fades exactly as it
    /// would in use.</summary>
    internal void PinFamily(Anomaly family)
    {
        _pinnedFamily = family;
        _weather.Family = family;
    }

    private Anomaly? _pinnedFamily;

    private (Weather To, Weather? From, double Progress, double StormIntensity)? _pinned;

    /// <summary>Takes the family census and hands the answer to the weather.
    ///
    /// Called when the population changes and when something is collected, which are the only
    /// two moments the mix of artifacts on screen can move. The census decides whether that is
    /// enough to change the sky; the layer cross-fades if it is.</summary>
    private void TakeCensus()
    {
        // A pinned tint takes the census over outright, the way a pinned state takes the cycle
        // over. Collections go on happening under the demo, and without this the census would
        // repaint the sky out from under whichever family was being looked at.
        if (_pinnedFamily is not null) return;

        if (_census.Take(_field.FamilyCounts)) _weather.Family = _census.Dominant;
    }

    /// <summary>How long after an Electrical pickup the sky answers, in seconds. Long enough to
    /// read as a consequence rather than as a coincidence.</summary>
    private const double ElectricalAnswer = 0.4;

    /// <summary>How long a Thermic pickup thins the fog for, and how much of it goes.</summary>
    private const double ThermicClearing = 2.6;
    private const double ThermicDepth = 0.45;

    private double _fogDipLeft;

    /// <summary>What the sky does about a collection.
    ///
    /// Every family gets the flourish, at the detector, in its own colour. Two of them reach
    /// further because they have somewhere obvious to reach: Electrical brings the next distant
    /// strike forward, and Thermic burns a clearing in the fog. Both are parameters given to
    /// machinery that already exists -- neither draws anything new. Chemical and Gravitational
    /// get the flourish alone, because inventing a mechanism per family would be four times the
    /// surface for a second of animation.</summary>
    private void OnCollected(Anomaly family)
    {
        TakeCensus();

        // Weather off is weather off. None of this is a detector effect that happens to be
        // drawn on the weather layer -- it is the sky reacting, and there is no sky.
        if (!WeatherWanted || _blackout) return;

        _weather.Flourish(_field.CollectPoint ?? _detector.SensorPoint, family);

        switch (family)
        {
            case Anomaly.Electrical when _ambientLightning:
                // Advanced onto the schedule's own next strike rather than given a new one. The
                // storm was always going to produce that bolt; it produces it now.
                var wait = _lightning.NextStrikeIn(_ambientTime);
                if (wait > ElectricalAnswer) _ambientTime += wait - ElectricalAnswer;
                break;

            case Anomaly.Thermic:
                _fogDipLeft = ThermicClearing;
                break;
        }
    }

    /// <summary>How much fog survives a Thermic pickup, as a multiplier that starts and ends at
    /// one. It thins and comes back; nothing is switched.</summary>
    private double ThermicDamping()
    {
        if (_fogDipLeft <= 0) return 1;

        return 1 - ThermicDepth * Math.Sin(Math.PI * (_fogDipLeft / ThermicClearing));
    }

    /// <summary>The distant strikes of the stormy weather, on the same layer the Emission uses.
    ///
    /// Gated on the Lightning setting as well as the weather one: this is the same sky doing the
    /// same thing more quietly, and somebody who turned lightning off did not ask for it back
    /// because the weather changed.</summary>
    private void TickAmbientLightning(double step, double storm)
    {
        if (_emitting || !_settings.Lightning || storm <= 0)
        {
            StopAmbientLightning();
            return;
        }

        _lightning.Ambient = true;
        _ambientTime += step;

        // Held at 1 rather than faded with the storm's intensity: the strikes themselves are
        // sparse enough that a fading sky would just make them intermittent, and animating this
        // layer's opacity is what the baked levels exist to avoid.
        if (!_ambientLightning)
        {
            _ambientLightning = true;
            Settle(_lightning, 1);
        }

        // The same early-out the Emission uses: a strike is a fraction of a second inside a
        // window measured in tens, so almost every frame has nothing to redraw.
        var striking = _lightning.HasStrike(_ambientTime);
        _strikeOnScreen = striking;

        if (striking || _lightningDrawn)
        {
            _lightning.Time = _ambientTime;
            _lightning.InvalidateVisual();
            _lightningDrawn = striking;
        }
    }

    private void StopAmbientLightning()
    {
        if (!_ambientLightning) return;

        _ambientLightning = false;
        _lightning.Ambient = false;
        _ambientTime = 0;
        _lightningDrawn = false;
        if (!_emitting) _strikeOnScreen = false;

        if (!_emitting) HideLightning();
    }

    /// <summary>How much of the fog survives at this point in an Emission: all of it as the
    /// tremors start, none by the time the wavefront hits.</summary>
    private static double FogDampingAt(double time) =>
        time >= BuildupEnds ? 0 : Math.Clamp(1 - time / BuildupEnds, 0, 1);

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

    /// <summary>Reports frames that took too long, when BUBBLES_FRAMES is set.
    ///
    /// A screensaver that stutters is a screensaver somebody turns off, and a stutter is the one
    /// defect that leaves no trace in a screenshot -- the tinted weather froze the rain for a
    /// tenth of a second and every test and every export panel passed. This is how that gets
    /// answered with a number instead of an impression.</summary>
    private readonly bool _reportFrames =
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BUBBLES_FRAMES"));

    private double _worstFrame;
    private int _longFrames;
    private int _frames;

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var dt = (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        if (_reportFrames && dt > 0)
        {
            _frames++;

            // Two frames at 60fps. Below that the compositor's own jitter dominates and every
            // run would report hundreds.
            if (dt > 0.033)
            {
                _longFrames++;
                if (dt > _worstFrame) _worstFrame = dt;
                Diagnostics.Log($"long frame: {dt * 1000:F0}ms");
            }

            if (_frames % 600 == 0)
                Diagnostics.Log($"frames {_frames}, long {_longFrames}, " +
                                $"worst {_worstFrame * 1000:F0}ms");
        }

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

            // The Emission takes the layer over outright. A storm that was overhead when it
            // began does not get to keep striking underneath it.
            _lightning.Ambient = false;
            _strikeOnScreen = false;

            if (_settings.Lightning)
            {
                // Only redraw while a bolt is actually on screen, plus the one frame after the
                // last one dies so the sky is left clean.
                var striking = _lightning.HasStrike(_emissionTime);
                _strikeOnScreen = striking;

                if (striking || _lightningDrawn)
                {
                    _lightning.Time = _emissionTime;
                    _lightning.InvalidateVisual();
                    _lightningDrawn = striking;
                }
            }
        }

        TickWeather(step);

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
