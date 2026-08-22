## 1. Settings migration

- [x] 1.1 Add a schema version field to `Settings` and write it on `Save()`
- [x] 1.2 Redocument `BubbleCount` as artifacts per 1920x1080 baseline screen, keeping the 1..400 clamp
- [x] 1.3 Expose `NeedsDensityMigration` and the conversion arithmetic. The conversion is *applied* where the regions are known (task 3.2), not in `Load()`: field coordinates need the window, and nothing at load time has it
- [x] 1.4 Stamp fresh default settings as already converted so the conversion never runs on them
- [x] 1.5 Assert the conversion is idempotent and that a fresh install is untouched

## 2. Shared region model

- [x] 2.1 Add the region type carrying the list of screen rects in field coordinates, with the area helpers the dealing needs
- [x] 2.2 Add the area-weighted split with a largest-remainder pass, guaranteeing the parts sum to the total and no non-empty region gets zero
- [x] 2.3 Add the derived-total calculation from `BubbleCount`, baseline area and combined region area, clamped to 1..400 and logged through `Diagnostics` when clamped
- [x] 2.4 Assert the split and the total directly, with no window: single baseline screen, two equal screens, a 1:4 pair, a region that rounds to zero, and a desktop that trips the ceiling

## 3. Region-aware layer plumbing

- [x] 3.1 Give the region-aware layers a `Regions` property that falls back to a single region covering `ActualWidth`/`ActualHeight` when empty
- [x] 3.2 Feed the layers from `UpdateRegions()`, alongside the existing `_field.SetRegions` and detector-screen assignment
- [x] 3.3 Keep the existing "same regions, do nothing" short-circuit and extend it to cover the new consumers
- [x] 3.4 Confirm every layer still renders unchanged with an empty region list

## 4. Artifact field

- [x] 4.1 Replace `_bubbles[i].Region = i % _regions.Count` in `SetRegions` with the area-weighted assignment
- [x] 4.2 Drive the population loop in `Resize` from the derived total rather than `_settings.BubbleCount` directly
- [x] 4.3 Re-deal by area on a display change, keeping `PlaceInsideRegion` and the `PopulationChanged` rebuild
- [x] 4.4 Verify density per unit area is even across a mismatched pair, and that connecting a screen leaves the first screen's count alone

## 5. Lightning per region

- [x] 5.1 Make the strike schedule per region, seeded by region index so no two regions share a schedule
- [x] 5.2 Keep the strike count per region equal to today's single-screen count
- [x] 5.3 Take the bolt's x-position, reach, step deviation and fork spread from its own region's rect rather than the union
- [x] 5.4 Draw the strike wash over the striking region only
- [x] 5.5 Extend `HasStrike` to test across regions so the no-op early-out still holds
- [x] 5.6 Verify a bolt on a short panel is scaled by that panel's height and stays inside its own region

## 6. Sky and flash per region

- [x] 6.1 Replace the `_emission` rectangle with a layer that draws `EmissionSkyBrush()` once per region, ramping over that region's own top and bottom
- [x] 6.2 Replace the `_flash` rectangle the same way with `ShockwaveLightBrush()`
- [x] 6.3 Keep both brush factories as the single definition of the colour ramps
- [x] 6.4 Confirm the opacity plumbing still applies: `Settle`, `SettleLayers`, `LayerRest` resting values and the emission animations
- [x] 6.5 Verify a given gradient stop lands at the same fraction of height on every screen

## 7. Offline renderers

- [x] 7.1 Confirm `Export.cs` still builds `LightningLayer` and the sky and flash layers with no regions and renders as a single screen
- [x] 7.2 Regenerate the export strips and compare against the previous output

## 8. Verification

- [x] 8.1 Run an emission on a single-monitor desktop and confirm it is indistinguishable from the current build
- [x] 8.5 Guard the density conversion against a layout that has not settled, and assert the condition
- [x] 8.2 Run an emission on the laptop-plus-external pair and confirm even density, correctly scaled bolts and a consistent horizon on both — verified by simulation, not hardware: `--export` now writes `screens.png`, the real layers driven with a life-size 1920x1080 + 3840x2160 region pair. Both screens read 1.06 artifacts per 100k sq, bolts scale to their own screen, both show the full ramp. Still worth one look on real hardware
- [ ] 8.3 Connect and disconnect a monitor mid-run and confirm the re-deal is clean — BLOCKED: needs a second display
- [ ] 8.4 Check frame cost on the widest desktop available against the current build — BLOCKED: the widest desktop here is one screen
