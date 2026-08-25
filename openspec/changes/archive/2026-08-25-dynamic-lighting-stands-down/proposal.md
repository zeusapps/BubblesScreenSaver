## Why

Every place this feature describes itself says the same thing, and it is backwards.

> ASUS Aura keyboards only, and it needs Windows' Dynamic Lighting switched **on** [...] While
> that is off, Armoury Crate owns the keys and nothing will happen -- the writes are accepted
> and discarded with no error.

Measured on 2026-08-25 with two `--emission-demo` runs of the same binary, four minutes apart,
with one registry value changed between them and nothing else:

| `AmbientLightingEnabled` | what the keys did |
|---|---|
| `1` (on, as documented) | held one static red the whole time, ignored the Emission, **stayed lit through the blackout** |
| `0` (off) | followed the Emission and went dark with the screen, as designed |

Bubbles' own log was equivalent across both runs -- device opened, every write accepted, clean
hand-back, not one error. It cannot tell the difference, because when somebody else owns the
lighting the writes succeed and are thrown away. That is what makes the wrong sentence so
expensive: the feature has exactly one externally visible symptom, "nothing happens", and the
documentation points at the wrong cause.

The mechanism is the reverse of what was assumed. While Dynamic Lighting is on, *Windows* owns
the LampArray and repaints the keys with its own effect -- here a solid accent colour at full
brightness -- over the top of anything written to the vendor collection. An app is granted
control only in the foreground, or when MSIX-packaged with the `com.microsoft.windows.lighting`
extension; the overlay is deliberately focus-less and is neither. While Dynamic Lighting is
off, Armoury Crate owns the keys and *does* yield to those writes.

The `1` row above is also, in itself, the bug that has been reported more than once: an Emission
ends, the screen goes black, and the keyboard stays lit.

Having got the sentence right, the second half follows. Requiring the user to know this, find
it in Windows Settings, and leave it that way is a poor answer when the app can stand Dynamic
Lighting down for the length of the loan and give it back -- which is what this application
already does with monitor backlights, HDR and the keyboard handle itself.

## What Changes

- **Correct the requirement everywhere it is stated.** Four places say Dynamic Lighting must be
  on: the settings dialog note, `Settings.KeyboardLighting`'s doc, `AuraKeyboard`'s class doc,
  and the `using 0B05:19B6 ...` log line. The log line is the worst of them -- it fires at the
  moment of the failure and tells the reader to do the thing that causes it.
- **Say what actually happens instead**, in the same breath: while Dynamic Lighting is on,
  Windows holds the keys at its own colour and Bubbles' writes are discarded without error.
- **Add an opt-in setting that stands Dynamic Lighting down for the duration of the loan.**
  Off by default. Reads the current value, writes it to the debt ledger, sets it to off, and
  restores whatever it found on waking, on exit, and at the next startup if this run ended
  badly -- the same `PendingRestore` machinery that already carries the backlight, HDR and the
  keyboard.
- **Restore the value that was there, never a fixed one.** Somebody who already keeps Dynamic
  Lighting off must not have it switched on by Bubbles giving something back it never took.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `keyboard-lighting`: the requirement "The setting says what it needs and what it costs" is
  corrected -- what it needs is the opposite of what it says. Gains the rule that Dynamic
  Lighting can be borrowed and given back like anything else this application takes, behind its
  own setting.

## Impact

- `src/Bubbles/Session/SettingsWindow.xaml.cs` -- the note under the keyboard lighting checkbox,
  and one new checkbox with its own note.
- `src/Bubbles/Settings.cs` -- the corrected doc on `KeyboardLighting`, and one new key
  defaulting to off.
- `src/Bubbles/Keyboard/AuraKeyboard.cs` -- the class doc and the log line, both inverted.
- `src/Bubbles/Keyboard/` -- a new borrower for the Dynamic Lighting value, shaped like the
  existing ones: read, record, change, restore.
- `src/Bubbles/App.cs` -- wiring it to the same events the keyboard loan already uses.
- `openspec/specs/keyboard-lighting/spec.md` -- one requirement corrected, two added.
- No change to the Emission, the weather, the colours, the rationing, or the keyboard handle.

## Open Questions

Recorded here, to be settled in `design.md`:

1. **How long the loan should last.** Matching the keyboard's own loan is the obvious answer,
   but with `KeyboardWeather` on that loan runs for the whole time the screensaver is up, which
   could be all night -- a long time to hold somebody's OS setting.
2. **How quickly the change takes effect.** The one measurement so far had three seconds
   between the write and the Emission. The buildup opens from black over six and a half
   seconds, so some latency is affordable, but the budget is not known.
3. **Whether one machine is enough evidence to invert a documented requirement.** Everything
   here was measured on a single ASUS `0B05:19B6`.
