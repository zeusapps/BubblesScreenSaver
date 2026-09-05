## 1. The worker holds the blackout

- [x] 1.1 Add the two cadences to `KeyboardLighting` as named constants: `Settling` (2s),
      `SettlesAfter` (30s) and `Holding` (20s), each documented with what it is for -- the first
      two cover the display work that provokes the repaint, the third matches
      `DisplayBlackout._whileDark` against the same class of problem.
      **Superseded by 6.1**: the first night in production showed the repaint arriving hours in,
      not in the first half-minute, so the two fixed cadences became a ramp measured from the last
      disturbance.
- [x] 1.2 Make the interval injectable through the internal constructor, defaulting to those
      constants, so the tests can drive the loop in milliseconds rather than waiting half a minute.
- [x] 1.3 Add worker-thread-only state for the hold: whether the screen is dark, and when it went
      dark, so the cadence can be chosen without a lock.
- [x] 1.4 Change the worker's `_wake.WaitOne()` to a timed wait when the screen is dark, and act on
      the timeout: `if (Ensure() && !_device!.GoDark()) Lost("holding the blackout")`.
- [x] 1.5 Set the dark flag where `Chore.Dark` is handled, and clear it on `Chore.Restore`,
      `Chore.Recover`, on any colour going out, and wherever `Lost` is called -- nothing to send to
      is nothing to hold.
- [x] 1.6 Leave `SendPolicy` untouched, and comment at the re-assert why it does not go through it:
      `Worth` suppresses a colour that has not moved, which is every re-assert there is.
- [x] 1.7 Confirm nothing new runs with `KeyboardLighting` off -- `WentDark` already returns early,
      so the worker is never started and never waits.

## 2. The overlay says when it leaves the screen

- [x] 2.1 Add `public event Action? LeftScreen` to `OverlayWindow`, documented as the second of the
      two ways the screensaver leaves -- the one that never went black.
- [x] 2.2 Fold the three lines both hide paths end in (`Visibility.Hidden`, `Suspended = true`,
      `Collapse()`) into one private `Hidden(bool wasShown)`, and call it from the immediate hide
      and from the fade's `Completed`.
- [x] 2.3 Raise `LeftScreen` from `Hidden` only when something was actually on screen, so
      `HideBubbles(immediate: true)` at startup raises nothing.
- [x] 2.4 Raise it through the existing `Raise` helper, so a subscriber that throws cannot leave the
      overlay hidden and suspended over a live desktop.

## 3. The hand-back gains its second caller

- [x] 3.1 Subscribe `_keyboard.LeftDark` to `_overlay.LeftScreen` in `App`, on its own, with a
      comment that the workstation lock is deliberately not on this path.
- [x] 3.2 Verify by reading that nothing else in `App` subscribes to `LeftScreen`, and that
      `reachedBlack` and `SessionLock.Request()` remain reachable only from `LeftDark`.
- [x] 3.3 Leave `KeyboardLighting.LeftDark` itself unchanged -- it already resets both policies and
      the weather clock and queues `Chore.Restore`, which settles Dynamic Lighting first.

## 4. Tests

- [x] 4.1 The keys are re-asserted while the screen is black: after `WentDark`, with a short
      injected interval, black reaches the fake keyboard more than once.
- [x] 4.2 Nothing is re-asserted after `LeftDark`: the count of sends stops climbing once the
      blackout ends.
- [x] 4.3 A re-assert that is refused ends the session's lighting once: one `Lost`, no loop, and
      `Abandoned` afterwards.
- [x] 4.4 A device that reports `IsOpen == false` mid-blackout is opened again and gets its black.
- [x] 4.5 An artifacts stage that ends without a blackout hands the keyboard back: after a weather
      frame and then the hand-back, the fake is restored and the Dynamic Lighting fake is back to
      what it was. Note: the test reaches `KeyboardLighting.LeftDark` directly, which is what
      `LeftScreen` is wired to. No test constructs an `OverlayWindow` -- nothing in this suite does,
      by design -- so the one `+=` in `App` and the `wasShown` guard are verified by reading (3.2)
      and by the end-to-end run below.
- [x] 4.6 A blackout that ends normally still hands back exactly once, with the second hand-back
      finding nothing owed.
- [x] 4.7 Nothing happens with the setting off: a blackout with `KeyboardLighting` false opens no
      device and sends nothing, however long it is left.

## 5. Confirm

- [x] 5.1 `dotnet test` green.
- [x] 5.2 `openspec validate --changes keyboard-loan-follows-the-screen` green.
- [x] 5.3 Read back the diff for anything that reaches the dispatcher, blocks a frame, or runs on a
      machine with the feature off.
- [x] 5.4 End-to-end on the real keyboard: `--emission-demo` with `BUBBLES_LOG` on re-asserted black
      at 2s, 4s, 6s and 8s into the blackout and stopped at the hand-back. Run without the external
      monitor attached ("no external display has HDR on"), so it proves the re-assert runs and stops
      correctly; whether it wins the keys back from Armoury Crate still needs a blackout with the
      monitor connected, which is the open question in the design.

## 6. The ramp, after the first night in production (v1.18.1)

- [x] 6.1 Replace the two fixed cadences with a ramp: `Floor` (2s), `Ceiling` (20s), `Growth`
      (1.5), relaxing after every re-assert and returning to the floor on a disturbance.
- [x] 6.2 Measure the ramp from the last disturbance rather than from the start of the blackout --
      an overnight log showed the repaint arriving hours in, which is where a blackout-clocked ramp
      is at its ceiling and least attentive.
- [x] 6.3 Add `MachineEvents`, reporting the transitions that can actually be observed: session
      switch, power mode, display settings. Raised on the SystemEvents thread with subscriber
      failures contained, since that thread is shared with the rest of the process.
- [x] 6.4 Add `KeyboardLighting.Disturbed(what)`: resets the ramp and says black at once, starts no
      worker on a machine that has never borrowed a keyboard, and is not a chore -- it does not
      replace what was asked for.
- [x] 6.5 Fix the latent defect this exposed: a bare wake fell through to the colour arm and
      cleared `_holdingDark`, so any wake that carried no chore would have cancelled the hold.
- [x] 6.6 Log the cadence beside the elapsed time, so the next night's log says how relaxed the
      ramp was when a green was seen.
- [x] 6.7 Tests: the ramp relaxes and stops at the ceiling; a disturbance sends black without
      waiting; a disturbance does not end the blackout or send a colour; a disturbance on a machine
      with no loan opens nothing and starts no thread.
