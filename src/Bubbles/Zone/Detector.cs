using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace Bubbles.Zone;

/// <summary>The VELES screen. The detector sits still at the centre and the world moves
/// around it, the way any radar scope works -- so the crosshair never wanders and a blip
/// creeping towards the middle is an artifact genuinely closing on the detector. The monitor
/// edge is drawn as a rectangle sliding about, which is what tells you where on the screen
/// the detector currently is.</summary>
internal sealed class Scope : FrameworkElement
{
    private static readonly Color Phosphor = Color.FromRgb(0x53, 0xE0, 0x63);
    private static readonly Brush Screen = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x22, 0x0E)));
    private static readonly Pen Arc = FreezePen(Color.FromArgb(0xA0, 0x53, 0xE0, 0x63), 1.1);
    private static readonly Pen Axis = FreezePen(Color.FromArgb(0xC8, 0x53, 0xE0, 0x63), 1.2);
    private static readonly Pen Tick = FreezePen(Color.FromArgb(0x90, 0x53, 0xE0, 0x63), 1.0);

    /// <summary>Blips as offsets from the detector, in field units.</summary>
    public List<(double DX, double DY, Anomaly Family, double Strength)> Blips { get; } = new();

    /// <summary>Pickup radius in field units -- drawn as the tight inner ring.</summary>
    public double PickupRadius { get; set; } = 60;

    /// <summary>World distance represented by the outermost ring.</summary>
    public double RangeWorld { get; set; } = 1200;

    private static Brush Freeze(Brush b)
    {
        b.Freeze();
        return b;
    }

    private static Pen FreezePen(Color c, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(c), thickness);
        pen.Freeze();
        return pen;
    }

    private static Color FamilyColour(Anomaly family) => family switch
    {
        Anomaly.Thermic => Color.FromRgb(0xFF, 0xB0, 0x48),
        Anomaly.Electrical => Color.FromRgb(0x9A, 0xE2, 0xFF),
        Anomaly.Gravitational => Color.FromRgb(0xE0, 0xD0, 0x80),
        _ => Phosphor,
    };

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        dc.DrawRectangle(Screen, null, new Rect(0, 0, w, h));
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));

        var centre = new Point(w / 2, h / 2);
        var radius = Math.Min(w, h) / 2 - 4;
        var scale = radius / Math.Max(1, RangeWorld);

        // Bearing spokes, every forty-five degrees.
        for (var i = 0; i < 8; i++)
        {
            var a = i * Math.PI / 4;
            dc.DrawLine(Arc, centre,
                new Point(centre.X + Math.Cos(a) * radius, centre.Y + Math.Sin(a) * radius));
        }

        // Range rings, evenly spaced out to the edge of the scope.
        for (var i = 1; i <= 5; i++)
        {
            var r = radius * i / 5.0;
            dc.DrawEllipse(null, i == 5 ? Axis : Arc, centre, r, r);
        }

        // The pickup radius: cross this and the artifact is collected.
        var pickup = PickupRadius * scale;
        if (pickup > 2) dc.DrawEllipse(null, Axis, centre, pickup, pickup);

        // The operator, dead centre and staying there.
        dc.DrawLine(Axis, new Point(centre.X - 6, centre.Y), new Point(centre.X + 6, centre.Y));
        dc.DrawLine(Axis, new Point(centre.X, centre.Y - 6), new Point(centre.X, centre.Y + 6));

        // Edge ticks, so the graticule reads as an instrument rather than a target.
        for (var i = 0; i < 12; i++)
        {
            var a = i * Math.PI / 6;
            var inner = radius * (i % 3 == 0 ? 0.9 : 0.95);
            dc.DrawLine(Tick,
                new Point(centre.X + Math.Cos(a) * inner, centre.Y + Math.Sin(a) * inner),
                new Point(centre.X + Math.Cos(a) * radius, centre.Y + Math.Sin(a) * radius));
        }

        foreach (var (dx, dy, family, strength) in Blips)
        {
            var at = new Point(centre.X + dx * scale, centre.Y + dy * scale);
            var colour = FamilyColour(family);
            var size = 1.8 + strength * 2.6;

            var glow = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.5),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.5,
                RadiusY = 0.5,
            };
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0xB0, colour.R, colour.G, colour.B), 0));
            glow.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, colour.R, colour.G, colour.B), 1));
            glow.Freeze();

            dc.DrawEllipse(glow, null, at, size * 3, size * 3);
            dc.DrawEllipse(new SolidColorBrush(colour), null, at, size, size);
        }

        dc.Pop();
    }
}

/// <summary>A VELES artifact detector: hinged screen housing with a ribbed cap, a red LED bar
/// under the screen, the ON/OFF toggle and lamp, a segmented thumb pad, a speaker grille, and
/// the x0.1..x1000 range keys down the right side.
///
/// It wanders slowly across the primary screen rather than sitting in a corner -- a lit panel
/// parked in one spot for hours is exactly the burn-in this app exists to avoid.</summary>
public sealed class Detector : Border
{
    private const double PanelWidth = 250;
    private const double PanelHeight = 384;

    /// <summary>How far the scope reaches, in field units.</summary>
    private const double Range = 1000;
    private const double MetresPerDip = 0.42;

    private static readonly FontFamily Mono = new("Consolas, Cascadia Mono, Courier New, monospace");
    private static readonly Color Phosphor = Color.FromRgb(0x53, 0xE0, 0x63);
    private static readonly Color PhosphorDim = Color.FromRgb(0x2E, 0x8C, 0x3C);
    private static readonly Color Label = Color.FromArgb(0xD8, 0xE0, 0xE6, 0xEC);

    private readonly Scope _scope = new();
    private readonly TextBlock _header = ScreenText(PhosphorDim, 7);
    private readonly TextBlock _coords = ScreenText(Phosphor, 7.5);
    private readonly TextBlock _nearest = ScreenText(Phosphor, 7.5);
    private readonly TextBlock _tiny = ScreenText(PhosphorDim, 5.5);
    private readonly TextBlock _bright;
    private readonly List<Rectangle> _leds = new();
    private readonly List<Border> _keys = new();
    private readonly Ellipse _lamp;

    private double _time;
    private double _sinceRefresh = 999;
    private double _smoothedRad;
    private double _x, _y;
    private int _seenCollected;
    private double _flashUntil = -1;
    private int _target = -1;
    private double _targetDistance = double.MaxValue;

    /// <summary>How fast the detector closes on its quarry, in DIP per second. A little above
    /// the artifacts' own drift, so it can actually run one down.</summary>
    private const double HuntSpeed = 46;

    /// <summary>Where the detector reads from, in field coordinates. The field picks up any
    /// artifact that drifts within its collection radius of this point.</summary>
    public Point SensorPoint { get; private set; }

    public Detector()
    {
        Width = PanelWidth;
        Height = PanelHeight;
        IsHitTestVisible = false;
        Background = Brushes.Transparent;

        // Deliberately not BitmapCache'd: the readout changes five times a second, so the
        // cache would be rebuilt almost as often as it is used, and measuring it that way
        // came out worse than letting WPF draw the panel directly.

        _bright = new TextBlock
        {
            FontFamily = Mono,
            FontSize = 8,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x1A, 0x0A)),
        };

        _lamp = new Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(Color.FromRgb(0x8C, 0x1A, 0x12)),
            Stroke = new SolidColorBrush(Color.FromArgb(0x90, 0x18, 0x18, 0x18)),
            StrokeThickness = 1,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };

        var chassis = new StackPanel();
        chassis.Children.Add(BuildHousing());
        chassis.Children.Add(BuildBody());
        Child = chassis;
    }

    // ---- the hinged screen housing --------------------------------------------------------

    private FrameworkElement BuildHousing()
    {
        var stack = new StackPanel();

        stack.Children.Add(new Border
        {
            Height = 12,
            Margin = new Thickness(28, 0, 28, 0),
            CornerRadius = new CornerRadius(6, 6, 0, 0),
            Background = Ribs(true, Color.FromRgb(0x92, 0x9E, 0xAA), Color.FromRgb(0x56, 0x62, 0x6E)),
        });

        var screen = new Grid();
        screen.Children.Add(_scope);

        var topLeft = new StackPanel { Margin = new Thickness(5, 3, 0, 0) };
        topLeft.Children.Add(_coords);
        topLeft.Children.Add(_nearest);
        screen.Children.Add(topLeft);

        screen.Children.Add(new Border
        {
            Child = _header,
            Margin = new Thickness(0, 0, 0, 2),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
        });

        var right = new StackPanel
        {
            Margin = new Thickness(0, 3, 5, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Top,
        };
        right.Children.Add(new Border
        {
            Background = new SolidColorBrush(Phosphor),
            Padding = new Thickness(3, 1, 3, 1),
            Child = _bright,
        });
        right.Children.Add(_tiny);
        screen.Children.Add(right);

        stack.Children.Add(new Border
        {
            Height = 168,
            Margin = new Thickness(10, 0, 10, 0),
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(9, 9, 3, 3),
            Background = Metal(),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x46, 0x50)),
            BorderThickness = new Thickness(1),
            Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x07, 0x14, 0x09)),
                CornerRadius = new CornerRadius(2),
                Child = screen,
            },
        });

        var ledRow = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        };

        for (var i = 0; i < 10; i++)
        {
            var led = new Rectangle
            {
                Width = 8,
                Height = 10,
                Margin = new Thickness(2, 0, 2, 0),
                RadiusX = 1,
                RadiusY = 1,
                Fill = new SolidColorBrush(Color.FromRgb(0x3A, 0x10, 0x0E)),
            };
            _leds.Add(led);
            ledRow.Children.Add(led);
        }

        stack.Children.Add(new Border
        {
            Margin = new Thickness(16, 0, 16, 0),
            Padding = new Thickness(4, 3, 4, 3),
            Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x12)),
            Child = ledRow,
        });

        return stack;
    }

    // ---- the chassis and its controls -------------------------------------------------------

    private FrameworkElement BuildBody()
    {
        var controls = new Grid { Margin = new Thickness(7) };
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();

        var toggleRow = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 7),
        };
        toggleRow.Children.Add(Caption("ON/OFF", 5));
        toggleRow.Children.Add(new Border
        {
            Width = 20,
            Height = 9,
            CornerRadius = new CornerRadius(1),
            Background = Ribs(true, Color.FromRgb(0xC8, 0xCE, 0xD4), Color.FromRgb(0x80, 0x88, 0x90)),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        toggleRow.Children.Add(_lamp);
        left.Children.Add(toggleRow);

        left.Children.Add(ThumbPad());

        left.Children.Add(new Border
        {
            Width = 40,
            Height = 22,
            Margin = new Thickness(2, 8, 0, 0),
            CornerRadius = new CornerRadius(10),
            Background = Ribs(false, Color.FromRgb(0x2C, 0x30, 0x34), Color.FromRgb(0x10, 0x12, 0x14)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        });

        var barRow = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(2, 9, 0, 0),
        };
        barRow.Children.Add(new Border
        {
            Width = 28,
            Height = 8,
            CornerRadius = new CornerRadius(1),
            Background = new SolidColorBrush(Color.FromRgb(0xD8, 0x24, 0x18)),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        });
        barRow.Children.Add(Caption(" +y", 0));
        left.Children.Add(barRow);

        Grid.SetColumn(left, 0);
        controls.Children.Add(left);

        var right = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Top };

        foreach (var label in new[] { "X0.1-", "X1-", "X10-", "X100-", "X1000-" })
        {
            var row = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 5),
            };

            row.Children.Add(new TextBlock
            {
                Text = label,
                FontFamily = Mono,
                FontSize = 7,
                Width = 28,
                TextAlignment = TextAlignment.Right,
                Foreground = new SolidColorBrush(Label),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            });

            var key = new Border
            {
                Width = 32,
                Height = 12,
                CornerRadius = new CornerRadius(2),
                Background = KeyBrush(lit: false),
            };
            _keys.Add(key);
            row.Children.Add(key);
            right.Children.Add(row);
        }

        Grid.SetColumn(right, 1);
        controls.Children.Add(right);

        var body = new StackPanel();
        body.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x18)),
            Child = controls,
        });

        body.Children.Add(new Border
        {
            Height = 11,
            Margin = new Thickness(14, 6, 14, 0),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromRgb(0x17, 0x19, 0x1B)),
        });

        body.Children.Add(new Border
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
            Padding = new Thickness(8, 1, 8, 1),
            CornerRadius = new CornerRadius(1),
            Background = Ribs(false, Color.FromRgb(0xC4, 0xCA, 0xD0), Color.FromRgb(0x98, 0x9E, 0xA4)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x66)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = "V E L E S",
                FontFamily = Mono,
                FontSize = 8.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1C, 0x1E)),
            },
        });

        return new Border
        {
            // Narrower than the screen housing above it, the way the real case steps in
            // below the hinge.
            Margin = new Thickness(26, 3, 26, 0),
            CornerRadius = new CornerRadius(10, 10, 16, 16),
            Background = Metal(),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3C, 0x46, 0x50)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 9, 9, 7),
            Child = body,
        };
    }

    private static TextBlock Caption(string text, double rightMargin) => new()
    {
        Text = text,
        FontFamily = Mono,
        FontSize = 7,
        Foreground = new SolidColorBrush(Label),
        VerticalAlignment = System.Windows.VerticalAlignment.Center,
        Margin = new Thickness(0, 0, rightMargin, 0),
    };

    private static FrameworkElement ThumbPad()
    {
        var pad = new Grid
        {
            Width = 42,
            Height = 42,
            Margin = new Thickness(2, 0, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        };

        pad.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromRgb(0x2A, 0x30, 0x3A)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x14)),
            StrokeThickness = 2,
        });

        for (var i = 0; i < 8; i++)
        {
            pad.Children.Add(new Rectangle
            {
                Width = 5,
                Height = 15,
                Fill = new SolidColorBrush(Color.FromArgb(0xC0, 0xB8, 0xC2, 0xCE)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(0, 4, 0, 0),
                RenderTransformOrigin = new Point(0.5, 1.4),
                RenderTransform = new RotateTransform(i * 45),
            });
        }

        pad.Children.Add(new Ellipse
        {
            Width = 18,
            Height = 18,
            Fill = new SolidColorBrush(Color.FromRgb(0x8E, 0x96, 0xA0)),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        });

        return pad;
    }

    private static Brush Metal()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0.25, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x7E, 0x8A, 0x96), 0.00));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x62, 0x6E, 0x7A), 0.35));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x4A, 0x54, 0x5E), 0.75));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x36, 0x3E, 0x46), 1.00));
        brush.Freeze();
        return brush;
    }

    private static Brush KeyBrush(bool lit) => lit
        ? Ribs(true, Color.FromRgb(0xC8, 0xE0, 0x9A), Color.FromRgb(0x7E, 0x96, 0x54))
        : Ribs(true, Color.FromRgb(0xB4, 0xBC, 0xC4), Color.FromRgb(0x6E, 0x76, 0x7E));

    private static readonly Dictionary<(bool, uint, uint), Brush> RibCache = new();

    /// <summary>Ribbed metal or rubber, as on the cap, the keys and the speaker grille.
    ///
    /// Baked to a bitmap and tiled as an ImageBrush rather than tiled as a DrawingBrush: a
    /// tiled DrawingBrush is re-rasterised from its geometry on every composition pass, and
    /// with a dozen of them on the case that was most of what this panel cost. Cached by
    /// colour pair, so a repaint of the keys reuses the same two brushes forever.</summary>
    private static Brush Ribs(bool vertical, Color high, Color low)
    {
        var key = (vertical, PackColour(high), PackColour(low));
        if (RibCache.TryGetValue(key, out var cached)) return cached;

        const int tile = 4;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(low), null, new Rect(0, 0, tile, tile));
            dc.DrawRectangle(new SolidColorBrush(high), null,
                vertical ? new Rect(0, 0, 2, tile) : new Rect(0, 0, tile, 2));
        }

        var bmp = new RenderTargetBitmap(tile, tile, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();

        var brush = new ImageBrush(bmp)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, tile, tile),
            ViewportUnits = BrushMappingMode.Absolute,
        };
        brush.Freeze();
        return RibCache[key] = brush;
    }

    private static uint PackColour(Color c) =>
        (uint)((c.A << 24) | (c.R << 16) | (c.G << 8) | c.B);

    // Repaint targets, built once rather than on every readout refresh.
    private static readonly Brush LedOn = FreezeBrush(Color.FromRgb(0xFF, 0x2E, 0x1E));
    private static readonly Brush LedOff = FreezeBrush(Color.FromRgb(0x3A, 0x10, 0x0E));
    private static readonly Brush LampOn = FreezeBrush(Color.FromRgb(0xFF, 0x3A, 0x24));
    private static readonly Brush LampOff = FreezeBrush(Color.FromRgb(0x8C, 0x1A, 0x12));
    private static readonly Brush RadNormal = FreezeBrush(Color.FromRgb(0x53, 0xE0, 0x63));
    private static readonly Brush RadHot = FreezeBrush(Color.FromRgb(0xFF, 0x6B, 0x3A));

    private static Brush FreezeBrush(Color c)
    {
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return brush;
    }

    private static TextBlock ScreenText(Color colour, double size) => new()
    {
        FontFamily = Mono,
        FontSize = size,
        Foreground = new SolidColorBrush(colour),
    };

    /// <summary>Parks the panel at its origin. Only the offline exporter needs this.</summary>
    public void ResetPosition()
    {
        Canvas.SetLeft(this, 0);
        Canvas.SetTop(this, 0);
    }

    public void Tick(double dt, BubbleField field, Rect screen)
    {
        _time += dt;

        // Movement runs every frame so the hunt looks smooth; the readout is recomputed a
        // few times a second, which is as often as it is worth reading.
        Hunt(dt, field, screen);

        _sinceRefresh += dt;
        if (_sinceRefresh < 0.2) return;
        _sinceRefresh = 0;

        Refresh(field, screen);
    }

    /// <summary>Closes on the nearest artifact. A stalker with a detector walks towards the
    /// signal; drifting on a fixed path made the pickups look like coincidence.</summary>
    private void Hunt(double dt, BubbleField field, Rect screen)
    {
        if (_x == 0 && _y == 0)
        {
            _x = screen.Left + (screen.Width - PanelWidth) / 2;
            _y = screen.Top + (screen.Height - PanelHeight) / 2;
        }

        var here = SensorOffsetFrom(_x, _y);
        var quarry = Quarry(field, screen, here);

        if (quarry is { } aim)
        {
            var dx = aim.X - here.X;
            var dy = aim.Y - here.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance > 0.5)
            {
                var step = Math.Min(HuntSpeed * dt, distance);
                _x += dx / distance * step;
                _y += dy / distance * step;
            }
        }
        else
        {
            // Nothing to chase: wander gently rather than stand still.
            _x += Math.Sin(_time * 0.21) * HuntSpeed * 0.25 * dt;
            _y += Math.Cos(_time * 0.17) * HuntSpeed * 0.25 * dt;
        }

        // Nothing may hang off the screen, least of all the bottom, where the virtual
        // desktop carries on but no monitor does.
        _x = Clamp(_x, screen.Left + 8, screen.Right - PanelWidth - 8);
        _y = Clamp(_y, screen.Top + 8, screen.Bottom - PanelHeight - 8);

        Canvas.SetLeft(this, _x);
        Canvas.SetTop(this, _y);
    }

    /// <summary>The artifact currently being chased. Sticks with its choice unless another is
    /// clearly closer, so it does not dither between two equidistant signals.</summary>
    private Point? Quarry(BubbleField field, Rect screen, Point here)
    {
        var best = -1;
        var bestDistance = double.MaxValue;

        for (var i = 0; i < field.Bubbles.Count; i++)
        {
            var a = field.Bubbles[i];

            // Only what is on this detector's own monitor is reachable.
            if (a.X < screen.Left || a.X > screen.Right || a.Y < screen.Top || a.Y > screen.Bottom)
                continue;

            var dx = a.X - here.X;
            var dy = a.Y - here.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        if (best < 0)
        {
            _target = -1;
            return null;
        }

        // Keep the current quarry unless the new candidate is meaningfully nearer.
        if (_target >= 0 && _target < field.Bubbles.Count && _target != best)
        {
            var held = field.Bubbles[_target];
            var hx = held.X - here.X;
            var hy = held.Y - here.Y;
            var heldDistance = Math.Sqrt(hx * hx + hy * hy);

            if (heldDistance <= bestDistance * 1.25 && heldDistance < screen.Width)
            {
                _targetDistance = heldDistance;
                return new Point(held.X, held.Y);
            }
        }

        _target = best;
        _targetDistance = bestDistance;
        var chosen = field.Bubbles[best];
        return new Point(chosen.X, chosen.Y);
    }

    private Point SensorOffsetFrom(double x, double y) =>
        new(x + PanelWidth / 2, y + PanelHeight * 0.25);

    private static double Clamp(double value, double low, double high) =>
        high <= low ? low : Math.Clamp(value, low, high);

    private void Refresh(BubbleField field, Rect screen)
    {
        // The detector reads from its screen, not the middle of the case.
        var here = new Point(_x + PanelWidth / 2, _y + PanelHeight * 0.25);
        SensorPoint = here;

        var width = Math.Max(1, screen.Width);
        var height = Math.Max(1, screen.Height);

        _scope.Blips.Clear();
        _scope.PickupRadius = field.CollectRadius;

        // The outer ring reaches most of the way across the monitor, so nearly everything on
        // screen has a blip while the ones nearby still have room to read against.
        _scope.RangeWorld = Math.Sqrt(width * width + height * height) * 0.8;

        var raw = 0.0;
        var nearestDistance = double.MaxValue;
        var nearestSkin = -1;
        var onScreen = 0;

        foreach (var a in field.Bubbles)
        {
            var dx = a.X - here.X;
            var dy = a.Y - here.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);

            // Inverse-square-ish, weighted by size. A big artifact up close reads hot.
            raw += a.Radius * a.Radius / Math.Max(180.0, distance * distance);

            // Only what is actually on this monitor belongs on the scope.
            if (a.X >= screen.Left - 20 && a.X <= screen.Right + 20 &&
                a.Y >= screen.Top - 20 && a.Y <= screen.Bottom + 20)
            {
                onScreen++;
                var art = Artifacts.All[a.Skin % Artifacts.Count];
                _scope.Blips.Add((dx, dy, art.Family, Math.Clamp(1 - distance / Range, 0.2, 1)));
            }

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestSkin = a.Skin;
            }
        }

        _smoothedRad += (raw * 0.55 - _smoothedRad) * 0.25;
        _scope.InvalidateVisual();

        // A pickup lights the panel for a moment.
        if (field.Collected != _seenCollected)
        {
            _seenCollected = field.Collected;
            _flashUntil = _time + 0.8;
        }
        var flashing = _time < _flashUntil;

        _header.Text = $"{DateTime.Now:dd.MM/yy} Parametrik";
        _coords.Text = $"WXZ\nParametr:[{onScreen}]\nx   {_x:F1}\ny   {_y:F1}";

        if (nearestSkin >= 0)
        {
            var art = Artifacts.All[nearestSkin % Artifacts.Count];
            _nearest.Text = $"\n{art.Name.ToUpperInvariant()}\n{nearestDistance * MetresPerDip:F0} m {Abbrev(art.Family)}";
        }

        _bright.Text = _smoothedRad.ToString("F2", CultureInfo.InvariantCulture) + " mSv";

        var lastName = field.LastCollectedSkin >= 0
            ? Artifacts.All[field.LastCollectedSkin % Artifacts.Count].Name.ToUpperInvariant()
            : "-";
        _tiny.Text = $"ZIBRANO {field.Collected}\n{lastName}";

        var lit = flashing ? 10 : (int)Math.Clamp(Math.Round(_smoothedRad * 3.5), 0, 10);
        for (var i = 0; i < _leds.Count; i++)
            _leds[i].Fill = i < lit ? LedOn : LedOff;

        _lamp.Fill = flashing || nearestDistance < Range * 0.3 ? LampOn : LampOff;

        var key = nearestDistance switch
        {
            < 90 => 0,
            < 220 => 1,
            < 450 => 2,
            < 800 => 3,
            _ => 4,
        };
        for (var i = 0; i < _keys.Count; i++)
            _keys[i].Background = KeyBrush(i == key);
    }

    private static string Abbrev(Anomaly family) => family switch
    {
        Anomaly.Chemical => "CHM",
        Anomaly.Electrical => "ELE",
        Anomaly.Thermic => "THM",
        _ => "GRV",
    };
}
