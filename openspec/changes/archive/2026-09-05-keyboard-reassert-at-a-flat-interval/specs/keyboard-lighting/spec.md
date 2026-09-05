## REMOVED Requirements

### Requirement: The blackout takes the keyboard dark

**Reason**: Re-stated below without the ramp. The interval between re-asserts was specified as
relaxing from a floor to a ceiling and returning to the floor on a disturbance; a week of
production showed 99.2% of all waits falling at the ceiling, because the observable transitions
the ramp relaxed from are rare and a blackout is hours. The ceiling was therefore the only
interval that materially existed, and specifying a ramp around it described behaviour the system
did not meaningfully have. Two scenarios go with it -- "Attention relaxes while nothing happens"
and "The ceiling is a bound, not a tendency" -- because neither describes anything that still
occurs.

**Migration**: None. Everything the requirement asked for other than the shape of the interval is
carried across unchanged into "The blackout holds the keyboard dark", including every other
scenario. The name moves with it: what the requirement is mostly about is the holding, not the
first packet. No persisted state, setting, or wire format is involved, and the difference is visible
only within a single blackout.

## ADDED Requirements

### Requirement: The blackout holds the keyboard dark

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

The interval SHALL be constant for the whole of a blackout. It SHALL NOT vary with how long the
screen has been black, nor with how long it has been since anything happened.

The interval is the longest a repaint can sit on the keys unanswered, because nothing here can
observe a repaint and the wait in hand is therefore the whole of the exposure. A repaint arrives
at no particular moment within that wait, so the interval SHALL be chosen as the worst case that
is acceptable to see, not as an average.

An interval that varies SHALL NOT be preferred on the grounds that attention is worth paying after
a disturbance and worth relaxing without one. Measured in production, all but a hundredth of the
waits taken fall at the relaxed end, because the transitions the system can observe are rare and a
blackout is hours; the relaxed end is therefore the only interval that materially exists, and a
scheme that hides it behind a ramp obscures the one number that decides the behaviour.

The interval SHALL NOT be derived from the interval the display blackout uses against a drifting
monitor backlight. That mechanism reads each display before it writes and acts only on what has
actually moved, so its interval bounds a detection delay on hardware that answers questions. This
one bounds a blind assertion on hardware that does not, and the two SHALL be free to differ.

The cost of the interval SHALL be weighed against what this application already writes to the same
device: an Emission sends colours to it several times a second for the whole of its length, so a
blackout interval far longer than that is not the expensive part of this feature and SHALL NOT be
lengthened as though it were.

The system SHALL treat as a disturbance those transitions it can actually observe -- the session
locking or unlocking, a power mode change, a display reconfiguration -- and SHALL say black again
at once when one arrives rather than waiting out the interval in hand. It cannot observe the
repaint itself, and SHALL NOT be designed as though it could. A disturbance SHALL affect only when
the next black is sent, and SHALL NOT change the interval that follows it.

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

#### Scenario: The interval does not drift
- **WHEN** many successive intervals are taken with no disturbance between them
- **THEN** each SHALL be the same length as the first
- **AND** none SHALL be longer than the worst-case exposure the interval is chosen to bound

#### Scenario: A disturbance arrives
- **WHEN** the machine reports a transition that may have disturbed the keys
- **AND** the screen is black
- **THEN** black SHALL be sent at once, without waiting out the interval in hand
- **AND** the interval used afterwards SHALL be unchanged

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
