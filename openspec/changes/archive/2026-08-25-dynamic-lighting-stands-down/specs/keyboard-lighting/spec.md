## MODIFIED Requirements

### Requirement: The setting says what it needs and what it costs

The system SHALL state, where the setting is presented, that it drives ASUS Aura keyboards and
that it requires Windows' Dynamic Lighting to be switched **off**.

This SHALL be stated rather than left to be discovered, and it SHALL be stated correctly. While
Dynamic Lighting is on, Windows owns the keyboard and repaints its own effect over every write
the system makes; while it is off, the vendor's software owns the keyboard and yields to those
writes. In both cases the write is accepted and reports success, so the system cannot tell
which owner it is talking past, and a reader given the wrong direction has no way to discover
the error from the application.

Wherever the system names Dynamic Lighting -- the setting, the settings dialog, and any
diagnostic written when the feature appears not to work -- it SHALL name the same direction,
and SHALL describe the symptom of the wrong state: the keys hold one colour, ignore the
Emission, and stay lit through the blackout.

A diagnostic written at the moment the device is opened SHALL NOT instruct the reader to put
Dynamic Lighting into the state that prevents the feature from working.

#### Scenario: Reading the setting before turning it on
- **WHEN** the user reads the keyboard lighting setting
- **THEN** the Dynamic Lighting requirement SHALL be described there
- **AND** it SHALL say that Dynamic Lighting must be off
- **AND** the silent-failure behaviour SHALL be described there

#### Scenario: The diagnostic written when the device is opened
- **WHEN** the system logs that it has taken the lighting collection
- **AND** that line names Dynamic Lighting
- **THEN** it SHALL name switching Dynamic Lighting off as the remedy

#### Scenario: Every statement agrees
- **WHEN** any two places in the system describe what Dynamic Lighting must be
- **THEN** they SHALL describe the same state

## ADDED Requirements

### Requirement: Dynamic Lighting is borrowed and given back like anything else

The system SHALL be able to switch Windows' Dynamic Lighting off for the duration of a keyboard
loan, and SHALL give it back.

Giving it back SHALL mean restoring the value that was found, not a fixed value. Somebody who
already keeps Dynamic Lighting off SHALL NOT have it switched on by the system returning
something it never took.

The value found SHALL be written to disk before it is changed, so that a run ending badly
leaves a record behind, and that record SHALL be settled on waking, on exit, and at the next
startup if the previous run did not settle it.

The loan SHALL last as long as the keyboard's own loan, being taken where the device is opened
and given back where the device is released.

#### Scenario: Taken for an Emission
- **WHEN** the keyboard device is opened and this setting is on
- **AND** Dynamic Lighting is on
- **THEN** its value SHALL be recorded on disk
- **AND** Dynamic Lighting SHALL then be switched off

#### Scenario: The record is written first
- **WHEN** the system is about to change Dynamic Lighting
- **THEN** the previous value SHALL already be recorded on disk

#### Scenario: Given back on waking
- **WHEN** the keyboard is released
- **THEN** Dynamic Lighting SHALL be set to the value that was recorded
- **AND** the record SHALL be cleared

#### Scenario: It was already off
- **WHEN** Dynamic Lighting is already off at the moment of the loan
- **THEN** the recorded value SHALL be "off"
- **AND** releasing SHALL leave it off

#### Scenario: The previous run ended badly
- **WHEN** the application starts
- **AND** a pending Dynamic Lighting record is found on disk
- **THEN** the recorded value SHALL be restored before anything else is sent to the device

#### Scenario: The setting is off
- **WHEN** the keyboard lighting setting is on but this setting is off
- **THEN** Dynamic Lighting SHALL NOT be read, changed or recorded

### Requirement: Standing Dynamic Lighting down is its own opt-in

The system SHALL provide a setting for switching Dynamic Lighting off during a loan, defaulting
to off, which SHALL have no effect unless the keyboard lighting setting is also on.

The setting SHALL state that it changes a Windows setting and puts it back, and that with
keyboard weather enabled it holds that setting for as long as the screensaver is on screen
rather than for the length of an Emission.

Enabling keyboard lighting alone SHALL NOT change any Windows setting.

#### Scenario: An existing installation
- **WHEN** a `settings.json` written before this feature is loaded
- **THEN** the setting SHALL read as off

#### Scenario: Keyboard lighting on, this setting off
- **WHEN** keyboard lighting is on and this setting is off
- **AND** an Emission begins
- **THEN** no Windows setting SHALL be changed

#### Scenario: This setting on, keyboard lighting off
- **WHEN** this setting is on but keyboard lighting is off
- **THEN** no Windows setting SHALL be changed

#### Scenario: Reading the setting
- **WHEN** the user reads this setting
- **THEN** it SHALL say that a Windows setting is changed and restored
- **AND** the length of the loan SHALL be described there
