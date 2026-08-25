## MODIFIED Requirements

### Requirement: The startup entry reflects the system, not a stored setting

The startup entry SHALL be ticked according to the operating system's state, read when the
menu opens, rather than according to a value the application stores.

Whether the application starts with Windows is recorded by the system and can be changed
outside the application entirely.

The per-user `Run` value SHALL remain the authority on that state. Where other things are
written to the machine alongside it, their presence or absence SHALL NOT change what the entry
shows.

#### Scenario: Startup was disabled outside the application
- **WHEN** startup is disabled by some other means and the tray menu is then opened
- **THEN** the entry SHALL show as not ticked

#### Scenario: The registration is the authority
- **WHEN** the application is registered to start with Windows
- **AND** something else the registration writes has been removed from the machine
- **THEN** the entry SHALL still show as ticked

## ADDED Requirements

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
itself, or the application, from proceeding. There is nowhere to report it to from a tray
toggle, and a machine that refuses a shortcut is not a machine that should fail to run the
screensaver.

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

### Requirement: The application is recognisable where it is found

The executable SHALL carry an icon, so that the Start Menu entry, Windows search, Alt-Tab and
the taskbar show the application rather than a generic default.

The icon SHALL be rendered from the same artwork the tray icon is drawn from, rather than drawn
separately, so that the two cannot come to disagree about what this application looks like. It
SHALL carry the sizes Windows asks for at the scales those surfaces use, rather than one size
stretched.

The system SHALL provide a way to render that icon from the artwork, so it can be regenerated
when the artwork changes rather than maintained by hand.

#### Scenario: Finding it in search
- **WHEN** the application is found through the Start Menu entry
- **THEN** it SHALL be shown with its own icon

#### Scenario: The icon comes from the artwork
- **WHEN** the icon is regenerated
- **THEN** it SHALL be produced from the same source the tray icon uses

#### Scenario: More than one size
- **WHEN** the icon is inspected
- **THEN** it SHALL contain more than one size
