## Context

The keyboard layer is told two things by the overlay: an Emission is running, and the screen has
reached black. Everything it does hangs off those. The loan is taken on the first colour and given
back on `LeftDark`, and for an Emission-shaped feature that is complete -- the screen goes black,
the keys go black, the screen comes back, the keys go back to their owner.

Keyboard weather widened what the feature covers without widening either boundary:

```
                    take                             give back
  before weather:   EmissionBegan ................... LeftDark          complete
  after weather:    first Weather frame ............. LeftDark          leaks
                    (artifacts stage)                 (blackout only)

  during a blackout: GoDark() once ................... nothing, for hours
```

Both symptoms in the proposal come out of that diagram. The right-hand column has one exit where
the left-hand column has two entrances, and the middle of the blackout has nothing in it at all.

Three properties of the hardware shape every decision below, and all three are already documented
in `AuraKeyboard`:

- **The protocol only listens.** There is no read. The colour on the keys cannot be sampled, so
  nothing can be checked -- only asserted.
- **The handle is shared, deliberately.** `Hid.Open` asks for `ShareReadWrite` because "the
  lighting is not ours exclusively". Another process writing to the same collection is expected,
  not exceptional.
- **A write reports success whoever else is holding the keys.** So the failure is silent in both
  directions: this application cannot tell that it has been painted over, and cannot tell that its
  own write did nothing.

Together those mean there is no design available in which Bubbles *knows* the keys are black. The
best available is: assert it often enough that being wrong is brief.

## Goals / Non-Goals

**Goals:**

- The keys stay dark for as long as the screen is black, whatever else writes to them.
- The keyboard and Dynamic Lighting are given back whenever the screensaver leaves the screen, not
  only when it leaves a blackout.
- Nothing new runs on a machine with the feature off, and nothing new runs on the dispatcher.
- A keyboard that has genuinely gone is still given up on once, quietly, exactly as today.

**Non-Goals:**

- Finding out which ASUS process repaints the keys, or stopping it. Bubbles cannot see the write,
  cannot read the keys, and cannot take the collection exclusively.
- Restoring a colour on hand-back. Releasing is still the whole of giving back.
- Any change to what colour the keys are, when, or how often, outside the blackout.
- A setting. This is what the blackout already claims to do.

## Decisions

### The re-assert is a timeout on the wait the worker already does

The worker blocks on `_wake.WaitOne()` between chores. Waiting *with a timeout* turns that same
blocking call into the re-assert loop:

```csharp
// Worker thread only.
var woken = _wake.WaitOne(_holdingDark ? _reassertAfter : Timeout.InfiniteTimeSpan);

if (!woken)
{
    // Nobody asked for anything; the screen is still black. Say so again.
    if (Ensure() && !_device!.GoDark()) Lost("holding the blackout");
    continue;
}
```

`_holdingDark` is set where `Chore.Dark` is handled and cleared by `Chore.Restore`, `Chore.Recover`
and by any colour going out -- all on the worker thread, so it needs no lock.

**Alternatives considered.** A `DispatcherTimer`, like `DisplayBlackout._whileDark`: rejected
because it puts device work back on the thread that draws, which the whole class exists to keep it
off. A second background thread: rejected as a thread whose entire job is to wait, next to one
already waiting. A queued `Chore.Reassert` posted by a timer: rejected because `Queue` coalesces,
so a re-assert could be swallowed by an unrelated chore arriving in the same window -- and the
re-assert is precisely the message that must not be dropped.

### Two cadences, because the evidence names when the repaint happens

The trigger is not random. It is the blackout's own display work -- an HDR mode change at T+0, DDC
writes and a standby request at T+2.5, monitors dropping out somewhere in T+3..8. So the first
half-minute of a blackout is when the keys are most likely to be taken, and the following eight
hours are when they are least likely to be.

```
  T+0 ---- settling: every 2s ---- T+30 ---- holding: every 20s ---- wake
           covers the display work            covers everything else:
           that provokes the repaint          vendor restarts, power
                                              transitions, an unexplained
                                              repaint an hour in
```

Twenty seconds is not chosen for symmetry. It is what `DisplayBlackout._whileDark` already uses
against the same class of problem on the same machine, and re-using it means one number to reason
about when both are wrong.

The cost, at the worst end: an eight-hour blackout is about 1,440 wakeups and some 4,300 packets of
128 bytes. That is the price of a protocol that cannot be read; the monitor's re-assert gets to
read first and only writes when something moved.

**Alternative considered.** One flat interval. A flat 2s makes an overnight blackout 14,000
wakeups for a keyboard nobody is looking at; a flat 20s leaves the green on screen for up to twenty
seconds every time, which is most of what is being complained about.

### The re-assert goes through `Ensure()`, not around it

`Ensure()` already distinguishes the three states that matter, and all three are right here:

| state | `Ensure()` | re-assert |
|---|---|---|
| in hand | true | writes black, as intended |
| found once, handed back | opens again | black lands again |
| searched, nothing there | false | costs a branch, sends nothing |

This is also what makes the change robust to a question the recon could not settle without a
ten-minute blackout: if the handle silently died during the blackout, the device reports
`IsOpen == false`, `Ensure()` opens it again, and the black lands. If the write is genuinely
refused, `Lost()` fires, exactly as it does for a refused colour today -- one line, and the session
gives up.

### Re-asserting stops when there is nothing to re-assert to

`Lost()` nulls the device and makes `Abandoned` true. The loop clears `_holdingDark` at the same
time, so a keyboard that has gone does not leave a thread waking every twenty seconds until the
screensaver ends. Nothing to send to is nothing to hold.

### The overlay gains `LeftScreen`, and it is not `LeftDark`

`LeftDark` cannot be reused for the artifacts stage, because `App` hangs the workstation lock off
it:

```csharp
if (reachedBlack && _host.Current.LockAfterBlackout && !SessionState.Locked) SessionLock.Request();
```

`reachedBlack` guards it today. Raising `LeftDark` from a path that never reached black would make
that guard the only thing standing between an ordinary "you came back to your desk" and a locked
machine, which is too much weight for one bool.

So the overlay raises a second event where it actually leaves the screen. Both hide paths -- the
0.35s fade's completion and the immediate hide -- already end in the same three lines, which become
one place:

```csharp
private void Hidden(bool wasShown)
{
    Visibility = Visibility.Hidden;
    Suspended = true;
    Collapse();

    // Only when something was actually on screen. HideBubbles(immediate: true) is also how the
    // window gets out of the way at startup, and a hand-back is not owed for that.
    if (wasShown) Raise(LeftScreen, nameof(LeftScreen));
}
```

Raised through `Raise`, like the blackout events, so a subscriber that throws cannot leave the
overlay hidden and suspended with the desktop underneath it.

### The hand-back itself is not touched

`KeyboardLighting.LeftDark()` already does everything the artifacts stage needs: it clears
`_emitting`, resets both send policies and the weather clock, and queues `Chore.Restore`, which
settles the Dynamic Lighting debt first and the keyboard second. So the second entrance is a second
caller, not a second implementation. The method keeps its name -- it is still "the screensaver is
leaving" -- and `App` wires both events to it.

Ordering when a blackout ends normally: `LeftDark` fires, then the overlay hides a moment later and
`LeftScreen` fires. The second finds an empty ledger and `GiveBack` returns after settling nothing,
which is what it already does whenever nothing is owed.

## Risks / Trade-offs

- **The vendor's software holds an animated effect** → then no interval wins, and the keys alternate
  between black and whatever it is drawing. Black becomes the steady state at the cadence rather
  than the exception; that is strictly better than today, where green is the steady state. Worth
  saying out loud rather than discovering: this design bounds how long the wrong colour is shown,
  it cannot prevent it.
- **A blind write every 2s for the first half-minute** → measurably nothing on this hardware (128
  bytes, three reports), and it is on a below-normal-priority thread that exists to be blocked.
- **`LeftScreen` fires on a path nobody anticipated** → the only subscriber is a hand-back that is a
  no-op when nothing is owed, and the lock is unreachable from it.
- **Re-assert resurrects a keyboard the session gave up on** → it cannot: `Ensure()` returns false
  forever once the search has failed, and `_holdingDark` is cleared when the device is lost.

## Migration Plan

None. No setting, no file format, no stored state changes. Existing `keyboard-state.json` and
`dynamic-lighting-state.json` files are read and written exactly as before. Rollback is reverting
the commit; nothing on disk outlives it.

## Open Questions

- **Are 2s/30s/20s the right numbers?** They are derived from one observation -- green within five
  to ten seconds of black -- on one machine. `BUBBLES_LOG` is now on, so the next blackout records
  what actually happened; the numbers are three constants in one file if it turns out the repaint
  arrives later, or repeats.
- **Does a blind re-assert actually win the keys back from Armoury Crate?** It should -- the same
  packets win them in the first place, and the memory of watching a hand-back says the vendor
  reasserts within a moment, which is a race this now re-enters every cadence. If it turns out the
  vendor's software wins permanently once it has repainted, the re-assert becomes a diagnostic
  rather than a fix, and the answer moves to standing Armoury Crate down the way Dynamic Lighting
  already is.
