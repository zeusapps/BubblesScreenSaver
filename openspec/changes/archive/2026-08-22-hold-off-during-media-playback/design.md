## Context

`UserBusy.Reason(settings)` returns `string?` — a reason, or null. `IdleController.Tick()` reads
it and, if it is not null, forces `Active` and returns. Every signal is therefore total: it
suppresses the whole overlay or nothing.

That was correct while every signal meant "somebody is here." A media session does not: it says
what is playing, and watching a film and listening to an album call for opposite treatment.
Making one signal partial requires the shape of the hold-off to change, which is why this is a
design document rather than one more clause in `Evaluate`.

Constraints inherited from the codebase:

- **An unbounded hold-off is the worst outcome.** `QUNS_BUSY` is documented in the source as the
  mistake not to repeat: a signal that reads true nearly always and stops the screensaver ever
  running. Every new signal must fail towards running, not towards holding.
- **Nothing may block the dispatcher.** `Tick` runs every 400 ms on a `DispatcherTimer`, and the
  overlay renders on the same thread.
- **The signals lie, so they must be inspectable.** `--audio`, `--inputs`, `--dim-test` and
  `--hold-test` all exist because a Windows API reported something untrue on real hardware.

## Goals / Non-Goals

**Goals:**

- Catch silent video — the case that defeats every current signal.
- Stop music holding an OLED lit indefinitely.
- Keep the change to the hold-off boundary: the overlay and rendering code are not touched,
  beyond selecting a plain fade over an Emission.
- Keep every existing signal behaving exactly as it does today.

**Non-Goals:**

- Per-monitor stages. A fullscreen video on one screen still suppresses the overlay on all of
  them. That is a real gap and a much larger change; it is not this one.
- Replacing the audio meter. It covers players that register no session.
- Enumerating `ES_DISPLAY_REQUIRED` holders. It would catch the same players and more, but it
  needs an undocumented structure and per-process filtering to skip PowerToys Awake. Kept in
  reserve if media sessions prove to have gaps.
- Detecting *what* is on screen. Nothing here inspects pixels or window contents.

## Decisions

### A suppression mask, not an ordered ceiling

The natural first model is a ceiling — the furthest stage a reason permits. It does not fit.
Music must permit `Blackout` while suppressing `Artifacts`, and `Blackout` is the *further*
stage, so no single ordered bound expresses it.

So a reason declares independently which stages it suppresses:

```
  readonly record struct HoldOff(bool Artifacts, bool Blackout, string? Reason)

      locked / mic / camera / fullscreen / sound     (true,  true )
      media: PlaybackType.Video                      (true,  true )
      media: PlaybackType.Music                      (true,  false)
      nothing                                        (false, false)
```

Composition is a union over what is suppressed. Resolution in `Tick` becomes:

```
  wantsBlackout && !hold.Blackout   ->  Blackout
  wantsArtifacts && !hold.Artifacts ->  Artifacts
  otherwise                         ->  Active
```

Note this is *not* `min(desired, ceiling)`: with music playing, the machine passes through
`Active` at the artifacts threshold and then jumps to `Blackout`, never having shown the
artifacts. That is the intended behaviour and falls out of the model rather than being a
special case.

**Alternative considered:** keep `string?` and add a second call, `BlackoutAllowed()`. Rejected —
two calls that must agree, evaluated separately, is how the reason and the stage drift apart.

### The idle clock discounts only a total hold-off

`IdleClock.Elapsed(idle, heldOff, now)` discounts time so that hanging up restarts the countdown
rather than arriving with every threshold already passed. Passing `heldOff: true` for a partial
hold-off would freeze the countdown short of `BlackoutSeconds`, and the blackout the reason
deliberately permitted would never arrive.

So the clock is told `hold.Artifacts && hold.Blackout` — held off only when everything is.

This is the subtlest part of the change. `IdleClockTests` already covers the discount arithmetic;
it gains the partial case.

**Alternative considered:** one clock per stage. Rejected as more state than the problem needs —
a single clock with the correct predicate is sufficient.

### The session manager is acquired once and read synchronously

`GlobalSystemMediaTransportControlsSessionManager.RequestAsync()` is the only asynchronous part
of the API. Once held, `GetSessions()` and each session's `GetPlaybackInfo()` are synchronous
property reads, which is what `Evaluate` needs on the dispatcher thread.

So: request the manager once, off the dispatcher, and cache it. Until it arrives, report no
media reason — consistent with the requirement that an unreadable state is not a reason. On a
read failure, drop the cached manager and re-request, in the same shape as `AudioActivity`
reacquiring its meter.

The existing two-second cache in `UserBusy.Reason` stays and covers this too: media state
changes on a human timescale, and `Tick` runs every 400 ms.

**Note on a bug this avoids.** `AudioActivity` caches its meter and only drops it when
`GetPeakValue` returns a *failing* HRESULT. A meter on a superseded endpoint can keep returning
`S_OK` with a peak of zero, reporting silence for ever. The media manager must not repeat that:
it is refreshed on a failure *and* the session list is re-read each time rather than cached.
(The `AudioActivity` staleness is a real latent bug, but it is not this change — see Open
Questions.)

### Windows SDK projection over a hand-rolled COM interop

`AudioActivity` declares its COM interfaces by hand. That was reasonable for three methods on
two interfaces. The media session API is much wider, and the projection is the supported route.

This means the target framework moves from `net10.0-windows` to `net10.0-windows10.0.17763.0`,
in both projects.

**Why that number.** 17763 is Windows 10 1809, the release that introduced
`GlobalSystemMediaTransportControlsSessionManager` — the lowest SDK version that has the API at
all. The conventional default of 19041 (Windows 10 2004) also works and was tried first, but it
declares a floor two releases higher than anything here needs. The TFM's SDK version becomes
`TargetPlatformMinVersion`, becomes `SupportedOSPlatformVersion`, and is stamped into the
assembly as `SupportedOSPlatform`, so the number is not cosmetic: it is a claim about who can
run this. It was `Windows7.0` before, and it should rise exactly as far as the API forces and no
further.

Consequences to verify rather than assume:

- the single-file, framework-dependent `Release` publish still produces one `Bubbles.exe`;
- CI builds on its current image;
- `SupportedOSPlatformVersion` lands on 10.0.17763.0 and no higher — it is stamped into the
  assembly, and it was `Windows7.0` before this change.

**Alternative considered:** hand-rolled interop against `IMediaSessionManager` to avoid the TFM
change. Rejected — the surface is large, it is not a documented COM contract, and the projection
exists precisely for this.

### A blackout with the artifacts suppressed uses the plain fade

`OverlayWindow.SetBlackout(true)` chooses between `BeginEmission()` and `BeginPlainFade()` on
`IsZone && _settings.Emission`. An Emission is twelve and a half seconds of artifacts, lightning
and a burning sky — the exact thing a music session asked to withhold.

`SetBlackout` gains a parameter for whether the artifacts are welcome; the controller passes
what the hold-off said. This is the only change reaching into the overlay, and it adds no state
to it.

### Reporting the reason

Where several reasons hold, the reported reason should be the one that suppresses the stage
actually being withheld, so the log explains what the user is seeing. With music playing and the
microphone open, "microphone in use" is the useful line, not "music is playing."

## Risks / Trade-offs

**A player misreports `Playing` and never clears it** → unbounded hold-off, the `QUNS_BUSY`
shape. Mitigated by gating on `PlaybackStatus == Playing` rather than session existence, by
`--media` making the state inspectable on the machine where it happens, and by
`PauseWhileMediaPlaying: false` as an escape. Considered and rejected: a maximum hold-off
duration — an arbitrary timer that would cut off a genuinely long film.

**A player classifies a film as `Music`** → the screen blacks out during a film. The failure is
recoverable in one keypress, unlike the reverse, which is why the mask is drawn this way round.

**The TFM bump breaks the single-file publish or CI** → caught at build time, before anything
ships. This is the only part of the change with a blast radius beyond hold-off, and it is
verified first in the task order.

**Browsers register a session per tab** → several sessions, possibly stale ones. Handled by the
union: any *playing* video session suppresses, and non-playing sessions contribute nothing.

**Music now reaches blackout, which is a behaviour change** → deliberate, documented in the
README, and reversible with one setting.

## Migration Plan

No data migration; `Settings` gains one boolean with a default and its existing round-trip test.
`PauseWhileMediaPlaying: false` restores today's behaviour exactly, so the rollback is a setting
rather than a release.

The TFM bump is verified before any behaviour is written, so the riskiest step fails first and
cheaply.

## Open Questions

1. **Does the Windows 11 Media Player app register a `PlaybackType` of `Video` for local files?**
   The whole change rests on it. `--media` answers it on the reporting machine, and the task
   order puts that first — the diagnostic is built before the logic that depends on it.

2. **Should `PlaybackType.Image` be treated as anything?** Currently no. A photo viewer slideshow
   is arguably watched, but it is also the shape of a stale session.

3. **Should the audio meter be narrowed once media sessions are in?** With sessions covering the
   registered players, the meter's remaining job is the unregistered ones, where its
   false-positive cost — an album on a player with no session, holding the screen lit — is
   unchanged. Not resolved here.

4. **`AudioActivity` caches a meter that can go stale silently** (`_meter` is only dropped on a
   failing HRESULT). Found while investigating this change; it is a separate defect and belongs
   in its own change rather than being smuggled in with this one.
