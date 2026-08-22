## Why

The Zone's sky has exactly two states: calm, or an Emission. Everything between the
artifacts arriving and the screen going black looks the same every night. The setting
is a place with weather in it, and the overlay never shows any.

Separately, the Emission's storm is thinner than it was meant to be. Of the 22 strikes
`LightningLayer` schedules, only 18 land before the screen reaches black at 12.5s -- the
last four are scheduled into darkness and have never been seen by anyone.

## What Changes

- A weather system runs while the artifacts are on screen, cycling between four states:
  **clear**, **fog**, **rain**, and **rain with lightning**.
- Weather is chosen at random, re-rolled roughly once a minute, and moves between states
  by cross-fade rather than by cutting.
- Weather is one state for the whole desktop -- it is one sky -- while its density is
  derived per monitor from the region model that `per-monitor-layers` introduces.
- The Emission carries 50% more lightning: 27 strikes reach the screen instead of 18.
  This comes from tightening the interval between strikes, **not** from raising the
  strike count -- raising the count alone adds strikes after the screen is already
  black and changes nothing visible. **BREAKING** for nothing user-facing; the strike
  schedule is internal.
- The strike schedule is built until it runs past the end of the Emission rather than
  to a fixed number, so strikes are never again scheduled into darkness.
- A tray toggle turns weather off, alongside the existing Zone toggles.

Explicitly out of scope: weather in the Soap theme, weather affecting artifact
behaviour or the detector, and any change to the Emission's timeline or palette.

## Capabilities

### New Capabilities

- `zone-weather`: The ambient weather of the Zone theme -- which states exist, how one
  is chosen and how long it lasts, how states blend into one another, how weather
  behaves around an Emission and a blackout, and how densely each state renders.

### Modified Capabilities

None. `per-monitor-layers` states that every region receives the same number of strikes
as a single-screen desktop would, which stays true when both figures rise together;
`idle-hold-off` and `overlay-transparency` are untouched.

## Impact

- **Depends on `per-monitor-layers` being implemented first.** Weather density is
  per-region, and building it against the union rectangle would repeat the defect that
  change exists to fix.
- `src/Bubbles/Zone/` -- a new weather layer, a weather state machine, and the fog and
  rain renderers.
- `src/Bubbles/Zone/LightningLayer.cs` -- the gap curve that sets strike spacing, the
  schedule's termination condition, and an ambient mode for the storm weather state that
  is quieter than an Emission.
- `src/Bubbles/Overlay/OverlayWindow.cs` -- the new layer's place in the z-order, its
  resting opacity per stage, its suspension during a blackout, and the weather clock.
- `src/Bubbles/Overlay/LayerRest.cs` -- a resting value for the weather layer per stage.
- `src/Bubbles/Settings.cs` and `src/Bubbles/Session/TrayIcon.cs` -- the weather toggle.
- `src/Bubbles/Export.cs` -- a strip showing the four weather states.
- Continuous per-frame cost: unlike lightning, weather is on screen the whole time the
  artifacts are, so its rendering has to be near-free rather than merely affordable.
