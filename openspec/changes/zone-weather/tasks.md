## 1. Emission lightning

- [x] 1.1 Replace the fixed `Strikes` array length with a schedule built until it passes the Emission's darkness time
- [x] 1.2 Scale the gap curve to `1.05 - 0.74 * progress` so 27 strikes fall before darkness
- [x] 1.3 Assert the derived schedule directly: 27 strikes before 12.5s, at most one after it
- [x] 1.4 Assert the schedule still terminates correctly if the darkness time is changed
- [ ] 1.5 Watch a full Emission and confirm the storm reads as denser rather than as faster — NEEDS YOU: a judgement about how it feels

## 2. Weather layer scaffolding

- [x] 2.1 Add the weather state type covering clear, fog, rain and rain with lightning
- [x] 2.2 Add the weather layer element, region-aware via the `Regions` property from `per-monitor-layers`
- [x] 2.3 Place it above `_canvas` and below `_flash` in the z-order
- [x] 2.4 Give it a resting value per stage in `LayerRest` and wire it into `SettleLayers`
- [x] 2.5 Settle it to zero and stop its animations at black, alongside `HideLightning`
- [x] 2.6 Confirm the scene is unchanged with the layer present and pinned to clear

## 3. Settings and tray

- [x] 3.1 Add the weather setting, documented as Zone theme only, defaulting on
- [x] 3.2 Add the tray toggle beside the existing Zone toggles
- [x] 3.3 Make weather stop and start immediately when toggled while the artifacts are showing
- [x] 3.4 Confirm no weather work runs at all with the setting off, or in the Soap theme

## 4. Fog

- [x] 4.1 Build the fog blobs as frozen gradient brushes on a canvas, count derived from region area
- [x] 4.2 Drift them with slow per-blob `TranslateTransform` animations
- [x] 4.3 Pre-bake the intensity levels rather than animating layer opacity
- [x] 4.4 Cap fog intensity so the artifacts stay legible through it
- [x] 4.5 Compare density on a mismatched monitor pair

## 5. Rain

- [x] 5.1 Build the streak `DrawingBrush` and tile it, streak density derived from region area
- [x] 5.2 Scroll it diagonally with a `TranslateTransform` animation
- [x] 5.3 Add two or three tile scales at different speeds for parallax and to mask the repeat
- [x] 5.4 Pre-bake the intensity levels
- [x] 5.5 Check the tiling repeat is not obvious on the widest desktop available

## 6. Ambient storm lightning

- [x] 6.1 Add an ambient mode to `LightningLayer` with a continuous schedule rather than one anchored to an Emission start
- [x] 6.2 Make ambient strikes fewer and weaker, weighted towards wash over bolt
- [x] 6.3 Let an Emission take the layer over outright for its duration, and hand it back after
- [x] 6.4 Confirm a storm is not mistaken for an Emission starting — confirmed: the two are never confusable because an Emission burns the sky red. The colour tells them apart before the strikes do, so the ambient bolt only has to be weaker, not unrecognisable

## 7. The cycle

- [x] 7.1 Add the weather clock: nominal 60s dwell with +/-25% jitter
- [x] 7.2 Add the weighted roll excluding the current state, weights clear 35 / fog 25 / rain 25 / storm 15
- [x] 7.3 Add the 6 second cross-fade driving outgoing and incoming intensity levels
- [x] 7.4 Hold at most two states live, and release the outgoing slot when a transition ends
- [x] 7.5 Assert selection and dwell directly, with no window: never repeats, dwell within 45..75s, all four states occur over a long run

## 8. Emission interaction

- [x] 8.1 Suspend the cycle for the duration of an Emission and resume after
- [x] 8.2 Let a transition already in flight when an Emission begins finish
- [x] 8.3 Fade fog out over the buildup and bring it back after, leaving precipitation running
- [ ] 8.4 Watch an Emission begin under each of the four states — NEEDS YOU: four Emissions, one per state

## 9. Cost

- [ ] 9.1 Measure the artifacts stage with rain showing against the same stage with weather off, on the widest desktop available — BLOCKED: the widest desktop here is one screen
- [x] 9.2 Confirm precipitation motion comes from transforms, with no per-frame layer redraw
- [x] 9.3 Confirm weather stops entirely when `Suspended` or blacked out — `Suspended` needed a setter: OnRendering returns before any tick, so the compositor kept scrolling into a dark panel

## 10. Export and verification

- [x] 10.1 Add an export strip showing the four states and a mid-transition frame
- [x] 10.2 Confirm the weather layer renders with no regions, as a single screen, for the offline renderers
- [ ] 10.3 Run for an evening and revisit the weights, the dwell and the fog cap
