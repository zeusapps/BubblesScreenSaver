## ADDED Requirements

### Requirement: Precipitation shimmers and fog does not

Rain and storms SHALL vary in brightness over time, because on screen they are three sheets
scrolling past one another at different speeds and a single zone of backlight cannot scroll.

Fog SHALL hold still, being the one weather that does.

The variation SHALL be a deterministic function of the clock, like everything else here.

#### Scenario: Rain over a few seconds
- **WHEN** rain is sampled repeatedly over several seconds at one intensity
- **THEN** it SHALL produce several different colours

#### Scenario: Fog over the same span
- **WHEN** fog is sampled the same way
- **THEN** it SHALL produce exactly one colour

### Requirement: The keyboard carries the weather that is on screen

The system SHALL derive an ambient keyboard colour from the weather state, its intensity, and
the anomaly tint the sky is drawn with, as a pure function of those three inputs.

`Clear` SHALL leave the keys unlit. `Fog`, `Rain` and `Storm` SHALL tint the keys with the same
`AnomalyTint` the weather layer uses, so that the keys and the sky are the same colour without
either being told what the other chose.

The function SHALL NOT sample the rendered frame.

#### Scenario: A clear sky
- **WHEN** the weather is `Clear` at full intensity
- **THEN** the keyboard colour SHALL be black

#### Scenario: Fog over a green field
- **WHEN** the weather is `Fog`
- **AND** the dominant anomaly's tint is green
- **THEN** the keyboard colour SHALL be dominated by green

#### Scenario: The same inputs give the same colour
- **WHEN** the function is asked twice with the same state, intensity and tint
- **THEN** it SHALL return the same colour both times

#### Scenario: Intensity scales the result
- **WHEN** the same state and tint are given at half intensity rather than full
- **THEN** the colour SHALL be dimmer

### Requirement: Cross-fades are inherited, not reimplemented

The system SHALL compute the ambient colour by summing the contribution of every weather state
reported by `WeatherCycle.IntensityOf`, so that a transition between two states produces a blend
of both.

The system SHALL NOT run a timer, easing curve or fade of its own for weather.

#### Scenario: Mid-transition
- **WHEN** the cycle reports two states each at half intensity
- **THEN** the keyboard colour SHALL lie between the colours of those two states

#### Scenario: Settled weather
- **WHEN** the cycle reports one state at full intensity and no other
- **THEN** the keyboard colour SHALL be that state's colour exactly

### Requirement: Ambient light never competes with an Emission

The system SHALL cap the ambient colour at a small fraction of the Emission's brightness, so
that the brightest ambient weather is dimmer than the Emission's own buildup.

The fraction SHALL be a named constant, and the relationship SHALL be asserted rather than left
to judgement.

#### Scenario: The brightest weather against the Emission's own colour
- **WHEN** the brightest ambient colour any state and tint can produce is compared with the
  Emission's deepest red, which the buildup climbs to and the wavefront departs from
- **THEN** the ambient colour SHALL be no more than half as bright

#### Scenario: Where the Emission overtakes the weather
- **WHEN** the Emission's buildup is three quarters through
- **THEN** it SHALL already be brighter than the brightest ambient weather

#### Scenario: The wavefront against the weather
- **WHEN** the wavefront flares
- **THEN** it SHALL be several times brighter than any weather can be

Note: the Emission opens from black and is dimmer than ambient weather for its first seconds.
That is the buildup working, not the cap failing -- an Emission that began brighter than the
weather it interrupts would have no build to it.

### Requirement: An Emission takes the keyboard over outright

While an Emission is running, the system SHALL NOT compute or send an ambient colour, and the
Emission SHALL be the only source of keyboard colour.

When the overlay leaves the blackout and the artifacts return, ambient weather SHALL resume
through its ordinary path, with no special case for the transition.

#### Scenario: An Emission begins
- **WHEN** an Emission starts while weather is lighting the keys
- **THEN** the ambient source SHALL stop sending
- **AND** the keyboard SHALL follow the Emission

#### Scenario: Coming back from a blackout
- **WHEN** the overlay leaves the blackout and the artifacts return
- **THEN** ambient weather SHALL light the keys again

#### Scenario: The screen is black
- **WHEN** the overlay has reached black
- **THEN** no ambient colour SHALL be sent

### Requirement: An ambient strike rides the query already made

The system SHALL flash the keys on an ambient lightning strike, taking the strike from the value
already computed where the ambient bolt is drawn, and SHALL NOT issue a second `HasStrike` query.

An ambient flash SHALL use the same edge-triggered, decaying behaviour as an Emission's, at the
same brightness. Lightning is the one part of ambient weather legibly tied to something visible
on screen, and dimming it discards the clearest signal the feature has.

#### Scenario: A distant bolt
- **WHEN** the ambient lightning reports a strike beginning
- **THEN** the keyboard SHALL be sent a brighter colour for that frame

#### Scenario: A bolt that lingers
- **WHEN** an ambient strike is reported on every frame for more than a second
- **THEN** the keys SHALL return to the storm's own colour before that second is out

#### Scenario: An ambient strike against an Emission's
- **WHEN** an ambient flash is compared with an Emission's strike
- **THEN** they SHALL be the same colour

### Requirement: Ambient colour is rationed more slowly than an Emission's

The system SHALL apply a longer minimum interval between ambient sends than between an
Emission's, because ambient weather changes over a minute rather than over twelve seconds.

Strike flashes SHALL remain exempt from that interval.

#### Scenario: A minute of still weather
- **WHEN** fog holds for a minute at the default frame rate
- **THEN** it SHALL cost fewer than a dozen writes

#### Scenario: A minute of rain
- **WHEN** rain holds for a minute at the default frame rate
- **THEN** it SHALL cost more than a dozen writes, because it shimmers
- **AND** fewer than a quarter of the frames it was offered

#### Scenario: A cross-fade
- **WHEN** the weather crosses from one state to another over six seconds
- **THEN** the keys SHALL be updated more than once, so the change reads as a fade

### Requirement: The weather setting is separate, off by default, and subordinate

The system SHALL provide a setting for keyboard weather, defaulting to off, which SHALL have no
effect unless the keyboard lighting setting is also on.

The setting SHALL state that enabling it holds the keyboard for the whole time the screensaver
is on screen, rather than for the length of an Emission.

#### Scenario: Neither setting on
- **WHEN** both settings are off
- **THEN** no device SHALL be opened and nothing SHALL be sent

#### Scenario: Weather on, master off
- **WHEN** the weather setting is on but keyboard lighting is off
- **THEN** nothing SHALL be sent

#### Scenario: An existing installation
- **WHEN** a `settings.json` written before this feature is loaded
- **THEN** the weather setting SHALL read as off

#### Scenario: Reading the setting
- **WHEN** the user reads the keyboard weather setting
- **THEN** the length of the loan SHALL be described there

### Requirement: The keyboard is held for the artifacts stage, not surrendered between states

While ambient weather is enabled and the artifacts are on screen, the system SHALL hold the
device rather than releasing it when the weather is `Clear`.

Releasing on `Clear` would return the keyboard to the vendor's software, which lights it -- so
surrendering during the calmest weather would produce more light, not less, and would hand the
device back and forth roughly once a minute.

The device SHALL still be released on the paths that already release it: leaving the blackout,
exit, and a record found at startup.

#### Scenario: Clear weather
- **WHEN** the weather is `Clear` and the artifacts are on screen
- **THEN** the device SHALL remain held
- **AND** the keys SHALL be unlit

#### Scenario: Waking
- **WHEN** the overlay leaves the blackout
- **THEN** the device SHALL be released, as it already is
