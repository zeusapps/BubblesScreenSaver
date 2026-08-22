## 1. Name the resting state

Pure logic, no window, and the thing every later task is checked against.

- [x] 1.1 Add `Overlay/LayerRest.cs`: a readonly record of the six layer opacities and a static `For(Stage, Settings)`
- [x] 1.2 Fill in the table from the spec — root, scrim, sky, flash, artifacts, detector — for `Active`, `Artifacts` and `Blackout`
- [x] 1.3 Move the stage enum somewhere both `IdleController` and the overlay can name, or pass the stage in a form the overlay already has; do not duplicate it
- [x] 1.4 Add `tests/Bubbles.Tests/OverlayLayersTests.cs` asserting the per-stage values against default settings and against non-default `Dim`/`Opacity`
- [x] 1.5 Add the sequence property: for every ordering of transitions ending at `Artifacts`, the resting state equals the `Artifacts` state

## 2. Make assignment beat a held animation

- [x] 2.1 Add one helper that clears a layer's opacity animation and then assigns a value, replacing the hand-rolled pair in `SetDetectorVisible`
- [x] 2.2 Use it for the scrim, the sky and the flash wherever they are assigned directly
- [x] 2.3 Have `ShowBubbles` settle every layer to `LayerRest.For(Artifacts, settings)` before starting the fade in
- [x] 2.4 Have `Apply` assign the whole resting state rather than only the scrim and the artifacts, closing the asymmetry noted in the design
- [x] 2.5 Keep the existing comment explaining *why* the clear is needed — it is the only record of how that bug was found

## 3. Restore before telling anyone

- [x] 3.1 In `EndBlackout`, move the layer restoration ahead of `LeftDark?.Invoke()`
- [x] 3.2 Raise `LeftDark` and `WentDark` through a guard that catches, logs via `Diagnostics.Log`, and continues
- [x] 3.3 Confirm the App.cs handler still does displays before the lock request — that ordering is internal to the handler and must not change
- [ ] 3.4 **Not done, and not worth doing here:** exercising a throwing `LeftDark` subscriber needs a real `OverlayWindow`, which needs an STA thread and an `Application`. The test project states that nothing it runs puts anything on screen. The guarantee is structural — the restore precedes the raise, and the raise is wrapped — and is covered by review rather than by a test

## 4. Re-assert the glass

- [x] 4.1 Have `Native.MakeGlass` keep returning its result; log through `Diagnostics.Log` at the call sites when it is false
- [x] 4.2 Apply it in `StretchOverVirtualDesktop`, so both `ShowBubbles` and `OnDisplaySettingsChanged` re-assert it
- [x] 4.3 Confirm it is still applied at window creation, before the first stretch
- [x] 4.4 Confirm no glass call happens on the render path

## 5. Let the machine be asked

- [x] 5.1 Add `Bubbles.exe --glass-test` in `Program.cs`, in the family of `--dim-test` and `--hold-test`
- [x] 5.2 Call `SetProcessDpiAwarenessContext(-4)` before any capture — the README records what getting this wrong cost
- [x] 5.3 Paint a known colour, show the overlay at the artifacts stage over it, capture, sample the centre of each monitor
- [x] 5.4 Report per monitor whether the colour came through, and print the sampled value so an opaque overlay is distinguishable from a black desktop
- [x] 5.5 Exit cleanly leaving nothing on screen, and keep hands off a running instance's state the way `--dim-test` does

## 6. Verify

- [x] 6.1 `dotnet build` and `dotnet test` clean
- [x] 6.2 Run `--glass-test` on this machine and record what it reports
- [x] 6.3 Verified through a full cycle: Emission, black, lock, unlock, and the artifacts return over the live desktop. Log shows `scrim=1,00` at black and `scrim=0,55` on the next `ShowBubbles`
- [x] 6.4 Did not recur, so `snap.png` was not needed. Which mechanism caused the original fault is therefore still unattributed — with both fixed, neither error path fires. Recorded rather than guessed at

## 7. Write it down

- [x] 7.1 README: say that the frame extension is re-asserted, and why — the same reason topmost and brightness are
- [x] 7.2 README: document `--glass-test` alongside `--dim-test` and `--hold-test`
- [x] 7.3 Record the ordering rule in the source: the overlay restores its own state before raising anything
