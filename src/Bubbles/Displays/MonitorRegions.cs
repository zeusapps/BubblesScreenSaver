using System.Windows;

namespace Bubbles.Displays;

/// <summary>How much of a full-desktop layer belongs to each monitor.
///
/// The overlay is one window stretched over the virtual desktop, so every layer inside it is
/// laid out against the union of the screens -- a rectangle no single monitor resembles. A
/// laptop panel beside a larger external is the case that shows it: dealing evenly by monitor
/// gave both the same number of artifacts, which is roughly four times the density on the small
/// one, and a fixed total meant plugging in a second screen halved the density on the first.
///
/// Everything here is pure arithmetic on rectangles already converted to field coordinates
/// (DIP) by the caller, so it can be asserted without a window or a display.</summary>
internal static class MonitorRegions
{
    /// <summary>The range a derived total is held to. The same range <see cref="Settings.Clamped"/>
    /// holds the density itself to, since both are counts of things on screen.</summary>
    private const int MinTotal = 1;
    private const int MaxTotal = 400;

    private static double BaselineArea => Settings.BaselineWidth * Settings.BaselineHeight;

    /// <summary>Whether two region lists describe the same desktop.
    ///
    /// The layout is re-derived on every <c>DisplaySettingsChanged</c>, and most of those change
    /// nothing -- a resolution set to what it already was, a monitor waking. Consumers re-deal,
    /// re-seed and re-place on a change, so telling the two cases apart is what keeps a spurious
    /// display event from visibly disturbing the screen.</summary>
    public static bool Same(IReadOnlyList<Rect> a, IReadOnlyList<Rect> b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a.Count != b.Count) return false;

        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i])
                return false;

        return true;
    }

    /// <summary>A region's area in square DIP, floored at zero so a degenerate rectangle
    /// contributes nothing rather than subtracting.</summary>
    public static double AreaOf(Rect region) =>
        Math.Max(0, region.Width) * Math.Max(0, region.Height);

    public static double TotalArea(IReadOnlyList<Rect> regions)
    {
        var total = 0.0;
        foreach (var region in regions) total += AreaOf(region);
        return total;
    }

    /// <summary>How many artifacts a desktop of these regions carries, given the per-baseline-screen
    /// density from settings.
    ///
    /// Clamped, because area-derived counts grow without bound as monitors are added and the render
    /// loop is not free. A clamp that bites is logged rather than silent: "I asked for more bubbles
    /// and nothing happened" is otherwise unexplainable from the outside.</summary>
    public static int DerivedTotal(int density, IReadOnlyList<Rect> regions)
    {
        var area = TotalArea(regions);

        // No layout yet, or a collapsed window. One baseline screen is the honest assumption.
        if (area <= 0) return Math.Clamp(density, MinTotal, MaxTotal);

        var wanted = (int)Math.Round(density * area / BaselineArea, MidpointRounding.AwayFromZero);
        var total = Math.Clamp(wanted, MinTotal, MaxTotal);

        if (total != wanted)
            Diagnostics.Log($"artifact count clamped: wanted {wanted} for {area / BaselineArea:N2} " +
                            $"baseline screens at density {density}, using {total}");

        return total;
    }

    /// <summary>Deals a total across the regions in proportion to their areas.
    ///
    /// Largest remainder rather than rounding each share independently: independent rounding does
    /// not sum back to the total, and being one artifact out is a bubble that either never spawns
    /// or spawns on no screen.
    ///
    /// Every region with real area gets at least one, so a small secondary monitor is never left
    /// empty by rounding. That guarantee cannot hold when there are more screens than artifacts to
    /// go round; there the largest screens are served and the rest stay empty.</summary>
    public static int[] Split(int total, IReadOnlyList<Rect> regions)
    {
        var counts = new int[regions.Count];
        if (regions.Count == 0 || total <= 0) return counts;

        var areas = new double[regions.Count];
        for (var i = 0; i < regions.Count; i++) areas[i] = AreaOf(regions[i]);

        var area = 0.0;
        foreach (var a in areas) area += a;

        // Nothing has area: the split is meaningless, so it all goes to the first region rather
        // than nowhere.
        if (area <= 0)
        {
            counts[0] = total;
            return counts;
        }

        var dealt = 0;
        var remainders = new (int Index, double Fraction)[regions.Count];

        for (var i = 0; i < regions.Count; i++)
        {
            var exact = total * areas[i] / area;
            counts[i] = (int)Math.Floor(exact);
            dealt += counts[i];
            remainders[i] = (i, exact - counts[i]);
        }

        Array.Sort(remainders, (a, b) => b.Fraction.CompareTo(a.Fraction));

        for (var i = 0; dealt < total; i++)
        {
            counts[remainders[i % remainders.Length].Index]++;
            dealt++;
        }

        LiftEmptyRegions(counts, areas);
        return counts;
    }

    /// <summary>Moves one artifact from the most crowded region to each region that has area but
    /// was rounded down to nothing. Conserves the total, so the caller's count still holds.</summary>
    private static void LiftEmptyRegions(int[] counts, double[] areas)
    {
        for (var i = 0; i < counts.Length; i++)
        {
            if (counts[i] > 0 || areas[i] <= 0) continue;

            var donor = -1;
            for (var j = 0; j < counts.Length; j++)
                if (counts[j] > 1 && (donor < 0 || counts[j] > counts[donor]))
                    donor = j;

            // More screens than artifacts. Nothing left to give without emptying another screen.
            if (donor < 0) return;

            counts[donor]--;
            counts[i]++;
        }
    }

    /// <summary>The density that reproduces a total written under the old meaning, on the layout
    /// present when the conversion runs.
    ///
    /// The inverse of <see cref="DerivedTotal"/>, so that upgrading on the desk the count was
    /// tuned on leaves the picture alone and the new meaning only shows itself when the layout
    /// later changes.
    ///
    /// Inverse only as far as an integer allows. On a desktop five baseline screens in area,
    /// one step of density is five artifacts, so a stored 22 comes back as 20 -- the nearest
    /// reachable total, not the same one. Making the density fractional would fix that and cost
    /// a hand-editable settings file a number nobody can reason about, which is a bad trade for
    /// two artifacts.</summary>
    public static int DensityFor(int total, IReadOnlyList<Rect> regions)
    {
        var area = TotalArea(regions);
        if (area <= 0) return Math.Clamp(total, MinTotal, MaxTotal);

        var density = (int)Math.Round(total * BaselineArea / area, MidpointRounding.AwayFromZero);
        return Math.Clamp(density, MinTotal, MaxTotal);
    }
}
