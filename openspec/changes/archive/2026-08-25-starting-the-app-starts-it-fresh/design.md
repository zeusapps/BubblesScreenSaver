## Context

`IdleClock` answers one question -- how long has the user actually been away -- from one input
that cannot answer it alone. `NativeInput.IdleSeconds()` is a system-wide counter maintained by
Windows since the last keyboard or mouse event. It survives this process starting and stopping,
because it was never about this process.

The class already corrects that number once, for a reason of exactly the same shape:

```
                systemIdle          what it means            what the clock does
  ────────────  ──────────────────  ───────────────────────  ────────────────────
  on a call     climbs all call     not away, just quiet     subtract the held time
  fresh start   already at 149s     away from *what?*        nothing -- today
```

Both are cases where the system counter is measuring something other than "time this
screensaver could have been running". The first is handled. The second is not, and the failure
is worse, because it lands on the first seconds of every run rather than after a call.

The stage machine above it is not at fault and does exactly what it should:

```
  Resolve(wantsBubbles: true, wantsBlackout: true, held: None) -> Blackout
```

`Resolve` returns the furthest stage asked for. Asked for `Blackout` on the second tick of the
process, it gives `Blackout`. The lie is in the input.

Separately, `Startup` writes `HKCU\...\CurrentVersion\Run\Bubbles` and nothing else. Windows
search indexes Start Menu shortcuts; it does not index `Run` values, and there is no installer
here to have created one. So the application has never had an entry, and nothing about the
existing code is wrong -- it simply does less than it needs to.

## Goals / Non-Goals

**Goals:**

- A run that starts into an idle machine begins its countdown at zero and walks Active ->
  Bubbles -> Blackout in order, at the configured intervals.
- The existing hold-off subtraction keeps working exactly as specified, and is proved to.
- The application is findable by name in Windows search.
- What is written to the machine is removed again when startup is turned off.

**Non-Goals:**

- Any change to the thresholds, to `Resolve`, to the stage machine, or to the overlay.
- Any change to the media hold-off. The blackout-without-artifacts path it owns is correct and
  is a different fault from this one, which fires with nothing held at all.
- Persisting idle time across runs. The opposite is wanted: a run should start fresh.
- An installer, an uninstaller, or an entry in Apps & Features.

## Decisions

### The ceiling lives in `IdleClock`, next to the subtraction it composes with

`IdleClock` is the answer to "how long have you actually been away", and both corrections
belong to that question. The controller owns *thresholds*; the clock owns *how much time
counts*. Putting the ceiling in the controller would split one question across two classes and
leave `IdleClock` still able to return an answer nobody should act on.

It also keeps the whole thing testable without a dispatcher, which is why `IdleClockTests`
exists and drives the class directly at 400 ms ticks.

The clock already receives a monotonic clock (`Environment.TickCount64`) on every call, so the
start of the run is the first value it is handed. No new input, no new dependency:

```
  _startedAt ??= now                          // first call wins
  sinceStart = (now - _startedAt) / 1000
  return Math.Min(systemIdle - _heldOffSeconds, sinceStart)
```

*Order matters.* The ceiling is applied to the result, after the hold-off subtraction, not to
`systemIdle` before it. Clamping the input would make the hold-off tally meaningless for the
first minutes of a run.

*Alternative considered: reset by faking an input event at startup.* `SetLastInputInfo` does
not exist as a public API, and forcing a synthetic mouse move would lie to every other
application on the machine about whether somebody is present -- including Windows' own idle
handling. The fault is that this process reads a number that is not about it; the fix is to
stop trusting the part of that number that predates it, not to rewrite the number for
everybody.

*Alternative considered: have the controller ignore the first N ticks.* A grace period is a
different rule that happens to hide this one, and it would have to be tuned against
`BlackoutSeconds`. The ceiling is exact and needs no number.

### The first tick sets the origin, not the constructor

`_startedAt` is captured on the first call to `Elapsed` rather than at construction, because
the class is handed `now` rather than reading a clock itself, and taking an origin from a clock
it does not otherwise touch would be the one place it reached outside its inputs. The
difference between construction and the first tick is a fraction of a second and is in the
right direction: the clock cannot report time from before it was asked.

### The Start Menu entry is written where the `Run` value is written

`Startup.Set(true)` is the single place that says "this application should be present on this
machine after a reboot"; `Set(false)` is the single place that says otherwise. Being findable
belongs to the same statement, and one call site means no path can write half of it.

This does couple two wishes that are not identical -- somebody may want the entry without
starting at login. That is disclosed rather than solved: the entry is small, removable from the
same tray toggle that created it, and the alternative -- a second setting for it -- is more
surface than the problem deserves. If it is asked for, it is a later change.

`IsEnabled` continues to read the `Run` key alone. The tray menu's tick already reflects the
operating system's state and the registry value is the authority on it; a missing shortcut
beside a present `Run` value should not make the menu say startup is off.

The shortcut is a `.lnk` under the per-user Start Menu, written through `WScript.Shell` COM:

```
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Bubbles.lnk  ->  Environment.ProcessPath
```

Per-user, matching `HKCU`: no elevation, and nothing written for other accounts.

### Failure stays silent, as it already does here

Every operation in `Startup` is wrapped and swallows. A machine that refuses the shortcut is
not a machine that should fail to start the screensaver, and there is nowhere to report it to
from a tray toggle. That is the existing convention in this class and the shortcut follows it.

## Risks / Trade-offs

- **The ceiling could mask a genuine long idle.** A run started after a real two-hour absence
  now counts from launch rather than from the last keypress. -> That is the intent. The
  screensaver cannot have been idle before it existed, and the user is by definition present at
  the moment they start it.

- **`Environment.TickCount64` is monotonic but wraps at ~292 million years.** -> Not a risk;
  noted only so nobody reaches for `DateTime.Now`, which is not monotonic and would break the
  clock across a time-zone change or an NTP correction.

- **A shortcut appearing unrequested on existing installations.** Startup is on for these
  users, so the next run writes the entry without them asking. -> It is one `.lnk` in their own
  Start Menu, it is what they were missing, and turning startup off removes it. The alternative
  is that the fix reaches nobody who already has the app.

- **`Environment.ProcessPath` changes after an update swaps the binary.** -> The path is stable:
  the updater swaps the file in place at `%LOCALAPPDATA%\Programs\Bubbles\Bubbles.exe`. The
  shortcut is rewritten on every `Set(true)` regardless, so a moved binary is corrected the
  next time startup is toggled.

- **COM interop for a `.lnk`.** There is no managed shell-link API in .NET. -> `WScript.Shell`
  is the ordinary route, is already how the shortcut was created by hand on this machine to
  confirm search picks it up, and is inside the existing swallow-everything envelope.

## Migration Plan

None. Both changes take effect on the next run: the clock corrects itself immediately, and the
shortcut is written the first time `Startup.Set(true)` runs -- which happens when the tray
toggle is used. Existing installations with startup already on need the entry written without a
toggle, so the application SHALL reconcile the shortcut with the `Run` value at startup; that
is a task, not a migration, and it is idempotent.

## Open Questions

None blocking. The three raised in the proposal are answered above: the ceiling goes in
`IdleClock`, the entry is tied to the `Run` value with the coupling disclosed, and existing
installations get it reconciled at startup rather than being asked.
