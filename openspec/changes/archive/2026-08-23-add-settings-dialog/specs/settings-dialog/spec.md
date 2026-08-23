## ADDED Requirements

### Requirement: Every persisted setting is reachable from the window

The settings window SHALL present every setting that `settings.json` persists. A setting SHALL
NOT be reachable only by hand-editing the file.

This is the requirement the whole change exists to satisfy. Nine settings -- `AutoUpdate`,
`UpdateCheckHours`, `MaxFps`, `ClickThrough`, `Wobble`, `SpeedVariance`, `CollectRadius`,
`FadeInSeconds` and `MonitorStandby` -- have never had any surface at all, and the two tray
entries that were dropped for want of room were dropped because there was nowhere else to put
them.

Each setting SHALL show its current value, not merely whether it differs from a preset.

#### Scenario: A setting that never had a menu entry
- **WHEN** the settings window is opened
- **THEN** `AutoUpdate`, `UpdateCheckHours`, `MaxFps`, `ClickThrough`, `Wobble`,
  `SpeedVariance`, `CollectRadius`, `FadeInSeconds` and `MonitorStandby` SHALL each be
  present and editable

#### Scenario: A numeric setting shows where it stands
- **WHEN** the window presents a setting holding a number, such as the count of shapes
- **THEN** the current value SHALL be legible from the control
- **AND** it SHALL NOT be conveyed only as a tick beside the nearest named preset

### Requirement: An edit reaches the running application immediately

An edit made in the window SHALL be applied to the running application as it is made, without
waiting for the window to close.

The application already works this way: settings are held in one mutable object that is
mutated in place and then handed to each component. The window SHALL use that same path rather
than accumulating a pending copy.

#### Scenario: A setting is changed while the window is open
- **WHEN** a setting is changed in the window
- **THEN** the overlay, the idle controller and the updater SHALL each be given the updated
  settings before the next edit is accepted

#### Scenario: No component reads a stale copy
- **WHEN** any setting is edited from any surface, the window or the tray menu
- **THEN** every component SHALL observe the change
- **AND** the settings object SHALL NOT be replaced by a different instance, because a
  component holding the previous instance would read stale values for the rest of the process

### Requirement: Legal values are decided in one place

`Clamped()` SHALL remain the only definition of what a legal value is. The window SHALL NOT
restate those bounds independently.

A control offering a value that `Clamped()` rejects would appear to accept an edit and then
silently move it somewhere else, which reads as the application losing the setting.

#### Scenario: A control cannot offer an illegal value
- **WHEN** the window presents a setting that `Clamped()` bounds
- **THEN** the range offered by the control SHALL be the range `Clamped()` enforces

#### Scenario: The bounds are asserted, not assumed
- **WHEN** the test suite runs
- **THEN** it SHALL fail if any control's range disagrees with the bound `Clamped()` applies
  to that setting

### Requirement: The blackout delay is presented as time after the screensaver starts

The window SHALL present the blackout delay as time measured from when the screensaver
starts, so that the constraint on it is visible in the label rather than applied silently
afterwards.

`Clamped()` raises `BlackoutSeconds` to at least `IdleSeconds`, so a blackout delay set below
the start delay does not mean what it says.

#### Scenario: A blackout delay below the start delay
- **WHEN** the start delay is raised above the current blackout delay
- **THEN** the window SHALL show the blackout delay as the value `Clamped()` has produced
- **AND** the relationship between the two delays SHALL be stated by the window

#### Scenario: A blackout that arrives with the screensaver
- **WHEN** the blackout delay equals the start delay, so that the screen goes black at the
  moment the artifacts would have appeared
- **THEN** the window SHALL distinguish this from no blackout at all
- **AND** closing the window SHALL leave the blackout enabled

#### Scenario: No blackout at all
- **WHEN** the setting for no blackout is chosen
- **THEN** `BlackoutSeconds` SHALL be zero
- **AND** this SHALL be offered as a distinct choice from a blackout that arrives with the
  screensaver, because the two are different settings and sharing one value between them would
  read a configured blackout back as never and switch it off

### Requirement: Cancel restores the state the window opened with

The window SHALL capture the settings as they stood when it opened, and SHALL offer a way to
restore that capture.

Applying edits immediately leaves no other way to back out of them. Closing the window by any
other means SHALL keep what is on screen, because those edits are already in effect.

#### Scenario: Backing out of a session of edits
- **WHEN** several settings have been changed and the restore-on-cancel action is taken
- **THEN** every setting SHALL return to the value it held when the window opened
- **AND** the restored values SHALL be applied to the running application by the same path as
  any other edit

#### Scenario: Closing the window keeps the edits
- **WHEN** the window is closed by its close button or by the keep action
- **THEN** the edits made while it was open SHALL remain in effect

#### Scenario: Restoring defaults is distinct from cancelling
- **WHEN** the restore-defaults action is taken
- **THEN** every setting SHALL take its default value
- **AND** this SHALL be presented separately from cancelling, which returns to the values the
  window opened with rather than to defaults

### Requirement: Settings are written to disk when the window closes

The window SHALL persist settings when it closes, and SHALL NOT write the file on every
individual edit.

Writing continuously while a slider is dragged would write the file many times a second for no
benefit.

#### Scenario: Closing the window
- **WHEN** the settings window closes, whether the edits were kept or cancelled
- **THEN** the settings then in effect SHALL be written to `settings.json`

#### Scenario: Dragging a control
- **WHEN** a continuous control is dragged through many intermediate values
- **THEN** each intermediate value SHALL be applied to the running application
- **AND** `settings.json` SHALL NOT be written for each intermediate value

### Requirement: The screensaver does not start over the settings window

While the settings window is open, the idle timer SHALL NOT start the screensaver or the
blackout.

Reading a settings window without touching the keyboard is exactly the situation the idle
timer misreads as absence. Covering the window somebody is configuring the application in
would be the most conspicuous possible instance of that failure.

The suppression SHALL be expressed as an ordinary hold-off reason, composed with the reasons
`UserBusy` already reports, rather than as a separate mechanism.

#### Scenario: The window is left open and untouched
- **WHEN** the settings window is open and the idle time passes the start delay
- **THEN** the screensaver SHALL NOT start

#### Scenario: The window is left open past the blackout delay
- **WHEN** the settings window is open and the idle time passes the blackout delay
- **THEN** the screen SHALL NOT go black

#### Scenario: The suppression ends with the window
- **WHEN** the settings window closes
- **THEN** the idle timer SHALL resume governing the screensaver

#### Scenario: An explicit request still wins
- **WHEN** the settings window is open and the screensaver is explicitly asked for from the
  tray
- **THEN** the screensaver SHALL start, because a deliberate request is not idleness

### Requirement: Asking for settings twice does not open a second window

The window SHALL be single-instance. Asking for settings while it is already open SHALL bring
the existing window forward rather than create another.

Two windows editing one settings object would each show values the other was changing.

#### Scenario: The settings entry is used while the window is open
- **WHEN** settings are requested and the window is already open
- **THEN** the existing window SHALL be activated and brought to the front
- **AND** no second window SHALL be created

### Requirement: Settings the current theme ignores are shown disabled

A setting that the selected theme does not act on SHALL be shown disabled rather than hidden
or silently inert.

The tray menu already does this for the Zone-only settings, and the reasoning holds: offering
a setting the current theme ignores invites the user to change it and conclude the application
is broken when nothing happens. Disabling says which theme it belongs to; hiding it makes the
window's contents change shape for reasons that are not apparent.

#### Scenario: A Zone-only setting under another theme
- **WHEN** the selected theme is not the Zone theme
- **THEN** the artifact detector, artifact animation, Emission, lightning and weather settings
  SHALL be shown disabled

#### Scenario: Switching back to the Zone theme
- **WHEN** the Zone theme is selected in the window
- **THEN** those settings SHALL become editable without the window being reopened

### Requirement: A setting that can suspend the machine says so

The window SHALL state, where `MonitorStandby` is offered, that it can suspend the whole
machine.

The setting drives the monitor into standby through a call that, on a machine using Modern
Standby, suspends the system rather than the panel.

It is being surfaced because the window's purpose is that no setting is hidden, but a
checkbox labelled neutrally would invite exactly the wake/sleep loop that caused it to be
abandoned.

#### Scenario: The setting is presented
- **WHEN** the window shows `MonitorStandby`
- **THEN** its label SHALL state that on a Modern Standby machine it can suspend the system
- **AND** it SHALL be grouped apart from the everyday controls

#### Scenario: The default is unchanged
- **WHEN** settings are created fresh
- **THEN** `MonitorStandby` SHALL be off
