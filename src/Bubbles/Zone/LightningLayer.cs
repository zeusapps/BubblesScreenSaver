using System.Windows;
using System.Windows.Media;

using Bubbles.Displays;

namespace Bubbles.Zone;

/// <summary>Lightning across the sky during an Emission.
///
/// Strikes are scheduled procedurally from the time into the Emission, so they are the same
/// every run without anything being stored: they start sparse, crowd together as the pressure
/// builds, and stop once the wavefront has passed and the sky is collapsing to black.
///
/// Every monitor gets its own schedule and its own storm. Spreading one storm across the union
/// of the screens meant each of them saw a fraction of it, and scaling the bolts by the union's
/// height made them the size of the tallest panel rather than the one they landed on. Schedules
/// are seeded by region index so the screens do not flash in lockstep -- separate skies would
/// be wrong, but a desktop-wide strobe is worse.
///
/// Alpha levels are pre-baked, as everywhere else in this app, because varying intensity with
/// PushOpacity forces WPF onto an intermediate surface -- expensive for something that covers
/// the whole desktop.</summary>
internal sealed class LightningLayer : RegionLayer
{
    /// <summary>Strikes per screen, not per desktop.</summary>
    private const int Strikes = 22;

    private const int Levels = 8;
    private const double StrikeLength = 0.42;

    private static readonly Color Core = Color.FromRgb(0xFF, 0xF4, 0xE2);
    private static readonly Color Glow = Color.FromRgb(0xBF, 0xD8, 0xFF);

    private static readonly Pen[] CorePens = new Pen[Levels];
    private static readonly Pen[] GlowPens = new Pen[Levels];
    private static readonly Brush[] Washes = new Brush[Levels];

    private double[][] _schedules = Array.Empty<double[]>();

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
            // Relative gradient coordinates, so it ramps over whichever region it is drawn into.
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

    /// <summary>Schedules depend only on how many screens there are, not on their geometry, so
    /// they survive a resolution change and are rebuilt only when a monitor comes or goes.</summary>
    private double[][] SchedulesFor(int regions)
    {
        if (_schedules.Length == regions) return _schedules;

        var built = new double[regions][];
        for (var i = 0; i < regions; i++) built[i] = BuildSchedule(i);

        _schedules = built;
        return _schedules;
    }


    protected override void OnRegionsChanged() => _schedules = Array.Empty<double[]>();

    /// <summary>Strike times for one screen: sparse to begin with, crowding as the Emission
    /// builds, and finished before the sky goes dark.
    ///
    /// Internal rather than private so the schedules can be compared directly. What matters
    /// about them -- that every screen gets a full storm, and that no two screens get the same
    /// one -- is invisible in a rendered frame and awkward to sample through
    /// <see cref="HasStrike"/>.</summary>
    internal static double[] BuildSchedule(int region)
    {
        var times = new double[Strikes];
        var at = 0.9;

        // Offsetting the jitter by region is what stops two screens striking together.
        var salt = 7 + region * 31;

        for (var i = 0; i < Strikes; i++)
        {
            times[i] = at;

            // The gap closes as the pressure rises, then eases off after the wavefront.
            var progress = Math.Min(1, at / 8.0);
            var gap = 1.5 - 1.05 * progress;
            at += gap * (0.6 + Hash(i, salt) * 0.8);
        }

        return times;
    }

    /// <summary>Whether anything is on screen at this moment, so the owner can skip redrawing
    /// a layer that has nothing to show.</summary>
    public bool HasStrike(double time)
    {
        var regions = RegionsToDraw();
        if (regions.Count == 0) return false;

        foreach (var schedule in SchedulesFor(regions.Count))
        {
            foreach (var start in schedule)
            {
                if (start > time) break;
                if (time - start <= StrikeLength) return true;
            }
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
        var regions = RegionsToDraw();
        if (regions.Count == 0) return;

        var schedules = SchedulesFor(regions.Count);

        for (var r = 0; r < regions.Count; r++)
        {
            var region = regions[r];
            if (region.Width <= 0 || region.Height <= 0) continue;

            var schedule = schedules[r];

            for (var i = 0; i < schedule.Length; i++)
            {
                var start = schedule[i];
                if (start > Time) break;

                var intensity = Intensity(Time - start);
                if (intensity < 0.02) continue;

                var level = (int)Math.Clamp(Math.Round(intensity * (Levels - 1)), 0, Levels - 1);
                DrawBolt(dc, i + r * 101, level, region);
            }
        }
    }

    private static void DrawBolt(DrawingContext dc, int seed, int level, Rect region)
    {
        var w = region.Width;
        var h = region.Height;

        // Each bolt owns a slice of its own screen, so two strikes never land on top of each
        // other and none of them lands on the monitor next door.
        var x = region.Left + w * (0.06 + Hash(seed, 1) * 0.88);
        var reach = h * (0.55 + Hash(seed, 2) * 0.4);
        var steps = 11 + (int)(Hash(seed, 3) * 5);

        dc.DrawRectangle(Washes[level], null, new Rect(region.Left, region.Top, w, h * 0.75));

        var at = new Point(x, region.Top);
        var branchAt = 3 + (int)(Hash(seed, 4) * (steps - 6));

        for (var step = 0; step < steps; step++)
        {
            // Deviation is scaled by height, not width. Scaled by width it looked right on a
            // single screen and became a scribble across a desktop three times as wide. The
            // height is this screen's, not the tallest one's, for the same reason.
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
