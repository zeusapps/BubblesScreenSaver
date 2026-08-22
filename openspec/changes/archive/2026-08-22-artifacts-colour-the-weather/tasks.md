## 1. Family colours

- [x] 1.1 Add a tint per anomaly family, derived from the artifact palettes already in `Artifacts.All`
- [x] 1.2 Take `Core` for Chemical, Electrical and Thermic; take `Shell` for Gravitational, whose cores are near-black
- [x] 1.3 Assert every family yields a tint that is actually visible against a dark desktop

## 2. The census

- [x] 2.1 Count artifacts per family in `BubbleField`, recomputed on population change and on collection only
- [x] 2.2 Add the dominant-family decision: a challenger must lead by a margin, and a tint must hold for a minimum dwell
- [x] 2.3 Hold the current tint when the field is empty or nothing leads by the margin
- [x] 2.4 Assert the decision directly, with no window: a one-artifact lead does not flip it, a clear lead does, an empty field changes nothing, and a field oscillating by one artifact never flaps
- [x] 2.5 Confirm nothing counts on the render path

## 3. Tinted tiles

- [x] 3.1 Take a tint when rasterising a sheet, applied to the tile's existing alpha rather than replacing its brightness
- [x] 3.2 Cache tinted tiles per family, built lazily the first time that family becomes dominant
- [x] 3.3 Keep the intensity ladder as it is: brushes sharing one bitmap, differing only in `Opacity`
- [x] 3.4 Assert a tinted sheet and an untinted one have the same opacity at the same rung
- [x] 3.5 Put the tinted tiles behind the existing lookup with the family pinned, and confirm nothing on screen changes

## 4. The tint cross-fade

- [x] 4.1 Make the cross-fade carry `(state, family)` rather than the state alone
- [x] 4.2 Drive a tint change through the same path a weather change uses
- [x] 4.3 Hold the two-live-sheets limit when a state change and a tint change coincide
- [x] 4.4 Turn the census on and watch a tint change land

## 5. Strike-lit rain

- [x] 5.1 Add a lit state to the precipitation: a few rungs brighter, as a `Fill` swap to an already-built brush
- [x] 5.2 Drive it from `HasStrike`, which the render loop already consults, for both Emission and ambient strikes
- [x] 5.3 Leave the lightning layer where it is, below the artifacts
- [x] 5.4 Confirm the lift lasts exactly the strike and needs no clock of its own
- [x] 5.5 Watch a storm and an Emission and judge whether it reads as the sky lighting the rain rather than the rain flashing

## 6. The collection flourish

- [x] 6.1 Carry the collected artifact's family out of `BubbleField`, not just that something was collected
- [x] 6.2 Add the flourish: one short-lived element at the detector, in the family colour, animated and then removed
- [x] 6.3 Place it behind the detector in the z-order
- [x] 6.4 Keep it shorter than the 1.6s collection cooldown so at most one is ever alive
- [x] 6.5 Assert only one is alive when collections come as fast as the cooldown allows

## 7. The two that reach further

- [x] 7.1 Electrical: bring the next ambient strike forward
- [x] 7.2 Thermic: dip the fog briefly and let it return
- [x] 7.3 Leave Chemical and Gravitational with the flourish alone
- [x] 7.4 Confirm both are parameter changes to existing machinery, with no new drawing

## 8. Off means off

- [x] 8.1 Confirm no tinting, no lit rain and no flourish with weather switched off
- [x] 8.2 Confirm the same in the Soap theme
- [x] 8.3 Confirm no flourish when the detector is switched off

## 9. Cost

- [x] 9.1 Confirm no sheet is repainted while on screen, and a steady frame rasterises nothing
- [x] 9.2 Measure CPU and GPU across the weather demo against v1.8.1 and confirm neither moves
- [x] 9.3 Check the bitmap memory once several families have been seen in one run

## 10. Export and verification

- [x] 10.1 Add an export strip showing each family's weather
- [x] 10.2 Confirm the offline renderers still work with no display layout
- [x] 10.3 Run for an evening and judge the tints, the margin and the dwell
