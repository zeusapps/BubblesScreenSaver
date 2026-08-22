## Why

The hold-off signals exist so the overlay never arrives while somebody is still there.
Watching silent video defeats every one of them at once. Drone footage in a window produces
no keyboard input, no microphone or camera use, no window that fills a screen, and no sound —
so the artifacts fade in partway through the clip.

Audio was introduced to cover exactly this case. The commit is titled *"Hold off while a video
is playing, windowed or not"*, and its reasoning is that sound coming out means somebody is
listening to it. That is true in the direction it is stated. The implementation depends on the
converse — that no sound means nobody is watching — and that one is false. Silent footage is
the counterexample, and so are muted playback, a clip with no audio track, and anything routed
to an endpoint other than the metered one.

The same signal fails in the opposite direction too. Music holds the screensaver off for as
long as it plays, so an album keeps an OLED lit for three hours over a static desktop. That is
the burn-in this application exists to prevent.

Both failures come from measuring the loudspeaker rather than asking what is playing.

## What Changes

- **Ask Windows what is playing.** `GlobalSystemMediaTransportControlsSessionManager` exposes
  the records behind the media flyout: every session reports whether it is playing, and whether
  it is `Video`, `Music` or `Image`. None of that depends on an audio track existing, on the
  volume, or on which endpoint the sound goes to.

- **Hold-off becomes per-stage.** Today any reason collapses the whole machine to `Active`. A
  reason will instead name which stages it suppresses. The two stages answer different questions
  — the artifacts are something drawn *at* you, the blackout is the absence of anything drawn at
  all — and a media session is the case that separates them:

  | Reason | Artifacts | Blackout |
  |---|---|---|
  | session locked, microphone, camera, fullscreen | held | held |
  | a **video** session is playing | held | held |
  | a **music** session is playing | held | **allowed** |
  | sound on the meter (existing signal) | held | held |

  Music suppresses the artifacts, which are the intrusive part, without keeping the panel lit
  indefinitely. The screen still reaches black on schedule.

- **The audio meter stays.** Not every player registers a media session — `mpv` and some games
  do not — so this is an additional signal rather than a replacement. Where several reasons
  hold, every stage any of them suppresses is suppressed.

- **New setting** `PauseWhileMediaPlaying` (default on), alongside the existing pause settings
  and its own tray entry.

- **`Bubbles.exe --media`** lists what Windows believes is playing — app, playback status and
  playback type — in the family of `--audio`, `--inputs`, `--dim-test` and `--hold-test`.

## Capabilities

### New Capabilities
- `idle-hold-off`: when the overlay must not advance, which stage each reason permits, and how
  the reasons compose. Covers the existing signals as well as the new one, because per-stage
  ceilings change the behaviour of all of them.

### Modified Capabilities

None — `openspec/specs/` is currently empty, so the existing hold-off behaviour is captured as
part of the new capability rather than as a delta.

## Impact

**Target framework.** `Windows.Media.Control` is a WinRT namespace and needs
`net10.0-windows10.0.17763.0` in place of `net10.0-windows`. This touches `Bubbles.csproj`,
`Bubbles.Tests.csproj`, and must be checked against the single-file `Release` publish and CI.
This is the only part of the change with a blast radius beyond the hold-off code.

**Code.**
- `Session/UserBusy.cs` — returns which stages are suppressed, with the reason, instead of a bare `string?`
- `Session/IdleController.cs` — resolves the stage against what is suppressed rather than forcing `Active`
- `Session/IdleClock.cs` — discounts idle time only under a total hold-off, not a partial one
- `Interop/MediaSessions.cs` — new; reads the SMTC session list
- `Settings.cs`, `Session/TrayIcon.cs`, `Program.cs` — the setting, its menu entry, `--media`
- `README.md` — the hold-off table, and why the audio meter is kept

**Behaviour.** One user-visible change beyond the fix: with music playing and no other reason,
the screen now reaches blackout where previously it stayed lit. That is the intent, and
`PauseWhileMediaPlaying: false` restores the old behaviour.

**Risk.** A stale or wrongly-reported session would hold the overlay off indefinitely, which is
the failure mode this codebase treats most seriously — it is the `QUNS_BUSY` mistake. Sessions
are therefore gated on `PlaybackStatus == Playing`, never on a session merely existing, and an
unreadable media state is treated as no reason at all.

**Discovered while specifying.** Two consequences of the per-stage model that were not obvious
from the outset, both now pinned by the spec: the idle clock must *not* discount time under a
partial hold-off, or the blackout it permits would never arrive; and a blackout reached while
the artifacts are suppressed must use the plain fade, since an Emission is twelve seconds of the
very artifacts that were withheld.
