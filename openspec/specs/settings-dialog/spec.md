# settings-dialog Specification

## Purpose

The window in which every setting is read and changed: which settings it presents and how it
groups them, how an edit reaches the running application, what may be backed out of and how,
what it does to the idle timer while it is open, and how it treats settings the current theme
ignores.

It exists because the tray menu could not show a value and had no room. Nine settings had no
surface at all, and two menu entries were built and then dropped for want of space. A window
has room for all of them, which is what allows the menu to go back to being commands.

## Requirements

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

### Requirement: The theme is chosen from pictures, not from names

The window SHALL offer each theme as its own picture, and selecting a theme SHALL be done by
choosing its picture.

A theme is the one setting in the window that a name describes poorly. Every other setting is a
quantity or a yes-or-no, and two minutes means two minutes; a theme is a picture, and with the
screensaver held off while the window is open the only way to see it is to close the window and
wait out the idle delay.

#### Scenario: The window is opened
- **WHEN** the settings window is opened
- **THEN** every theme SHALL be shown as a picture with its name beneath
- **AND** the selected one SHALL be marked as selected

#### Scenario: A theme is chosen
- **WHEN** a theme's picture is chosen
- **THEN** that theme SHALL become the selected theme
- **AND** the marking SHALL move to it, without the window being closed and reopened

#### Scenario: Choosing without a mouse
- **WHEN** the theme pictures have keyboard focus
- **THEN** it SHALL be possible to move between them and select one from the keyboard
- **AND** the selection SHALL be reported to assistive technology as a selection

### Requirement: The picture is drawn by the code that draws the screensaver

The picture SHALL be rendered from the same drawing types the overlay uses, and SHALL NOT be a
stored image shipped alongside them.

A stored screenshot is a claim about the artwork that stops being true the moment the artwork
changes, and nothing would report the drift. The application already holds to this rule for
every image in its documentation, which is generated from the code by `--export`.

#### Scenario: The artwork changes
- **WHEN** the code that draws an artifact or a soap sprite changes
- **THEN** the picture SHALL change with it, with no separate asset to update

#### Scenario: What the picture is composed of
- **WHEN** a theme's picture is built
- **THEN** it SHALL be composed of the same layers the overlay draws, in the same order: a
  stand-in desktop, the dimming over it, and then the theme's artwork
- **AND** the stand-in desktop SHALL NOT resemble a real screen or anybody's content

### Requirement: The picture is the same every time

The picture SHALL be deterministic: the same theme SHALL produce the same picture on every
opening and on every machine.

Where the composition needs arbitrary choices -- which shapes appear and where they sit -- they
SHALL be drawn from a fixed seed. A preview that differed between openings would read as a
defect, and one that differed between machines could not be described here.

#### Scenario: The window is opened twice
- **WHEN** the settings window is opened, closed, and opened again with the same theme selected
- **THEN** the picture SHALL be identical both times

### Requirement: The picture does not follow the other settings

The picture SHALL be drawn from fixed artwork, fixed dimming and a fixed selection of shapes.
It SHALL NOT reflect `Dim`, `Opacity`, `BubbleCount`, the radii, `Animated`, or the weather and
detector settings.

It answers which theme is selected, not what the current settings look like. The window holds
the screensaver off, so it cannot honestly answer the second question, and a picture that moved
when a slider was dragged would look like it was answering it while still being wrong about
everything else.

It also keeps the picture legible: settings that dim almost to black would otherwise offer two
near-identical dark rectangles to choose between.

#### Scenario: A slider is moved
- **WHEN** the dimming, the brightness, the count or a radius is changed
- **THEN** the picture SHALL NOT change

#### Scenario: Settings that would render the artwork invisible
- **WHEN** the dimming is at its maximum and the brightness at its minimum
- **THEN** both themes' pictures SHALL remain legible and distinguishable from each other

### Requirement: The form stays readable at any window size

The window SHALL keep its contents to a readable width however wide the window is made, and
SHALL fill the height it is given.

A form is read in a column. Left to stretch, the delay dropdowns ran the full width of a
maximized window with their labels stranded at the far left; and a maximum height, which was
there to size the window to its content, capped the maximized state too, so maximizing produced
a window that neither filled the screen nor looked deliberate.

#### Scenario: The window is maximized
- **WHEN** the settings window is maximized on a wide display
- **THEN** the controls SHALL keep a readable width rather than stretching to the window's
- **AND** the window SHALL fill the height available to it

#### Scenario: The window is made narrow
- **WHEN** the window is resized smaller than its contents
- **THEN** the contents SHALL remain reachable by scrolling

### Requirement: The picture costs nothing after it is built

Each theme's picture SHALL be rendered once and reused, and SHALL NOT be left in the window as
live vector content.

The artifacts are vector drawings, which WPF re-rasterises on every composition pass -- the
documented reason `Animated` is a setting carrying a CPU warning. A settings window must not
pay that cost for a picture that never moves.

#### Scenario: The theme is switched back and forth
- **WHEN** the theme is changed away and then back within one opening of the window
- **THEN** the first picture SHALL be reused rather than rendered again

#### Scenario: The window is left open
- **WHEN** the settings window is open and untouched
- **THEN** the picture SHALL NOT redraw, animate, or run a timer

### Requirement: A picture that cannot be drawn does not take the window with it

If a theme's picture cannot be rendered, the window SHALL still open and the theme SHALL still
be selectable.

The picture is an addition to a control that already worked. It must not become a way for the
settings window -- the only place several settings can be reached at all -- to fail to open.

#### Scenario: Rendering fails
- **WHEN** a theme's picture cannot be produced
- **THEN** the window SHALL open with the theme control present and working
- **AND** the space the picture would have occupied SHALL be left empty rather than showing a
  broken or placeholder image

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
