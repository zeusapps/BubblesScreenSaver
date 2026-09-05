## Context

The re-assert loop keeps a borrowed keyboard black through a blackout by writing black to it on a
timer, blind, because the Aura protocol has no read. The previous change gave that timer a ramp:
two seconds after a disturbance, relaxing by half again each time to a ceiling of twenty, and back
to two whenever `MachineEvents` reported the session locking, the power mode changing, or the
displays being reconfigured.

A week of production logging is now available and it decides the question the ramp was built to
guess at.

## Goals / Non-Goals

**Goals**

- Bound the time a repaint can sit visible on the keys at two seconds rather than twenty.
- Remove the ramp rather than retune it, so that one number decides the behaviour and it is the
  number that has always decided the behaviour.

**Non-Goals**

- Detecting a repaint. There is no read on this hardware and there never will be; every design
  here is an assertion into the dark and must stay honest about that.
- Taking the device exclusively so that no other owner can repaint it. That would dissolve the
  problem rather than bound it, and is a larger question about whether `ShareReadWrite` can be
  given up without breaking the hand-back. Left open deliberately.
- Removing `MachineEvents`. See below.

## Decisions

### The interval is flat, at two seconds

The evidence is a histogram of every wait the application has taken:

```
next in 20s   11555   99.2%
next in 15,2s    52
next in 10,1s    52
next in 6,8s     52
next in 4,5s     52
next in 3s       40
next in 2s       52
```

Fifty-two blackouts contributed six ramp steps each and then sat at the ceiling for as long as the
screen was black -- in the last case, 5,085 seconds and 254 consecutive twenty-second waits. The
ramp is about sixty-two seconds of attention per blackout and nothing thereafter.

Worst-case exposure equals the interval exactly, since a repaint arrives at no particular point
inside the wait. Twenty seconds of ceiling is what produced the five-to-seven-second green observed
in production; that observation is not a fault but an ordinary draw from a distribution with a mean
of ten. Any ceiling at or above five seconds would reproduce it. Two seconds puts the worst case
below what has been reported as noticeable, and the mean at one.

**Why not a lower ceiling with the ramp kept.** Below about seven seconds the ramp has two or three
steps left and stops meaning anything, while still costing `Growth`, `Relax`, a mutable `_cadence`
and a reset path in two places. At two seconds the floor and the ceiling meet and the machinery has
nothing at all to express. Deleting it is the honest consequence of the number, not a separate
tidying-up.

### The cost objection does not hold

Three 128-byte HID output reports per re-assert. A nine-hour blackout goes from 1,620 re-asserts to
16,200.

Set against what this application already does: `SendPolicy.ForEmission()` has a floor of 0.12s, so
the keyboard is written to at up to eight sends a second for the whole twelve seconds of every
Emission. The blackout cadence at two seconds is sixteen times slower than the rate this codebase
already treats as unremarkable.

The wear question -- whether `Apply` (0xB4) commits to onboard memory, in which case repetition
would be a hardware concern rather than a power one -- was checked against asusctl, whose `rog-aura`
crate drives `Breathe` and `DoomFlicker` as host-streamed per-frame writes for as long as the effect
runs. Continuous high-rate writing is this protocol's ordinary mode of operation, so 0xB4 is not a
flash commit and there is nothing to wear out.

### The twenty seconds was borrowed from a mechanism that does not match

The ceiling was justified as "the same interval the display blackout already uses against the same
class of problem, so there is one number to reason about rather than two". The comment on
`DisplayBlackout._whileDark` says what that interval actually is:

> This reads over DDC/CI and only writes when something has actually moved, so an undisturbed
> blackout costs one read per monitor every twenty seconds and nothing else.

That is a **detection** interval on hardware that answers questions. This is an **assertion**
interval on hardware that does not. The absence of a read is the entire shape of the keyboard
feature -- it is why the loop exists at all -- so the one place the two mechanisms differ most is
precisely the place the number was copied across. The symmetry was appealing and wrong.

### `MachineEvents` stays, and is rescoped

A disturbance did two things: send black immediately, and return the ramp to its floor. The second
disappears with the ramp. The first survives, and is now the whole of it.

Its value shrinks accordingly -- from "up to twenty seconds of green avoided, plus sixty seconds of
renewed attention" to "up to two seconds of green avoided", on the sixteen occasions a disturbance
has fired across the whole log. That is thin.

It is kept anyway. It costs nothing when idle, it is already built and tested, it writes at exactly
the moments other software is most likely to have just reasserted itself, and deleting it would
also unwind the subscription in `App.cs`. If it stops paying rent it can go as its own change,
where the deletion can be judged on its own.

What must change is its documentation, which currently justifies the class like this:

> a lighting layer that wants to keep the keys black has to choose between writing constantly and
> writing at the right moments. These are the right moments

This change settles that choice the other way. The comment is rewritten to say that the system now
writes constantly and that these moments are worth not waiting through -- a smaller claim, and the
true one.

### The bare-wake guard is independent and stays

```csharp
if (chore != Chore.Open && colour is null) break;
```

This exists because a wake carrying no chore and no colour used to fall through to the colour arm
and clear `_holdingDark`, silently cancelling the blackout hold. It is a fix for a latent defect,
not part of the ramp, and it is untouched here. With `MachineEvents` retained it also still has a
live caller.

## Risks / Trade-offs

- **Two seconds may still be visible.** If a repaint is ever seen and the log shows the loop
  running normally beside it, the interval is the number to lower and there is now only one of
  them. The log line keeps reporting how far into the blackout each re-assert falls, which is what
  makes that diagnosable.
- **Ten times the writes during a blackout.** Accepted on the reasoning above. If a battery cost
  is ever measured on this, the answer is a lower rate while on battery, not a ramp -- a ramp does
  not track the thing that would actually matter.
- **The ramp's premise is not disproved, only shown to be inert here.** Repaints could genuinely
  cluster after disturbances; nothing observed can say, because a repaint is invisible. What the
  log does show is that disturbances are far too rare for the ramp to act on that premise often
  enough to matter.

## Migration Plan

None. No persisted state, no settings, and no wire format changes. The behaviour difference is
entirely within one blackout.

## Open Questions

- Can the device be opened exclusively for the duration of a blackout, so that no other owner can
  repaint it and the loop is unnecessary? `Hid.Open` asks for `ShareReadWrite` deliberately, and it
  is unknown whether an exclusive open is grantable while the vendor's service holds its own handle,
  or what it would do to the hand-back. This is the fix that removes the problem rather than
  bounding it, and it is worth its own investigation.
