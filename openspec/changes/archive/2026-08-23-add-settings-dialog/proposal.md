## Why

Bubbles has around thirty settings and no window in which to set them. Everything reachable
lives on a tray menu that has grown by accretion into roughly twelve top-level rows, and
everything else -- nine settings including `AutoUpdate`, `MaxFps`, `ClickThrough` and
`MonitorStandby` -- is reachable only by opening `settings.json` in a text editor, which is
what `Edit settings...` does today.

The menu is also carrying two kinds of weight it is bad at. It cannot show a value, only a
tick, so `More bubbles` clicked four times leaves you with no idea where you landed. And it
has no room, so settings that have nothing to do with each other end up adjacent: the `Theme`
submenu currently holds the theme, five Zone toggles, and three system toggles about the
pointer, the backlight and HDR.

Two entries make the point sharply. `Check for updates` and `Start with Windows` are
constructed in `TrayIcon`, wired to working handlers, and never added to the menu. There was
no room, so they were quietly dropped, and nobody noticed because there is nowhere else to
look for them.

A settings window fixes the cause rather than the symptom. It has room for every setting, it
can show current values instead of ticks, and it lets the tray menu shrink back to what a tray
menu is genuinely good at: the two or three things you do often, immediately.

## What Changes

- **Add a settings window.** A single WPF dialog, opened from `Settings...` on the tray menu,
  presenting every setting in `Settings.cs` under headed groups. It shows current values --
  a slider or spinner reads `22 shapes`, not a tick beside `Some` -- and it is the first
  place in the app where a setting with no menu entry becomes visible at all.
- **Surface the nine hidden settings.** `AutoUpdate`, `UpdateCheckHours`, `MaxFps`,
  `ClickThrough`, `Wobble`, `SpeedVariance`, `CollectRadius`, `FadeInSeconds` and
  `MonitorStandby` gain a place in the UI. `MonitorStandby` ships with a warning attached:
  on a Modern Standby machine it suspends the system rather than the panel.
- **Cut the tray menu to its commands.** The menu becomes `Start now`, `Black screen now`,
  `Pause`, then `Settings...`, `Check for updates`, `Start with Windows`, `Exit`. Every
  configuration submenu -- `Start after`, `Black screen after`, `Dim the desktop`,
  `Ask for a PIN`, `Hold off while`, `Theme`, `Look` -- moves into the dialog. Top-level rows
  fall from roughly twelve to seven, and nothing is more than one click deep.
- **Restore the two unreachable entries.** `Check for updates` and `Start with Windows` are
  added to the menu at last. Their handlers already work; only the `menu.Items.Add` calls
  were missing. This is a defect being fixed, not a feature being added.
- **Adopt one neutral word for the idle visual.** The UI says *screensaver*, never *bubbles*,
  wherever it names the thing that appears when you go idle: `Start bubbles now` becomes
  `Start now`, and `BubbleCount` is presented as a count of *shapes*. Theme-specific wording
  survives only where it names a theme-specific event -- the blackout command still reads
  `Emission now` under the Zone theme.
- **Retire `Edit settings...` and `Reload settings`.** Both existed because the JSON file was
  the real interface. The dialog replaces the first; the second becomes unnecessary once the
  dialog is the writer, though the file stays hand-editable for anyone who prefers it.
- **BREAKING (interaction, not data):** the relative nudges (`More`/`Fewer`,
  `Bigger`/`Smaller`, `Faster`/`Slower`, `Brighter`/`Dimmer`) are gone, along with the
  configuration submenus. No stored value changes meaning, no migration is needed, and
  `settings.json` keeps its current shape and version.

## Capabilities

### New Capabilities

- `settings-dialog`: the settings window -- which settings it presents and how they are
  grouped, how an edit reaches the running overlay, how it behaves while the screensaver
  wants to start, what happens when the same dialog is asked to open twice, and how it treats
  settings the current theme ignores.
- `tray-menu`: what the tray menu offers once configuration has left it. Covers the rule that
  every constructed entry is reachable, the neutral vocabulary, the theme-conditional
  blackout label, and the boundary that decides whether something belongs on the menu or in
  the dialog.

### Modified Capabilities

None. `idle-hold-off` and `overlay-transparency` describe behaviour these surfaces select
between, not the surfaces themselves. No requirement of either changes: this change moves and
relabels controls without altering what any setting does.

## Impact

- **New** `src/Bubbles/Session/SettingsWindow.xaml` and `.xaml.cs` -- the repo's first XAML.
  Every window so far, including the 1221-line `OverlayWindow`, is hand-built code-behind, so
  this establishes a UI convention as well as a window.
- `src/Bubbles/Session/TrayIcon.cs` -- shrinks substantially. The constructor loses every
  configuration submenu; `RefreshChecks`, the `_checks` list, `_zoneOnly` and the `Toggle` and
  `Choice` helpers go with them. `RefreshUpdateItem`, `CheckForUpdates` and `Notify` stay and
  finally become reachable.
- `src/Bubbles/Settings.cs` -- no schema change. `Clamped()` becomes load-bearing for input
  validation, since a dialog can offer values a menu never could.
- `src/Bubbles/Session/IdleController.cs` -- needs to know an open dialog is a reason not to
  start the screensaver.
- `app.manifest` already declares per-monitor DPI awareness; the new window must honour it
  rather than assume system DPI.
- `tests/Bubbles.Tests` -- `SettingsTests` covers `Clamped()` already; the dialog's
  value mapping deserves the same treatment. Window construction itself is not unit-testable
  here and is out of scope for automated coverage.
- Docs and README mentioning menu paths or `Edit settings...` need a pass.

## Open Questions

Recorded here, to be settled in `design.md`:

1. **Live apply, or OK and Cancel?** Live apply is the better fit for settings you tune by
   eye -- dim level, shape count, speed -- because the overlay is often on screen behind the
   dialog. It also means there is no obvious way to back out of a change you dislike. A
   likely answer is live apply plus `Restore defaults`, but it deserves a decision rather
   than a default.
2. **What does the dialog do to the idle clock?** An open settings window is user activity,
   so the screensaver will not start while you are typing in it. Whether it should also
   suppress the blackout, and whether closing it should reset the idle clock, is a
   hold-off question and belongs alongside the existing `idle-hold-off` reasons.
3. **Does the dialog preview the theme?** Changing `Theme` while the overlay is idle behind
   the dialog is either a useful live preview or a distraction.
