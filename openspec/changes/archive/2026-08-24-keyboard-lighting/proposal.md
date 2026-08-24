## Why

On one machine the keyboard already does this, and nobody wrote it. Armoury Crate's Aura
screen-sync samples the display and paints the keys from it, so an Emission floods the screen
crimson and the keyboard turns crimson with it; the blackout takes the screen to black and the
backlight goes out. It was noticed, and liked, before anyone realised it was an accident.

It is an accident with three problems. It only happens where a particular vendor's software is
installed and set to a sampling effect -- switch the profile and it stops, which is what has
since happened. It is an approximation, because a sampler reading average screen colour is
slow and soft where a lightning strike is short and sharp. And nothing about it belongs to this
application, so it cannot be reasoned about, tested, or relied on.

The Emission is the one moment this screensaver has that is worth spilling off the screen. It
is twelve seconds of storm, already the loudest thing the app does, and the hardware to carry
it further is sitting under the user's hands.

## What Changes

- **Add an optional keyboard lighting layer**, driven by what the overlay is already doing:
  - the Emission turns the keyboard red, tracking the storm rather than sitting on one colour
  - a lightning strike flashes it, on the same query that draws the strike on screen
  - the blackout turns it off, because a lit keyboard beside a screen deliberately taken to
    black defeats the point of the black
  - waking restores whatever the keyboard was doing before
- **Drive it over HID, in this process.** Nothing else has to be installed or started. The
  route was chosen by elimination, on the actual hardware: OpenRGB does not support this
  keyboard's product id at all; the ASUS Aura SDK is deprecated and absent; and Windows' own
  LampArray API only grants control to the *foreground* application unless the app is packaged
  as MSIX and declares a lighting extension -- and this overlay is deliberately focus-less and
  click-through, so it is never the foreground application. What remains is the vendor HID
  protocol, which is three packets to a collection the firmware exposes for the purpose.
- **Off by default, and silent when it cannot work.** Almost nobody else running Bubbles has
  this keyboard, and this must cost them nothing: no error dialog, no retry storm, no delay to
  the Emission. A setting turns it on; the absence of a keyboard turns it off again without
  comment.
- **Give the keyboard back.** Whatever the keyboard was set to is recorded before it is
  changed and restored afterwards, including after a crash -- the same discipline
  `PendingRestore` already applies to monitor brightness and HDR.
- **BREAKING for the user who enables it:** it only works while Windows owns the lighting, which
  means Dynamic Lighting switched on and Armoury Crate no longer managing the keys. While
  Dynamic Lighting is off, every write is accepted and discarded in silence -- indistinguishable
  from the feature being broken. That is a real trade and a real trap, and the setting must say
  both rather than discovering them for people.

## Capabilities

### New Capabilities

- `keyboard-lighting`: what the keyboard does while the screensaver runs -- which events drive
  it, what it does when the hardware or the server is missing, and what it owes back to the
  keyboard it borrowed.

### Modified Capabilities

None. The overlay gains a signal for the Emission beginning, but that is a new fact reported
about existing behaviour rather than a change to it; nothing in `overlay-transparency` or
`idle-hold-off` means anything different afterwards.

## Impact

- **New** a keyboard lighting layer in `src/Bubbles/Keyboard/`, and a HID client speaking the
  ASUS Aura protocol through SetupAPI and `hid.dll`. No package reference: the protocol is
  three short packets, and this project already speaks two binary protocols by hand.
- `src/Bubbles/Overlay/OverlayWindow.cs` -- gains an event for the Emission beginning, beside
  the existing `WentDark` and `LeftDark`. The lightning flash must ride the `HasStrike` query
  already made at lines 990 and 1139 rather than asking a second time, which the comment at
  line 72 asks for explicitly.
- `src/Bubbles/App.cs` -- wires the layer to those events, as it already wires
  `DisplayBlackout`.
- `src/Bubbles/Settings.cs` -- one new key, defaulting off. It appears in the settings window
  automatically, since that window's rule is that every persisted setting is reachable.
- `src/Bubbles/Displays/PendingRestore.cs` -- reused as-is. It is already generic.
- No change to the overlay's drawing, the idle timer, or the hold-off logic.

## Open Questions

Recorded here, to be settled in `design.md`:

1. **Whether the keyboard should follow the Emission's shape or just its colour.** The storm
   builds; a keyboard that goes red at the start and stays there is a poorer imitation of it
   than the sampler this replaces.
2. **What "off" means during the blackout.** Black is a colour the keyboard can be set to, but
   some hardware supports actually powering the backlight down, which is darker and is what an
   OLED-minded user would want.
3. **How often to send.** The lightning wants frames; the Emission wants a slow ramp. A socket
   written to at frame rate for twelve seconds is a different proposition from one written to
   twice, and the app has an explicit stance on CPU cost during the artifacts stage.
4. **Whether this can be verified at all before it ships.** The payoff is entirely visual. The
   parts that can be tested -- the colour timeline, the packet encoding, the event sequencing,
   the choice of device -- should be separated from the part that cannot.

   *Settled, better than expected:* the hardware turned out to be the machine this was built
   on, so the protocol, the Emission and the restore were all confirmed by running them and
   watching the keys. See "What was actually verified" in `design.md`.
