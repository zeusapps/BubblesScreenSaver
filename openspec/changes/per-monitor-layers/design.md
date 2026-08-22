## Context

The overlay is a single borderless window stretched over the whole virtual desktop
(`OverlayWindow.StretchOverVirtualDesktop`). Everything inside it is laid out against
that one rectangle, and the union of several monitors is not a shape any single
monitor resembles.

`UpdateRegions()` already derives the piece that was missing: it converts each
`Screen.Bounds` into field coordinates (DIP) using the `pixelsPerDip` ratio between
the window's physical span and its WPF width, and produces a `List<Rect>` of screens.
Today those regions reach exactly two consumers -- `BubbleField.SetRegions` and the
detector's home screen. Every other layer ignores them:

- `BubbleField` receives the regions but only uses them to *bind* a bubble to a screen
  and keep it inside; the split itself is `i % _regions.Count`, so screens get equal
  counts regardless of area, and `_settings.BubbleCount` caps the total across all of
  them.
- `LightningLayer` is one `FrameworkElement` filling the union. Bolt x-positions span
  `0.06..0.94` of the union width, and bolt reach, step deviation and fork spread are
  all multiples of the union *height* -- which is the tallest monitor, not the one the
  bolt happens to land on.
- `_emission` and `_flash` are plain `Rectangle`s filled with vertical
  `LinearGradientBrush`es (`EmissionSkyBrush`, `ShockwaveLightBrush`). The 0..1 ramp
  covers the union height, so a monitor occupying the lower half of the union sees
  only the bottom half of the sky and never the crimson at the top.

Constraint that shapes everything below: the window must stay a single window with a
single render surface. It is DWM-composited with `AllowsTransparency = false`
specifically to keep GPU rendering, it re-asserts topmost and glass on a timer, and it
collapses to 1x1 when hidden to release the surface. Splitting into one window per
monitor would multiply all of that machinery and is out of scope.

## Goals / Non-Goals

**Goals:**

- Each monitor shows a self-consistent scene: the same artifact density per unit area,
  bolts sized for that screen, and the sky's horizon at the same relative height.
- One shared definition of "the monitor regions", consumed by every full-desktop layer
  rather than re-derived per layer.
- A layer given no regions still renders correctly against its own bounds, so the
  offline renderers in `Export.cs` keep working untouched.
- The region model is ready to carry per-area density for layers that do not exist yet
  (fog, rain), since that is why this change comes first.

**Non-Goals:**

- One window per monitor.
- Changing which screen the detector lives on. It is deliberately pinned and stays so.
- Any new weather type, any change to the number of strikes *per screen*, or any change
  to emission timing. Those belong to `zone-weather`.
- Handling the parts of the virtual desktop that no monitor covers, beyond what already
  happens (bubbles are already kept inside their region for exactly this reason).

## Decisions

### Regions are pushed into layers as a property, not modelled as one element per screen

Each region-aware layer gets an `IReadOnlyList<Rect> Regions` property in field
coordinates, assigned from `UpdateRegions()`. When the list is empty the layer treats
its own `ActualWidth`/`ActualHeight` as a single region.

Alternative considered: instantiate one layer element per monitor and position them on
a `Canvas`. Rejected on three counts. The opacity plumbing (`Settle`, `SettleLayers`,
`HideLightning`, the emission animations) addresses exactly one element per layer and
would have to fan out to a collection -- and that code already carries scar tissue
about held animation values outranking direct assignment. The visual-tree cost
multiplies per monitor for layers that are empty most of the time. And `Export.cs`
builds `new LightningLayer { Time = ... }` directly inside a fixed-size `Grid`; the
empty-list fallback means those call sites need no change at all.

### The sky and the flash become drawn layers rather than filled rectangles

`_emission` and `_flash` change from `Rectangle` + `LinearGradientBrush` to a small
`FrameworkElement` that draws one gradient rectangle per region in `OnRender`, with the
gradient's 0..1 ramp mapped to that region's own top and bottom.

Alternative considered: keep the `Rectangle` and express the per-screen repeat with a
tiled `DrawingBrush` viewport. Rejected because it only produces the right result when
every monitor has identical bounds and alignment; a laptop panel next to a larger
external is precisely the case that breaks it.

The existing brush factories stay as the single source of the colour ramp, so the sky's
palette is still defined in one place and `Export.cs` continues to call them.

### Density is derived from area against a fixed baseline screen

`Settings.BubbleCount` is reinterpreted as "artifacts on a 1920x1080 screen". The total
becomes `BubbleCount * (total region area / baseline area)`, and that total is dealt to
regions in proportion to each region's area, with a largest-remainder pass so the parts
sum exactly to the total and no region with real area gets zero.

Alternative considered: keep `BubbleCount` an absolute total and only fix the split so
it is area-weighted instead of index-weighted. This avoids any breaking change, and it
does fix the "crowded laptop, sparse external" imbalance -- but it leaves the second
half of the defect in place, where plugging in a monitor thins out the screen you were
already looking at. Since the imbalance and the dilution have the same root cause, the
proposal takes the reinterpretation.

The baseline is a constant, not a setting. It exists to give the stored number a
meaning, and exposing it would be a second knob that only interacts with the first.

### The stored `BubbleCount` is migrated once, against the layout at first run

On first load after upgrade, the stored absolute count is converted into the density
value that reproduces it on the monitor layout present at that moment, and written back.
A user who tuned the count on their current desk keeps the picture they tuned; the new
meaning only shows itself when the layout later changes.

This needs a marker in the settings file to know the migration has run, and `Settings`
currently has no version field, so one is added. Without it the conversion would
re-apply on every launch and compound.

### The derived total is clamped

Area-derived counts grow without bound as monitors are added. The derived total is
clamped to the `1..400` range `Settings` already enforces on `BubbleCount`, and the
clamp is logged through `Diagnostics` when it bites, so a very large desktop degrades to
"fewer than the formula asked for" rather than to a stalled render loop.

### Strike count stays per screen, so each screen looks like today's single screen

`LightningLayer.Strikes` becomes a count *per region*: every monitor schedules its own
22 strikes over its own timeline. A three-monitor desktop therefore sees more bolts in
total than it does today, which is the intent -- today it sees 22 spread across three
screens, i.e. a third of a storm each.

Each region seeds its schedule from its index, so the screens do not flash in lockstep.
The faint full-sky wash that accompanies a strike is drawn only over the region that
struck, since after this change a bolt belongs to a screen.

Note for sequencing: `zone-weather` asks for 50% more lightning. After this change that
increase applies to the per-screen count, not to a figure shared across the desktop.

### Region list identity keeps coming from `Screen.AllScreens` order

`SetRegions` already compares the incoming list against the stored one and re-deals only
when it genuinely differs. That behaviour is kept and extended to the other layers, so a
`DisplaySettingsChanged` that does not actually change the layout costs nothing.

## Risks / Trade-offs

- **[Saved settings change meaning]** -> One-time migration plus a version marker, so an
  existing desk keeps its current picture. A user who edits the file by hand between
  versions gets the new meaning, which is the documented behaviour.

- **[Artifact count balloons on large multi-monitor desks, costing frames]** -> Clamped
  to the existing 400 ceiling and logged. Per-frame cost is already bounded by `MaxFps`
  and the `ArtifactRedrawInterval` stagger.

- **[More bolts in total means more draws per frame]** -> Each bolt now draws its wash
  over one region instead of the union, so a single strike covers less area than it does
  today. `HasStrike` stays the early-out that keeps the layer from redrawing when nothing
  is on screen, and becomes a test across regions.

- **[Re-dealing on a display change teleports artifacts]** -> Already the case today
  (`SetRegions` calls `PlaceInsideRegion` on every bubble). Area-weighted dealing does not
  make it worse, and a display change is already a visible event.

- **[Gradients per region look wrong where monitors are stacked vertically]** -> Each
  screen becomes self-consistent, which is the goal, but two vertically stacked monitors
  will show two horizons rather than one continuous sky. That is the correct trade for
  the common case (side-by-side, mismatched sizes) and is accepted deliberately.

- **[`Export.cs` renders drift from what the app shows]** -> The empty-region fallback
  makes the offline strips render as a single screen, which is what they are meant to
  depict. They will no longer exercise the multi-region path; that is accepted rather
  than adding a multi-monitor mock.

## Migration Plan

1. Add the version marker to `Settings` and the one-time `BubbleCount` conversion in
   `Load()`, behind the marker.
2. Introduce the shared region model and the empty-list fallback, with every layer still
   behaving exactly as it does now when given no regions.
3. Move layers over one at a time -- field, lightning, sky, flash -- each independently
   verifiable on a single-monitor desktop before multi-monitor is considered.
4. Wire `UpdateRegions()` to feed all of them.

Rollback: the change is confined to the overlay's rendering and one settings field. The
version marker means a downgrade reads a file whose `BubbleCount` has the new meaning;
the value stays inside the existing clamp, so an older build renders a different count
rather than failing.

### The density baseline is 1920x1080 in DIP

Regions already arrive in field coordinates, so measuring the baseline in the same units
keeps the whole calculation in one space with no conversion. It also means density follows
what the user sees: raising the scaling on a screen makes everything on it bigger, the
screen holds fewer artifact-sized things, and the count drops to match.

The consequence, accepted deliberately: two physically identical monitors at different
scale factors have different areas in field coordinates and therefore get different
counts. That is the correct outcome under this reading -- the more-scaled screen is
showing a smaller working area -- and is not corrected for.

## Open Questions

None outstanding.
