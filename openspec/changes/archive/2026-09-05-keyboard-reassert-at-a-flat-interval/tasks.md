## 1. The interval becomes one constant

- [x] 1.1 Replace `Floor`, `Ceiling` and `Growth` in `KeyboardLighting` with a single `Reassert`
      constant of two seconds, documenting it as the worst case a repaint can sit on the keys and
      naming the production histogram that chose it.
- [x] 1.2 Delete the `_cadence` field and the `Relax` method.
- [x] 1.3 Collapse the constructor's `floor` and `ceiling` test seams to one `cadence` parameter
      defaulting to `Reassert`, and use it directly as the bounded wait in `Work`.
- [x] 1.4 Remove the ramp reset from the `Chore.Dark` arm, leaving the rest of that arm untouched.
- [x] 1.5 Remove the ramp reset from the disturbance path, keeping the immediate `Reassert()` call
      and its log line.
- [x] 1.6 Drop the `next in Ns` suffix from the re-assert log line, keeping how far into the
      blackout the re-assert falls.

## 2. What the change means is written down where it is relied on

- [x] 2.1 Rewrite the class comment on the re-assert so it describes a flat interval and says why
      the twenty seconds borrowed from `DisplayBlackout._whileDark` did not apply -- that one reads
      before it writes and this one cannot.
- [x] 2.2 Rewrite the `MachineEvents` doc comment, which currently justifies the class by a choice
      between writing constantly and writing at the right moments; the system now writes constantly
      and a disturbance is only a reason not to wait out the interval in hand.

## 3. Tests follow the requirement

- [x] 3.1 Delete `TheReassertRelaxesButNeverPastTheCeiling`, whose subject no longer exists.
- [x] 3.2 Collapse the `Layer` helper's `floor:`/`ceiling:` arguments to the single `cadence:`
      parameter, leaving every existing call site's behaviour unchanged.
- [x] 3.3 Add a test for the flat interval: several successive waits during one blackout are the
      same length, covering the `The interval does not drift` scenario.
- [x] 3.4 Add a test that a disturbance sends black at once and leaves the following interval
      unchanged, covering the amended `A disturbance arrives` scenario.
- [x] 3.5 Run the full suite and confirm the keyboard tests pass.

## 4. Confirmed against the real thing

- [ ] 4.1 Run with `BUBBLES_LOG` set through a blackout and confirm from the log that re-asserts
      arrive every two seconds for its whole length, with no ramp and no twenty-second waits.
