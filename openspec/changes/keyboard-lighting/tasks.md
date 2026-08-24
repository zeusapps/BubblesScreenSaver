## 1. The colour timeline

- [x] 1.1 Move `BuildupEnds`, `WaveEnds` and `DarknessAt` out of `OverlayWindow`'s private
      constants into a shared, internal place both the overlay and the keyboard read, so the
      two cannot be edited apart. The overlay's existing keyframes must keep their current
      values exactly.
- [x] 1.2 Add `EmissionLight` in `src/Bubbles/Keyboard/`: a static, pure function from elapsed
      Emission seconds to an RGB triple, written against those constants -- black at zero,
      rising to deep red through the buildup, a flare toward white at `BuildupEnds + 0.3`,
      falling to black at `DarknessAt`.
- [x] 1.3 Give it a separate flash colour for a lightning strike, so a strike is a distinct
      value and not a nudge to the ramp.
- [x] 1.4 Add `EmissionLightTests`: black at zero and at `DarknessAt`, red rising monotonically
      through the buildup, the flare brighter than any buildup sample with green and blue at
      their maximum, and the same input giving the same output twice.
- [x] 1.5 Assert in the same tests that the timeline reads the same constants the overlay
      animates with, so a change to one fails the other.

## 2. The Aura packet encoding

- [x] 2.1 Add `AuraProtocol` in `src/Bubbles/Keyboard/`: the effect packet (`5D B3`), SET
      (`5D B5`) and APPLY (`5D B4`), and the padding to a declared report length.
- [x] 2.2 Match the lighting collection on vendor, usage page, usage and report length -- never
      on position. One keyboard presents ten collections and several accept writes.
- [x] 2.3 Refuse to pad a packet into a report shorter than itself, rather than truncate: a
      short write is discarded by firmware without an error.
- [x] 2.4 Add `AuraProtocolTests`: the byte layout, the ordering of the three packets, the
      padding, and the choice of collection against the real device's siblings.

## 3. The HID access

- [x] 3.1 Add `Hid` in `src/Bubbles/Keyboard/`: enumerate HID collections through SetupAPI,
      reading vendor, product, usage page, usage and output report length from each.
- [x] 3.2 Open collections read-only to describe them, and skip any that refuse -- the keyboard
      and mouse collections are owned by Windows and refusing us is correct.
- [x] 3.3 Add `AuraKeyboard` implementing `IKeyboardDevice` over that: find, open, show a
      colour, go dark, release.
- [x] 3.4 Make releasing the device the way the keyboard is handed back, since the protocol
      cannot be asked what it was showing before.
- [x] 3.5 Swallow every failure into a bool and a log line, so nothing above writes a try/catch.

## 4. The lighting layer

- [x] 4.1 Add `KeyboardLighting`: owns the client, the once-per-session connection decision, and
      the send policy. All socket work runs off the dispatcher; nothing it exposes is awaited by
      a caller.
- [x] 4.2 Look for a keyboard on the first Emission after the setting is enabled, once. On
      failure, log the reason once through `Diagnostics` and stay off for the session -- no
      retry, no dialog, no second attempt mid-Emission.
- [x] 4.3 Record the device's prior mode through `PendingRestore<T>` -- written to disk before
      the first colour is sent, keyed on something stable about the device, settled only once a
      restore is verified.
- [x] 4.4 Implement send-on-change: compute the colour every frame, send when it has moved by a
      visible amount or the floor interval has passed, and send the flare and every strike
      unconditionally.
- [x] 4.5 Implement going dark on `WentDark` and restoring on `LeftDark`, on exit, and at
      startup where a pending restore is found on disk.
- [x] 4.6 Add `KeyboardLightingTests` over a fake client: one connect attempt per session across
      several Emissions, a failed connect leaving the layer silent afterwards, imperceptible
      ramp steps coalescing, the flare and strikes never coalescing, the prior mode recorded
      before the first send, and the restore happening on each of the three paths.

## 5. Wiring into the overlay

- [x] 5.1 Add an event to `OverlayWindow` for the Emission beginning, raised from
      `BeginEmission` through the same `Raise` helper `WentDark` and `LeftDark` use.
- [x] 5.2 Report the Emission's elapsed time and the strike already in hand at
      `OverlayWindow.cs:1139` to the keyboard, without a second `HasStrike` call. Leave the
      ambient path at line 990 alone -- the keyboard is not lit outside the Emission.
- [x] 5.3 Wire the layer in `App.cs` beside `DisplayBlackout`: the Emission event, `WentDark`,
      `LeftDark`, and disposal on exit.
- [x] 5.4 Confirm the artifacts stage sends nothing, and add a test that the layer is untouched
      while the overlay is showing artifacts with no Emission running.

## 6. The setting

- [x] 6.1 Add one key to `Settings.cs`, defaulting off, with the settings-window text saying
      that OpenRGB and vendor lighting software claim the keyboard exclusively and that
      enabling this stops the vendor software managing the keys.
- [x] 6.2 Confirm the setting appears in the settings window by that window's existing rule, and
      that `settings.json` keeps its shape and version for anybody who never touches it.
- [x] 6.3 Add a settings-compatibility test that an older `settings.json` without the key loads
      with the feature off.
- [x] 6.4 Assert, in a test, that with the setting off no connection is attempted when an
      Emission begins.

## 7. Closing out

- [x] 7.1 Run the full test suite and the build with warnings as errors.
- [x] 7.2 Verify by hand on the machine with the keyboard: enabled the setting, ran
      `--emission-demo`, and watched the keys follow the sky through the buildup, flash with the
      lightning, go dark with the screen, and come back on waking (`restored 1 (awake)` in the
      log). Required Dynamic Lighting to be switched on; while it is off every write is accepted
      and silently discarded.
- [x] 7.3 Note in the change what was verified on hardware and what was only verified by test,
      so the boundary the design drew is still visible after this ships.

## 8. What the hardware sent us back to

Added after the feature was run on the real keyboard.

- [x] 8.1 Make a lightning strike edge-triggered and decaying rather than a level. Holding the
      strike colour for as long as `HasStrike` was true left the keys solid white through the
      dense part of a storm, while the screen showed thin bright lines over red.
- [x] 8.2 Mix the flash over the ramp instead of replacing it, and vary the colour per bolt, so
      a burst reads as separate strikes rather than one block of light.
- [x] 8.3 Cover both in `SendPolicyTests`, including a bolt held on screen for a second and a
      half that must end up red again.
- [x] 8.4 Re-run the Emission on hardware and confirm the lightning reads as distinct flashes.
