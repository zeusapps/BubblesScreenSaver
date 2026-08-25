## 1. The checkbox

- [x] 1.1 `SettingsWindow` -- a `Check` variant that reads and writes `Startup` rather than
      `Settings`, so it does not go through `Edit` and contributes nothing to the save on close.
- [x] 1.2 Register it with `_refreshers`, so it re-reads the operating system every time the
      window refreshes, like every other control re-reads `_host.Current`.
- [x] 1.3 Put it in a group where somebody would look for it, with a note saying it starts the
      application at sign-in and puts an entry in the Start Menu.
- [x] 1.4 Capture the state at construction, beside `_opened`.
- [x] 1.5 `OnCancel` -- put it back, and only when it actually differs, so cancelling a window
      that never touched it writes nothing.
- [x] 1.6 `OnRestoreDefaults` -- leave it alone, with a comment saying why, because the next
      person will read the omission as an oversight otherwise.

## 2. The menu

- [x] 2.1 `TrayIcon` -- remove the entry, its field, and its line in `Refresh`.
- [x] 2.2 Check nothing else referenced it, and that the separators still fall where they should
      with one fewer entry between them.
- [x] 2.3 Leave `Pause` alone. It has the same invisible tick and it is staying: a command, not
      configuration, and nobody has asked for its label to change.

## 3. Tests

- [x] 3.1 Whatever of this can be exercised without a window -- behind the seam the startup
      registration already has -- covering: turning it on registers, turning it off unregisters.
- [x] 3.2 Cancel after a change puts the registration back, in both directions.
- [x] 3.3 Cancel with no change writes nothing at all -- asserted on the writes, not on the end
      state, since the end state is identical either way.
- [x] 3.4 Restoring defaults leaves the registration untouched.
- [x] 3.5 The tray menu no longer builds a startup entry, and every entry it does build is still
      reachable.

## 4. Verify

- [x] 4.1 `dotnet build` and `dotnet test` clean.
- [x] 4.2 Open the window and confirm the checkbox shows the true state -- turn startup off from
      Task Manager first, so it is showing something that was changed behind its back.
- [x] 4.3 Tick it, close, and confirm both the `Run` value and the Start Menu entry are there.
- [x] 4.4 Untick it, cancel, and confirm both came back.
- [x] 4.5 Restore defaults and confirm the registration is untouched.
- [x] 4.6 Open the tray menu and confirm the entry is gone and nothing else moved.
