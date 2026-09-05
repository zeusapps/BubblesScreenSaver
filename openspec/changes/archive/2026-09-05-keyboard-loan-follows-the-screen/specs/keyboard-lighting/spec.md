## MODIFIED Requirements

### Requirement: The blackout takes the keyboard dark

The system SHALL take the keyboard's lighting off when the overlay goes dark, rather than
releasing the device, because releasing it returns the keyboard to whatever its owner was
showing and that is probably lit.

This SHALL hold for every blackout of a session, not only the first. Between blackouts the
device has been given back and the vendor's software has reasserted its own lighting, so a
later blackout that failed to take the keys dark would leave a lit keyboard beside a black
screen -- the precise thing this requirement exists to prevent.

The system SHALL keep the keys dark for as long as the screen is black, by sending black again on
an interval, and SHALL NOT treat one send as sufficient. The device is opened with shared access
because the lighting is not this application's exclusively, so another owner may repaint the keys
at any moment; the protocol only accepts commands and cannot be asked what the keys are showing,
so the system cannot detect that this has happened and SHALL assert rather than check. This is the
same stance already taken toward a monitor backlight that drifts back up during a blackout, with
the reading step removed because this hardware has none.

The interval SHALL relax while nothing is happening, from a floor to a ceiling, and SHALL return
to the floor whenever the machine passes through something that may have disturbed the keys.

The clock it relaxes against SHALL be the time since the last such disturbance, not the time since
the screen went black. A ramp measured from the start of the blackout reaches its ceiling within
the first minute and a blackout is hours, so all but a handful of its waits happen at the ceiling
either way; measured from the last disturbance, the attentive phase happens again every time there
is a reason for it.

Reaching black SHALL itself count as a disturbance, since that is when the blackout's work on the
displays begins.

The system SHALL treat as a disturbance those transitions it can actually observe -- the session
locking or unlocking, a power mode change, a display reconfiguration -- and SHALL say black again
at once when one arrives rather than waiting out the interval in hand. It cannot observe the
repaint itself, and SHALL NOT be designed as though it could.

The ceiling SHALL be the same interval the display blackout already uses against the same class of
problem, and is the longest a repaint can sit on the keys unanswered.

Re-asserting SHALL stop when the screen is no longer black, and SHALL stop when there is no longer
a keyboard to send to.

#### Scenario: The overlay goes dark
- **WHEN** `WentDark` is raised
- **THEN** the keyboard SHALL be taken to black
- **AND** the device SHALL NOT be released while the screen stays black

#### Scenario: A blackout that was not an Emission
- **WHEN** the screen reaches black by a plain fade, with no Emission
- **THEN** the keyboard SHALL still be taken dark
- **AND** no Emission colour SHALL have been sent

#### Scenario: The second blackout of a session
- **WHEN** a session has already been through one full blackout and hand-back
- **AND** the overlay goes dark again
- **THEN** the keyboard SHALL be taken to black again
- **AND** the colours of the Emission leading up to it SHALL have reached the keyboard

#### Scenario: The screen stays black
- **WHEN** the screen has been black for longer than the re-assert interval
- **THEN** black SHALL have been sent to the keyboard more than once
- **AND** the device SHALL still be held

#### Scenario: Somebody else repaints the keys
- **WHEN** another owner of the lighting writes its own colour while the screen is black
- **THEN** the system SHALL send black again without having been told that it happened

#### Scenario: Attention relaxes while nothing happens
- **WHEN** successive intervals are taken with no disturbance between them
- **THEN** each SHALL be longer than the one before it

#### Scenario: The ceiling is a bound, not a tendency
- **WHEN** the interval is relaxed repeatedly without limit
- **THEN** it SHALL stop at the ceiling and stay there

#### Scenario: A disturbance arrives
- **WHEN** the machine reports a transition that may have disturbed the keys
- **AND** the screen is black
- **THEN** black SHALL be sent at once, without waiting out the interval in hand
- **AND** the interval SHALL return to the floor

#### Scenario: A disturbance is not a colour
- **WHEN** a disturbance wakes the system during a blackout
- **THEN** the blackout SHALL still be held afterwards
- **AND** no colour SHALL have been sent

#### Scenario: A disturbance on a machine that never took a keyboard
- **WHEN** a disturbance arrives and no keyboard has ever been borrowed this session
- **THEN** no device SHALL be opened
- **AND** no thread SHALL be started for it

#### Scenario: The blackout ends
- **WHEN** the overlay leaves the blackout
- **THEN** no further black SHALL be sent

#### Scenario: A keyboard that stops answering mid-blackout
- **WHEN** a re-assert is refused by the device
- **THEN** the failure SHALL be logged once
- **AND** no further re-assert SHALL be attempted for the remainder of the session

#### Scenario: A device handed back mid-blackout
- **WHEN** the device reports that it is no longer open while the screen is still black
- **THEN** the system SHALL open it again and send black
- **AND** SHALL NOT treat that as a failed search

### Requirement: Waking gives the keyboard back by releasing it

The system SHALL give the keyboard back when the screensaver leaves the screen, on exit, and at the
next startup if the previous run ended without doing so.

Leaving the screen SHALL mean either of the two ways it happens: leaving the blackout, and the
artifacts stage ending without a blackout ever being reached. The loan lasts as long as the
screensaver is on screen, which is what enabling keyboard weather is described as costing, so every
route by which the screensaver leaves SHALL settle it. A loan that could only be settled by leaving
a blackout was one an ordinary "you came back to your desk" never settled at all, and it left both
the device held and a Windows setting changed for the rest of the run.

The hand-back raised by the artifacts stage ending SHALL NOT be the same event as the one raised by
leaving the blackout, because that one also carries the request to lock the workstation, and a
lock SHALL NOT become reachable from a path on which the screen never went black.

Giving it back SHALL mean releasing the device. The protocol only accepts commands and cannot
be asked what the keyboard was showing beforehand, so the system SHALL NOT record a colour to
restore to, and SHALL NOT invent one. Whatever owns the lighting reasserts itself once the
device is free, and a process that dies has its handles closed for it.

The fact that a keyboard was taken SHALL be written to disk before the first colour is sent, so
that a run ending badly leaves a record behind. That SHALL be true of every loan, not only the
first of a session: a keyboard given back and taken again is a fresh debt and SHALL be recorded
as one.

Having given the keyboard back, the system SHALL NOT afterwards behave as though it still holds
it. Anything the system remembers about holding a device SHALL be reconciled with the device
itself before that device is written to.

#### Scenario: Leaving the blackout
- **WHEN** `LeftDark` is raised
- **THEN** the keyboard SHALL be released
- **AND** the record of the debt SHALL be cleared

#### Scenario: The artifacts stage ends without a blackout
- **WHEN** the keyboard has been taken by the ambient weather
- **AND** the screensaver leaves the screen without the blackout ever being reached
- **THEN** the keyboard SHALL be released
- **AND** any Dynamic Lighting loan SHALL be settled
- **AND** the record of both debts SHALL be cleared

#### Scenario: Coming back to the desk does not lock the machine
- **WHEN** the artifacts stage ends without a blackout
- **THEN** no request to lock the workstation SHALL be made

#### Scenario: A blackout that ends normally
- **WHEN** the overlay leaves the blackout and then leaves the screen
- **THEN** the keyboard SHALL be handed back once
- **AND** the second hand-back SHALL find nothing owed and do nothing

#### Scenario: The overlay was never on screen
- **WHEN** the overlay is hidden without having been shown, as it is at startup
- **THEN** no hand-back SHALL be raised

#### Scenario: The record is written first
- **WHEN** the layer is about to send its first colour of a session
- **THEN** the debt SHALL already be recorded on disk

#### Scenario: The record is written again for a later loan
- **WHEN** the keyboard has been given back and a later Emission takes it again
- **THEN** the debt SHALL be recorded on disk again before that Emission's first colour

#### Scenario: The previous run ended badly
- **WHEN** the application starts
- **AND** a pending keyboard record is found on disk
- **THEN** it SHALL be settled before anything else is sent to the device

#### Scenario: Nothing is sent to a device already given back
- **WHEN** the device has been released
- **AND** a colour is to be sent
- **THEN** the system SHALL NOT write to the released device

### Requirement: Colours are sent on change, not on every frame

The system SHALL compute the colour every frame and send it only when it has moved by a visible
amount, and SHALL observe a floor on the interval between sends, measured on the Emission's own
clock rather than the wall clock.

The wavefront flare and lightning flashes SHALL be exempt from that rationing.

The blackout's re-assert SHALL also be exempt, and SHALL NOT pass through this rationing at all. It
is not a frame and carries no new colour: it is the same black, sent again precisely because
nothing in this application has changed and something outside it may have. The rule that suppresses
a colour which has not moved is the rule that would suppress every re-assert there is.

#### Scenario: A slow ramp through the buildup
- **WHEN** consecutive frames yield colours that differ imperceptibly
- **THEN** the later frames SHALL NOT each produce a send

#### Scenario: The cost of a whole Emission
- **WHEN** an Emission runs at the default frame rate with no strikes
- **THEN** it SHALL cost fewer than a quarter as many writes as there were frames

#### Scenario: The flare is not rationed
- **WHEN** the wavefront flare is reached
- **THEN** it SHALL be sent, whatever the interval since the last send

#### Scenario: The re-assert is not rationed
- **WHEN** black is re-asserted during a blackout
- **AND** the last colour sent was already black
- **THEN** it SHALL be sent anyway

### Requirement: The keyboard is held for the artifacts stage, not surrendered between states

While ambient weather is enabled and the artifacts are on screen, the system SHALL hold the
device rather than releasing it when the weather is `Clear`.

Releasing on `Clear` would return the keyboard to the vendor's software, which lights it -- so
surrendering during the calmest weather would produce more light, not less, and would hand the
device back and forth roughly once a minute.

The device SHALL still be released on the paths that release it: the screensaver leaving the
screen, by either route, exit, and a record found at startup.

#### Scenario: Clear weather
- **WHEN** the weather is `Clear` and the artifacts are on screen
- **THEN** the device SHALL remain held
- **AND** the keys SHALL be unlit

#### Scenario: Waking
- **WHEN** the overlay leaves the blackout
- **THEN** the device SHALL be released, as it already is

#### Scenario: Leaving the screen from the artifacts stage
- **WHEN** the artifacts stage ends without a blackout
- **THEN** the device SHALL be released

### Requirement: Off by default, and free for everybody who leaves it off

The system SHALL default the keyboard lighting setting to off, and SHALL enumerate no devices
and open no handles while it is off.

Nothing the blackout does to hold the keys dark SHALL run while the setting is off: no device is
open, so there is nothing to re-assert, and the system SHALL NOT wake to discover that.

#### Scenario: A machine that never enables it
- **WHEN** the setting is off
- **AND** an Emission begins
- **THEN** no device SHALL be opened
- **AND** the Emission SHALL be indistinguishable from one in a build without this feature

#### Scenario: A blackout with the setting off
- **WHEN** the setting is off
- **AND** the screen reaches black and stays there
- **THEN** nothing SHALL be sent to any keyboard
- **AND** no re-assert SHALL be scheduled

#### Scenario: Upgrading an existing installation
- **WHEN** a `settings.json` written before this feature is loaded
- **THEN** the setting SHALL read as off

#### Scenario: The setting is reachable
- **WHEN** the settings window is opened
- **THEN** the keyboard lighting setting SHALL appear there, as every persisted setting does
