## ADDED Requirements

### Requirement: The selected theme is shown, not only named

The window SHALL show a picture of the selected theme alongside the control that selects it.

A theme is the one setting in the window that a name describes poorly. Every other setting is a
quantity or a yes-or-no, and two minutes means two minutes; a theme is a picture, and with the
screensaver held off while the window is open the only way to see it is to close the window and
wait out the idle delay.

#### Scenario: The window is opened
- **WHEN** the settings window is opened
- **THEN** a picture of the currently selected theme SHALL be shown with the theme control

#### Scenario: The theme is changed
- **WHEN** a different theme is selected
- **THEN** the picture SHALL change to that theme
- **AND** it SHALL do so without the window being closed and reopened

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
