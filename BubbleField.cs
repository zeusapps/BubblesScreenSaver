using System.Windows;

namespace Bubbles;

public sealed class Bubble
{
    public double X, Y;          // centre, in DIP
    public double VX, VY;        // DIP per second
    public double Radius;        // DIP
    public double Phase;         // where it is in its wobble cycle
    public double PhaseSpeed;    // radians per second
    public double Alpha;         // per-bubble opacity multiplier
    public int Skin;             // index into the current theme's sprites
    public int Region;           // which monitor this one lives on
}

/// <summary>The simulation: bubbles drifting inside their monitor, bouncing off its edges.
///
/// Each bubble is bound to one screen rather than to the whole virtual desktop. Two reasons:
/// the count per screen then stays even instead of drifting into clumps, and a virtual
/// desktop spanning monitors of different heights contains rectangles that are on no
/// physical screen at all -- bubbles that wandered in there simply vanished.</summary>
public sealed class BubbleField
{
    private readonly Random _rng = new();
    private readonly List<Bubble> _bubbles = new();
    private IReadOnlyList<Rect> _regions = Array.Empty<Rect>();
    private readonly List<int> _deck = new();
    private int _deckSkinCount = -1;
    private Settings _settings;

    public BubbleField(Settings settings) => _settings = settings;

    public IReadOnlyList<Bubble> Bubbles => _bubbles;
    public Size Bounds { get; private set; }

    /// <summary>Velocity multiplier. Driven above 1 during an Emission, when the Zone
    /// stops being calm.</summary>
    public double Agitation { get; set; } = 1;

    /// <summary>How many distinct sprites the current theme offers.</summary>
    public int SkinCount { get; set; } = 6;

    /// <summary>Where the detector is, in field coordinates. Artifacts that drift within
    /// <see cref="CollectRadius"/> of it are picked up. Null when no detector is shown.</summary>
    public Point? CollectPoint { get; set; }

    public double CollectRadius { get; private set; } = 60;

    /// <summary>Seconds before the detector can pick up again. Without it, arriving in a
    /// crowded corner collected four artifacts in two seconds, which read as hoovering
    /// rather than as finding something.</summary>
    private const double CollectCooldown = 1.6;

    private double _collectReadyIn;

    /// <summary>How many artifacts the detector has collected this session.</summary>
    public int Collected { get; private set; }

    /// <summary>The kind most recently collected, or -1.</summary>
    public int LastCollectedSkin { get; private set; } = -1;

    /// <summary>Raised when bubbles are added or removed, so the view can rebuild its visuals.</summary>
    public event Action? PopulationChanged;

    /// <summary>The screens, in field coordinates. Bubbles are dealt out between them evenly.</summary>
    public void SetRegions(IReadOnlyList<Rect> regions)
    {
        if (regions.Count == 0 || SameAs(regions)) return;

        _regions = regions.ToArray();

        // Re-deal existing bubbles so the split stays even after a monitor comes or goes.
        for (var i = 0; i < _bubbles.Count; i++)
        {
            _bubbles[i].Region = i % _regions.Count;
            PlaceInsideRegion(_bubbles[i]);
        }
    }

    private bool SameAs(IReadOnlyList<Rect> other)
    {
        if (_regions.Count != other.Count) return false;

        for (var i = 0; i < other.Count; i++)
            if (_regions[i] != other[i])
                return false;

        return true;
    }

    /// <summary>Deals the next artifact kind from a shuffled deck rather than rolling a die.
    /// Independent rolls put obvious duplicates on screen constantly -- with sixteen kinds and
    /// twenty-two artifacts a collision is a certainty -- whereas dealing without replacement
    /// shows every kind once before any kind repeats.</summary>
    private int NextSkin()
    {
        var count = Math.Max(1, SkinCount);

        if (_deckSkinCount != count)
        {
            _deckSkinCount = count;
            _deck.Clear();
        }

        if (_deck.Count == 0)
        {
            for (var i = 0; i < count; i++) _deck.Add(i);

            for (var i = _deck.Count - 1; i > 0; i--)
            {
                var j = _rng.Next(i + 1);
                (_deck[i], _deck[j]) = (_deck[j], _deck[i]);
            }
        }

        var skin = _deck[^1];
        _deck.RemoveAt(_deck.Count - 1);
        return skin;
    }

    private Rect RegionOf(Bubble b) =>
        _regions.Count == 0
            ? new Rect(0, 0, Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height))
            : _regions[b.Region % _regions.Count];

    public void Apply(Settings settings)
    {
        _settings = settings;
        CollectRadius = settings.CollectRadius;

        foreach (var b in _bubbles)
        {
            // Re-roll anything the user may have just changed, keeping positions intact.
            b.Radius = Math.Clamp(b.Radius, _settings.MinRadius, _settings.MaxRadius);
            if (b.Skin >= SkinCount) b.Skin = NextSkin();
            RescaleVelocity(b);
        }

        Resize(Bounds);
    }

    public void Resize(Size bounds)
    {
        Bounds = bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        var changed = false;

        while (_bubbles.Count > _settings.BubbleCount)
        {
            _bubbles.RemoveAt(_bubbles.Count - 1);
            changed = true;
        }

        while (_bubbles.Count < _settings.BubbleCount)
        {
            // Round-robin, so two screens always differ by at most one bubble.
            _bubbles.Add(Spawn(_bubbles.Count % Math.Max(1, _regions.Count)));
            changed = true;
        }

        foreach (var b in _bubbles) ClampIntoRegion(b);

        if (changed) PopulationChanged?.Invoke();
    }

    public void Update(double dt)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;

        // Clamp dt so a stalled frame (debugger break, monitor wake) doesn't teleport everything.
        dt = Math.Min(dt, 0.1);

        if (_collectReadyIn > 0) _collectReadyIn -= dt;

        foreach (var b in _bubbles)
        {
            var area = RegionOf(b);

            // Close enough to the detector counts as picked up: it vanishes and a fresh one
            // wanders in from an edge, the way the Zone keeps restocking itself.
            if (CollectPoint is { } pickup && CollectRadius > 0 && _collectReadyIn <= 0)
            {
                var gx = b.X - pickup.X;
                var gy = b.Y - pickup.Y;
                var reach = CollectRadius + b.Radius * 0.35;

                if (gx * gx + gy * gy < reach * reach)
                {
                    Collected++;
                    LastCollectedSkin = b.Skin;
                    _collectReadyIn = CollectCooldown;
                    Diagnostics.Log($"collected {Artifacts.All[b.Skin % Artifacts.Count].Name} " +
                                    $"(total {Collected})");
                    RespawnAtEdge(b);
                    continue;
                }
            }

            b.VY -= _settings.Buoyancy * dt;

            b.X += b.VX * dt * Agitation;
            b.Y += b.VY * dt * Agitation;
            b.Phase += b.PhaseSpeed * dt;

            if (b.X - b.Radius < area.Left)
            {
                b.X = area.Left + b.Radius;
                b.VX = Math.Abs(b.VX);
            }
            else if (b.X + b.Radius > area.Right)
            {
                b.X = area.Right - b.Radius;
                b.VX = -Math.Abs(b.VX);
            }

            if (b.Y - b.Radius < area.Top)
            {
                // With buoyancy on, a bubble that reaches the ceiling is reborn at the floor
                // instead of bouncing -- that reads as "popped and replaced".
                if (_settings.Buoyancy > 0) Respawn(b, atBottom: true);
                else { b.Y = area.Top + b.Radius; b.VY = Math.Abs(b.VY); }
            }
            else if (b.Y + b.Radius > area.Bottom)
            {
                if (_settings.Buoyancy < 0) Respawn(b, atBottom: false);
                else { b.Y = area.Bottom - b.Radius; b.VY = -Math.Abs(b.VY); }
            }
        }
    }

    private Bubble Spawn(int region)
    {
        var b = new Bubble
        {
            Region = region,
            Radius = Lerp(_settings.MinRadius, _settings.MaxRadius, Math.Pow(_rng.NextDouble(), 1.6)),
            Phase = _rng.NextDouble() * Math.Tau,
            PhaseSpeed = Lerp(0.35, 1.5, _rng.NextDouble()) * (_rng.Next(2) == 0 ? 1 : -1),
            Alpha = Lerp(0.55, 1.0, _rng.NextDouble()),
            Skin = NextSkin(),
        };

        PlaceInsideRegion(b);
        RescaleVelocity(b);
        return b;
    }

    private void PlaceInsideRegion(Bubble b)
    {
        var area = RegionOf(b);
        var right = Math.Max(area.Left + b.Radius, area.Right - b.Radius);
        var bottom = Math.Max(area.Top + b.Radius, area.Bottom - b.Radius);

        b.X = Lerp(area.Left + b.Radius, right, _rng.NextDouble());
        b.Y = Lerp(area.Top + b.Radius, bottom, _rng.NextDouble());
    }

    private void ClampIntoRegion(Bubble b)
    {
        var area = RegionOf(b);
        var right = Math.Max(area.Left + b.Radius, area.Right - b.Radius);
        var bottom = Math.Max(area.Top + b.Radius, area.Bottom - b.Radius);

        b.X = Math.Clamp(b.X, area.Left + b.Radius, right);
        b.Y = Math.Clamp(b.Y, area.Top + b.Radius, bottom);
    }

    private void Respawn(Bubble b, bool atBottom)
    {
        var area = RegionOf(b);

        b.Radius = Lerp(_settings.MinRadius, _settings.MaxRadius, Math.Pow(_rng.NextDouble(), 1.6));
        b.X = Lerp(area.Left, area.Right, _rng.NextDouble());
        b.Y = atBottom ? area.Bottom + b.Radius : area.Top - b.Radius;
        b.Skin = NextSkin();
        b.Alpha = Lerp(0.55, 1.0, _rng.NextDouble());

        RescaleVelocity(b);
        b.VY = atBottom ? -Math.Abs(b.VY) : Math.Abs(b.VY);
    }

    /// <summary>Sends a replacement in from one edge of the same screen, heading inward.</summary>
    private void RespawnAtEdge(Bubble b)
    {
        var area = RegionOf(b);

        b.Radius = Lerp(_settings.MinRadius, _settings.MaxRadius, Math.Pow(_rng.NextDouble(), 1.6));
        b.Skin = NextSkin();
        b.Alpha = Lerp(0.55, 1.0, _rng.NextDouble());
        b.Phase = _rng.NextDouble() * Math.Tau;
        RescaleVelocity(b);

        switch (_rng.Next(4))
        {
            case 0:
                b.X = area.Left + b.Radius;
                b.Y = Lerp(area.Top + b.Radius, area.Bottom - b.Radius, _rng.NextDouble());
                b.VX = Math.Abs(b.VX);
                break;
            case 1:
                b.X = area.Right - b.Radius;
                b.Y = Lerp(area.Top + b.Radius, area.Bottom - b.Radius, _rng.NextDouble());
                b.VX = -Math.Abs(b.VX);
                break;
            case 2:
                b.X = Lerp(area.Left + b.Radius, area.Right - b.Radius, _rng.NextDouble());
                b.Y = area.Top + b.Radius;
                b.VY = Math.Abs(b.VY);
                break;
            default:
                b.X = Lerp(area.Left + b.Radius, area.Right - b.Radius, _rng.NextDouble());
                b.Y = area.Bottom - b.Radius;
                b.VY = -Math.Abs(b.VY);
                break;
        }
    }

    /// <summary>Gives the bubble a random heading at a speed near Settings.Speed.
    /// Big bubbles are heavier, so they drift a little slower.</summary>
    private void RescaleVelocity(Bubble b)
    {
        var span = _settings.MaxRadius - _settings.MinRadius;
        var heaviness = span <= 0.001 ? 0.5 : (b.Radius - _settings.MinRadius) / span;
        var speed = _settings.Speed
                    * Lerp(1.35, 0.6, heaviness)
                    * Lerp(1 - _settings.SpeedVariance * 0.7, 1 + _settings.SpeedVariance * 0.7, _rng.NextDouble());

        var angle = _rng.NextDouble() * Math.Tau;
        b.VX = Math.Cos(angle) * speed;
        b.VY = Math.Sin(angle) * speed;
    }

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
