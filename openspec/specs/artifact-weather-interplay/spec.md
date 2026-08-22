# artifact-weather-interplay Specification

## Purpose

How the artifacts on screen colour and disturb the weather around them: which anomaly family the
weather takes its cue from, how a strike reaches the precipitation, and what a collection does to
the sky.

## Requirements

### Requirement: Weather takes its colour from the artifacts drifting in it

The weather SHALL be tinted by the anomaly family holding the most artifacts on screen, so that
precipitation and fog belong to the field rather than to a fixed palette.

Each family's tint SHALL be derived from the colours already carried by its artifacts, so the
palette is defined in one place. Chemical, Electrical and Thermic SHALL take their emitting
colour; Gravitational SHALL take its shell colour, because its artifacts are dark bodies whose
cores are nearly black and would tint nothing.

The tint SHALL apply to the tile's existing alpha rather than replacing it, so a tinted sheet is
never more opaque than an untinted one.

#### Scenario: A field dominated by one family
- **WHEN** most artifacts on screen belong to the Thermic family and rain is showing
- **THEN** the precipitation SHALL carry the Thermic tint

#### Scenario: The dark family still tints
- **WHEN** the dominant family is Gravitational
- **THEN** the tint SHALL be taken from its shell colour and SHALL be visible

#### Scenario: Tinting does not thicken the weather
- **WHEN** a tinted sheet and an untinted sheet are compared at the same intensity
- **THEN** they SHALL have the same opacity

### Requirement: The dominant family changes slowly and never flickers

The dominant family SHALL only change when a challenger leads the incumbent by a margin, and
SHALL hold for a minimum dwell once changed.

Sixteen artifact kinds across four families leave two families within one artifact of each other
much of the time, and a single collection SHALL NOT be able to flip the sky.

With no artifacts on screen, or with no family leading by the margin, the tint SHALL stay where
it is.

#### Scenario: A one-artifact lead is not enough
- **WHEN** the leading family is ahead by fewer artifacts than the margin
- **THEN** the tint SHALL NOT change

#### Scenario: A clear lead takes over
- **WHEN** a family leads by more than the margin and the current tint has held for its dwell
- **THEN** the tint SHALL change to that family

#### Scenario: An empty field
- **WHEN** there are no artifacts on screen
- **THEN** the tint SHALL remain unchanged rather than resetting

### Requirement: A tint change behaves exactly like a weather change

A change of tint SHALL cross-fade through the same mechanism a change of weather state uses, and
SHALL be subject to the same limit of two live sheets.

At no moment SHALL more than two sheets of the same kind be rendered, including when a state
change and a tint change coincide.

#### Scenario: The tint changes
- **WHEN** the dominant family changes while rain is showing
- **THEN** the outgoing tint SHALL fade out as the incoming tint fades in

#### Scenario: A state change and a tint change at once
- **WHEN** the weather state and the dominant family change together
- **THEN** at most two sheets SHALL be live

### Requirement: The census is taken on change, not per frame

The dominant family SHALL be recomputed only when the population changes or an artifact is
collected. It SHALL NOT be counted on the render path.

#### Scenario: A frame with nothing happening
- **WHEN** a frame is rendered and no artifact has been collected or respawned
- **THEN** no census SHALL be taken

### Requirement: Precipitation brightens while a strike is on screen

Precipitation SHALL render brighter for exactly as long as a strike is on screen, so that
lightning reaches the rain.

This SHALL apply to an Emission's strikes and to the ambient strikes of stormy weather alike.

The lightning SHALL keep its place below the artifacts. It is the sky, and it silhouettes them;
reaching the rain SHALL NOT be achieved by drawing bolts over the top of the scene.

The brightening SHALL be a change of intensity on an already-built sheet, not a repaint.

#### Scenario: A bolt strikes during rain
- **WHEN** a strike is on screen while precipitation is showing
- **THEN** the precipitation SHALL render brighter than it does between strikes

#### Scenario: The strike ends
- **WHEN** no strike is on screen
- **THEN** the precipitation SHALL return to its ordinary intensity

#### Scenario: A storm, not only an Emission
- **WHEN** an ambient strike occurs during stormy weather
- **THEN** the precipitation SHALL brighten for it as it would for an Emission's

### Requirement: Collecting an artifact disturbs the sky where it happened

The detector picking up an artifact SHALL produce a short visual flourish at the detector's
position, coloured by that artifact's family.

It SHALL be a single short-lived element rather than a change to the weather sheets, because a
sheet is a desktop-wide tile and cannot be disturbed in one place without being repainted.

The flourish SHALL be shorter than the detector's collection cooldown, so at most one is ever
alive.

It SHALL sit behind the detector, so it reads as the sky answering rather than as part of the
readout.

#### Scenario: An artifact is collected
- **WHEN** the detector collects an artifact
- **THEN** a flourish SHALL appear at the detector's position in that artifact's family colour
- **AND** it SHALL fade out on its own

#### Scenario: Only ever one
- **WHEN** artifacts are collected as often as the cooldown allows
- **THEN** no more than one flourish SHALL be alive at any moment

#### Scenario: No detector, no flourish
- **WHEN** the detector is switched off
- **THEN** nothing SHALL be collected and no flourish SHALL appear

### Requirement: Two families reach further than the flourish

Collecting an Electrical artifact SHALL bring an ambient strike forward, and collecting a
Thermic artifact SHALL briefly thin the fog.

Both SHALL be parameter changes to existing machinery rather than new drawing. Chemical and
Gravitational SHALL produce the flourish alone.

#### Scenario: An Electrical pickup
- **WHEN** an Electrical artifact is collected during stormy weather
- **THEN** an ambient strike SHALL follow shortly after

#### Scenario: A Thermic pickup
- **WHEN** a Thermic artifact is collected while fog is showing
- **THEN** the fog SHALL thin briefly and then return

#### Scenario: The other two
- **WHEN** a Chemical or Gravitational artifact is collected
- **THEN** the flourish SHALL appear and the weather itself SHALL be unchanged

### Requirement: Per-frame cost does not move

No sheet SHALL be repainted while it is on screen. Tinted tiles SHALL be rasterised once per
family and reused, and every change described here SHALL be an assignment of an already-built
brush, a parameter, or a short animation on a small element.

Weather is on screen continuously, and the cost of repainting a desktop-wide tile per frame is
the defect this layer was rebuilt to remove.

#### Scenario: A family becomes dominant for the first time
- **WHEN** a family's tinted tiles are needed and have not been built
- **THEN** they SHALL be rasterised once and kept for the rest of the run

#### Scenario: Steady weather
- **WHEN** the weather and the dominant family are both unchanged across a frame
- **THEN** no rasterising SHALL occur

### Requirement: Turning weather off removes all of it

None of the tinting, the strike-lit precipitation or the collection flourishes SHALL appear
with weather switched off, or in a theme that has no weather.

#### Scenario: Weather is off
- **WHEN** the weather setting is off and an artifact is collected
- **THEN** no flourish SHALL appear
