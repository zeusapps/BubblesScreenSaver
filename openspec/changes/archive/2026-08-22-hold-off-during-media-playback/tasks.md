## 1. Prove the platform first

The riskiest and least reversible step, and the one the whole change rests on. It fails cheaply
here and expensively later.

- [x] 1.1 Move `Bubbles.csproj` and `Bubbles.Tests.csproj` to `net10.0-windows10.0.17763.0`
- [x] 1.2 Confirm `dotnet publish -c Release` still produces a single `dist\Bubbles.exe`, framework-dependent, and that it runs
- [x] 1.3 Confirm CI builds on its current image; adjust the workflow only if it does not
- [x] 1.4 Confirm `SupportedOSPlatformVersion` lands on 10.0.17763.0 — the lowest SDK that has the SMTC API — and not on the conventional 19041, which would declare a floor two Windows releases higher than anything here needs

## 2. Read the media sessions, and look at what they say

The diagnostic comes before the logic that depends on it, because Open Question 1 — whether the
Media Player app reports `PlaybackType.Video` for a local file — is answered by running it.

- [x] 2.1 Add `Interop/MediaSessions.cs`: request the session manager once off the dispatcher, cache it, expose a synchronous read of (application, `PlaybackStatus`, `PlaybackType`) per session
- [x] 2.2 Drop and re-request the cached manager on a read failure; re-read the session list every call rather than caching it
- [x] 2.3 Log read failures through `Diagnostics.Log` and report no media reason on failure
- [x] 2.4 Add `Bubbles.exe --media` in `Program.cs`, listing each session and the stages that would be suppressed, exiting without an overlay
- [x] 2.5 **Run `--media` while playing the drone footage in the Media Player app.** Confirmed: `Microsoft.ZuneMusic_8wekyb3d8bbwe` reports `PlaybackType.Video` for local footage, logged as `holding off: video is playing`

## 3. Turn the hold-off into a suppression mask

Pure logic, no Windows APIs, and the part most worth testing.

- [x] 3.1 Add `HoldOff(bool Artifacts, bool Blackout, string? Reason)` and give `UserBusy` a method returning it, replacing `Reason(settings)`
- [x] 3.2 Map the existing signals — locked, microphone, camera, fullscreen, fills-a-screen, sound — to suppressing both stages, with behaviour unchanged
- [x] 3.3 Compose several reasons by union, reporting the reason that suppresses the stage actually withheld
- [x] 3.4 Extend `HoldOffTests` to cover the mask and its composition, including a permissive and a strict reason together

## 4. Resolve the stage against the mask

- [x] 4.1 Replace the `if (heldOffBy is not null) { Enter(Active); return; }` branch in `IdleController.Tick` with the resolution from the design
- [x] 4.2 Pass `hold.Artifacts && hold.Blackout` to `IdleClock.Elapsed`, so a partial hold-off does not discount time
- [x] 4.3 Extend `IdleClockTests` with the partial case: the countdown must keep running and reach `BlackoutSeconds`
- [x] 4.4 Add a test that a reason suppressing only the artifacts goes `Active` → `Blackout`, never showing the artifacts
- [x] 4.5 Keep the existing `Diagnostics.Log` lines meaningful — they should now say which stages are suppressed and why

## 5. Wire the media signal in

- [x] 5.1 A playing `Video` session suppresses both stages
- [x] 5.2 A playing `Music` session suppresses the artifacts only
- [x] 5.3 Ignore any session not reporting `Playing`, and any with an absent or `Image` playback type
- [x] 5.4 Add `PauseWhileMediaPlaying` to `Settings` (default on) with its clamp/round-trip coverage in `SettingsTests`
- [x] 5.5 Add its tray entry alongside the other pause settings
- [x] 5.6 Test the classification table directly: video, music, paused, stopped, no type, no sessions, unreadable

## 6. Do not run an Emission nobody asked for

- [x] 6.1 Give `OverlayWindow.SetBlackout` a parameter for whether the artifacts are welcome; choose `BeginPlainFade` over `BeginEmission` when they are not
- [x] 6.2 Pass it from `IdleController` out of the hold-off mask
- [ ] 6.3 Confirm by hand: music playing, Zone theme, `Emission: true` — the screen fades plainly; with nothing playing, the Emission runs as before

## 7. Confirm the bug is actually gone

The reason the change exists. Each of these is the real failure, reproduced.

- [x] 7.1 Play silent drone footage in a window in the Media Player app, wait past `IdleSeconds`, confirm nothing is drawn — verified on the reporting machine
- [ ] 7.2 Mute a video with an audio track, wait past `IdleSeconds`, confirm nothing is drawn
- [ ] 7.3 Play an album, confirm no artifacts appear and the screen reaches black at `BlackoutSeconds`
- [ ] 7.4 Pause the video, confirm the overlay arrives normally
- [ ] 7.5 Set `PauseWhileMediaPlaying: false`, confirm today's behaviour returns
- [ ] 7.6 Confirm a call still holds everything off, with a video session playing at the same time

## 8. Write it down

- [x] 8.1 Update the README hold-off table with the new signal and the per-stage column
- [x] 8.2 Explain why the audio meter is kept rather than replaced, and record the silent-video counterexample that motivated this
- [x] 8.3 Document `--media` alongside `--audio` and `--inputs`
- [x] 8.4 Note the behaviour change: music now reaches blackout, and how to turn it off
- [ ] 8.5 Raise the stale `AudioActivity` meter (design, Open Question 4) as its own change rather than folding it in here
