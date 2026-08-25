## MODIFIED Requirements

### Requirement: The blackout takes the keyboard dark

The system SHALL take the keyboard's lighting off when the overlay goes dark, rather than
releasing the device, because releasing it returns the keyboard to whatever its owner was
showing and that is probably lit.

This SHALL hold for every blackout of a session, not only the first. Between blackouts the
device has been given back and the vendor's software has reasserted its own lighting, so a
later blackout that failed to take the keys dark would leave a lit keyboard beside a black
screen -- the precise thing this requirement exists to prevent.

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

### Requirement: Waking gives the keyboard back by releasing it

The system SHALL give the keyboard back on leaving the blackout, on exit, and at the next
startup if the previous run ended without doing so.

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

### Requirement: Failure is silent, and decided once per session

The system SHALL make one attempt per session to *find* a keyboard, on the first Emission after
the setting is enabled. Where no supported keyboard is attached, or the device will not open,
the system SHALL log the reason once through `Diagnostics` and stay off for the remainder of
the session.

"Once per session" SHALL govern the search, not the holding. A keyboard that was found once and
has since been given back SHALL be opened again when it is next needed, because that is not a
retry of a decision made against: the search succeeded, and the device is known to be there.

A keyboard that stops accepting writes mid-session SHALL be treated as a failure and given up
on for the remainder of the session, rather than reopened. The system SHALL notice that the
write was refused rather than discarding the answer, SHALL say so once, and SHALL then stop
computing colours for it, as it does for a search that found nothing.

The system SHALL NOT show a dialog, schedule a retry, or attempt a second search mid-Emission.

#### Scenario: No supported keyboard
- **WHEN** the setting is on
- **AND** no ASUS Aura keyboard is attached
- **THEN** the failure SHALL be logged once
- **AND** no further attempt SHALL be made this session
- **AND** no dialog SHALL be shown

#### Scenario: A later Emission in the same session
- **WHEN** the search has already failed once this session
- **AND** a further Emission begins
- **THEN** no search SHALL be made

#### Scenario: A later Emission after a successful session
- **WHEN** a keyboard was found earlier this session and has since been given back
- **AND** a further Emission begins
- **THEN** the device SHALL be opened again
- **AND** its colours SHALL reach the keyboard

#### Scenario: The keyboard stops accepting writes
- **WHEN** a colour is sent and the device reports that it could not be written
- **THEN** the failure SHALL be logged
- **AND** no further colour SHALL be computed or sent for the remainder of the session
- **AND** the device SHALL NOT be reopened

#### Scenario: Nothing is owed for a keyboard never found
- **WHEN** the search fails
- **THEN** no record SHALL be written to disk

## ADDED Requirements

### Requirement: The device is asked whether it is held, never assumed

The system SHALL determine whether a keyboard is currently in hand by asking the device, which
is the only thing that knows. The device releases itself on a hand-back, on a refused write and
on an error, so any answer cached above it expires without notice.

The system SHALL NOT infer that a device is open from the fact that it was opened earlier.

#### Scenario: A device that has released itself
- **WHEN** the device has been released by any path
- **THEN** it SHALL report that it is not open

#### Scenario: A device in hand
- **WHEN** the device has been opened and not released
- **THEN** it SHALL report that it is open

#### Scenario: Deciding whether to open
- **WHEN** the system needs a device to write to
- **THEN** it SHALL consult the device's own account of whether it is open
- **AND** SHALL open it if it is not

### Requirement: A test keyboard behaves as unkindly as a real one

A substitute keyboard used to exercise this layer SHALL release itself when it is restored, and
SHALL be able to refuse a write, because the real device does both and every defect worth
catching here lives in what happens afterwards.

Tests SHALL assert what reaches the keyboard rather than how many times a device was opened.
The count of opens is an implementation detail that changed with this requirement; the keys
going dark is the behaviour that was wanted all along.

#### Scenario: Restoring the substitute
- **WHEN** the substitute keyboard is restored
- **THEN** it SHALL afterwards report that it is not open

#### Scenario: A substitute that refuses writes
- **WHEN** the substitute is configured to refuse writes
- **AND** a colour is sent to it
- **THEN** it SHALL report the write as failed
