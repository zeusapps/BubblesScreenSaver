# tray-menu Specification

## Purpose

What the tray menu offers now that configuration has left it: the rule that every entry it
builds is reachable, the neutral vocabulary it names the screensaver with, the labels that
describe what a command is about to do, and the boundary deciding whether something belongs on
the menu or in the settings window.

The menu is the only surface reachable without opening anything, which makes it good for what
is done often and poor for what is set once.

## Requirements

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

### Requirement: The menu names the screensaver neutrally

Where the menu refers to the thing drawn when the machine goes idle, it SHALL use one neutral
word and SHALL NOT name a particular theme's visual.

The menu said `Start bubbles now` and `More bubbles` while the default theme was the Zone,
naming a visual most users never see. A neutral word stays true whichever theme is selected
and does not need to be revised when a theme is added.

#### Scenario: Starting the screensaver
- **WHEN** the tray menu is opened
- **THEN** the entry that starts the screensaver SHALL NOT name the bubbles or any other
  theme's visual

### Requirement: The blackout command names what it will actually do

Where a theme turns the blackout into a distinct event, the command SHALL say so, because the
label is describing an action about to be taken rather than a visual style.

Under the Zone theme with Emissions enabled, blacking the screen runs an Emission first. A
command reading `Black screen now` would understate twelve seconds of storm.

#### Scenario: The Zone theme with Emissions enabled
- **WHEN** the menu is opened while the Zone theme is selected and Emissions are enabled
- **THEN** the blackout command SHALL name the Emission

#### Scenario: Any other combination
- **WHEN** the menu is opened under any other theme, or with Emissions disabled
- **THEN** the blackout command SHALL name the black screen

### Requirement: The update entry states what clicking it will do

The update entry SHALL describe the action it will take, which differs according to whether an
update has already been downloaded.

#### Scenario: No update is staged
- **WHEN** the menu is opened and no update has been downloaded
- **THEN** the entry SHALL offer to check for updates

#### Scenario: An update is staged
- **WHEN** the menu is opened and an update has been downloaded and is waiting
- **THEN** the entry SHALL name the waiting version and say that choosing it installs and
  restarts

#### Scenario: A check is in progress
- **WHEN** a check for updates has been started from the menu and has not yet finished
- **THEN** the entry SHALL indicate that a check is under way

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
