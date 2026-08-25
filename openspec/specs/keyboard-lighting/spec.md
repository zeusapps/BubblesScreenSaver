# keyboard-lighting Specification

## Purpose

What the keyboard backlight does while the screensaver is running, and what it owes back to the
keyboard it borrowed.

Two things reach the keys. An Emission -- twelve seconds of burning sky -- carries onto them at
full strength, because it is the one moment worth spilling off the screen. The ambient weather
carries onto them far more quietly, so that an Emission still arrives as an event rather than as
more of the same. Both read the clock and the colours the screen is drawn from, rather than
sampling what was drawn, so the keys cannot fall out of step with the picture.

Everything here is opt-in and silent when it cannot work. Most machines have no keyboard this
can talk to, so that is the common path and it must cost nothing: no dialog, no retry, no frame
delayed. And nothing borrowed is kept -- the device is released on waking, on exit, and after a
run that ended badly.
## Requirements
### Requirement: The keyboard is lit from the Emission's own clock

The system SHALL derive the keyboard colour as a function of elapsed Emission time, computed
against the same `BuildupEnds`, `WaveEnds` and `DarknessAt` constants the overlay animates the
screen with, so that the keyboard cannot drift out of step with what is drawn.

The colour SHALL rise from black to deep red through the buildup, flare toward white at the
wavefront, and fall back to black by the time the screen reaches darkness.

The function SHALL be pure: given an elapsed time it SHALL return a colour, without a device or
a rendered frame.

#### Scenario: Before the Emission begins
- **WHEN** the elapsed time is zero
- **THEN** the colour SHALL be black

#### Scenario: Through the buildup
- **WHEN** the elapsed time is between zero and `BuildupEnds`
- **THEN** the red component SHALL increase monotonically with elapsed time
- **AND** the colour SHALL be dominated by red

#### Scenario: At the wavefront
- **WHEN** the elapsed time is at the wavefront flare
- **THEN** the colour SHALL be brighter than at any point during the buildup
- **AND** the green and blue components SHALL be at their highest, carrying the flare toward white

#### Scenario: Arriving at darkness with the screen
- **WHEN** the elapsed time reaches `DarknessAt`
- **THEN** the colour SHALL be black

#### Scenario: The same time gives the same colour
- **WHEN** the function is asked twice for the same elapsed time
- **THEN** it SHALL return the same colour both times

### Requirement: A lightning strike is a flash that decays, not a state

The system SHALL treat a strike as an event, triggered on the frame a bolt first appears, and
SHALL fade it back to the sky colour behind it within a fixed short interval.

The system SHALL NOT hold the strike colour for as long as a bolt is on screen. A bolt is drawn
for a substantial fraction of a second and a storm keeps several overlapping, so a keyboard
following the presence of a bolt shows solid white through the whole storm while the screen is
showing thin bright lines over a red sky.

The strike SHALL be mixed over the ramp colour rather than replacing it, and successive strikes
SHALL vary in colour so that a burst does not read as one unbroken block of light.

The strike SHALL be taken from the value already in hand where the overlay draws it, and the
system SHALL NOT issue a second `HasStrike` query for the keyboard's benefit.

#### Scenario: A bolt appears
- **WHEN** the overlay draws a frame in which a strike has just begun
- **THEN** the keyboard SHALL be sent a bright, near-white colour for that frame

#### Scenario: A bolt that stays on screen
- **WHEN** a strike is reported on every frame for more than a second
- **THEN** the keyboard SHALL return to the Emission's own colour before that second is out
- **AND** the keys SHALL be dominated by red again rather than left washed out

#### Scenario: Several bolts in succession
- **WHEN** three separate strikes begin, each after a frame with no strike
- **THEN** each SHALL produce its own flash

#### Scenario: Bolts differ from one another
- **WHEN** the colours of successive strikes are compared
- **THEN** they SHALL NOT all be identical
- **AND** each SHALL still be bright enough to read as lightning

#### Scenario: No second query
- **WHEN** the keyboard layer needs to know whether a strike is happening
- **THEN** it SHALL be told by the drawing code that already asked
- **AND** it SHALL NOT call `HasStrike` itself

### Requirement: The blackout takes the keyboard dark

The system SHALL take the keyboard's lighting off when the overlay goes dark, rather than
releasing the device, because releasing it returns the keyboard to whatever its owner was
showing and that is probably lit.

This SHALL hold for every blackout of a session, not only the first. Between blackouts the
device has been given back and the vendor's software has reasserted its own lighting, so a
later blackout that failed to take the keys dark would leave a lit keyboard beside a black
screen -- the precise thing this requirement exists to prevent.

#### Scenario: The overlay goes dark
- **WHEN** `WentDark` is raised
- **THEN** the keyboard SHALL be taken to black
- **AND** the device SHALL NOT be released while the screen stays black

#### Scenario: A blackout that was not an Emission
- **WHEN** the screen reaches black by a plain fade, with no Emission
- **THEN** the keyboard SHALL still be taken dark
- **AND** no Emission colour SHALL have been sent

#### Scenario: The second blackout of a session
- **WHEN** a session has already been through one full blackout and hand-back
- **AND** the overlay goes dark again
- **THEN** the keyboard SHALL be taken to black again
- **AND** the colours of the Emission leading up to it SHALL have reached the keyboard

### Requirement: Waking gives the keyboard back by releasing it

The system SHALL give the keyboard back on leaving the blackout, on exit, and at the next
startup if the previous run ended without doing so.

Giving it back SHALL mean releasing the device. The protocol only accepts commands and cannot
be asked what the keyboard was showing beforehand, so the system SHALL NOT record a colour to
restore to, and SHALL NOT invent one. Whatever owns the lighting reasserts itself once the
device is free, and a process that dies has its handles closed for it.

The fact that a keyboard was taken SHALL be written to disk before the first colour is sent, so
that a run ending badly leaves a record behind. That SHALL be true of every loan, not only the
first of a session: a keyboard given back and taken again is a fresh debt and SHALL be recorded
as one.

Having given the keyboard back, the system SHALL NOT afterwards behave as though it still holds
it. Anything the system remembers about holding a device SHALL be reconciled with the device
itself before that device is written to.

#### Scenario: Leaving the blackout
- **WHEN** `LeftDark` is raised
- **THEN** the keyboard SHALL be released
- **AND** the record of the debt SHALL be cleared

#### Scenario: The record is written first
- **WHEN** the layer is about to send its first colour of a session
- **THEN** the debt SHALL already be recorded on disk

#### Scenario: The record is written again for a later loan
- **WHEN** the keyboard has been given back and a later Emission takes it again
- **THEN** the debt SHALL be recorded on disk again before that Emission's first colour

#### Scenario: The previous run ended badly
- **WHEN** the application starts
- **AND** a pending keyboard record is found on disk
- **THEN** it SHALL be settled before anything else is sent to the device

#### Scenario: Nothing is sent to a device already given back
- **WHEN** the device has been released
- **AND** a colour is to be sent
- **THEN** the system SHALL NOT write to the released device

### Requirement: Off by default, and free for everybody who leaves it off

The system SHALL default the keyboard lighting setting to off, and SHALL enumerate no devices
and open no handles while it is off.

#### Scenario: A machine that never enables it
- **WHEN** the setting is off
- **AND** an Emission begins
- **THEN** no device SHALL be opened
- **AND** the Emission SHALL be indistinguishable from one in a build without this feature

#### Scenario: Upgrading an existing installation
- **WHEN** a `settings.json` written before this feature is loaded
- **THEN** the setting SHALL read as off

#### Scenario: The setting is reachable
- **WHEN** the settings window is opened
- **THEN** the keyboard lighting setting SHALL appear there, as every persisted setting does

### Requirement: The setting says what it needs and what it costs

The system SHALL state, where the setting is presented, that it drives ASUS Aura keyboards and
that it requires Windows' Dynamic Lighting to be switched on.

This SHALL be stated rather than left to be discovered. While Dynamic Lighting is off, the
vendor's own software owns the keyboard and every write is accepted and discarded with no
error, which is indistinguishable from the feature being broken.

#### Scenario: Reading the setting before turning it on
- **WHEN** the user reads the keyboard lighting setting
- **THEN** the Dynamic Lighting requirement SHALL be described there
- **AND** the silent-failure behaviour SHALL be described there

### Requirement: Failure is silent, and decided once per session

The system SHALL make one attempt per session to *find* a keyboard, on the first Emission after
the setting is enabled. Where no supported keyboard is attached, or the device will not open,
the system SHALL log the reason once through `Diagnostics` and stay off for the remainder of
the session.

"Once per session" SHALL govern the search, not the holding. A keyboard that was found once and
has since been given back SHALL be opened again when it is next needed, because that is not a
retry of a decision made against: the search succeeded, and the device is known to be there.

A keyboard that stops accepting writes mid-session SHALL be treated as a failure and given up
on for the remainder of the session, rather than reopened. The system SHALL notice that the
write was refused rather than discarding the answer, SHALL say so once, and SHALL then stop
computing colours for it, as it does for a search that found nothing.

The system SHALL NOT show a dialog, schedule a retry, or attempt a second search mid-Emission.

#### Scenario: No supported keyboard
- **WHEN** the setting is on
- **AND** no ASUS Aura keyboard is attached
- **THEN** the failure SHALL be logged once
- **AND** no further attempt SHALL be made this session
- **AND** no dialog SHALL be shown

#### Scenario: A later Emission in the same session
- **WHEN** the search has already failed once this session
- **AND** a further Emission begins
- **THEN** no search SHALL be made

#### Scenario: A later Emission after a successful session
- **WHEN** a keyboard was found earlier this session and has since been given back
- **AND** a further Emission begins
- **THEN** the device SHALL be opened again
- **AND** its colours SHALL reach the keyboard

#### Scenario: The keyboard stops accepting writes
- **WHEN** a colour is sent and the device reports that it could not be written
- **THEN** the failure SHALL be logged
- **AND** no further colour SHALL be computed or sent for the remainder of the session
- **AND** the device SHALL NOT be reopened

#### Scenario: Nothing is owed for a keyboard never found
- **WHEN** the search fails
- **THEN** no record SHALL be written to disk

### Requirement: Nothing the keyboard does delays the screen

The system SHALL perform all device work off the dispatcher thread, and SHALL NOT await a
device, a handle or a write from any code path that draws a frame or advances the Emission.

A keyboard that cannot be reached, or one that answers slowly, SHALL affect nothing on screen.

#### Scenario: A device that stops answering during the Emission
- **WHEN** writes hang or the device stops accepting them
- **THEN** the Emission SHALL continue at its normal frame rate
- **AND** no frame SHALL be delayed waiting on the device

### Requirement: Colours are sent on change, not on every frame

The system SHALL compute the colour every frame and send it only when it has moved by a visible
amount, and SHALL observe a floor on the interval between sends, measured on the Emission's own
clock rather than the wall clock.

The wavefront flare and lightning flashes SHALL be exempt from that rationing.

#### Scenario: A slow ramp through the buildup
- **WHEN** consecutive frames yield colours that differ imperceptibly
- **THEN** the later frames SHALL NOT each produce a send

#### Scenario: The cost of a whole Emission
- **WHEN** an Emission runs at the default frame rate with no strikes
- **THEN** it SHALL cost fewer than a quarter as many writes as there were frames

#### Scenario: The flare is not rationed
- **WHEN** the wavefront flare is reached
- **THEN** it SHALL be sent, whatever the interval since the last send

### Requirement: The keyboard is only lit by the Emission and the blackout

The system SHALL drive the keyboard from the Emission and the blackout only. The artifacts
stage SHALL leave the keyboard alone.

The system SHALL set the whole device to one colour rather than addressing individual keys or
zones.

#### Scenario: The artifacts stage
- **WHEN** the overlay is showing artifacts and no Emission is running
- **THEN** nothing SHALL be sent to the keyboard

### Requirement: The device is chosen by what it declares, never by position

The system SHALL select the HID collection to write to by matching the vendor id, the usage
page, the usage, and a report length large enough to carry the command.

The system SHALL NOT select a collection by its index or its order of enumeration. One keyboard
presents many collections behind a single product id; several of them accept writes and only
one acts on them, so a collection chosen by position is a keyboard that silently does nothing.

Every packet SHALL be padded to the output report length the chosen collection declares, since
firmware discards a short write without reporting an error.

#### Scenario: The lighting collection among its siblings
- **WHEN** a device exposes several collections, only one of which has the lighting usage
- **THEN** that one SHALL be chosen

#### Scenario: A sibling collection with a different usage
- **WHEN** a collection shares the vendor and usage page but not the usage
- **THEN** it SHALL NOT be chosen

#### Scenario: Another vendor's keyboard
- **WHEN** a collection matches the usage page and usage but not the vendor
- **THEN** it SHALL NOT be chosen

#### Scenario: A collection too small for the command
- **WHEN** a matching collection declares an output report shorter than the command
- **THEN** it SHALL NOT be chosen

#### Scenario: Padding
- **WHEN** a packet is prepared for a collection declaring a longer report
- **THEN** it SHALL be padded to that length with zeroes

#### Scenario: Padding that would truncate
- **WHEN** a packet is longer than the report length it is padded to
- **THEN** the system SHALL refuse rather than send a truncated command

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

### Requirement: The device is asked whether it is held, never assumed

The system SHALL determine whether a keyboard is currently in hand by asking the device, which
is the only thing that knows. The device releases itself on a hand-back, on a refused write and
on an error, so any answer cached above it expires without notice.

The system SHALL NOT infer that a device is open from the fact that it was opened earlier.

#### Scenario: A device that has released itself
- **WHEN** the device has been released by any path
- **THEN** it SHALL report that it is not open

#### Scenario: A device in hand
- **WHEN** the device has been opened and not released
- **THEN** it SHALL report that it is open

#### Scenario: Deciding whether to open
- **WHEN** the system needs a device to write to
- **THEN** it SHALL consult the device's own account of whether it is open
- **AND** SHALL open it if it is not

### Requirement: A test keyboard behaves as unkindly as a real one

A substitute keyboard used to exercise this layer SHALL release itself when it is restored, and
SHALL be able to refuse a write, because the real device does both and every defect worth
catching here lives in what happens afterwards.

Tests SHALL assert what reaches the keyboard rather than how many times a device was opened.
The count of opens is an implementation detail that changed with this requirement; the keys
going dark is the behaviour that was wanted all along.

#### Scenario: Restoring the substitute
- **WHEN** the substitute keyboard is restored
- **THEN** it SHALL afterwards report that it is not open

#### Scenario: A substitute that refuses writes
- **WHEN** the substitute is configured to refuse writes
- **AND** a colour is sent to it
- **THEN** it SHALL report the write as failed

