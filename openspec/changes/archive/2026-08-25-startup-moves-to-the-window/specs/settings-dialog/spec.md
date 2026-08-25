## ADDED Requirements

### Requirement: Starting with Windows is set in the window, and read from the system

The settings window SHALL present whether the application starts with Windows, as a control
that shows its current state.

The state SHALL be read from the operating system each time the window opens, and SHALL NOT be
stored by this application. Startup can be turned off from Task Manager, from Windows Settings,
or by another copy of this application, and a value kept here would disagree with the machine
the first time any of those happened.

Changing the control SHALL take effect at once, by the same path that registers and
unregisters startup, rather than being applied when the window closes.

This control SHALL be the exception the window states rather than hides: it is not a setting
`settings.json` persists, and the window's rules about persisting and about defaults are
written for settings that are.

#### Scenario: Reading the current state
- **WHEN** the settings window is opened
- **THEN** the startup control SHALL show whether the application is registered to start with
  Windows

#### Scenario: Changed outside the application
- **WHEN** startup is turned off by some other means and the settings window is then opened
- **THEN** the control SHALL show it as off

#### Scenario: Turning it on
- **WHEN** the startup control is turned on
- **THEN** the application SHALL be registered to start with Windows immediately
- **AND** the Start Menu entry SHALL be written

#### Scenario: Nothing is persisted for it
- **WHEN** the settings window closes after the startup control was changed
- **THEN** `settings.json` SHALL NOT gain a key recording startup

### Requirement: Cancel restores startup, and restoring defaults does not touch it

Where the startup control was changed while the window was open, cancelling SHALL put it back
to the state it was in when the window opened. Where it was not changed, cancelling SHALL write
nothing.

Cancel means undoing what was done in this window. Leaving somebody registered because the
control that registered them was not a `settings.json` setting would be the window quietly
keeping one of the edits it promised to drop.

Restoring defaults SHALL leave startup exactly as it is.

Restoring defaults means putting the screensaver back to how it ships. Whether the application
starts with Windows is a property of the installation rather than of the screensaver's
appearance, and the action is reached by somebody who dislikes what is on screen. Unregistering
their autostart, and removing their Start Menu entry with it, is a longer reach than that
action implies.

#### Scenario: Cancelling after turning startup on
- **WHEN** the startup control is turned on and the window is then cancelled
- **THEN** the application SHALL NOT be registered to start with Windows

#### Scenario: Cancelling after turning startup off
- **WHEN** the startup control is turned off and the window is then cancelled
- **THEN** the application SHALL be registered to start with Windows

#### Scenario: Cancelling a window in which startup was not touched
- **WHEN** the window is cancelled and the startup control was never changed
- **THEN** no registration SHALL be written or removed

#### Scenario: Restoring defaults
- **WHEN** the restore-defaults action is taken
- **THEN** startup SHALL be left in whatever state it was already in

### Requirement: Registering to start with Windows also makes the application findable

Where the system registers itself to start with Windows, it SHALL also write a Start Menu entry
naming the application, so that it can be found by name in Windows search and started again
after it has been exited.

Windows search indexes Start Menu shortcuts and does not index the `Run` key, so an application
that writes only the latter cannot be found by name at all. There is no installer here to have
created an entry, and somebody who exits from the tray otherwise has no way to start the
application again short of knowing its path.

Where the system removes that registration, it SHALL remove the entry as well, leaving nothing
behind that it did not leave behind before.

Both SHALL be written for the current user only, requiring no elevation and touching no other
account.

Failure to write or remove the entry SHALL be silent and SHALL NOT prevent the registration
itself, or the application, from proceeding. There is nowhere to report it to, and a machine
that refuses a shortcut is not a machine that should fail to run the screensaver.

An installation already registered to start with Windows SHALL have the entry reconciled
without being asked, so that the entry reaches installations that predate this requirement.
Reconciling SHALL be idempotent.

#### Scenario: Turning startup on
- **WHEN** the system is asked to start with Windows
- **THEN** a Start Menu entry naming the application SHALL exist
- **AND** it SHALL point at the running executable

#### Scenario: Turning startup off
- **WHEN** the system is asked not to start with Windows
- **THEN** the Start Menu entry SHALL NOT exist
- **AND** the `Run` value SHALL NOT exist

#### Scenario: An installation that predates this
- **WHEN** the application starts and is already registered to start with Windows
- **AND** no Start Menu entry exists
- **THEN** the entry SHALL be written

#### Scenario: Reconciling twice
- **WHEN** the entry is reconciled on a machine where it already exists and is correct
- **THEN** nothing SHALL change

#### Scenario: Not registered to start with Windows
- **WHEN** the application starts and is not registered to start with Windows
- **THEN** no Start Menu entry SHALL be written

#### Scenario: The machine refuses the entry
- **WHEN** writing the Start Menu entry fails
- **THEN** the registration SHALL still be recorded
- **AND** the application SHALL continue without reporting an error

## MODIFIED Requirements

### Requirement: A setting says what it actually reaches

The window SHALL describe a setting by what it does, and SHALL NOT attribute to it an effect
it cannot have.

`MonitorStandby` is the case that made this worth writing down. It was first offered with a
warning that it could suspend the whole machine on a Modern Standby laptop. That was untrue,
and it belonged to a different mechanism: the `SC_MONITORPOWER` broadcast this application
abandoned years ago. What the setting sends is a DDC/CI power request, which reaches external
monitors and cannot reach the operating system's power state at all.

A false warning is worse than none. It frightens somebody away from a setting that would have
suited them, and it spends the credibility the labels need for the warnings that are real.

Where a control reaches outside this application -- changing the machine rather than the
screensaver -- it SHALL say so where it is presented, and SHALL say what it leaves behind.

#### Scenario: A setting with a limited reach
- **WHEN** the window shows `MonitorStandby`
- **THEN** its label SHALL say that it reaches external monitors over DDC/CI
- **AND** it SHALL say that it does nothing for a machine with only its own built-in panel
- **AND** it SHALL NOT claim any effect on the machine's power state

#### Scenario: A setting that depends on another
- **WHEN** the window shows a setting that only takes effect while another is on
- **THEN** it SHALL name the setting it depends on

#### Scenario: A control that changes the machine
- **WHEN** the window shows the startup control
- **THEN** it SHALL say that the application will start when signing in
- **AND** it SHALL say that an entry is put in the Start Menu

#### Scenario: The default is unchanged
- **WHEN** settings are created fresh
- **THEN** `MonitorStandby` SHALL be off
