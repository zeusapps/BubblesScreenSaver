## Context

The overlay already knows everything this feature needs to know; none of it is currently told
to anybody outside the window.

**The Emission is a fixed timeline, not a state.** `BeginEmission` lays keyframes against three
constants -- `BuildupEnds` at 6.5s, `WaveEnds` at 8.4s, `DarknessAt` at 12.5s -- and every layer
on screen is animated against them. The sky rises to 0.94 opacity through the buildup, the
wavefront is a hard flare at `BuildupEnds + 0.3`, and everything falls to nothing by
`DarknessAt`. A keyboard that reads the same constants moves with the screen by construction
rather than by being kept in step.

**The events mostly exist.** `WentDark` and `LeftDark` are public and already consumed by `App`
to drive `DisplayBlackout`. `ReachedBlack` is where the screen has genuinely arrived at black,
which is deliberately not the same moment as the blackout starting. Only the Emission's
beginning has no signal.

**There is a house rule about the lightning.** The comment at `OverlayWindow.cs:72` asks that
`HasStrike` be consulted where it is already being asked -- lines 990 and 1139 -- rather than
queried a second time. A keyboard flash is exactly the kind of second caller it warns about.

**Borrowed hardware gets given back.** `PendingRestore<T>` records what is owed to each display
before anything is changed, on disk, so that a process that dies mid-blackout does not strand a
monitor at minimum brightness. It is generic, and a keyboard is the same problem.

## Goals / Non-Goals

**Goals:**

- The Emission carries onto the keyboard, following its shape rather than sitting on one red.
- A lightning strike flashes the keys at the moment it flashes the screen.
- The blackout takes the keyboard dark, because a lit keyboard beside a deliberately black
  screen defeats the black.
- Whatever the keyboard was doing before is restored afterwards, including after a crash.
- A machine without OpenRGB pays nothing: no delay, no error, no retry.

**Non-Goals:**

- Per-key addressing, ripples, or anything travelling across the board. The Emission is a
  whole-sky event; the keyboard is one colour at a time.
- Colouring the artifacts stage. Twelve seconds of storm is worth spilling off the screen; four
  hours of drifting artifacts is a lightshow nobody asked for, and a socket written to for four
  hours is a different proposition from one written to for twelve seconds.
- Vendor SDKs. The ASUS Aura SDK is deprecated and absent, and a reverse-engineered HID
  protocol would be one keyboard's worth of work that helps nobody else.
- Mice, fans, RAM, or anything else OpenRGB exposes. Keyboards only, because the keyboard is
  the thing under the user's hands while the screen is doing something.

## Decisions

### OpenRGB's TCP SDK, spoken directly

The wire format is a 16-byte header -- `ORGB` magic, device id, packet id, payload size -- and
then a payload. Setting a device's colour is `SETCUSTOMMODE` followed by `UPDATELEDS` with a
block of RGB values. That is a `TcpClient` and a `BinaryWriter`.

No package reference. This project publishes framework-dependent single-file specifically to
stay small, has no NuGet dependencies at all, and already speaks two binary protocols by hand
(DDC/CI over `dxva2`, and the Win32 surface in `Interop`). A client library would be a larger
change to the project's shape than the feature is.

*Alternative considered:* a vendor SDK. Not available -- `AURA_SDK.dll` is absent and
`AuraServiceLib` is unregistered on the machine this was reported from, because ASUS deprecated
it. Also single-vendor, where OpenRGB covers the same hardware and more.

### The keyboard reads the Emission's own clock

The lighting is a pure function from elapsed Emission time to a colour, written against the
same `BuildupEnds` / `WaveEnds` / `DarknessAt` constants the screen is animated with:

- through the buildup, a rise from nothing to deep red, tracking how the sky takes over
- at the wavefront, a hard flare toward white, then gone
- through the darkness, a fall to black, arriving there when the screen does

Because it is a function of time and not a sequence of commands, it cannot drift out of step
with the screen, and it is testable without a socket or a keyboard: give it a time, assert a
colour.

*Alternative considered:* sampling the rendered frame, which is what Aura does. Rejected --
that is the approximation this change exists to replace, it costs a readback of the composited
frame every frame, and it produces a soft average exactly where the strike is sharp.

### The flash rides the query that is already being made

`HasStrike` is asked at lines 990 and 1139 while drawing. The keyboard takes its flash from the
value already in hand at those points, as the comment at line 72 asks. A second call would be
both wasteful and capable of disagreeing with the screen about whether a strike is happening.

### Sent on change, not on frame

A frame is 33ms at the default `MaxFps` of 30. Writing a socket that often for twelve seconds
is affordable but pointless: the eye cannot see keyboard steps that fine, and the app has an
explicit stance about CPU cost while the overlay is up -- `Animated` exists as a setting with a
warning on it for exactly this reason.

So the layer computes the colour every frame and sends only when it has moved by a visible
amount, with a floor on the interval between sends. The flare and the strikes are the moments
that must not be coalesced away; the buildup ramp can be sent coarsely without anybody seeing
the difference.

### Black, then handed back

During the blackout the keyboard is set to black rather than released, because releasing it
returns it to whatever effect it had before and that effect is probably lit. On `LeftDark` the
device is restored and control handed back.

*Open, for implementation:* some hardware can power the backlight down rather than display
black, which is genuinely darker and is what somebody who cares about an OLED would want. If
the protocol exposes it for the device in hand, prefer it; if not, black is honest.

### Failure is silence, and it is decided once

On the first Emission after the setting is enabled, one connection attempt. If it fails -- no
server, wrong port, no keyboard among the devices -- log it once through `Diagnostics` and stay
off for the rest of the session. No retry timer, no dialog, no second attempt mid-Emission.

Nobody else running Bubbles has an OpenRGB server listening, and the failure path is therefore
the common path. It must cost a connect attempt on a loopback socket and nothing else. The
Emission must never wait on it: all socket work happens off the dispatcher thread, and a
keyboard that cannot be reached delays no frame.

### What is owed is written down before it is taken

`PendingRestore<T>` is reused as-is. The device's prior mode is recorded on disk before the
first colour is sent, and restored on `LeftDark`, on exit, and at the next startup if this run
ended badly -- the same three paths `DisplayBlackout` already uses.

This matters more than it does for monitors. A monitor left dim is visible and fixable from its
own buttons; a keyboard left black by a process that is no longer running looks like broken
hardware.

### Enumerating devices is the real cost

`UPDATELEDS` needs the LED count, which comes from the controller data blob, which is a
variable-length structure of counts, names, modes and zones. The layer reads only what it needs
-- the device type and the LED count -- skipping the rest by the lengths the blob declares.

This is the bulkiest and least interesting part of the work, and the part most likely to be
wrong against a protocol version. It is isolated behind one function, given the protocol
version it was written against, and covered by tests over recorded bytes rather than a live
server.

## Risks / Trade-offs

- **It cannot be verified without the hardware** → The colour timeline, the packet encoding and
  the event sequencing are pure and get tests. The socket and the keyboard get none, and that
  boundary is drawn deliberately so the untested part is as small as possible. The feature ships
  disabled; nobody meets it by accident.
- **OpenRGB and Armoury Crate fight over the keyboard** → Unavoidable, and the setting says so.
  Enabling this in practice means the vendor software stops managing the keys.
- **OpenRGB may not support this particular keyboard** → Then the device list contains no
  keyboard, the layer logs it and stays off. That is the same path as no server at all.
- **The protocol changes between OpenRGB versions** → The client declares the protocol version
  it speaks, and the enumeration is version-checked rather than assuming. A mismatch is a
  refusal to start, not a misparse.
- **A dead socket stalls the Emission** → Nothing is awaited on the dispatcher. The worst case
  is a keyboard that does nothing while the screen is unaffected.
- **A crash mid-Emission leaves the keyboard black** → The same on-disk record and
  startup-recovery path the displays already use.

## Migration Plan

None. One new setting, defaulting off; no existing setting changes meaning; `settings.json`
keeps its shape and version. A user who never enables it cannot tell this change happened.

Ordered so the untestable part comes last: the colour timeline first, with tests, then the
protocol encoding, with tests over recorded bytes, then the socket, then the wiring into the
overlay's events. Everything before the socket is verifiable on any machine.

## Open Questions

- Whether to expose the OpenRGB port as a setting or to fix it at the default. A setting is one
  more knob for a feature almost nobody will enable; a fixed port is one more reason it will not
  work for somebody.
- Whether the artifacts stage should tint the keyboard faintly rather than leaving it alone,
  which the non-goals currently rule out and which is the obvious next thing somebody will ask
  for after seeing the Emission work.

## What was actually verified, and how

Rewritten after the transport changed and the feature was run on real hardware. The section
this replaces was written when the OpenRGB route was still the plan and the hardware was
assumed to be out of reach; both turned out to be wrong.

### The transport changed, twice, on evidence

The design above chose OpenRGB's TCP SDK. That did not survive contact with the machine:

1. **OpenRGB does not support this keyboard.** Product id `0B05:19B6` appears nowhere in
   OpenRGB's ASUS detector. The feature would have found no keyboard even with a server running.
2. **The user does not want to run anything external**, which rules out the whole shape of the
   original decision regardless.
3. **Windows' own LampArray API cannot be used here.** The keyboard does expose a standards
   compliant lighting collection, and Windows enumerates it as a one-lamp keyboard. But desktop
   applications are granted control only while they are in the *foreground*, unless they are
   MSIX-packaged and declare the `com.microsoft.windows.lighting` extension. This overlay is
   deliberately focus-less and click-through -- it never becomes the foreground application --
   and packaging it as MSIX would be a larger change to the project's shape than the feature is.
4. **The vendor HID protocol works**, and is what shipped.

### Verified on the hardware

The keyboard is an ASUS `0B05:19B6` (ROG Strix G614/G615). Confirmed by writing bytes and
watching the keys:

- The lighting collection is the one declaring usage page `0xFF31`, usage `0x0079`, with a
  128-byte output report. The device presents ten collections; several accept writes and only
  this one acts on them.
- The command sequence is effect (`5D B3`), then SET (`5D B5`), then APPLY (`5D B4`), each
  padded to the declared report length. Confirmed by a deliberate blink pattern, then by
  holding individual colours.
- Releasing the device hands the keyboard back: the previous owner reasserted its colour within
  a moment of the handle closing, every time.
- **The whole feature, in the real application.** `--emission-demo` with the setting on: the
  keys followed the sky through the buildup, flashed with the lightning, went dark with the
  screen, and were handed back on waking. The log line `restored 1 (awake)` is the restore path
  running for real.

**The condition that makes or breaks it:** Dynamic Lighting must be switched on. While it is
off, the vendor's software owns the keyboard and every write is accepted and silently
discarded -- no error, no clue. This cost several rounds of "nothing happened" before it was
understood, and it is now said in the setting, in the log line, and in the class comment,
because it is the single thing most likely to make somebody think the feature is broken.

### Verified by test, on any machine (382 pass)

The colour timeline; the send policy and its exemptions; the strike behaviour; the packet bytes
and their padding; the choice of HID collection among its siblings; and the layer's own rules --
one attempt per session, silence after a failure, the record written before the first colour,
and the hand-back on each of the three paths.

### Not verified

Whether other ASUS models with the same collection accept the same packets. The protocol is
reverse-engineered by third parties rather than documented by ASUS, and only one board was
available to try.

### Bugs the tests and the hardware found

- **Every packet was addressed to device zero.** `Send` had a defaulted device id and three
  callers took it. On a machine where the keyboard was not the first device, an Emission would
  have lit something else. Found by the fake-server tests the original design had planned not to
  write. (In the OpenRGB client, since removed -- but the same class of mistake is why the HID
  collection is now matched on what it declares rather than on position.)
- **The first colour of every Emission was dropped.** The "open the device" chore and the first
  frame arrived in one wake, and the worker handled one or the other.
- **A held bolt pinned the keys white.** `HasStrike` is true for every frame a bolt is drawn and
  a storm keeps several overlapping, so treating it as a level left the keyboard solid white
  through the whole storm while the screen showed thin lines over red. Found by watching the
  real Emission. A strike is now edge-triggered and decays over 0.18s, mixed over the ramp
  rather than replacing it, with per-bolt colour variation so a burst does not read as one
  block.

## Where the implementation departed from this design

- **The transport**, as above.
- **Nothing is recorded to restore *to*.** The design assumed the prior mode could be read back
  and handed over. This protocol only accepts commands; it cannot be asked what the keyboard is
  showing. So the record says only that a keyboard was taken, and giving it back means releasing
  it -- which works because the owner reasserts, and because process death closes handles. This
  is weaker than the monitor case on paper and stronger in practice: there is no state to get
  wrong.
- **The send-interval floor is measured on the Emission's clock, not the wall's.** The colour is
  a function of Emission time, so rationing by wall time let a frame earn a send by arriving
  late. It also made the rule untestable without sleeping.
- **The flash's exemption is narrower than the flare.** Exempting the flare's whole one-second
  decay meant sending at frame rate for a second to describe a fade.
- **The send policy is its own class.** The layer's worker keeps only the latest colour, so
  counting sends through it measures thread scheduling rather than the rule.
