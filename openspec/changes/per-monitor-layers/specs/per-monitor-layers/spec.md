## ADDED Requirements

### Requirement: Monitor regions are derived once and shared by every layer

The overlay SHALL derive, from the physical screens, a list of monitor regions expressed in
field coordinates (DIP), and SHALL supply that same list to every full-desktop layer.

A region SHALL correspond to exactly one physical screen. No layer SHALL derive its own
notion of where the screens are.

The list SHALL be re-derived when the display layout changes, and SHALL be treated as
unchanged when re-derivation produces an identical list, so that a display event which does
not move anything costs nothing.

#### Scenario: Two monitors of different sizes
- **WHEN** the regions are derived on a desktop with a 1920x1080 screen and a 3840x2160 screen
- **THEN** there SHALL be two regions
- **AND** each region SHALL match its screen's bounds converted to field coordinates

#### Scenario: A display event that changes nothing
- **WHEN** the display layout is re-derived and the resulting regions equal the stored ones
- **THEN** no layer SHALL be re-dealt, re-seeded or otherwise disturbed

### Requirement: A layer given no regions renders against its own bounds

Every region-aware layer SHALL accept an empty region list, and SHALL then behave as though
it had been given a single region covering its own width and height.

This SHALL hold for the artifact field, the lightning layer, the sky and the flash, so that
a layer constructed outside the overlay window renders correctly without a display layout.

#### Scenario: The offline renderer builds a layer directly
- **WHEN** a lightning layer is constructed with no regions inside a fixed-size container
- **THEN** it SHALL schedule and draw its strikes across that container's full bounds

#### Scenario: The overlay before the layout is known
- **WHEN** a layer renders before any regions have been supplied
- **THEN** it SHALL render as a single-screen scene rather than drawing nothing

### Requirement: Artifact population scales with the total display area

`BubbleCount` SHALL mean the number of artifacts on a baseline screen of 1920x1080 field
units, not the number of artifacts across the whole desktop.

The total number of artifacts SHALL be `BubbleCount` scaled by the ratio of the combined
area of all regions to the baseline area, and SHALL then be clamped to the range 1 to 400.
When the clamp reduces the derived total, that SHALL be recorded through diagnostics.

#### Scenario: A single baseline screen
- **WHEN** the desktop is one 1920x1080 screen and `BubbleCount` is 22
- **THEN** the total number of artifacts SHALL be 22

#### Scenario: A second screen is connected
- **WHEN** a second screen of equal area is connected and `BubbleCount` is 22
- **THEN** the total number of artifacts SHALL be 44
- **AND** the number of artifacts on the first screen SHALL be unchanged

#### Scenario: The derived total exceeds the ceiling
- **WHEN** the derived total exceeds 400
- **THEN** the total SHALL be 400
- **AND** the reduction SHALL be recorded through diagnostics

### Requirement: Artifacts are dealt to regions in proportion to area

Artifacts SHALL be distributed across regions in proportion to each region's area, not
evenly by region count.

The per-region counts SHALL sum exactly to the total, and every region with non-zero area
SHALL receive at least one artifact.

#### Scenario: A laptop panel beside a larger external screen
- **WHEN** artifacts are dealt across a small screen and a screen of four times its area
- **THEN** the larger screen SHALL receive approximately four times as many artifacts
- **AND** the density per unit area SHALL be equal on both within one artifact

#### Scenario: Counts sum to the total
- **WHEN** artifacts are dealt across any set of regions
- **THEN** the sum of the per-region counts SHALL equal the total exactly

#### Scenario: A very small screen
- **WHEN** a region is small enough that its proportional share rounds to zero
- **THEN** that region SHALL still receive at least one artifact

### Requirement: Lightning is scheduled and drawn per region

Each region SHALL carry its own strike schedule and SHALL receive the same number of strikes
as any other region, so that a single screen sees the same storm regardless of how many
other screens are attached.

A bolt SHALL be positioned within its own region and SHALL be scaled by that region's height,
not by the height of the tallest screen. The wash that accompanies a strike SHALL cover only
the region that struck.

Region schedules SHALL differ from one another, so that screens do not flash in lockstep.

#### Scenario: A short panel beside a tall one
- **WHEN** a strike is drawn on a 1080-high region while a 2160-high region is also attached
- **THEN** that bolt's reach and deviation SHALL be derived from 1080
- **AND** the bolt SHALL be drawn entirely within its own region

#### Scenario: Each screen gets a full storm
- **WHEN** an emission runs on a desktop with three screens
- **THEN** each screen SHALL receive the same number of strikes as a single-screen desktop would

#### Scenario: Screens do not flash together
- **WHEN** the schedules for two regions are derived
- **THEN** they SHALL NOT be identical

#### Scenario: Nothing is on screen
- **WHEN** no region has a strike in progress at the current emission time
- **THEN** the layer SHALL report that it has nothing to show, and SHALL NOT be redrawn

### Requirement: Sky and flash gradients are anchored to each region

The emission sky and the shockwave flash SHALL map their colour ramp across each region's own
top and bottom edge, so that every screen shows the full ramp.

The colour ramps themselves SHALL remain defined in one place and SHALL be identical for
every region.

#### Scenario: Monitors at different vertical offsets
- **WHEN** the sky is drawn over two regions whose vertical extents differ
- **THEN** each region SHALL show the ramp's first stop at its own top edge and the last stop
  at its own bottom edge

#### Scenario: The horizon sits consistently
- **WHEN** the sky is drawn over any region
- **THEN** the position of a given gradient stop within that region SHALL be the same fraction
  of the region's height on every screen

### Requirement: The stored artifact count is migrated once

A stored `BubbleCount` written under the previous meaning SHALL be converted, on the first
load after upgrade, to the density that comes closest to reproducing the same total on the
display layout present at that moment, and the converted value SHALL be written back.

The density is an integer, so on a desktop that is not a whole number of baseline screens the
reachable totals are spaced further apart than one. The conversion SHALL land on the nearest
reachable total; no other integer density SHALL come closer.

The settings file SHALL record that this conversion has run, and the conversion SHALL NOT run
a second time.

#### Scenario: A desk that is a whole number of baseline screens
- **WHEN** settings holding the old absolute count are loaded on a one-screen or two-equal-screen
  layout they were tuned on
- **THEN** the total number of artifacts SHALL match what the previous version displayed exactly

#### Scenario: A desk that is not
- **WHEN** settings holding the old absolute count are loaded on a layout whose area is not a
  whole multiple of the baseline
- **THEN** the total SHALL be the reachable total nearest the previous version's
- **AND** no other integer density SHALL reach a total closer to it

#### Scenario: The conversion does not repeat
- **WHEN** settings that have already been converted are loaded again
- **THEN** `BubbleCount` SHALL be left as stored

#### Scenario: A fresh install
- **WHEN** settings are created from defaults
- **THEN** they SHALL be recorded as already converted, and no conversion SHALL be applied
