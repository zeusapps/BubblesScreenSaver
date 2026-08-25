## 1. Correct the sentence, everywhere it is said

Zero-risk text, ordered first so it stands alone if the rest is dropped.

- [x] 1.1 `SettingsWindow.xaml.cs` -- rewrite the note under "Carry an Emission onto the keyboard
      backlight": Dynamic Lighting must be **off**, and while it is on Windows repaints the keys
      with its own effect so the writes are accepted and discarded.
- [x] 1.2 `Settings.KeyboardLighting` doc -- same inversion, same reasoning.
- [x] 1.3 `AuraKeyboard` class doc -- rewrite the "**It only works while Windows owns the
      lighting**" paragraph, which has the ownership exactly backwards.
- [x] 1.4 `AuraKeyboard.Open`'s log line -- "If nothing lights up, Dynamic Lighting is **on** and
      Windows is repainting over these writes -- switch it off in Settings › Personalization ›
      Dynamic Lighting."
- [x] 1.5 Grep for any other mention of Dynamic Lighting and check it agrees.

## 2. The borrower

- [x] 2.1 Add a record type beside `KeyboardRecord` holding the previous `AmbientLightingEnabled`
      value, with settable properties so `PendingRestore` can round-trip it through JSON.
- [x] 2.2 Add a class that reads, writes and restores
      `HKCU\Software\Microsoft\Lighting\AmbientLightingEnabled`, behind an interface so it can be
      exercised without touching the registry.
- [x] 2.3 Give it its own `PendingRestore` file beside `keyboard-state.json`, recording the found
      value before writing the new one.
- [x] 2.4 Restore the recorded value, never a fixed one; a machine that was already off stays off.
- [x] 2.5 Do nothing at all -- no read, no record -- while either setting is off.

## 3. Wiring

- [x] 3.1 Add the setting to `Settings.cs`, defaulting off, documented as subordinate to
      `KeyboardLighting` and as changing a Windows setting.
- [x] 3.2 Add the checkbox and note to the settings dialog, below the keyboard weather one.
- [x] 3.3 Take the loan where the keyboard device is opened; give it back where the device is
      released.
- [x] 3.4 Settle any record found at startup, on the same path as the keyboard's, and before
      anything is sent to the device.
- [x] 3.5 Confirm the registry work happens on the keyboard worker thread and never on the
      dispatcher -- nothing on screen waits for it.

## 4. Tests

- [x] 4.1 With both settings on, the previous value is recorded before it is changed.
- [x] 4.2 Releasing restores the recorded value and clears the record.
- [x] 4.3 A machine already at "off" records "off" and is left off after release.
- [x] 4.4 With the new setting off, nothing is read, written or recorded.
- [x] 4.5 With keyboard lighting off, nothing is read, written or recorded.
- [x] 4.6 A record left on disk by a previous run is settled at startup.
- [x] 4.7 A settings file written before this feature reads the setting as off.

## 5. Verify

- [x] 5.1 `dotnet build` and `dotnet test` clean.
- [x] 5.2 Read all four corrected texts end to end and check they say the same thing.
- [x] 5.3 With the setting on and Dynamic Lighting on, run `--emission-demo` and confirm by eye
      that the keys follow the Emission -- the case that fails today.
- [x] 5.4 Confirm Dynamic Lighting is back on afterwards, and that the record file is gone.
- [x] 5.5 Kill the process mid-Emission and confirm the next start restores Dynamic Lighting.
- [x] 5.6 Measure how long the change takes to take effect, and record it in the design if it
      eats into the buildup's six and a half seconds.
