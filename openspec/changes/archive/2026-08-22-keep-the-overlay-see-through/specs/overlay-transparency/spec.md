## ADDED Requirements

### Requirement: The desktop is visible through the artifacts

At the artifacts stage the overlay SHALL composite through to the live desktop. What is
underneath SHALL remain visible, and SHALL continue to run and update.

The overlay SHALL obtain transparency from the DWM frame extension rather than from
`AllowsTransparency`, which forces software rendering.

#### Scenario: The artifacts arrive
- **WHEN** the overlay enters the artifacts stage
- **THEN** the desktop SHALL be visible beneath the artifacts, dimmed by `Dim`
- **AND** the desktop SHALL NOT be replaced by a solid colour or a frozen image

#### Scenario: What is underneath keeps running
- **WHEN** the artifacts are on screen over an application that is updating
- **THEN** that application SHALL continue to update and remain visible

### Requirement: Each layer has a defined resting opacity per stage

The overlay SHALL define, as a function of stage and settings alone, the opacity every layer
rests at once any transition has completed:

| Layer | Active | Artifacts | Blackout |
|---|---|---|---|
| root | 0 | 1 | 1 |
| scrim | `Dim` | `Dim` | 1 |
| sky | 0 | 0 | 0 |
| flash | 0 | 0 | 0 |
| artifacts | `Opacity` | `Opacity` | 0 |
| detector | 0 | 0 or 1 by theme | 0 |

This SHALL be derivable without a window, so that it can be asserted directly.

#### Scenario: At rest in the artifacts stage
- **WHEN** the resting state for the artifacts stage is computed with default settings
- **THEN** the scrim SHALL be `Dim`, the sky SHALL be 0, and the flash SHALL be 0

#### Scenario: At rest in blackout
- **WHEN** the resting state for the blackout stage is computed
- **THEN** the scrim SHALL be 1 and the artifacts SHALL be 0

### Requirement: Every reachable stage sequence rests correctly

For every sequence of stage transitions ending at the artifacts stage, the resting state SHALL
be the artifacts-stage state defined above, whatever stages preceded it and whether or not any
of them were interrupted.

An interrupted blackout SHALL NOT leave any layer at a value belonging to another stage.

#### Scenario: Artifacts after a completed blackout
- **WHEN** the overlay has gone to blackout, returned to active, and entered the artifacts stage
- **THEN** the scrim SHALL be `Dim` and the sky SHALL be 0

#### Scenario: Artifacts after an interrupted Emission
- **WHEN** an Emission is interrupted partway and the overlay later enters the artifacts stage
- **THEN** the scrim SHALL be `Dim`, the sky SHALL be 0, and the flash SHALL be 0

### Requirement: A held animation never outranks the resting state

Before a layer is shown, the overlay SHALL clear any animation holding that layer's opacity, and
SHALL then assign the resting value.

Once a property has been animated with `FillBehavior.HoldEnd`, the held value outranks anything
assigned directly. This is already documented in the source for the detector layer, where it
left the detector frozen on screen in a theme that has none. The scrim, the sky and the flash
carry the identical hazard and SHALL be guarded the same way.

#### Scenario: A layer left held by an interrupted blackout
- **WHEN** a layer's opacity is being held by a completed animation
- **AND** the overlay enters a stage where that layer rests at a different value
- **THEN** the held animation SHALL be cleared and the resting value SHALL take effect

### Requirement: The overlay restores its own state before doing anything else

When leaving blackout, the overlay SHALL restore its own layer opacities before raising any
event to external subscribers.

`LeftDark` restores monitor backlights over DDC/CI, changes HDR mode, and may request a
workstation lock. Restoring the overlay after that work makes the overlay's own correctness
depend on the most failure-prone code in the application.

#### Scenario: Ordering
- **WHEN** the overlay leaves blackout
- **THEN** the layer restoration SHALL be started before `LeftDark` is raised

### Requirement: A failing subscriber cannot leave the overlay opaque

The overlay SHALL raise `LeftDark` and `WentDark` such that an exception thrown by a subscriber
is caught and written to the diagnostic log, and SHALL NOT allow it to abort the overlay's own
state handling.

#### Scenario: The display restore throws
- **WHEN** a `LeftDark` subscriber throws
- **THEN** the layers SHALL still be at their resting values for the stage
- **AND** the failure SHALL be written to the diagnostic log

#### Scenario: A later blackout still works
- **WHEN** a `LeftDark` subscriber has previously thrown
- **AND** the overlay enters blackout again
- **THEN** the blackout SHALL proceed normally

### Requirement: The glass is re-asserted, not set once

The overlay SHALL apply the DWM frame extension when the window is created, whenever the window
is shown, and whenever the display configuration changes.

Every other volatile Win32 state in this application is re-asserted: topmost on a three-second
timer, monitor brightness every twenty seconds while dark, HDR and brightness records at the
next launch, and window bounds on `DisplaySettingsChanged`. A blackout now performs two display
mode changes per cycle, and the frame extension SHALL NOT be assumed to survive them.

#### Scenario: After a display configuration change
- **WHEN** the display configuration changes
- **THEN** the frame extension SHALL be re-applied along with the window bounds

#### Scenario: On being shown
- **WHEN** the overlay is shown after having been collapsed
- **THEN** the frame extension SHALL be re-applied before the artifacts fade in

### Requirement: A failure to apply the glass is reported

The system SHALL write a diagnostic log entry when applying the DWM frame extension fails.

A discarded return value is how an opaque overlay becomes a silent failure with nothing to
point at.

#### Scenario: The call fails
- **WHEN** applying the frame extension returns a failure
- **THEN** the failure SHALL be written to the diagnostic log

### Requirement: Transparency can be verified on the machine that is failing

The system SHALL provide `Bubbles.exe --glass-test`, which puts a known colour on screen, shows
the overlay at the artifacts stage over it, captures the screen, reports whether that colour
came through, and exits.

Nothing in-process can observe whether the compositor honoured the frame extension: the layer
opacities can be perfectly correct while the window still paints opaque. This joins `--dim-test`,
`--hold-test` and `--inputs`, which exist for the same reason — a Windows API reporting success
while doing nothing.

The capturing process SHALL be made per-monitor DPI aware before capturing, or the capture is
silently truncated and every coordinate is wrong.

#### Scenario: Transparency working
- **WHEN** `--glass-test` runs and the overlay composites correctly
- **THEN** it SHALL report that the colour beneath came through
- **AND** SHALL exit without leaving anything on screen

#### Scenario: Transparency broken
- **WHEN** `--glass-test` runs and the overlay is opaque
- **THEN** it SHALL report that the colour beneath did not come through
- **AND** SHALL report the sampled colour so the failure can be told apart from a black desktop

#### Scenario: The capture is DPI correct
- **WHEN** `--glass-test` runs on a scaled display
- **THEN** the captured region SHALL cover the intended area rather than a fraction of it
