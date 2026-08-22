## Why

The overlay has started arriving opaque. The artifacts fade in over solid black instead of over
the live desktop — which is the one thing this application exists to do differently from the
screensaver Windows ships, and the reason it was written at all.

`OverlayWindow.cs` has not changed. Its last content edit was `5cb745e`, eleven commits back,
and that edit was three lines of `using` directives and a namespace. Every line of the
transparency setup, the layer stack and the fade logic is identical to when it worked. So the
regression is not in the rendering code; it is in what the rendering code depends on and does
not re-check.

Two such dependencies are unguarded, and both are real defects regardless of which one produced
this particular report.

**The restore is not unconditional.** `EndBlackout()` raises `LeftDark` — which restores monitor
backlights over DDC/CI, changes HDR mode, and may request a workstation lock — *before* it
restores its own five layer opacities. Any throw from that foreign work skips all five. By then
`_blackout` is already `false`, so `SetBlackout(false)` early-returns for the rest of the
process's life, and `FillBehavior.HoldEnd` pins the scrim at full black. The overlay is then
permanently opaque with no error on screen to explain it.

**The glass is set once and never re-asserted.** `Native.MakeGlass` is called in
`OnSourceInitialized`, its return value discarded, and never called again. Every other volatile
Win32 state in this application is re-asserted on a timer or an event — topmost every three
seconds, monitor brightness every twenty, HDR and brightness records at the next launch, window
bounds on `DisplaySettingsChanged`. The DWM frame extension is the last set-once call in the
file, and blackout now performs two display mode changes on every cycle.

## What Changes

- **`EndBlackout` restores its own state first**, then raises `LeftDark`. Nothing the overlay
  owns depends on foreign work succeeding. `LeftDark` is additionally raised inside a guard, so a
  failing display restore is logged rather than propagated.

- **Held animations are cleared before the layers are shown.** The codebase already documents
  this hazard and already fixed it for one layer: *"once a property has been animated with
  `FillBehavior.HoldEnd`, the held value outranks anything assigned directly"*. The same guard is
  applied to the scrim, the sky and the flash, so an interrupted blackout can never leave a
  layer stuck.

- **The glass is re-asserted**, on show and after a display change, and its result is logged when
  it fails. This costs one DWM call at the moments the window is already being repositioned.

- **`Bubbles.exe --glass-test`** puts the overlay up at the artifacts stage over a known colour,
  captures the screen, and reports whether the desktop actually came through. This is the only
  way to observe the failure: nothing in-process can tell you the compositor honoured the frame
  extension.

- **The invariant is pinned by tests.** The layer state at rest is extracted as a pure function
  of stage and settings, so "at the artifacts stage the scrim is `Dim` and the sky is zero" is
  asserted rather than hoped for, for every reachable sequence of transitions.

## Capabilities

### New Capabilities
- `overlay-transparency`: that the overlay composites through to the live desktop, what each
  layer must be at each stage, and that neither an interrupted blackout nor a failure in display
  restoration can leave it opaque.

### Modified Capabilities

None — `openspec/specs/` is empty.

## Impact

**Code.**
- `Overlay/OverlayWindow.cs` — restore ordering, clearing held animations, re-asserting the glass
- `Interop/Native.cs` — `MakeGlass` reports failure
- `App.cs` — the `LeftDark` handler no longer needs to be the first thing to run
- `Program.cs` — `--glass-test`
- `README.md` — the transparency section gains why it is re-asserted

**Tests.** A new `OverlayLayersTests` over the extracted rest-state function. No test puts
anything on screen, consistent with the existing test project.

**Not in scope.** The two mechanisms are fixed, but which one produced the report is still
unconfirmed — reproducing it requires a full blackout, and `LockAfterBlackout` is on, so it locks
the machine. `--glass-test` and the existing `BUBBLES_SNAP=1` settle it afterwards. Both fixes
stand on their own: one is an unguarded restore path, the other is the only set-once Win32 call
in a file whose every neighbour is re-asserted.
