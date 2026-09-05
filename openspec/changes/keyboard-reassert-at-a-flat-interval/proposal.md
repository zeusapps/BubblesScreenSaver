## Why

The last change replaced two flat re-assert cadences with a ramp, on the reasoning that attention
is worth paying just after something has disturbed the keys and worth relaxing when nothing has.
That reasoning was sound and the evidence has since refuted it. A week of production logging says
the ramp is inert and the ceiling it relaxes to is the only number that has ever mattered.

**The ramp almost never runs.** Every wait the application has taken, counted out of
`%APPDATA%\Bubbles\log.txt`:

```
next in 20s   11555   <-- the ceiling
next in 15,2s    52
next in 10,1s    52
next in 6,8s     52
next in 4,5s     52
next in 3s       40
next in 2s       52
```

Fifty-two blackouts, and 99.2% of all waits at the ceiling. The ramp costs about sixty-two seconds
of attention when the screen reaches black and then contributes nothing for however many hours
follow. It is clocked from the last disturbance so that "the attentive phase happens again every
time there is a reason for it" -- but across the whole log there have been **sixteen**
disturbances. The last blackout ran 5,085 seconds and logged none:

```
19:32:12  keyboard lighting: black again, 3960s into the blackout, next in 20s
...                                       (254 more, every one of them 20s)
19:50:58  keyboard lighting: black again, 5085s into the blackout, next in 20s
```

**The ceiling is the symptom.** A repaint arrives at a uniformly random point inside the wait in
hand, so with a 20s ceiling the keys sit green for a mean of ten seconds and up to twenty. The
green observed in production lasting five to seven seconds is not a malfunction; it is an ordinary
draw from that distribution. Lowering the ceiling is the whole of the fix, and the ramp is
irrelevant to it.

**Nothing defends the ceiling.** It was chosen as "the same interval the display blackout already
uses against the same class of problem", but that borrowed a number from a mechanism that does not
match: `DisplayBlackout._whileDark` reads each monitor over DDC/CI first and writes only when
something has moved. It has feedback. This hardware has none -- that asymmetry is the entire shape
of this feature -- so the two are not the same problem and should not share a number.

The remaining objection was cost, and it does not survive either. One re-assert is three 128-byte
HID output reports. `SendPolicy.ForEmission()` sets a floor of **0.12s**, so this application
already writes to the same keyboard at up to eight sends a second for the whole twelve seconds of
every Emission. A two-second blackout cadence is sixteen times slower than what it does routinely
and without comment. Nor is `Apply` (0xB4) a flash commit that repetition would wear out: asusctl's
`rog-aura` drives `Breathe` and `DoomFlicker` as host-streamed per-frame writes for as long as the
effect runs, so continuous high-rate writing is this protocol's normal mode of operation.

## What Changes

- The re-assert interval becomes a single constant, two seconds, for the whole of a blackout. The
  worst case a repaint can sit unanswered goes from twenty seconds to two.
- The ramp is removed rather than retuned. With the floor and the ceiling meeting, `Growth`,
  `Relax` and the ramping `_cadence` field have nothing left to express.
- A disturbance keeps its immediate write and loses its reset. It no longer restarts a ramp,
  because there is no ramp to restart; it still says black at once, which is the half of it that
  saves latency at the moment the keys are likeliest to have just been taken.
- The re-assert log line drops its `next in Ns` suffix, which now reports a constant.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `keyboard-lighting`: the re-assert interval is flat rather than a ramp between a floor and a
  ceiling, and a disturbance no longer returns the interval to a floor.

## Impact

- `src/Bubbles/Keyboard/KeyboardLighting.cs` -- the constants, the `_cadence` field, `Relax`, the
  ramp reset in the `Chore.Dark` and disturbance paths, the test seam on the constructor, and the
  re-assert log line.
- `tests/Bubbles.Tests/KeyboardLightingTests.cs` -- the ramp test is removed and the `Layer`
  helper's two cadence parameters collapse to one.
- `src/Bubbles/Interop/MachineEvents.cs` -- unchanged in behaviour; its doc comment is rewritten,
  because it currently justifies itself by a choice between "writing constantly and writing at the
  right moments" that this change settles the other way.
- No change to the wire protocol, the hand-back, the state file, or anything the user can see in
  the settings dialog.
