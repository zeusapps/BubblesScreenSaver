## ADDED Requirements

### Requirement: The countdown is measured from the start of the run as well as from the last input

The system SHALL NOT report more idle time than has passed since the current run began.

The idle time the system acts on is derived from a system-wide counter that measures the time
since the last keyboard or mouse input. That counter survives this application starting and
stopping, because it was never about this application. A run that begins while the machine has
already been sitting therefore begins with the thresholds already passed, and the overlay
arrives at a stage it should have walked to.

The bound SHALL be applied to the reported result, after any hold-off time has been discounted,
so that the existing discount continues to mean what it means. Reported idle time SHALL be the
lesser of the time counted since the last input and the time since the run began.

This SHALL hold whatever the thresholds are set to, and SHALL NOT be expressed as a grace
period, a number of ticks to ignore, or any value requiring tuning against `IdleSeconds` or
`BlackoutSeconds`.

#### Scenario: Starting into a machine that has been idle for longer than every threshold
- **WHEN** the application starts on a machine whose last input was longer ago than
  `BlackoutSeconds`
- **AND** nothing is holding any stage off
- **THEN** the overlay SHALL be at `Active`
- **AND** it SHALL reach `Bubbles` no sooner than `IdleSeconds` after the run began
- **AND** it SHALL reach `Blackout` no sooner than `BlackoutSeconds` after the run began

#### Scenario: The artifacts stage is not skipped
- **WHEN** a run starts into an already-idle machine and nothing is held off
- **THEN** the overlay SHALL pass through `Bubbles` before reaching `Blackout`
- **AND** it SHALL remain at `Bubbles` for the interval the settings describe

#### Scenario: Starting into a machine in use
- **WHEN** the application starts on a machine whose last input was a moment ago
- **THEN** the reported idle time SHALL follow the time since that input, as it does today

#### Scenario: Long after the run began
- **WHEN** the run has been going for longer than the time since the last input
- **THEN** the bound SHALL have no effect
- **AND** the reported idle time SHALL be the time since the last input, less any discount

#### Scenario: The bound composes with the hold-off discount
- **WHEN** a run has been going long enough for the bound not to apply
- **AND** a total hold-off has been in force for part of that time
- **THEN** that time SHALL still be discounted, exactly as it is without this requirement

#### Scenario: The clock is not reset by a restart of the countdown
- **WHEN** input arrives and the countdown restarts
- **THEN** the origin of the run SHALL be unchanged
- **AND** the bound SHALL continue to be measured from when the run began

