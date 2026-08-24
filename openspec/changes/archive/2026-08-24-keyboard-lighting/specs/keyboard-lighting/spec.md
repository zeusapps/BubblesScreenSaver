## ADDED Requirements

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

#### Scenario: The overlay goes dark
- **WHEN** `WentDark` is raised
- **THEN** the keyboard SHALL be taken to black
- **AND** the device SHALL NOT be released while the screen stays black

#### Scenario: A blackout that was not an Emission
- **WHEN** the screen reaches black by a plain fade, with no Emission
- **THEN** the keyboard SHALL still be taken dark
- **AND** no Emission colour SHALL have been sent

### Requirement: Waking gives the keyboard back by releasing it

The system SHALL give the keyboard back on leaving the blackout, on exit, and at the next
startup if the previous run ended without doing so.

Giving it back SHALL mean releasing the device. The protocol only accepts commands and cannot
be asked what the keyboard was showing beforehand, so the system SHALL NOT record a colour to
restore to, and SHALL NOT invent one. Whatever owns the lighting reasserts itself once the
device is free, and a process that dies has its handles closed for it.

The fact that a keyboard was taken SHALL be written to disk before the first colour is sent, so
that a run ending badly leaves a record behind.

#### Scenario: Leaving the blackout
- **WHEN** `LeftDark` is raised
- **THEN** the keyboard SHALL be released
- **AND** the record of the debt SHALL be cleared

#### Scenario: The record is written first
- **WHEN** the layer is about to send its first colour of a session
- **THEN** the debt SHALL already be recorded on disk

#### Scenario: The previous run ended badly
- **WHEN** the application starts
- **AND** a pending keyboard record is found on disk
- **THEN** it SHALL be settled before anything else is sent to the device

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

The system SHALL make one attempt per session to find a keyboard, on the first Emission after
the setting is enabled. Where no supported keyboard is attached, or the device will not open,
the system SHALL log the reason once through `Diagnostics` and stay off for the remainder of
the session.

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
