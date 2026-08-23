## 1. One canonical settings instance

Ordered first because everything else edits settings, and because doing it first fixes the
existing aliasing bug independently of the window.

- [x] 1.1 Add `SettingsHost` in `src/Bubbles/Session/`: holds the one `Settings` instance,
      exposes `Current` and `Edit(Action<Settings>)`, which mutates, calls `Clamped()`, then
      fans out to the registered listeners
- [x] 1.2 Give it `Snapshot()` and `Restore(Settings)` using a serialise/deserialise round-trip
      through `Settings.JsonOptions`, so the clone captures exactly what persists
- [x] 1.3 Construct `SettingsHost` in `App.OnStartup` and register the overlay, the idle
      controller and the updater as listeners, replacing the fan-out in `TrayIcon.Tweak`
- [x] 1.4 Point `App`'s `LeftDark` handler at `SettingsHost.Current` instead of its own captured
      `_settings`, so `LockAfterBlackout` is never read from a stale instance
- [x] 1.5 Route `TrayIcon.Tweak` through `SettingsHost.Edit` and delete `TrayIcon`'s own
      `_settings` field; verify the app still builds and every existing menu entry still works

## 2. Bounds shared between `Clamped()` and the UI

- [x] 2.1 Lift the literal bounds in `Settings.Clamped()` into named constants on `Settings`
      (one pair per clamped setting), and rewrite `Clamped()` to read them
- [x] 2.2 Add a test in `tests/Bubbles.Tests/SettingsTests.cs` asserting that a value at each
      bound survives `Clamped()` unchanged, and that a value outside it is brought to the bound
- [x] 2.3 Confirm the existing `SettingsTests` still pass, including the density migration path

## 3. Hold-off while the window is open

- [x] 3.1 Add a settable `HoldOff AppHold` to `IdleController`, defaulting to `HoldOff.None`
- [x] 3.2 In `IdleController.Tick`, merge it with `UserBusy.Held(_settings)` via the existing
      `HoldOff.And`, leaving the forced-start path that discards hold-off untouched
- [x] 3.3 Add a test alongside `HoldOffTests`/`HoldOffMaskTests` covering: the app hold-off
      alone suppresses both stages; it composes with a `UserBusy` reason; and an armed force
      still overrides it

## 4. The settings window

- [x] 4.1 Add `SettingsWindow.xaml` and `SettingsWindow.xaml.cs` in `src/Bubbles/Session/`,
      the repo's first XAML; no view-model, no third-party packages
- [x] 4.2 Lay out the settings in headed groups: when it starts, what it looks like, the theme
      and its Zone-only settings, holding off, the screen, and updates
- [x] 4.3 Bind every persisted setting to a control, with ranges taken from the constants added
      in 2.1 -- including the nine that have never had a UI (`AutoUpdate`, `UpdateCheckHours`,
      `MaxFps`, `ClickThrough`, `Wobble`, `SpeedVariance`, `CollectRadius`, `FadeInSeconds`,
      `MonitorStandby`)
- [x] 4.4 Show each numeric setting's current value next to its control
- [x] 4.5 Label the blackout delay as time measured from when the screensaver starts, matching
      what `Clamped()` enforces
- [x] 4.6 Group `MonitorStandby` apart from the everyday controls and label it with what it does
      on a Modern Standby machine
- [x] 4.7 Route every control's change through `SettingsHost.Edit` so edits apply immediately
- [x] 4.8 Disable the Zone-only settings when the selected theme is not the Zone, and re-enable
      them without a reopen when the theme is switched back
- [x] 4.9 Add `Cancel` (restore the snapshot taken on open), `Restore defaults` (apply
      `new Settings()`), and a keep-and-close action; place `Restore defaults` apart from
      `Cancel` so the two are not confused
- [x] 4.10 Save settings once on close, by any route, and not on individual edits
- [x] 4.11 Set `AppHold` to `HoldOff.Everything("the settings window is open")` on open and
      `HoldOff.None` on close
- [x] 4.12 Make the window single-instance: a second request activates the existing window
- [x] 4.13 Use no hard-coded pixel sizes, and check the layout on a scaled display, since
      `app.manifest` declares per-monitor DPI awareness

## 5. The tray menu

Last, so the app is usable at every earlier step and a rollback before this point leaves the
old menu intact.

- [x] 5.1 Add the `Settings...` entry, opening the window through the single-instance path
- [x] 5.2 Add `Check for updates` and `Start with Windows` to the menu -- the two entries that
      have always been constructed and never added
- [x] 5.3 Delete the configuration submenus: `Start after`, `Black screen after`,
      `Dim the desktop`, `Ask for a PIN`, `Hold off while`, `Theme` and `Look`
- [x] 5.4 Delete `Edit settings...` and `Reload settings`, along with `ReloadSettings`
- [x] 5.5 Remove the machinery the submenus needed: `_checks`, `_zoneOnly`, `Toggle`, `Choice`,
      `ZoneOnly`, and the `_pin` label rewriting; keep `RefreshUpdateItem`, `CheckForUpdates`
      and `Notify`, which are now reachable
- [x] 5.6 Rename `Start bubbles now` to `Start now`, and check no remaining menu string names a
      theme's visual
- [x] 5.7 Keep the blackout command's theme-conditional label (`Emission now` under the Zone
      with Emissions enabled) and the startup entry's tick read from `Startup.IsEnabled` on
      each opening
- [x] 5.8 Confirm the menu is seven top-level entries with no submenus

## 6. Verify

- [x] 6.1 `dotnet build` clean, and `dotnet test` green
- [~] 6.2 Run the app: open the window, change the theme, the dim level and a delay, confirm
      each takes effect; cancel a session of edits and confirm every value returns
      -- PART DONE: the window was run against a real settings.json and every value rendered
      correctly (see the blackout defect this caught). The live-apply and cancel/restore paths
      are covered by tests, but nobody has yet clicked through them in the running window.
- [x] 6.3 Leave the window open past both delays and confirm neither the screensaver nor the
      blackout arrives; close it and confirm the idle timer resumes
- [x] 6.4 With the window open, use `Start now` from the tray and confirm the screensaver still
      starts
- [x] 6.5 Confirm an existing `settings.json` from before the change is honoured unmodified in
      shape and `SettingsVersion`
- [x] 6.6 Update the README and any docs naming menu paths, `Start bubbles now`, or
      `Edit settings...`
