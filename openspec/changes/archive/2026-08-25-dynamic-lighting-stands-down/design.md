## Context

Two owners compete for these keys, and only one at a time yields.

```
  Dynamic Lighting ON                    Dynamic Lighting OFF
  ───────────────────                    ────────────────────
  Windows owns the LampArray             Armoury Crate owns the keys
  repaints its own effect continuously   yields to vendor-collection writes
  (accent colour, brightness 100)
        │                                       │
        ▼                                       ▼
  Bubbles writes → accepted → overpainted  Bubbles writes → accepted → shown
        │                                       │
   keys never change,                      keys follow the Emission,
   stay lit through the blackout           go dark with the screen
```

Both columns return success from `WriteFile`. Nothing in the process can distinguish them,
which is why the feature has spent its whole life documented backwards without anyone being
able to tell from inside the app.

Windows grants lighting control to a desktop application only while it is in the foreground,
unless the app is MSIX-packaged and declares the `com.microsoft.windows.lighting` extension.
The overlay is focus-less by design -- it must never take focus from what the user was doing --
so it can never be in the foreground, and it is not packaged. The supported route is closed;
the raw HID path is the only one there is, and it needs the other owner to stand down.

## Goals / Non-Goals

**Goals:**

- Every statement the application makes about Dynamic Lighting matches what the hardware does.
- The log line printed at the moment of failure names the real cause.
- Somebody who turns the feature on gets a working keyboard without editing Windows settings.
- Anything borrowed from Windows is written down before it is taken, and given back on the same
  paths as everything else -- waking, exit, and a bad ending recovered at next startup.

**Non-Goals:**

- Detecting which owner currently holds the keys. It cannot be done from inside the process;
  that is the whole shape of the problem.
- Turning Dynamic Lighting off for anybody who has not asked. The default stays exactly as it
  is today.
- Making the keyboard work while Dynamic Lighting is on. It cannot be done without MSIX
  packaging and foreground focus, and the overlay will not take focus.
- Any change to the colours, the clock, the rationing or the handle.

## Decisions

### The correction goes in all four places, in the same words

The sentence appears in the settings dialog, `Settings.KeyboardLighting`'s doc, `AuraKeyboard`'s
class doc, and the log line. All four say "on"; all four become "off", and each says what the
wrong state actually looks like -- keys held at one colour, writes accepted and discarded -- so
that the next person to meet the symptom recognises it.

The log line matters most and changes most:

```
current: "If nothing lights up, Dynamic Lighting is off and the vendor's software still
          owns the keys."
becomes: "If nothing lights up, Dynamic Lighting is on and Windows is repainting over these
          writes -- switch it off in Settings > Personalization > Dynamic Lighting."
```

It fires at the exact moment somebody is looking for the cause, and today it sends them the
wrong way.

### Standing Dynamic Lighting down is a borrow, on the ledger that already exists

One registry value, per-user, no elevation:

```
HKCU\Software\Microsoft\Lighting\AmbientLightingEnabled   REG_DWORD   0 = off, 1 = on
```

The lifecycle is the one this application already runs three times over:

```
take   →  read the current value, Remember it on disk, write 0
give   →  Settle: write back the remembered value, Forget it
recover→  RecoverFromCrash settles whatever a dead process left behind
```

`PendingRestore<T>` provides all of it -- `Remember` is idempotent by key and first-value-wins,
`Settle` forgets only what was confirmed, and the file is written before the change it
describes. A new record type beside `KeyboardRecord`, holding the previous value, is the whole
of the new state.

*Alternative considered: set it back to `1` on release.* Wrong for anyone who already keeps
Dynamic Lighting off -- Bubbles would hand back something it never took and switch on a feature
they had deliberately disabled. The ledger exists precisely so the previous value survives a
crash; use it.

*Alternative considered: leave it off once set.* Cheaper, and a betrayal. Whatever is borrowed
here is given back, and an OS personalization setting is the last thing that should be an
exception.

### Its own setting, off by default

`KeyboardLighting` says "carry an Emission onto the keyboard". It does not say "edit Windows
settings on my behalf", and somebody who wanted the first did not thereby ask for the second.
Flipping a user-visible OS toggle is a longer reach than anything else this application does --
further than dimming a backlight, which is at least obviously reversible from the monitor's own
buttons.

So: a third checkbox, subordinate to `KeyboardLighting` the way `KeyboardWeather` already is,
off by default, whose note says plainly that it changes a Windows setting and puts it back.

The cost of that choice is that the default experience is still a keyboard that does nothing.
The corrected text is what carries that case, and it is why the correction ships first and
stands on its own.

### The loan lasts as long as the keyboard's loan

Same events, same lifetime -- taken where the device is opened, given back where it is
released. That keeps one answer to "what does Bubbles currently hold", and it means the
recovery path already written for the keyboard covers this too.

It does inherit the weather's long loan: with `KeyboardWeather` on, the device is held for the
whole time the screensaver is up, so Dynamic Lighting would be off for that whole time -- all
night on a machine that never sleeps. That is disclosed in the setting's note rather than
solved, because the alternative -- taking and returning it around each Emission -- means
flipping an OS setting every few minutes, which is worse.

## Risks / Trade-offs

- **The registry value is undocumented.** No API exists for it; this is the Settings app's own
  store. -> If Microsoft moves it, the write silently does nothing and the feature degrades to
  exactly today's behaviour, which the corrected text already describes. Nothing breaks; it
  stops helping.

- **One machine, one keyboard.** Everything measured here is a single ASUS `0B05:19B6` with
  Armoury Crate installed. -> The corrected text describes what was observed rather than
  asserting a universal law, and the log line names the check rather than the verdict. The
  A/B is reproducible in two minutes by anyone who doubts it.

- **A user-visible OS setting moves on its own.** Open Settings > Personalization > Dynamic
  Lighting during an Emission and the toggle will be off. -> Opt-in, off by default, and stated
  in the note.

- **Latency between the write and the effect is unmeasured.** The one observation had three
  seconds of slack. -> The buildup opens from black over six and a half seconds, so there is
  room; worth measuring during implementation, and worth taking the value early -- at
  `EmissionBegan`, where the device is opened -- rather than at the first colour.

  Measured on 2026-08-25, from the log of a `--emission-demo` run:

  ```
  12:04:50.311  keyboard lighting: using 0B05:19B6, 128-byte reports
  12:04:50.321  dynamic lighting: stood down (was on)
  ```

  Ten milliseconds after the device comes into hand, and 0.58s after process launch -- so the
  whole of the buildup is still ahead of it, and the write costs the Emission nothing. What
  that does *not* measure is how long Windows takes to actually let go once the value changes;
  nothing in the process can see that, which is the shape of the whole problem, and it is why
  the check at the end of this change is somebody watching the keys.

- **A crash leaves Dynamic Lighting off.** -> Same as every other borrow here, and covered by
  the same ledger: the record is on disk before the value changes, and the next start settles
  it. The failure mode is a personalization setting to switch back on, not a dark keyboard.

## Open Questions

None blocking. The three raised in the proposal are answered above: the loan matches the
keyboard's, the latency is affordable and will be measured, and the single-machine evidence is
handled by describing rather than asserting.
