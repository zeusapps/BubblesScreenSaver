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
