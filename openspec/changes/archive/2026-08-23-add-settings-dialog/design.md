## Context

Bubbles is a tray application with no window except the click-through overlay it draws when
you go idle. Configuration has therefore always lived on the tray menu, and everything the
menu could not hold has lived in `settings.json`.

Three facts about the existing code shape this design more than anything in the proposal:

**`Settings` is a mutable object shared by reference.** `TrayIcon.Tweak` mutates the instance
in place, calls `Clamped()`, and then fans the same instance out to `OverlayWindow.Apply`,
`IdleController.Apply` and `Updater.Apply`. Live application of a setting is therefore already
how the app works; the dialog does not need a new mechanism, it needs the existing one.

**That sharing is currently broken in one place.** `TrayIcon.ReloadSettings` does
`_settings = Settings.Load()`, replacing its own reference while `App` keeps the original.
`App` closes over its instance in the `LeftDark` handler to read `LockAfterBlackout`, and
`Updater` holds another. After a reload, those read a stale object for the rest of the
process. Retiring `Reload settings` removes the symptom; this design removes the cause by
making the instance canonical and never reassigning it.

**Hold-off is already a composable value.** `HoldOff` is a record struct with an `And` that
merges two reasons, written for the case of music suppressing artifacts while permitting the
blackout. An open settings window is simply another reason, and needs no new concept -- only
a way for an app-level reason to reach `IdleController`, which today asks `UserBusy` and
nothing else.

## Goals / Non-Goals

**Goals:**

- One window presenting every setting in `Settings.cs`, including the nine that have never had
  a UI, with current values visible rather than implied by a tick.
- A tray menu of commands only, where nothing is more than one click deep and every entry that
  is constructed is reachable.
- A single canonical `Settings` instance for the process lifetime, mutated in place, so that
  no component can read a stale copy.
- `Clamped()` as the only place that decides what a legal value is, with the dialog's controls
  agreeing with it rather than duplicating it.
- The screensaver does not start over a settings window somebody is reading.

**Non-Goals:**

- **A live preview of visual settings.** See the decision below; this is the sharpest
  trade-off in the change and it is being accepted rather than solved.
- MVVM, a view-model layer, or any UI framework dependency. The publish is
  framework-dependent single-file precisely to stay small.
- Any change to `settings.json`'s schema, version, or migration path.
- Restyling the dialog to match the Zone theme. It is a settings window; it should look like
  the operating system.
- Automated UI tests. Window construction is not testable in this project's harness.

## Decisions

### A `SettingsHost` owns the canonical instance and the fan-out

`TrayIcon.Tweak` is the only thing that currently knows the sequence "mutate, clamp, then tell
the overlay, the idle controller and the updater". With a second editor of settings that
knowledge would be duplicated, and the two copies would drift the first time a fourth listener
is added.

A small non-UI class, `SettingsHost`, holds the one `Settings` instance and exposes
`Edit(Action<Settings>)`, which mutates, calls `Clamped()`, and fans out. `TrayIcon` and the
dialog both go through it. `App` constructs it and hands it to both.

*Alternative considered:* leave the fan-out in `TrayIcon` and have the dialog call back into
it. Rejected because it makes the tray menu -- soon to be the smaller surface -- the owner of
state the dialog is the primary editor of.

*Alternative considered:* an event on `Settings` itself. Rejected because `Settings` is a
serialisation DTO and giving it change notification drags `[JsonIgnore]` bookkeeping and
subscription lifetime into a type whose whole job is to round-trip cleanly.

### Live application, with a snapshot that Cancel restores

Settings apply as they are edited, because that is how the app already behaves and because the
alternative -- accumulating a pending copy and committing on OK -- would need the fan-out to
be reconstructed at commit time for every listener.

Applying live leaves no way to back out. So the dialog takes a snapshot when it opens and
offers `Cancel`, which restores it through the same `Edit` path; `Close` and the window's X
keep what is on screen, and `Restore defaults` applies `new Settings()`. `Settings` is saved
on close, not on each edit, so that dragging a slider does not write the file continuously.

The snapshot is taken by serialising and deserialising through the existing `JsonOptions`.
That is one line, needs no maintenance as properties are added, and clones exactly the
properties that persist -- `[JsonIgnore]` members are computed and should not be captured.

*Alternative considered:* `MemberwiseClone`. Equivalent today and cheaper, but it would
silently start copying any future field that persistence deliberately excludes.

### An app-level hold-off reason, composed with `UserBusy`

`IdleController.Tick` currently asks `UserBusy.Held(_settings)` and nothing else. It gains a
settable `HoldOff AppHold`, merged with `held.And(AppHold)`. The dialog sets
`HoldOff.Everything("the settings window is open")` while open and `HoldOff.None` on close.

`Everything`, not `ArtifactsOnly`: there is no reading of the situation in which blacking out
a screen somebody is reading is correct.

An explicit `Start now` or `Black screen now` from the tray still overrides it, because
`Tick` already discards all hold-off when a force is armed, and "if you ask for it, you get
it" should not acquire an exception.

*Alternative considered:* a static flag inside `UserBusy`. Rejected -- `UserBusy` reads system
state (the registry, the foreground window, media sessions) and mixing app-internal state into
it makes it untestable as the pure function it currently is.

### No live preview of visual settings, and the reason is the hold-off

Holding the screensaver off while the dialog is open means the overlay can never be on screen
while you are editing. Live application of `Dim`, `BubbleCount`, `Speed`, `Opacity` and the
theme is therefore real but invisible: you see it the next time the screensaver runs.

This is the cost of not having the screensaver cover the window you are configuring it in, and
it is the right side of that trade. A `Preview` button that forces the overlay is the obvious
fix, but `IdleController.Force` is cancelled by the next input, and moving the mouse back
toward the dialog is input -- so a preview would need the force-cancellation logic reworked to
ignore input directed at the dialog. That is a change to the most safety-critical logic in the
app (the guarantee that any keypress returns the desktop) and it does not belong bolted onto
this one. Recorded as a follow-up.

### A diagnostic flag opens the window directly

`--settings` opens the settings window at startup, joining the dozen diagnostic flags
`Program.cs` already carries (`--emission-demo`, `--weather-demo`, `--hold-test`, `--busy` and
the rest). The window is otherwise reachable only through the tray, which is awkward to drive
when checking the layout at a scaling factor or on a second monitor -- the same reason
`--emission-demo` exists rather than waiting out a real idle period.

### The dialog is XAML, and that is a new convention here

The repository has no `.xaml` file; `OverlayWindow` is 1221 lines of hand-built code-behind.
That is right for the overlay, whose content is drawn geometry with no static structure. A
settings form is the opposite -- mostly static layout with values bound into it -- and is
markedly less code in XAML.

The convention this sets, to be followed rather than reinvented: XAML for layout, code-behind
for wiring, no view-models and no third-party UI packages. The overlay stays as it is.

### Control ranges are derived from `Clamped()`, not restated

A slider whose maximum disagrees with `Clamped()` produces a value that silently snaps
somewhere else. The bounds move out of `Clamped()`'s body into named constants on `Settings`,
which both `Clamped()` and the dialog read, and a test asserts the two agree.

`BlackoutSeconds` is the awkward one: its clamp depends on `IdleSeconds`
(`Math.Clamp(BlackoutSeconds, IdleSeconds, 86400)`), so a blackout delay set below the start
delay is silently raised. The dialog presents the blackout delay as time measured from when
the screensaver starts, which is what the clamp actually enforces, so the constraint is
visible in the label instead of ambushing the user.

**Zero cannot also mean never.** Found while checking the window against a real
`settings.json`, and worth recording because the first attempt got it wrong. Presenting the
delay as a gap makes zero a legitimate setting -- black the moment the artifacts would have
appeared -- while `BlackoutSeconds = 0` separately means no blackout at all. A file with both
delays at 120 is the first case, and folding the two together displayed it as "never" and
would have written the blackout off on closing: a safety feature silently disabled by opening
a window and closing it. "Never" therefore carries a sentinel of its own, and the round-trip
is covered by a test over real values rather than left to inspection.

### `MonitorStandby` is exposed with its hazard stated

It has no UI today, and the class comment in `IdleController` records why: driving the monitor
off with `SC_MONITORPOWER` suspends the whole machine on a Modern Standby laptop, and with a
retry timer it became a wake/sleep loop. Surfacing every setting means surfacing this one, but
it ships defaulted off, grouped away from the everyday controls, and labelled with what it
does on such a machine rather than as a neutral checkbox.

## Risks / Trade-offs

- **Visual settings apply invisibly** -> Accepted, per the decision above. Mitigated by the
  dialog naming the delay ("takes effect next time the screensaver runs") rather than
  pretending the change did nothing.
- **Control ranges drift from `Clamped()`** -> Shared constants plus a test that walks them.
- **The dialog is a new window in an app that had none, so it inherits none of the overlay's
  hard-won Win32 care** -> It is an ordinary window and needs none of it, but it must honour
  the per-monitor DPI awareness already declared in `app.manifest`: no hard-coded pixel sizes,
  and sizing verified on a scaled display.
- **`Cancel` after a long editing session reverts more than the user expects** -> The button is
  labelled as reverting everything since the window opened, and `Restore defaults` is kept
  visually separate from it so the two are not confused.
- **Settings written by hand while the dialog is open are overwritten on close** -> The file
  stays hand-editable, but the dialog is the writer while it is open. Retiring
  `Reload settings` removes the only in-app way to provoke this mid-session.
- **Removing the configuration submenus is a visible loss for anyone who used them** ->
  Unavoidable given the goal, and the commands people actually use often (`Start now`,
  `Pause`, `Black screen now`) are the ones that stay.

## Migration Plan

No data migration: `settings.json` keeps its shape, its `SettingsVersion`, and the existing
density migration untouched. A user upgrading finds the same file honoured and a shorter menu.

The work is ordered so the app builds and runs at every step: introduce `SettingsHost` and
route the existing `Tweak` through it first, then add the window against it, then delete the
menu's configuration submenus last. Rollback at any point before that deletion leaves the old
menu intact.

## Open Questions

- Whether `Pause` belongs on the menu, in the dialog, or both. It is a command, so the menu
  by the boundary rule, but it is also the one piece of state the menu shows.
- Whether the dialog should offer the diagnostics log, which today has no surface at all.
