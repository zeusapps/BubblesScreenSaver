## Why

The keyboard follows the screen at the two moments it is told about, and nowhere else. That was
true and sufficient when the loan began at an Emission and ended at the blackout the Emission ran
into. Keyboard weather moved the beginning earlier -- to the artifacts stage, which has its own
beginning and its own end -- and moved nothing else. Both ends of the loan are now in the wrong
place, and each has its own visible symptom.

**The blackout does not hold.** The screen goes black, the keys go black with it, and five to ten
seconds later they come back in Armoury Crate's default green and stay there. Bubbles is not
writing that green, and this can be shown rather than guessed: `ReachedBlack` sets
`Suspended = true`, `OnRendering` returns at `if (Paused || Suspended || dt <= 0) return;`, and
nothing clears `Suspended` before the blackout ends -- the display-change handler only re-stretches
the window. One `GoDark()` at `WentDark` is the last packet this application sends for the whole
blackout, however many hours it lasts. Somebody else repaints over it: `Hid.Open` asks for
`ShareReadWrite` deliberately, because "the lighting is not ours exclusively", so a keyboard this
application holds is one the vendor's software can still write to. There is no way to notice --
the Aura protocol only listens, so the colour on the keys cannot be read back.

The timing names the trigger, and explains why this arrived with an external monitor:

```
T+0.0   ReachedBlack -> GoDark()             the last packet of the blackout
        _displays.Enter() -> TurnHdrOff()    a display MODE CHANGE, external targets only
T+2.5   _afterModeChange -> DimBacklights()  DDC/CI writes, then VCP 0xD6 standby
T+3..8  monitors drop into standby; the link renegotiates
        the keys go green
T+20    _whileDark -- re-asserts the monitor backlight. Nothing re-asserts the keyboard.
```

On the built-in panel alone every step from T+0 onwards is a no-op. The monitor backlight has been
here already, and the comment on `DisplayBlackout._whileDark` is the fix for the keyboard written
out in full:

> A dim is a request, not a lock: a monitor can put its own backlight back up, and does. Nothing
> raised it, no display event was logged, and an hour into a blackout the panel was lit again --
> so the state is checked rather than assumed to hold.

The keyboard is a request too. It was written as though it were a lock.

**The artifacts stage never gives it back.** Caught in the act on 2026-08-30, with
`KeyboardWeather` and `StandDynamicLightingDown` on:

```
21:38:49  keyboard-state.json          [{"Key":"0B05:19B6", ...}]          taken
21:38:49  dynamic-lighting-state.json  [{"AmbientLightingEnabled", true}]  stood down
21:46:05  still both, while the machine is in use
          HKCU\Software\Microsoft\Lighting\AmbientLightingEnabled = 0
```

Five minutes idle brought the artifacts up and the weather took the keyboard; the user came back
before the ten-minute blackout; nothing gave anything back. A Windows personalization setting the
user never asked to have edited stayed edited for the rest of the day.

One early return causes it. Everything that releases hangs off `LeftDark`, which is raised from
`EndBlackout`, which is reached only through `SetBlackout(false)`:

```csharp
if (_blackout == on) return;   // OverlayWindow.cs:407
```

Going from `Bubbles` to `Active` calls `SetBlackout(false)` when `_blackout` is already false, so
it returns there and `LeftDark` is never raised. The overlay fades out, the desktop comes back, and
as far as the keyboard layer is concerned nothing has happened.

The spec has the same hole in both places. "The blackout takes the keyboard dark" says what happens
when `WentDark` is raised and nothing about the hours after it, and the requirement that holds the
device through the artifacts stage says it is released on "leaving the blackout, exit, and a record
found at startup" -- three paths that were the complete list before the artifacts stage could take
a keyboard at all.

## What Changes

- **The keys are held dark, rather than sent dark once.** For as long as the screen is black the
  black is re-asserted on an interval, so whatever repaints the keyboard owns them until the next
  re-assert rather than until the next wake.
- **The re-assert is blind, and that is the whole difference from the monitor's.**
  `MonitorBacklight.Reassert` reads over DDC/CI and writes only when something has moved; this
  protocol cannot be read at all, so every tick costs its packets whether or not anything disturbed
  the keys. That is the cost being accepted, and it is why the interval is a decision rather than a
  detail -- the green arrives within ten seconds, so a twenty-second interval in sympathy with the
  monitor's would leave half of it visible.
- **It runs where every other write runs.** The keyboard's own worker thread already waits on an
  event and can wait with a timeout instead: no dispatcher timer, no second thread, nothing on
  screen waiting for a keyboard.
- **The end of the artifacts stage hands the keyboard back**, as the end of a blackout does, so the
  loan lasts as long as the screensaver is on screen -- which is what the setting already promises.
  The hand-back follows the overlay actually leaving the screen, covering the faded hide and the
  immediate one.
- **The hand-back is not a second `LeftDark`.** That event also carries the workstation lock --
  `if (reachedBlack && LockAfterBlackout && !SessionState.Locked) SessionLock.Request()` -- and
  raising it from a path that never reached black would be a new way to lock somebody's machine.
  The lock stays where it is.
- **Dynamic Lighting comes back with the keyboard**, through the same `GiveBack` that settles both
  debts in the order it already uses.
- **A refused re-assert is a lost keyboard, exactly as a refused colour already is.** Logged once,
  the session gives up, per the existing requirement; a re-assert must not become a retry loop
  against a device that has gone.
- **No new setting, and nothing new when the feature is off.** Somebody who enabled keyboard
  lighting asked for the keys to follow the screen; a session with it off still opens no device.

Explicitly out of scope: identifying *which* piece of ASUS software repaints the keys, or trying to
stop it. Bubbles cannot see the write, cannot read the keys and cannot lock the collection, so
re-assertion is not a workaround chosen over a better fix -- it is the only answer available from
inside this process.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `keyboard-lighting`: where the loan begins and ends, and what holds in between. "The blackout
  takes the keyboard dark" becomes "the blackout *holds* the keyboard dark". "Waking gives the
  keyboard back by releasing it" gains the end of the artifacts stage, so every route by which the
  screensaver leaves the screen settles the loan, and the release paths listed under "The keyboard
  is held for the artifacts stage" are corrected to match. The rationing requirement gains the
  re-assert as a named exception, since a re-send of a colour that has not changed is precisely
  what the send policy exists to suppress.

## Impact

- `src/Bubbles/Keyboard/KeyboardLighting.cs` -- the worker's `_wake.WaitOne()` becomes a timed wait
  while the screen is dark, armed by `WentDark` and disarmed by `LeftDark`; the interval wants to be
  injectable, because the tests drive a real worker thread and poll. The hand-back itself already
  exists and already resets the weather clock and both policies, so the artifacts half may need no
  more than a second caller -- which is the shape to aim for.
- `src/Bubbles/Overlay/OverlayWindow.cs` -- an event raised where the overlay leaves the screen,
  through the same `Raise` guard the blackout events use, so a subscriber's failure cannot become
  the overlay's.
- `src/Bubbles/App.cs` -- wires that event to the hand-back. Nothing else subscribes; in particular
  the lock is not reachable from it.
- `src/Bubbles/Keyboard/SendPolicy.cs` -- untouched. The re-assert deliberately does not go through
  the policy: `Worth` suppresses a colour that has not moved, which is exactly what is being sent.
- `tests/Bubbles.Tests/KeyboardLightingTests.cs` -- the keys are re-asserted while dark and not
  after the blackout ends; a refused re-assert ends the session's lighting once rather than looping;
  an artifacts stage that ends without a blackout hands the keyboard back and settles the Dynamic
  Lighting loan; a blackout that ends normally still hands back exactly once.
- No change to the ledger format, the settings file, the Emission, the weather colours, or any path
  that runs when `KeyboardLighting` is off.
