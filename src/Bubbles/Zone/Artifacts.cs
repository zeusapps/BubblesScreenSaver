using System.Windows.Media;

namespace Bubbles.Zone;

/// <summary>The four anomaly families the Zone sorts its artifacts into.</summary>
public enum Anomaly
{
    Chemical,
    Electrical,
    Thermic,
    Gravitational,
}

/// <summary>Silhouette archetypes. These decide how the outline is built and how it moves,
/// which is most of what stops every artifact looking like the same ball.</summary>
public enum ArtifactShape
{
    /// <summary>Soft and drippy, like something half-molten.</summary>
    Blob,

    /// <summary>Radial spines.</summary>
    Spiky,

    /// <summary>Lumpy, welded together out of several masses.</summary>
    Chunky,

    /// <summary>Angular, faceted, mineral.</summary>
    Shard,

    /// <summary>A ring: matter wrapped around a hole.</summary>
    Coil,

    /// <summary>A small body throwing out long needles. Several of the real ones are
    /// essentially a frozen burst.</summary>
    Starburst,

    /// <summary>A C, curled around empty space.</summary>
    Crescent,

    /// <summary>Several masses fused into one lumpy whole.</summary>
    Cluster,

    /// <summary>Small bodies strung along stems.</summary>
    Beads,
}

/// <param name="Name">Shown on the detector readout.</param>
/// <param name="Family">Decides the interior treatment and its animation.</param>
/// <param name="Shape">Decides the outline.</param>
/// <param name="Core">The part that emits.</param>
/// <param name="Shell">The body and its rim.</param>
/// <param name="Halo">The glow it throws into the air.</param>
public sealed record Artifact(
    string Name,
    Anomaly Family,
    ArtifactShape Shape,
    Color Core,
    Color Shell,
    Color Halo);

/// <summary>The artifact roster.
///
/// Palettes and families follow the artifact spreads in the official S.T.A.L.K.E.R. 2
/// artbook: chemical ones are acid greens and oily teals, electrical ones are deep blue
/// around a white-hot centre, thermic ones are dark crusts with molten light in the cracks,
/// and gravitational ones are muted, twisted and light-swallowing. Every one of them is
/// drawn procedurally at runtime -- see <see cref="ArtifactVisual"/>.</summary>
public static class Artifacts
{
    public static readonly Artifact[] All =
    {
        // -- chemical: acid and oil --------------------------------------------------------
        new("Slime",      Anomaly.Chemical, ArtifactShape.Blob,
            Rgb(0xC6, 0xFF, 0x8A), Rgb(0x5F, 0xA8, 0x4A), Rgb(0x2A, 0x5E, 0x28)),
        new("Slug",       Anomaly.Chemical, ArtifactShape.Chunky,
            Rgb(0xA8, 0xFF, 0xE0), Rgb(0x3E, 0x9C, 0x8E), Rgb(0x1C, 0x54, 0x50)),
        new("Bubble",     Anomaly.Chemical, ArtifactShape.Coil,
            Rgb(0xDA, 0xFF, 0xF4), Rgb(0x74, 0xC0, 0xB4), Rgb(0x2E, 0x62, 0x60)),
        new("Soul",       Anomaly.Chemical, ArtifactShape.Cluster,
            Rgb(0xB4, 0xFF, 0xB0), Rgb(0x4C, 0x9E, 0x58), Rgb(0x22, 0x56, 0x30)),

        // -- electrical: cold blue around a white core ---------------------------------------
        new("Flash",      Anomaly.Electrical, ArtifactShape.Starburst,
            Rgb(0xEE, 0xF6, 0xFF), Rgb(0x5E, 0x8E, 0xD8), Rgb(0x24, 0x44, 0x8E)),
        new("Moonlight",  Anomaly.Electrical, ArtifactShape.Blob,
            Rgb(0xD6, 0xE6, 0xFF), Rgb(0x46, 0x74, 0xC4), Rgb(0x1C, 0x32, 0x76)),
        new("Battery",    Anomaly.Electrical, ArtifactShape.Chunky,
            Rgb(0xBC, 0xE0, 0xFF), Rgb(0x38, 0x62, 0xB0), Rgb(0x16, 0x2A, 0x64)),
        new("Sparkler",   Anomaly.Electrical, ArtifactShape.Starburst,
            Rgb(0xFF, 0xFF, 0xFF), Rgb(0x6E, 0xA6, 0xE8), Rgb(0x2A, 0x50, 0xA0)),

        // -- thermic: dark crust, molten inside ----------------------------------------------
        new("Fireball",   Anomaly.Thermic, ArtifactShape.Cluster,
            Rgb(0xFF, 0x9A, 0x2E), Rgb(0x4A, 0x1E, 0x10), Rgb(0x9E, 0x38, 0x0C)),
        new("Crystal",    Anomaly.Thermic, ArtifactShape.Shard,
            Rgb(0xFF, 0xC8, 0x6A), Rgb(0x54, 0x2A, 0x18), Rgb(0xA8, 0x4E, 0x14)),
        new("Droplets",   Anomaly.Thermic, ArtifactShape.Beads,
            Rgb(0xFF, 0x6E, 0x36), Rgb(0x3E, 0x18, 0x0E), Rgb(0x8E, 0x2A, 0x0A)),
        new("Kolobok",    Anomaly.Thermic, ArtifactShape.Blob,
            Rgb(0xFF, 0xB4, 0x4A), Rgb(0x5A, 0x30, 0x14), Rgb(0xB0, 0x56, 0x10)),

        // -- gravitational: mangled matter, muted and heavy ------------------------------------
        new("Gravi",      Anomaly.Gravitational, ArtifactShape.Shard,
            Rgb(0x10, 0x10, 0x14), Rgb(0xC8, 0xB4, 0x84), Rgb(0x40, 0x38, 0x24)),
        new("Night Star", Anomaly.Gravitational, ArtifactShape.Spiky,
            Rgb(0x14, 0x10, 0x24), Rgb(0xA8, 0x9A, 0xD8), Rgb(0x3A, 0x2E, 0x62)),
        new("Goldfish",   Anomaly.Gravitational, ArtifactShape.Crescent,
            Rgb(0x1A, 0x16, 0x10), Rgb(0xD2, 0xA8, 0x54), Rgb(0x4A, 0x3A, 0x18)),
        new("Weird Ball", Anomaly.Gravitational, ArtifactShape.Cluster,
            Rgb(0x18, 0x18, 0x1A), Rgb(0x9E, 0x9A, 0x90), Rgb(0x38, 0x36, 0x30)),
    };

    public static int Count => All.Length;

    private static Color Rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}

/// <summary>The colour each anomaly family lends to the weather drifting past it.
///
/// Derived from the roster rather than declared beside it. The palettes in
/// <see cref="Artifacts.All"/> were chosen once, and a second set of family colours somewhere
/// else is a second set to keep in step -- so the tint is the average of what the family's own
/// artifacts already emit.
///
/// Averaging four palettes washes the hue out: a family whose members are all pale variations
/// of one colour averages to something very close to white, which against a dark desktop is
/// indistinguishable from the untinted grey sheet. So the average is pulled back out to a
/// saturation floor, which changes how strongly the colour reads without changing which colour
/// it is.</summary>
public static class AnomalyTint
{
    /// <summary>The least saturation a tint may have. Below this the four families are the same
    /// off-white and the tint stops being information.</summary>
    private const double Saturation = 0.45;

    private static readonly Dictionary<Anomaly, Color> Tints = Build();

    /// <summary>The tint for one family.</summary>
    public static Color Of(Anomaly family) => Tints[family];

    /// <summary>The family an artifact index belongs to, wrapped the same way the field wraps
    /// its skins.</summary>
    public static Anomaly FamilyOf(int skin) =>
        Artifacts.All[((skin % Artifacts.Count) + Artifacts.Count) % Artifacts.Count].Family;

    private static Dictionary<Anomaly, Color> Build()
    {
        var tints = new Dictionary<Anomaly, Color>();

        foreach (var family in Enum.GetValues<Anomaly>())
        {
            double r = 0, g = 0, b = 0;
            var n = 0;

            foreach (var artifact in Artifacts.All)
            {
                if (artifact.Family != family) continue;

                // Gravitational artifacts are dark bodies with pale shells: their cores are
                // near-black and would tint nothing at all. The shell is the part of one of
                // those that is actually visible, so it is the part that lends its colour.
                //
                // Two of its four shells are gold, so the average comes out a muted tan rather
                // than the violet the proposal imagined. Taking Night Star's violet shell alone
                // was tried and is worse: violet lands closer to Electrical's blue-white than
                // the tan lands to Thermic's amber, so the change of hue costs more separation
                // than it buys. Thermic and Gravitational are the tightest pair either way, and
                // they are told apart by saturation -- a bright amber against a dull tan.
                var source = family == Anomaly.Gravitational ? artifact.Shell : artifact.Core;

                r += source.R;
                g += source.G;
                b += source.B;
                n++;
            }

            if (n == 0) continue;

            tints[family] = Saturate(Color.FromRgb(
                (byte)Math.Round(r / n),
                (byte)Math.Round(g / n),
                (byte)Math.Round(b / n)));
        }

        return tints;
    }

    /// <summary>Pulls a colour out to the saturation floor, away from its brightest channel.
    /// Value is left where it was, so a tint is never made lighter or darker than the palette
    /// it came from -- only more itself.</summary>
    private static Color Saturate(Color c)
    {
        double max = Math.Max(c.R, Math.Max(c.G, c.B));
        double min = Math.Min(c.R, Math.Min(c.G, c.B));

        if (max <= 0) return c;

        var saturation = (max - min) / max;
        if (saturation <= 0 || saturation >= Saturation) return c;

        var pull = Saturation / saturation;

        return Color.FromRgb(Channel(c.R), Channel(c.G), Channel(c.B));

        byte Channel(byte v) => (byte)Math.Clamp(Math.Round(max - (max - v) * pull), 0, 255);
    }
}
