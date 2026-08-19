using System.Windows;
using System.Windows.Media;

namespace Bubbles;

/// <summary>Lightning across the sky during an Emission.
///
/// Strikes are scheduled procedurally from the time into the Emission, so they are the same
/// every run without anything being stored: they start sparse, crowd together as the pressure
/// builds, and stop once the wavefront has passed and the sky is collapsing to black.
///
/// Alpha levels are pre-baked, as everywhere else in this app, because varying intensity with
/// PushOpacity forces WPF onto an intermediate surface -- expensive for something that covers
/// the whole desktop.</summary>
internal sealed class LightningLayer : FrameworkElement
{
    private const int Strikes = 22;
    private const int Levels = 8;
    private const double StrikeLength = 0.42;

    private static readonly Color Core = Color.FromRgb(0xFF, 0xF4, 0xE2);
    private static readonly Color Glow = Color.FromRgb(0xBF, 0xD8, 0xFF);

    private static readonly double[] Schedule = BuildSchedule();
    private static readonly Pen[] CorePens = new Pen[Levels];
    private static readonly Pen[] GlowPens = new Pen[Levels];
    private static readonly Brush[] Washes = new Brush[Levels];

    /// <summary>Seconds into the Emission.</summary>
    public double Time { get; set; }

    static LightningLayer()
    {
        for (var i = 0; i < Levels; i++)
        {
            var f = (i + 1.0) / Levels;

            CorePens[i] = FrozenPen(Color.FromArgb((byte)(248 * f), Core.R, Core.G, Core.B), 3);
            GlowPens[i] = FrozenPen(Color.FromArgb((byte)(76 * f), Glow.R, Glow.G, Glow.B), 12);

            // A faint wash, so a strike lifts the whole sky rather than only drawing a line.
            var wash = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            wash.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(58 * f), Glow.R, Glow.G, Glow.B), 0));
            wash.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(14 * f), Glow.R, Glow.G, Glow.B), 0.45));
            wash.GradientStops.Add(new GradientStop(Color.FromArgb(0, Glow.R, Glow.G, Glow.B), 1));
            wash.Freeze();
            Washes[i] = wash;
        }
    }

    private static Pen FrozenPen(Color colour, double thickness)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();

        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }

    private static double Hash(int a, int b)
    {
        var x = Math.Sin(a * 91.7 + b * 47.3) * 24634.6345;
        return x - Math.Floor(x);
    }

    /// <summary>Strike times: sparse to begin with, crowding as the Emission builds, and
    /// finished before the sky goes dark.</summary>
    private static double[] BuildSchedule()
    {
        var times = new double[Strikes];
        var at = 0.9;

        for (var i = 0; i < Strikes; i++)
        {
            times[i] = at;

            // The gap closes as the pressure rises, then eases off after the wavefront.
            var progress = Math.Min(1, at / 8.0);
            var gap = 1.5 - 1.05 * progress;
            at += gap * (0.6 + Hash(i, 7) * 0.8);
        }

        return times;
    }

    /// <summary>Whether anything is on screen at this moment, so the owner can skip redrawing
    /// a layer that has nothing to show.</summary>
    public bool HasStrike(double time)
    {
        foreach (var start in Schedule)
        {
            if (start > time) return false;
            if (time - start <= StrikeLength) return true;
        }

        return false;
    }

    /// <summary>Three quick flashes inside one strike, fading as it goes.</summary>
    private static double Intensity(double age)
    {
        if (age < 0 || age > StrikeLength) return 0;

        var t = age / StrikeLength;
        var flicker = Math.Abs(Math.Sin(t * Math.PI * 3));
        return flicker * Math.Pow(1 - t, 0.8);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        for (var i = 0; i < Schedule.Length; i++)
        {
            var start = Schedule[i];
            if (start > Time) break;

            var intensity = Intensity(Time - start);
            if (intensity < 0.02) continue;

            var level = (int)Math.Clamp(Math.Round(intensity * (Levels - 1)), 0, Levels - 1);
            DrawBolt(dc, i, level, w, h);
        }
    }

    private static void DrawBolt(DrawingContext dc, int seed, int level, double w, double h)
    {
        // Each bolt owns a slice of the desktop, so two strikes never land on top of each other.
        var x = w * (0.06 + Hash(seed, 1) * 0.88);
        var reach = h * (0.55 + Hash(seed, 2) * 0.4);
        var steps = 11 + (int)(Hash(seed, 3) * 5);

        dc.DrawRectangle(Washes[level], null, new Rect(0, 0, w, h * 0.75));

        var at = new Point(x, 0);
        var branchAt = 3 + (int)(Hash(seed, 4) * (steps - 6));

        for (var step = 0; step < steps; step++)
        {
            // Deviation is scaled by height, not width. Scaled by width it looked right on a
            // single screen and became a scribble across a desktop three times as wide.
            var next = new Point(
                at.X + (Hash(seed, step + 20) - 0.5) * h * 0.055,
                at.Y + reach / steps);

            dc.DrawLine(GlowPens[level], at, next);
            dc.DrawLine(CorePens[level], at, next);

            // One fork, partway down, heading off at an angle and dying out quickly.
            if (step == branchAt)
            {
                var fork = next;
                var drift = (Hash(seed, 40) - 0.5) * 2;

                for (var limb = 0; limb < 4; limb++)
                {
                    var tip = new Point(
                        fork.X + drift * h * 0.03 + (Hash(seed, limb + 50) - 0.5) * h * 0.02,
                        fork.Y + reach / steps * 0.8);

                    dc.DrawLine(GlowPens[Math.Max(0, level - 2)], fork, tip);
                    dc.DrawLine(CorePens[Math.Max(0, level - 3)], fork, tip);
                    fork = tip;
                }
            }

            at = next;
        }
    }
}
