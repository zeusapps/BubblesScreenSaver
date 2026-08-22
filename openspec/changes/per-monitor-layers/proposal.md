## Why

Every full-desktop layer in the Zone theme is drawn once across the union of all
monitors, so what you actually see depends on which screen you look at. On a laptop
panel beside a larger external display the artifacts crowd the small screen and thin
out on the big one, the lightning is scaled for the taller monitor, and the burning
sky puts its horizon in a different place on each. The layers were written when the
desktop was assumed to be one rectangle.

This has to land before the Zone theme grows any more full-screen layers: fog, rain
and rain-driven lightning all carry a per-area density, and building them on the
union model would repeat the same defect three more times and then require unpicking.

## What Changes

- Artifact population is distributed across monitors **by area** rather than dealt
  round-robin by monitor index, so density per square inch is even across screens.
- `Settings.BubbleCount` is reinterpreted as a density reference against a baseline
  screen rather than an absolute total, so adding a monitor adds artifacts instead of
  halving the density on the ones already there. **BREAKING** for anyone whose saved
  `BubbleCount` was tuned against a multi-monitor desktop: their total artifact count
  will change on upgrade.
- Lightning is scheduled and drawn **per monitor region**: each screen gets its own
  strike positions and its own bolt geometry scaled to that screen's height, instead
  of bolts spread across the union width and sized by the tallest panel.
- The Emission sky and the shockwave flash ramp their gradients over **each monitor's
  own height**, so the horizon lands at the same relative position on every screen.
- A single shared notion of "monitor regions" is used by all of these, extending the
  per-screen rectangles `UpdateRegions()` already computes for the artifact field and
  the detector.

Explicitly out of scope: the detector keeps its current behaviour of living on one
screen, and no new weather types are introduced here.

## Capabilities

### New Capabilities

- `per-monitor-layers`: How full-desktop visual layers map onto a multi-monitor
  desktop -- how per-screen regions are derived, how element density scales with a
  region's area, and how per-screen geometry and gradients are anchored so that each
  monitor shows a self-consistent scene.

### Modified Capabilities

None. `idle-hold-off` and `overlay-transparency` govern when the overlay appears and
how it composites; neither states requirements about how a layer distributes itself
across screens.

## Impact

- `src/Bubbles/Zone/BubbleField.cs` -- region assignment (currently
  `_bubbles[i].Region = i % _regions.Count`) and the population loop in `Resize`.
- `src/Bubbles/Zone/LightningLayer.cs` -- the static `Schedule` and `Strikes` count
  become per-region; `DrawBolt` takes a region rect rather than the element's full
  width and height.
- `src/Bubbles/Overlay/OverlayWindow.cs` -- `UpdateRegions()` becomes the single
  source of monitor regions for every layer, not just the field and the detector;
  the `_emission` and `_flash` rectangles need per-region fills or a per-region draw.
- `src/Bubbles/Settings.cs` -- the meaning and documented default of `BubbleCount`.
- `src/Bubbles/Export.cs` -- the offline strip renderers construct layers directly and
  assume a single region.
- Saved user settings: existing `BubbleCount` values carry a different meaning after
  upgrade.
