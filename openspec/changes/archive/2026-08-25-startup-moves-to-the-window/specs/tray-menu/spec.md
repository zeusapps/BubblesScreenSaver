## REMOVED Requirements

### Requirement: The startup entry reflects the system, not a stored setting

**Reason**: The entry it governs has left the menu, and the requirement could never be met
while it was there. The menu is built without an image margin, which is where the check glyph
is drawn, so the tick it demanded rendered nothing. The rule it carried -- that the state is
read from the operating system rather than from a value the application stores -- is right and
is re-stated against the settings window's control in `settings-dialog`.

**Migration**: The control moves to the settings window. Nothing was persisted for it, so
nothing has to be carried across.

### Requirement: Registering to start with Windows also makes the application findable

**Reason**: It describes what the registration writes, not what the menu offers, and it was
placed here only because the menu was the one place startup was named. It moves to
`settings-dialog` with the control that triggers it. Unchanged in substance.

**Migration**: None. The behaviour is unchanged; only the capability that states it moves.

## MODIFIED Requirements

### Requirement: Every entry the menu constructs is reachable

Every `ToolStripMenuItem` the tray menu constructs SHALL be added to the menu or to one of its
submenus. An entry SHALL NOT be built, wired to an action, and then left unreachable.

An entry the menu builds and does not show is a command the user cannot reach and the compiler
cannot warn about.

The menu SHALL NOT rely on a check mark to convey the state of an entry. It is constructed
without an image margin, which is where a check mark is drawn, so an entry whose meaning
depends on being ticked shows nothing at all. Where an entry has a state worth reading, it
SHALL be moved to the settings window or SHALL say its state in its own label.

#### Scenario: No entry is built and abandoned
- **WHEN** the menu is constructed
- **THEN** every entry constructed SHALL be added to the menu or to one of its submenus

#### Scenario: An entry whose state cannot be shown
- **WHEN** an entry's meaning would depend on a check mark
- **THEN** it SHALL NOT be presented that way in the menu

### Requirement: The menu offers commands, and configuration lives in the window

The tray menu SHALL offer the actions taken often and immediately. Configuration SHALL live in
the settings window.

The menu is the only surface reachable without opening anything, which makes it valuable for
what is done often and a poor container for what is set once. It cannot show a value, and it
has no room -- the two properties that caused settings to be crowded into a `Theme` submenu
alongside the pointer, the backlight and HDR, and caused two entries to be dropped entirely.

That rule SHALL admit no exceptions on the grounds of habit. Whether the application starts
with Windows is set once and read rarely, and it stayed on the menu after everything else of
its kind had left, where its state could not be seen at all.

No entry SHALL be more than one level deep.

#### Scenario: What the menu offers
- **WHEN** the tray menu is opened
- **THEN** it SHALL offer starting the screensaver, blacking the screen, pausing, opening the
  settings window, checking for updates, and exiting

#### Scenario: Configuration has left the menu
- **WHEN** the tray menu is opened
- **THEN** it SHALL NOT offer submenus for the start delay, the blackout delay, dimming, the
  PIN, hold-off reasons, the theme, or the appearance nudges
- **AND** it SHALL NOT offer starting with Windows

#### Scenario: Nothing is buried
- **WHEN** the tray menu is opened
- **THEN** every entry SHALL be reachable without opening more than one submenu
