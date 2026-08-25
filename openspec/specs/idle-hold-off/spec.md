# idle-hold-off Specification

## Purpose
TBD - created by archiving change hold-off-during-media-playback. Update Purpose after archive.
## Requirements
### Requirement: A hold-off reason names which stages it suppresses

The system SHALL express every hold-off reason as the set of stages it suppresses, rather than
as a single veto that returns the overlay to `Active`.

A reason SHALL declare, independently, whether it suppresses `Artifacts` and whether it
suppresses `Blackout`. These are independent because the two stages answer different questions:
the artifacts are something drawn at somebody, and the blackout is the absence of anything
drawn at all. A reason may object to one and not the other, in either direction.

The stage SHALL be resolved as the furthest stage the idle timer asks for that is not
suppressed, falling back to `Active`.

#### Scenario: A reason suppressing both stages
- **WHEN** a reason suppressing `Artifacts` and `Blackout` holds
- **THEN** the overlay SHALL be at `Active`, whatever the idle timer reads

#### Scenario: A reason suppressing only the artifacts
- **WHEN** a reason suppressing `Artifacts` but not `Blackout` holds
- **AND** the idle time has passed `IdleSeconds` but not `BlackoutSeconds`
- **THEN** the overlay SHALL be at `Active`

#### Scenario: The same reason, once the blackout threshold passes
- **WHEN** a reason suppressing `Artifacts` but not `Blackout` holds
- **AND** the idle time has passed `BlackoutSeconds`
- **THEN** the overlay SHALL be at `Blackout`, having never shown the artifacts

#### Scenario: No reason at all
- **WHEN** no reason holds
- **THEN** the overlay SHALL advance by the idle timer alone

### Requirement: Reasons compose by union of what they suppress

Where more than one reason holds, the system SHALL suppress every stage that any of them
suppresses, and SHALL report the reason that suppresses the stage actually being withheld.

#### Scenario: A permissive and a strict reason together
- **WHEN** a music session is playing, suppressing only the artifacts
- **AND** the microphone is in use, suppressing both stages
- **THEN** the overlay SHALL be at `Active`
- **AND** the reported reason SHALL name the microphone

### Requirement: Presence signals suppress both stages

The system SHALL suppress both `Artifacts` and `Blackout` while any of the following hold: the
session is locked, the microphone is in use, the camera is in use, a full-screen or presenting
application is reported, a window fills a monitor, or sound is on the output meter.

The session lock SHALL have no setting to turn it off.

#### Scenario: On a call
- **WHEN** an application is holding the microphone open
- **AND** `PauseWhileMicrophoneInUse` is on
- **THEN** the overlay SHALL be at `Active`

#### Scenario: Locked
- **WHEN** the session is locked
- **THEN** the overlay SHALL be at `Active` regardless of every pause setting

### Requirement: A playing video session suppresses both stages

The system SHALL read the media sessions Windows exposes through
`GlobalSystemMediaTransportControlsSessionManager`, and SHALL suppress both `Artifacts` and
`Blackout` while any session reports a `PlaybackStatus` of `Playing` together with a
`PlaybackType` of `Video`.

This SHALL NOT depend on the session producing any sound. Silent footage, a clip with no audio
track, a muted player and a player routed to an unmetered endpoint are all watched the same way.

#### Scenario: Silent footage in a window
- **WHEN** a media session reports playing video
- **AND** the output meter reads silence
- **AND** no window fills a monitor
- **THEN** the overlay SHALL be at `Active`

#### Scenario: Muted playback
- **WHEN** a media session reports playing video and the application is muted
- **THEN** the overlay SHALL be at `Active`

### Requirement: A playing music session suppresses only the artifacts

The system SHALL suppress `Artifacts`, and SHALL NOT suppress `Blackout`, while a session
reports a `PlaybackStatus` of `Playing` with a `PlaybackType` of `Music`.

Listening is not watching. The artifacts are the intrusive part and are withheld; the panel
SHALL still reach black on schedule, because an album must not keep an OLED lit for hours over
a static desktop.

#### Scenario: An album playing over a static desktop
- **WHEN** a media session reports playing music and nothing else holds
- **AND** the idle time passes `IdleSeconds`
- **THEN** the artifacts SHALL NOT be shown

#### Scenario: The same album, later
- **WHEN** that music is still playing
- **AND** the idle time passes `BlackoutSeconds`
- **THEN** the overlay SHALL go to `Blackout`

#### Scenario: Music stops while the screen is black
- **WHEN** the screen is at `Blackout`
- **AND** the music stops
- **THEN** the screen SHALL remain at `Blackout` until there is input

### Requirement: Only a playing session is a reason

The system SHALL treat a media session as a reason only while it reports a `PlaybackStatus` of
`Playing`. A session that merely exists, and one reporting `Paused`, `Stopped`, `Changing` or
`Closed`, SHALL NOT hold the overlay off.

A session whose `PlaybackType` is absent, or is `Image`, SHALL NOT hold the overlay off.

An unbounded hold-off is the failure this codebase guards against most carefully — it is the
`QUNS_BUSY` mistake, where a signal that sounds right is true nearly always and keeps the
screensaver from ever running. A player left open overnight is exactly that shape.

#### Scenario: A paused film
- **WHEN** a media session exists and reports `Paused`
- **THEN** it SHALL NOT be a reason to hold off

#### Scenario: A player left open overnight
- **WHEN** a media session reports `Stopped`
- **THEN** it SHALL NOT be a reason to hold off

#### Scenario: A session with no playback type
- **WHEN** a session reports `Playing` and no `PlaybackType`
- **THEN** it SHALL NOT be a reason to hold off

### Requirement: An unreadable media state is not a reason

Where the media sessions cannot be read, the system SHALL treat that as no media reason and
SHALL NOT hold the overlay off on account of it. The failure SHALL be written to the diagnostic
log.

Holding off on a failure to read would be unbounded: a permanently failing call would keep the
overlay from ever running, which is worse than a screensaver arriving during a film. This
matches the existing treatment of an unreadable audio peak, where a null reading is not silence
but is likewise not a reason to hold off.

#### Scenario: The session manager cannot be reached
- **WHEN** reading the media sessions throws or returns nothing
- **THEN** no media reason SHALL hold
- **AND** the failure SHALL be written to the diagnostic log

### Requirement: A partial hold-off does not stop the countdown

The system SHALL discount idle time only while every stage is suppressed. While a reason leaves
any stage permitted, the idle countdown SHALL continue to run.

Discounting time under a partial hold-off would freeze the countdown at the artifacts
threshold, and the blackout it deliberately permitted would never arrive.

#### Scenario: Music playing from the moment the user leaves
- **WHEN** music begins playing and the user stops touching the machine
- **AND** `BlackoutSeconds` of real time passes
- **THEN** the overlay SHALL go to `Blackout`

#### Scenario: A call, then silence
- **WHEN** the microphone is in use for ten minutes and is then released
- **THEN** the idle countdown SHALL restart from the moment it was released

### Requirement: A blackout reached with the artifacts suppressed is a plain fade

Where the overlay enters `Blackout` while a reason suppresses `Artifacts`, the system SHALL
fade plainly to black and SHALL NOT run an Emission.

An Emission is twelve seconds of artifacts, lightning and a burning sky. Playing one in order
to reach a state whose whole premise is that the artifacts are unwelcome would contradict the
reason that permitted the blackout.

#### Scenario: Reaching black while music plays
- **WHEN** the overlay goes to `Blackout` with a music session playing
- **AND** the Zone theme is configured with `Emission: true`
- **THEN** the screen SHALL fade plainly to black

#### Scenario: Reaching black with nothing held
- **WHEN** the overlay goes to `Blackout` with no reason holding
- **AND** the Zone theme is configured with `Emission: true`
- **THEN** an Emission SHALL run as before

### Requirement: The media signal can be turned off

The system SHALL provide a `PauseWhileMediaPlaying` setting, defaulting to on, exposed in the
tray menu alongside the other pause settings. While it is off, no media session SHALL be a
reason to hold off, and the remaining signals SHALL be unaffected.

#### Scenario: Turned off
- **WHEN** `PauseWhileMediaPlaying` is off and a video session is playing
- **AND** no other reason holds
- **THEN** the overlay SHALL advance by the idle timer alone

### Requirement: The media state can be inspected

The system SHALL provide `Bubbles.exe --media`, which lists each media session Windows reports
— the owning application, its playback status and its playback type — states which stages would
be suppressed as a result, and exits without showing an overlay.

This joins `--audio`, `--inputs`, `--dim-test` and `--hold-test`. Every signal this application
depends on is one that other software reports unreliably, and each must be answerable on the
machine where it is misbehaving rather than by guessing.

#### Scenario: Asking what is playing
- **WHEN** `Bubbles.exe --media` is run
- **THEN** each session SHALL be listed with its application, status and type
- **AND** the stages that would be suppressed SHALL be stated
- **AND** the process SHALL exit without showing an overlay

#### Scenario: Asking on a machine with nothing playing
- **WHEN** `Bubbles.exe --media` is run and no session exists
- **THEN** it SHALL report that no session is present
- **AND** SHALL NOT report an error

### Requirement: The countdown is measured from the start of the run as well as from the last input

The system SHALL NOT report more idle time than has passed since the current run began.

The idle time the system acts on is derived from a system-wide counter that measures the time
since the last keyboard or mouse input. That counter survives this application starting and
stopping, because it was never about this application. A run that begins while the machine has
already been sitting therefore begins with the thresholds already passed, and the overlay
arrives at a stage it should have walked to.

The bound SHALL be applied to the reported result, after any hold-off time has been discounted,
so that the existing discount continues to mean what it means. Reported idle time SHALL be the
lesser of the time counted since the last input and the time since the run began.

This SHALL hold whatever the thresholds are set to, and SHALL NOT be expressed as a grace
period, a number of ticks to ignore, or any value requiring tuning against `IdleSeconds` or
`BlackoutSeconds`.

#### Scenario: Starting into a machine that has been idle for longer than every threshold
- **WHEN** the application starts on a machine whose last input was longer ago than
  `BlackoutSeconds`
- **AND** nothing is holding any stage off
- **THEN** the overlay SHALL be at `Active`
- **AND** it SHALL reach `Bubbles` no sooner than `IdleSeconds` after the run began
- **AND** it SHALL reach `Blackout` no sooner than `BlackoutSeconds` after the run began

#### Scenario: The artifacts stage is not skipped
- **WHEN** a run starts into an already-idle machine and nothing is held off
- **THEN** the overlay SHALL pass through `Bubbles` before reaching `Blackout`
- **AND** it SHALL remain at `Bubbles` for the interval the settings describe

#### Scenario: Starting into a machine in use
- **WHEN** the application starts on a machine whose last input was a moment ago
- **THEN** the reported idle time SHALL follow the time since that input, as it does today

#### Scenario: Long after the run began
- **WHEN** the run has been going for longer than the time since the last input
- **THEN** the bound SHALL have no effect
- **AND** the reported idle time SHALL be the time since the last input, less any discount

#### Scenario: The bound composes with the hold-off discount
- **WHEN** a run has been going long enough for the bound not to apply
- **AND** a total hold-off has been in force for part of that time
- **THEN** that time SHALL still be discounted, exactly as it is without this requirement

#### Scenario: The clock is not reset by a restart of the countdown
- **WHEN** input arrives and the countdown restarts
- **THEN** the origin of the run SHALL be unchanged
- **AND** the bound SHALL continue to be measured from when the run began
