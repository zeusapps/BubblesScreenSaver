## Context

`KeyboardLighting` holds two fields for one decision:

```csharp
private IKeyboardDevice? _device;
private bool _decided;
```

and reads them as though they answered two questions:

```csharp
private bool Abandoned => _decided && _device is null;   // "we looked and found nothing"
private bool Ensure()   { if (_decided) return _device is not null; ... }  // "we hold one"
```

Those are the same sentence only until the first hand-back. After it, `_device` refers to an
`AuraKeyboard` whose handle has been disposed, and the layer above cannot tell that from one
that is still held. The device knows; nobody asks it.

```
 first blackout            hand-back            second blackout
 ---------------           ---------            ---------------
 Ensure  -> Open() ok      Restore()            Ensure  -> cached "yes"  <- the lie
 Show    -> writes ok      +- Release()         Show    -> _handle null -> false, silent
 GoDark  -> black ok          handle gone       GoDark  -> false, silent
                              _device stale     keys stay wherever Aura put them
                              _decided stale
```

The hardware side is not at fault. Releasing *is* the hand-back -- the Aura protocol only
listens, so there is nothing to restore *to* and letting go is the whole of giving back. The
defect is entirely in remembering an answer that had already expired.

## Goals / Non-Goals

**Goals:**

- The keyboard goes dark on every blackout of a session, not only the first.
- The debt is on disk before the first colour of every loan, not only the first.
- A device that has gone away is noticed, said once, and stopped being sent colours.
- The fake keyboard in the tests is at least as unforgiving as the real one.

**Non-Goals:**

- Reopening after a *failed* search. One search per session stays exactly as specified.
- Retrying a keyboard that refused a write. That is a failure, and failures are decided once.
- Any change to when the keyboard is lit, what colour, or how often. The Emission, the weather,
  the rationing and the settings are untouched.
- Holding the device across the hand-back. The keyboard is still given back on `LeftDark`; this
  only makes the *next* loan possible.

## Decisions

### The device answers whether it is open; nothing above it caches that

`IKeyboardDevice` gains one member:

```csharp
/// <summary>Whether the device is in hand right now.</summary>
bool IsOpen { get; }
```

`AuraKeyboard` already keeps exactly this state -- `_handle` and `_device` are set on `Open`
and cleared by `Release`, which runs on a hand-back, a refused write, an exception and
`Dispose`. `IsOpen => _handle is not null && _device is not null` reports it, adding no state
and no behaviour.

*Alternative considered: track it in `KeyboardLighting`.* It would need a flag set on every path
that closes the device -- `GiveBack`, a false `Show`, a false `GoDark`, a failed re-`Open` --
which is a shadow copy of state the device already holds, kept in step by hand. That is the
class of bug being fixed here, so it is not the shape of the fix.

*Alternative considered: null `_device` in `GiveBack`.* One line, and it fixes the reported
symptom. But `_decided` would still be true, so `Abandoned` would become true and the ramp would
stop computing colours for a keyboard that is perfectly fine -- trading a lit keyboard for a
dark one. The two meanings have to come apart whatever else is done.

### `_decided` becomes `_searched`, and means only that

The rename is the point of the change, not decoration. Three states are then expressible with
the two fields, without an enum:

| `_searched` | `_device`             | meaning                           | `Ensure()`  |
|-------------|-----------------------|-----------------------------------|-------------|
| false       | `null`                | not looked yet                    | search      |
| true        | `{ IsOpen: true }`    | in hand                           | true        |
| true        | `{ IsOpen: false }`   | found once, handed back           | open again  |
| true        | `null`                | looked and found nothing, or lost | false       |

`Abandoned` stays `_searched && _device is null` -- the same expression, now true only when it
should be.

```csharp
private bool Ensure()
{
    if (_device is { IsOpen: true }) return true;
    if (_searched && _device is null) return false;

    // Never looked, or looked and found one that has since been handed back. Re-opening a
    // keyboard we have already found is not a second search: we know it is there.
    var device = _device ?? _open();
    var record = device.Open();
    ...
}
```

Re-opening reuses the same `IKeyboardDevice` instance rather than constructing a new one.
`AuraKeyboard.Open()` is already written to be callable on a closed device -- it assigns both
fields outright -- and reuse keeps `_open` meaning "how to reach a keyboard", once, rather than
"how to reach it again".

`_searched` is now set *after* the search rather than before it. During a slow open the layer is
no longer briefly `Abandoned`, so frames arriving while the handle is being acquired are
coalesced rather than dropped.

### Re-opening re-records the debt, because `Settle` forgot it

`GiveBack` settles the debt, which removes it from disk. Today nothing puts it back, so a
process that blacks out twice has a keyboard in hand and nothing on disk saying so -- and a
crash during that second Emission leaves a keyboard black with no record. Routing the re-open
through the same `_owed.Remember([record])` closes that, and needs no new code: it is the same
line the first open already runs.

This also gives the confirmation signature the investigation wanted. Before the fix, the second
blackout of a session logs no `restored 1 (awake)` at all, because `_owed` is empty. After it,
every blackout logs one.

### A refused write ends the session's lighting, and says so

The worker currently discards what `Show` and `GoDark` return. It stops:

```csharp
case Chore.Dark:
    if (Ensure() && !_device!.GoDark()) Lost();
    break;
```

`Lost()` logs once and nulls `_device`, leaving `_searched` true -- which is the "looked and
found nothing" row of the table, so `Abandoned` becomes true and the ramp stops computing
colours for a device that is gone. The device has already logged *why* it went.

*Alternative considered: re-open on a refused write.* It is a plausible recovery -- the keyboard
may have come back -- but a device that accepts `Open` and refuses every write would then loop
open-fail-open-fail once per rationed colour, roughly eight times a second, each with a log
line. The spec already says failure is decided once per session, and a write that is refused is
a failure. Give up, quietly, and let the next run try.

Dynamic Lighting being off is *not* this case: those writes are accepted and thrown away, so
`Hid.Write` succeeds and nothing here fires. That failure stays invisible by nature, which is
why the setting says so in words.

### The fake keyboard stops being kinder than the hardware

```csharp
public bool Restore(KeyboardRecord record) { lock (_gate) _restores++; return restores; }
```

That is the bug's hiding place: the real `Restore` closes the device, the fake's does not, so no
test could ever reach the stale state. The fake gets `IsOpen`, a `Restore` that closes it, an
`Open` that can be called again, and a switch to make `Show` fail.

`TheKeyboardIsOpenedOnceAcrossManyEmissions` then has to go, because it asserts the symptom:
three blackouts, `Assert.Equal(1, keyboard.Opens)`. What it was defending -- that a machine with
no keyboard does not go searching on every Emission -- is already covered by the tests around a
failed search, and is restated as a test that counts *searches* rather than *opens*.

## Risks / Trade-offs

- **A HID handle is opened once per blackout instead of once per session.** -> Blackouts are
  minutes apart at the shortest, and the handle is opened and closed on a below-normal
  background thread that nothing waits on. It is the same cost the first blackout already pays.

- **`AuraKeyboard.Open()` re-logs its "using 0B05:19B6..." line on every loan.** -> Kept
  deliberately. Logging is opt-in, blackouts are minutes apart, and that line is the evidence
  that would have made this bug visible in an afternoon rather than over several sightings.

- **Giving up on a refused write is one-way for the session.** -> A keyboard unplugged and
  plugged back in stays dark until Bubbles restarts. That is the existing behaviour, made
  explicit and logged rather than silent, and it is what "decided once per session" already
  says everywhere else in this capability.

- **`_device` is now kept after a hand-back rather than nulled.** -> It holds no handle and no
  collection at that point, so it is an empty object; `Dispose` still disposes it at exit.

## Open Questions

None. The one question the investigation could not answer from the log -- whether the reported
sighting was the second blackout of its session -- does not gate the fix: the defect is provable
from the code without reproducing it, and the fix is correct whether or not it explains that
particular evening.
