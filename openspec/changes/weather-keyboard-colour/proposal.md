## Why

The Emission carries onto the keyboard now, and it works. The first thing anybody notices
afterwards is that the other four hours are dark.

The Zone already has a sky outside the Emission. Weather cycles through `Clear`, `Fog`, `Rain`
and `Storm` about once a minute, tinted by whichever anomaly family is drifting on screen, with
distant lightning during a storm. All of it is drawn and none of it reaches the keys.

`keyboard-lighting`'s design ruled this out, and the objection has to be answered rather than
skipped:

> Twelve seconds of storm is worth spilling off the screen; four hours of drifting artifacts is
> a lightshow nobody asked for.

That is right about a lightshow and wrong about the weather. The Emission is loud because it is
short; the weather is the opposite thing, and the mistake would be to make it compete. A storm
that sits at a tenth of the Emission's brightness and changes once a minute is not a lightshow.
It is the room going slightly blue when it rains, which is what the screen is already doing.

The same design's open questions predicted this exactly -- *"the obvious next thing somebody
will ask for after seeing the Emission work"* -- so the reversal is a decision that was
deferred, not one that was made and is now being undone.

## What Changes

- **Add ambient keyboard colour, driven by the weather already on screen:**
  - `Clear` leaves the keys unlit, because nothing is overhead
  - `Fog` and `Rain` tint them with the same `AnomalyTint` the sky is drawn with, so the keys
    and the screen are the same colour by construction rather than by agreement
  - `Storm` is the same, darker, with the ambient strikes flashing the keys as the Emission's
    already do
  - cross-fades come free: `WeatherCycle.IntensityOf` already reports both sides of a
    transition, so the keys fade between states with the sky
- **Keep the Emission the loudest thing in the room.** Ambient weather is capped well below the
  Emission's deepest red. An Emission beginning takes the keyboard over outright; the weather
  gets it back when the screen does.
- **Ride the strike query that is already made.** Ambient lightning writes `_strikeOnScreen` at
  `OverlayWindow.cs:990`, the same way the Emission does at 1139. The keyboard takes the value
  from there, as the comment at line 72 asks.
- **A second setting, off by default and subordinate to the first.** `KeyboardLighting` stays
  the master switch; the weather is a separate opt-in under it. Somebody who wanted the Emission
  on their keyboard did not thereby ask for four hours of blue.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `keyboard-lighting`: gains what the keyboard does *outside* an Emission -- which weather
  drives it, how quietly, how it yields to an Emission, and when the device is held.

  Note: `keyboard-lighting` is not archived at the time of writing, so its requirements are
  still a delta rather than a main spec. Archiving it first would let this change be written
  against something rather than beside it.

## Impact

- `src/Bubbles/Keyboard/` -- a `WeatherLight` beside `EmissionLight`: another pure function,
  this one from weather state, intensity and anomaly tint to a colour. Same shape, same
  testability, no device required to exercise it.
- `src/Bubbles/Keyboard/KeyboardLighting.cs` -- gains an ambient source and the rule that an
  Emission outranks it. The once-per-session open, the send rationing, the record and the
  hand-back are all reused unchanged.
- `src/Bubbles/Keyboard/SendPolicy.cs` -- a slower floor for ambient colour, which moves over
  a minute rather than over twelve seconds.
- `src/Bubbles/Overlay/OverlayWindow.cs` -- an event for the ambient sky, beside `EmissionFrame`,
  carrying the weather, its intensity, the dominant anomaly and the strike already in hand.
- `src/Bubbles/Settings.cs` -- one new key, defaulting off.
- No change to the weather itself, the Emission, the artifacts, or the idle timer.

## Open Questions

Recorded here, to be settled in `design.md`:

1. **Whether `Clear` should mean unlit or mean released.** Unlit is coherent -- an empty sky
   over dark keys -- but it denies the vendor's software the keyboard for the whole idle
   period, which on a machine where the screen never sleeps is all night. Releasing hands it
   back, at the cost of the user's own lighting popping in and out roughly once a minute, since
   `Clear` comes up about a third of the time.
2. **How dim is dim enough.** The number that keeps the Emission an event. Too low and the
   feature is invisible; too high and the Emission stops being a surprise.
3. **Whether ambient lightning should flash the keys at all.** It is the one loud thing in an
   otherwise quiet feature, and it is also the thing that makes a storm read as a storm.
4. **What holding a HID handle for hours costs**, if anything, and whether the device should be
   reopened per weather state instead.
