## 1. The device answers for itself

- [x] 1.1 Add `bool IsOpen { get; }` to `IKeyboardDevice` in `KeyboardRecord.cs`, documented as
      "whether the device is in hand right now", with a note that it changes without anyone
      being told -- a hand-back, a refused write and an error all release it.
- [x] 1.2 Implement `IsOpen` on `AuraKeyboard` as `_handle is not null && _device is not null`,
      adding no new state.
- [x] 1.3 Note in the `AuraKeyboard.Restore` doc that the caller must ask again afterwards,
      since restoring is what closes it.

## 2. `KeyboardLighting` stops caching what it cannot know

- [x] 2.1 Rename `_decided` to `_searched` and update its comment to say it means the search has
      been made, and nothing about holding a device.
- [x] 2.2 Rewrite `Ensure()` to the three-state form: return true when `_device is { IsOpen:
      true }`; return false when `_searched && _device is null`; otherwise open, reusing
      `_device` if there is one and calling `_open()` only when there is not.
- [x] 2.3 Set `_searched` after the search rather than before it, and null `_device` on a failed
      open so `Abandoned` still reads correctly.
- [x] 2.4 Confirm the re-open path runs `_owed.Remember([record])` so a second loan is written
      to disk before its first colour.
- [x] 2.5 Update the `Abandoned` doc to cover both ways it becomes true: a search that found
      nothing, and a device lost mid-session.

## 3. The worker honours what the device tells it

- [x] 3.1 Add a private `Lost(string what)` that logs once and nulls `_device` under the lock,
      leaving `_searched` true.
- [x] 3.2 Call it from the `Chore.Dark` arm when `GoDark()` returns false.
- [x] 3.3 Call it from the default arm when `Show(wanted)` returns false.
- [x] 3.4 Check `GiveBack` still behaves when `_device` is non-null but closed: it must restore
      through the existing object rather than opening a new one, and must not treat it as
      borrowed.

## 4. The fake keyboard stops being kinder than the hardware

- [x] 4.1 Give `FakeKeyboard` an `IsOpen` backed by a field that `Open()` sets and `Restore()`
      and `Dispose()` clear.
- [x] 4.2 Make `Open()` callable more than once, counting opens and searches separately so tests
      can assert on either.
- [x] 4.3 Add a switch that makes `Show`/`GoDark` return false, to exercise the lost-device path.

## 5. Tests for what the fake was hiding

- [x] 5.1 Replace `TheKeyboardIsOpenedOnceAcrossManyEmissions` with a test that runs three full
      `EmissionBegan` -> `Frame` -> `WentDark` -> `LeftDark` cycles and asserts the keys were
      taken dark on every one of them.
- [x] 5.2 Assert that a second Emission's colours reach the device after a hand-back.
- [x] 5.3 Assert the debt is on disk again before the second loan's first colour, and cleared
      again after its hand-back.
- [x] 5.4 Assert a search that finds nothing is still made only once across several Emissions --
      counting searches, not opens.
- [x] 5.5 Assert that a refused write stops further sends for the session and does not reopen
      the device.
- [x] 5.6 Check `TheWeatherComesBackAfterABlackout` still holds, and extend it to a second
      blackout if it was resting on the same stale state.

## 6. Verify

- [x] 6.1 `dotnet build` and `dotnet test` clean.
- [x] 6.2 With `BUBBLES_LOG` set, let one instance black out twice and confirm the log shows two
      `using ...` lines and two `restored 1 (awake)` lines, where before it showed one of each.
- [x] 6.3 Confirm by eye that the keyboard goes dark with the screen on the second blackout.
