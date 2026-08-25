## Why

After an Emission, the screen goes black and the keyboard stays lit. It has happened more than
once, always on a machine that has been running for a while, and never on the first blackout
after a launch.

That last detail is the whole of it. `AuraKeyboard.Restore` gives the keyboard back by releasing
it -- disposing the handle and forgetting the collection -- which is the design and is correct.
But `KeyboardLighting.GiveBack` never tells its own state that this happened. `_device` still
points at the released object and `_decided` is still true, so the next `Ensure()` runs

```
if (_decided) return _device is not null;   // true
```

and answers yes to a question it has no business answering from cache. Every `Show` and every
`GoDark` for the rest of the process then hits `if (_handle is null) return false;` and returns
false in silence, because the worker discards what those calls return.

Meanwhile the vendor's software has reasserted its own lighting, exactly as the design says it
will. So the second blackout of any session is: screen black, keys lit, nothing in the log, and
no way for Bubbles to turn them off.

The same stale state is reached by a second road. A refused write also calls `Release()`, so a
keyboard that goes away mid-session strands the layer in the same lie -- and there `Abandoned`
stays false too, so the ramp keeps computing colours for a device that is gone.

Nothing caught it because the test fake is kinder than the hardware: its `Restore` releases
nothing and its `Show` never fails. `TheKeyboardIsOpenedOnceAcrossManyEmissions` runs three full
blackouts and asserts `Opens == 1` -- it asserts the symptom as the desired property.

## What Changes

- **`_decided` stops meaning two things.** It means "the search has been made", and nothing
  more. Whether the device is in hand is a separate question, asked of the device.
- **`IKeyboardDevice` gains `IsOpen`.** The device is the only thing that knows whether it still
  holds a handle, and it already changes that answer on its own -- on a hand-back, on a refused
  write, on an exception. Caching that above it is what went wrong.
- **`Ensure()` asks instead of remembering.** Holding the device still, it returns true. Handed
  back, it opens again -- which is not a retry of a failed search, because the search succeeded.
  Never searched, it searches. Searched and found nothing, it stays quiet, exactly as now.
- **The debt is re-recorded on each loan.** Re-opening goes through `_owed.Remember` again, so
  the second blackout of a session is written to disk before its first colour, like the first
  was. Today it is not written at all.
- **A refused write ends the session's lighting, out loud.** The worker honours what `Show` and
  `GoDark` return: a false answer nulls the device, which makes `Abandoned` true, which stops
  the ramp computing colours nobody will see. One log line, no retry loop.
- **The fake keyboard is made to behave like the real one** -- `Restore` closes it, `Show` can
  be told to fail -- and the test that asserted `Opens == 1` is replaced by one that asserts
  the keys actually go dark on the second blackout.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `keyboard-lighting`: what "once per session" governs. It governs the *search* for a keyboard,
  not the holding of one. A keyboard found once and handed back is opened again for the next
  Emission; a keyboard never found is never looked for again; a keyboard lost mid-session is
  given up on, and says so.

## Impact

- `src/Bubbles/Keyboard/KeyboardRecord.cs` -- `IKeyboardDevice` gains `IsOpen`.
- `src/Bubbles/Keyboard/AuraKeyboard.cs` -- implements `IsOpen`; no behavioural change, it
  already keeps the state the property reports.
- `src/Bubbles/Keyboard/KeyboardLighting.cs` -- `Ensure()` and `Abandoned` split the two
  meanings of `_decided`; the worker stops discarding `Show` and `GoDark` results.
- `tests/Bubbles.Tests/KeyboardLightingTests.cs` -- the fake gains the real device's
  destructiveness, and the tests it was hiding.
- No change to the Emission, the weather, the send rationing, the record on disk, or any
  setting.
