## ADDED Requirements

### Requirement: The Zone has four weather states

The overlay SHALL provide exactly four ambient weather states in the Zone theme: clear,
fog, rain, and rain with lightning.

Clear SHALL draw nothing. Fog SHALL soften the scene without obscuring the artifacts.
Rain SHALL show falling precipitation. Rain with lightning SHALL show that same
precipitation together with ambient strikes.

Each state SHALL be distinguishable from the other three at a glance.

#### Scenario: Clear weather
- **WHEN** the weather state is clear and no transition is in progress
- **THEN** the weather layer SHALL draw nothing
- **AND** the scene SHALL be identical to the theme with weather turned off

#### Scenario: Rain with lightning
- **WHEN** the weather state is rain with lightning
- **THEN** precipitation SHALL be drawn
- **AND** ambient strikes SHALL occur

### Requirement: Weather is chosen at random and never repeats immediately

The overlay SHALL choose each weather state by a weighted random roll that excludes the
state currently showing, so that every change is visible.

#### Scenario: A new state is rolled
- **WHEN** the weather cycle rolls the next state
- **THEN** the chosen state SHALL differ from the current one

#### Scenario: Over a long run
- **WHEN** the weather cycles many times
- **THEN** all four states SHALL occur

### Requirement: Weather changes about once a minute

The overlay SHALL hold a weather state for a nominal 60 seconds before rolling the next
one, varied by up to 25% either way so that changes do not fall on a fixed beat.

#### Scenario: A state runs its course
- **WHEN** a weather state has been showing for its dwell time
- **THEN** the next state SHALL be rolled and the transition SHALL begin

#### Scenario: Dwell times vary
- **WHEN** successive dwell times are drawn
- **THEN** each SHALL fall between 45 and 75 seconds
- **AND** they SHALL NOT all be equal

### Requirement: States blend by cross-fade rather than cutting

The overlay SHALL move between weather states by cross-fading the outgoing state out as
the incoming state comes in. No weather change SHALL appear as a cut.

At most two weather states SHALL be rendered at any moment, and outside a transition
exactly one SHALL be.

Cross-fading SHALL be driven by pre-baked intensity levels rather than by compositing the
layer at partial opacity.

#### Scenario: A transition is in progress
- **WHEN** the weather is part way from one state to another
- **THEN** both states SHALL be rendered, at complementary intensities

#### Scenario: A transition completes
- **WHEN** the transition finishes
- **THEN** only the incoming state SHALL be rendered
- **AND** the outgoing state SHALL be released

### Requirement: Weather is one state across the whole desktop

The overlay SHALL show the same weather state on every monitor at the same moment.

The rendered density of a state -- precipitation per unit area, fog per unit area --
SHALL be derived from each monitor region's own area, so that density is even across
screens of different sizes.

#### Scenario: Two screens of different sizes
- **WHEN** rain is showing on a small screen and a screen of four times its area
- **THEN** both SHALL show rain
- **AND** the precipitation per unit area SHALL be the same on each

#### Scenario: Monitors never disagree
- **WHEN** the weather state is sampled for any two regions at the same moment
- **THEN** the state SHALL be the same for both

### Requirement: Weather yields to an Emission

The overlay SHALL suspend the weather cycle for the duration of an Emission, so that the
weather state cannot change while the sky is burning. A transition already in flight when
an Emission begins SHALL be allowed to finish.

Fog SHALL fade out over the Emission's buildup. Precipitation SHALL continue.

The cycle SHALL resume when the Emission ends.

#### Scenario: An Emission begins
- **WHEN** an Emission starts while the weather is showing fog
- **THEN** the fog SHALL fade out over the buildup
- **AND** no new weather state SHALL be rolled until the Emission ends

#### Scenario: Rain during an Emission
- **WHEN** an Emission starts while rain is showing
- **THEN** the rain SHALL continue for the duration of the Emission

#### Scenario: The Emission ends
- **WHEN** the Emission ends and the artifacts return
- **THEN** the weather cycle SHALL resume from the state it was suspended in

### Requirement: Nothing is drawn once the screen is black

The overlay SHALL settle the weather layer to zero when the screen reaches black, and
SHALL stop its animations, exactly as the lightning layer is already stopped.

#### Scenario: The screen reaches black
- **WHEN** a blackout completes
- **THEN** the weather layer SHALL be at zero and SHALL NOT be animating

#### Scenario: Coming back
- **WHEN** the blackout ends and the artifacts return
- **THEN** weather SHALL resume

### Requirement: Weather can be turned off

The overlay SHALL provide a setting, exposed in the tray menu alongside the other Zone
options, that disables weather entirely.

With weather off, the Zone theme SHALL render as it does without this capability, and no
weather work SHALL be performed.

#### Scenario: Weather is disabled
- **WHEN** the weather setting is off
- **THEN** no weather SHALL be drawn and no cycle SHALL run

#### Scenario: Weather is re-enabled
- **WHEN** the weather setting is turned back on while the artifacts are showing
- **THEN** weather SHALL begin from a freshly rolled state

### Requirement: Weather is confined to the Zone theme

The overlay SHALL draw weather only in the Zone theme.

#### Scenario: The Soap theme
- **WHEN** the theme is Soap
- **THEN** no weather SHALL be drawn regardless of the weather setting

### Requirement: An Emission carries 50% more lightning

The Emission SHALL place around 50% more strikes on screen than it does today: about 27
before the screen reaches black, against the 18 currently seen.

This increase SHALL come from the interval between strikes, not from the total scheduled,
since strikes scheduled after darkness are never rendered.

Each screen jitters its own gaps, so the exact count differs between them: measured across
64 screens the counts run from 25 to 30, averaging 26.8. Every screen SHALL receive at
least 25 strikes and no more than 31 -- a rise of at least 39% on the unluckiest screen and
about 49% on average.

#### Scenario: Counting strikes in an Emission
- **WHEN** the strike schedule is derived for the first screen
- **THEN** 27 strikes SHALL fall before the darkness time

#### Scenario: Per screen, not per desktop
- **WHEN** an Emission runs on a desktop with several monitors
- **THEN** each monitor SHALL receive between 25 and 31 strikes
- **AND** every monitor SHALL receive more than the 18 seen today

### Requirement: The strike schedule ends at darkness

The strike schedule SHALL be built until it passes the Emission's darkness time, rather
than to a fixed number of strikes, so that no strike is ever scheduled where it cannot be
seen.

#### Scenario: No wasted strikes
- **WHEN** the schedule is derived
- **THEN** at most one strike SHALL start after the darkness time

#### Scenario: The timeline is retuned
- **WHEN** the Emission's darkness time changes
- **THEN** the schedule SHALL still run to the end of the Emission without any constant
  being re-derived by hand

### Requirement: Ambient storm lightning is quieter than an Emission

The rain-with-lightning state SHALL produce strikes that are fewer and weaker than an
Emission's, weighted towards the sky wash rather than the bolt, so that a storm is not
mistaken for an Emission beginning.

Ambient strikes SHALL be drawn behind the artifacts, in the same position as the
Emission's lightning, and SHALL be silhouetting rather than covering.

An Emission SHALL take the lightning over outright for its duration.

#### Scenario: A storm strikes
- **WHEN** an ambient strike occurs during rain with lightning
- **THEN** it SHALL be drawn behind the artifacts
- **AND** it SHALL be weaker than an Emission strike

#### Scenario: An Emission interrupts a storm
- **WHEN** an Emission begins while a storm is showing
- **THEN** the Emission's schedule SHALL replace the ambient one for the Emission's
  duration

### Requirement: Weather costs no measurable per-frame work

Weather SHALL be rendered by composited brushes and transforms rather than by redrawing
its content each frame, because unlike the Emission it is on screen continuously.

The per-frame cost of the artifacts stage with weather showing SHALL be comparable to the
same stage with weather off.

#### Scenario: Rain over a wide desktop
- **WHEN** rain is showing across the full virtual desktop
- **THEN** the frame cost SHALL be comparable to the same scene with weather off

#### Scenario: Motion without redraws
- **WHEN** precipitation is falling
- **THEN** its motion SHALL come from animated transforms rather than from per-frame
  redrawing of the layer
