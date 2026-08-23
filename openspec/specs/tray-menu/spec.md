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

Every menu entry the application builds SHALL be added to the menu.

This is not a truism. `Check for updates` and `Start with Windows` are both constructed in
`TrayIcon`, given working handlers, and refreshed on every menu opening -- and neither is ever
added, so no user has been able to reach either. The code that maintains them runs regardless,
which is why the omission went unnoticed.

#### Scenario: The update entry
- **WHEN** the tray menu is opened
- **THEN** an entry for checking for updates SHALL be present

#### Scenario: The startup entry
- **WHEN** the tray menu is opened
- **THEN** an entry for starting with Windows SHALL be present, ticked when startup is enabled

#### Scenario: No entry is built and abandoned
- **WHEN** the menu is constructed
- **THEN** every entry constructed SHALL be added to the menu or to one of its submenus

### Requirement: The menu offers commands, and configuration lives in the window

The tray menu SHALL offer the actions taken often and immediately. Configuration SHALL live in
the settings window.

The menu is the only surface reachable without opening anything, which makes it valuable for
what is done often and a poor container for what is set once. It cannot show a value, and it
has no room -- the two properties that caused settings to be crowded into a `Theme` submenu
alongside the pointer, the backlight and HDR, and caused two entries to be dropped entirely.

No entry SHALL be more than one level deep.

#### Scenario: What the menu offers
- **WHEN** the tray menu is opened
- **THEN** it SHALL offer starting the screensaver, blacking the screen, pausing, opening the
  settings window, checking for updates, starting with Windows, and exiting

#### Scenario: Configuration has left the menu
- **WHEN** the tray menu is opened
- **THEN** it SHALL NOT offer submenus for the start delay, the blackout delay, dimming, the
  PIN, hold-off reasons, the theme, or the appearance nudges

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

### Requirement: The startup entry reflects the system, not a stored setting

The startup entry SHALL be ticked according to the operating system's state, read when the
menu opens, rather than according to a value the application stores.

Whether the application starts with Windows is recorded by the system and can be changed
outside the application entirely.

#### Scenario: Startup was disabled outside the application
- **WHEN** startup is disabled by some other means and the tray menu is then opened
- **THEN** the entry SHALL show as not ticked
