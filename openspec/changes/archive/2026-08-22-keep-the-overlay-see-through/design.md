## Context

The symptom is that the artifacts arrive over solid black instead of over the live desktop.
`OverlayWindow.cs` is unchanged since `5cb745e` — three `using` lines and a namespace — so the
rendering code is not the regression. The eleven commits since are all display, session and
audio work, and they changed what happens *around* a blackout: the brightness hold, HDR
switching, the session lock, the PIN request.

There are exactly two ways this window can end up opaque, and they are indistinguishable on
screen:

```
  (A)  the compositor stops honouring the DWM frame extension
       →  WPF draws correctly; the window paints opaque anyway

  (B)  a layer is left holding an opacity from another stage
       →  the compositor is fine; WPF is drawing black
```

Both correspond to a real defect in the current code, and both are fixed here. Which one
produced this report is still unconfirmed, because reproducing it needs a completed blackout and
`LockAfterBlackout` is on, so it locks the machine.

## Goals / Non-Goals

**Goals:**

- The overlay cannot be left opaque by an interrupted blackout or by a failure in display
  restoration.
- The resting layer state becomes an assertable invariant rather than an emergent property of
  five animations.
- Transparency becomes observable on the machine where it is failing.

**Non-Goals:**

- Changing the transparency mechanism. `DwmExtendFrameIntoClientArea` with `-1` margins stays;
  `AllowsTransparency` would force software rendering across a multi-monitor desktop.
- Changing the visual design, timings, or the Emission.
- Rewriting the animation system. The fades stay as they are; only their preconditions and
  ordering change.
- Per-monitor stages.

## Decisions

### Restore first, then tell anyone

Today:

```
  EndBlackout()
      LeftDark?.Invoke()        ← DDC writes, HDR mode change, SessionLock.Request()
      ... five Animate() calls  ← skipped entirely if the above throws
```

`_blackout` is already `false` when this runs, so a throw leaves the scrim held at 1 *and*
`SetBlackout(false)` early-returning for the rest of the process's life. The overlay is opaque
permanently, with nothing on screen to say why.

Inverted: the overlay puts its own layers back, then raises `LeftDark` inside a guard that logs.

**Why not just wrap the existing call in try/catch and leave the order?** Because ordering is the
actual invariant. `_displays.Leave()` is synchronous and slow by design — the README says to
expect a second of black and a re-sync at each end — and it runs on the dispatcher thread. Even
when it succeeds it delays the visual restore by a display mode change. Restoring first is
correct on both counts.

**The App.cs comment says "Displays first, always."** That ordering is about `_displays.Leave()`
preceding `SessionLock.Request()` — the backlight must be back before the sign-in screen appears,
since the lock screen is the one thing this app cannot draw over. That ordering is preserved
exactly; it is internal to the handler. What changes is only that the overlay's own restore no
longer sits behind it.

### One resting state, computed, not accumulated

The layer values live in five `Animate` calls in `EndBlackout`, four keyframe timelines in
`BeginEmission`, three in `BeginPlainFade`, and two assignments in `Apply`. Nothing states what
the layers *should* be at rest, so nothing can check it — and `Apply` already demonstrates the
drift, resetting the scrim and the artifacts but not the sky or the flash.

So: a pure function, in its own file, with no WPF window involved.

```
  static LayerRest For(Stage stage, Settings s) -> (root, scrim, sky, flash, artifacts, detector)
```

`OverlayWindow` uses it as the target of its animations and as the value it assigns when
clearing. `OverlayLayersTests` asserts it directly, including the property that matters: every
sequence of transitions ending at the artifacts stage rests at the artifacts state.

**Alternative considered:** assert on real `UIElement` opacities in the test project, which is
`net10.0-windows` with `UseWPF` and could do it. Rejected — the existing test project states
that nothing it runs puts anything on screen, and a pure function is a better thing to own
anyway.

### Clear the held animation wherever a layer is assigned

The hazard is already documented in the source, for the detector:

> *Once a property has been animated with `FillBehavior.HoldEnd`, the held value outranks
> anything assigned directly — so after a single blackout, assigning Opacity did nothing and the
> detector stayed on screen in a theme that has no detector, frozen.*

That fix was applied to one layer. The scrim, the sky and the flash are animated the same way and
are not guarded. One helper — clear the animation, then assign — used for every layer, and
`ShowBubbles` settles all of them to the resting state before fading in.

This makes the fix idempotent and self-healing: even if some future path leaves a layer held,
the next entry into the artifacts stage corrects it.

### Re-assert the glass where the window is already being moved

`MakeGlass` is called once, in `OnSourceInitialized`, and its result is dropped. The natural
places to repeat it are the two that already exist for window bounds: `StretchOverVirtualDesktop`
(called from `ShowBubbles` and from `OnDisplaySettingsChanged`) and window creation.

Cost is one `DwmExtendFrameIntoClientArea` at moments the window is already being repositioned —
negligible, and nothing during the render loop.

`MakeGlass` already returns a bool. The change is to look at it and log when it is false.

**Alternative considered:** handle `WM_DWMCOMPOSITIONCHANGED` via an `HwndSourceHook`. That is
the documented trigger for reapplying the extension, and it is a good idea — but it needs a
window procedure hook where the existing re-assert points need three lines. Worth adding if
`--glass-test` shows the two points are not enough.

### `--glass-test` has to look at the screen

This is the part no unit test reaches: the layer opacities can be perfectly correct while the
window paints opaque, because the failure is in the compositor, not in WPF.

So the diagnostic paints a known colour, shows the overlay at the artifacts stage over it,
captures, and samples. A sampled colour close to the known one means the glass is working; black
means it is not; and reporting the sampled value distinguishes "opaque overlay" from "the
desktop happens to be black".

`SetProcessDpiAwarenessContext(-4)` must be called before capturing. The README records that
getting this wrong cost hours chasing a rendering bug that did not exist, and this diagnostic is
precisely the shape of thing that would repeat it.

## Risks / Trade-offs

**The report is caused by neither mechanism** → both fixes are still correct, and `--glass-test`
plus `BUBBLES_SNAP=1` now make a third cause diagnosable in one run instead of by inspection.
This is the main open risk and it is accepted deliberately: reproducing first would mean locking
the user's machine.

**Re-asserting glass on every show has a cost** → one DWM call, at a moment the window is already
being resized. Not on the render path.

**Swallowing subscriber exceptions could hide a display-restore failure** → it is logged, and
`_displays.Leave()` already has its own recovery path: records are written before changes and
replayed at the next launch. A silent opaque overlay is the worse of the two failures.

**Extracting the resting state touches code that currently works** → it is a pure function with
no behaviour of its own, introduced with tests, and the animation calls are re-pointed at it
rather than rewritten.

## Migration Plan

No settings, no persisted state, no user-visible behaviour change when everything is working.
The only observable differences are that a failure now logs instead of going silent, and that
`--glass-test` exists.

## Open Questions

1. **Is the cause (A) or (B)?** Unresolved by design. After this change, run `--glass-test`, and
   reproduce once with `BUBBLES_SNAP=1` to confirm the symptom is gone. If it recurs, `snap.png`
   separates the two in one look.

2. **Are `ShowBubbles` and `DisplaySettingsChanged` enough re-assert points?** If not, the
   `WM_DWMCOMPOSITIONCHANGED` hook above is the next step.

3. **Should `Apply` reset the sky and the flash too?** It currently resets only the scrim and the
   artifacts. Once the resting state is a function, `Apply` should assign all of it, and this
   asymmetry disappears rather than being fixed as such.
