## Why

Two faults, both met while starting the application, and both worse than they look because the
only way to observe either one is to start it.

**The countdown is inherited, not begun.** `IdleClock` reports
`NativeInput.IdleSeconds()` -- the *system* idle timer, which counts from the last real
keypress and knows nothing about this process. A run that starts while the machine has already
been sitting therefore begins with every threshold already passed. Caught in the log on
2026-08-25, with `IdleSeconds: 30` and `BlackoutSeconds: 150`:

```
13:04:38        the application starts
13:04:39.227    tick idle=149,0s cur=Active   next=Bubbles   idleCfg=30 blackCfg=150 holdA=False
13:04:39.227    ENTER Active -> Bubbles
13:04:40.454    tick idle=150,3s cur=Bubbles  next=Blackout
13:04:40.454    ENTER Bubbles -> Blackout
```

The artifacts stage, configured to run for two minutes, ran for 1.2 seconds. `holdA=False`
throughout: nothing was held off, nothing was suppressed, and this is not the media path that
already has a requirement of its own. The screen simply went black, and on this machine
`LockAfterBlackout` then locked the session.

This is the ordinary experience of restarting the application. It fires when a release is
installed, when the app is restarted to test something, and at login on a machine that sat at
the lock screen -- every one of which is a moment when somebody is *watching* to see whether it
works, and what they see is the screensaver skipping its own screensaver.

`IdleClock`'s own summary already states the principle it does not enforce:

> How long you have actually been away, which is not the same as how long it has been since the
> last keypress.

It subtracts time spent held off. It does not bound the answer by how long there has been an
application to be away from.

**The application cannot be found.** `Startup` writes one value to the per-user `Run` key and
nothing else. There is no Start Menu entry, so Windows search returns nothing for "Bubbles",
and somebody who exits from the tray has no way to start it again short of knowing the path
under `%LOCALAPPDATA%\Programs`. Reported directly: *"I can't find the app in the search
input."*

**A note on what was investigated and not changed.** The failure that prompted this change was
reported as the screensaver taking over during a YouTube clip in a picture-in-picture window.
That was chased through the media signal and the chase came back empty, which is worth writing
down so nobody repeats it.

Measured on 2026-08-25, with the clip actually playing:

```
--media : no media session is registered
--busy  : holding off: sound is playing
```

Edge registers no media session at all while a clip plays -- in a tab or in picture-in-picture.
A session appears while playback is *paused*, reporting `Music`, and vanishes when it starts. So
a rule reading a browser's `Music` as video, which was written and reverted here, could never
fire; what made it look justified was that paused session, which is not evidence about how
playback reports.

The case is already covered: the audio meter returns `HoldOff.Everything` and stops the
countdown, so nothing reaches black over a clip with sound. What that leaves uncovered is muted
or silent browser video -- the exact gap `MediaSessions` was introduced to close, and which it
does not close for this browser. Detecting the picture-in-picture window instead was considered
and rejected: the window is present whether or not anything is playing, so it would hold the
overlay off for a paused window left open overnight, which is the QUNS_BUSY mistake this
codebase warns against in three separate places. Recorded rather than fixed.

**The application has no icon.** `Bubbles.csproj` sets no `<ApplicationIcon>` and there is no
`.ico` in the tree, so the executable carries no icon resource. The Start Menu entry above,
Alt-Tab, the taskbar and search all show the generic default. The artwork exists -- the tray
icon is rendered from `BubbleArt` at run time -- it has simply never been given to the binary.

All three are the same subject from one end or another: being able to start the application,
recognising it when you find it, and it behaving once started.

## What Changes

- **Bound the reported idle time by the life of the process.** The clock SHALL never return
  more than the time since it began, so a run that starts into an already-idle machine begins
  its countdown at zero and walks the stages in order. Nothing about the thresholds, the
  hold-off arithmetic or the stage machine changes; only the ceiling is new.
- **Leave the hold-off subtraction exactly as it is.** The existing rule -- that time under a
  total hold-off is discounted, and time under a partial one is not -- is correct and stays.
  The ceiling composes with it rather than replacing it.
- **Give the application a Start Menu entry**, written where the `Run` key is written and
  removed where it is removed, so it is reachable from search and from the Start Menu by the
  name people know it by.
- **Separate `UserBusy`'s media mapping from reading the machine**, so what a playing session
  means for the stages can be tested rather than only inspected. No behaviour changes with it.
- **Give the executable an icon**, rendered from the same `BubbleArt` source the tray icon
  comes from, so what is found in search is recognisable as this application.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `idle-hold-off`: gains the rule that the idle countdown is measured from the start of the run
  as well as from the last input, so a fresh process cannot begin past its own thresholds.
- `tray-menu`: the requirement covering startup registration is extended -- registering to
  start with Windows also means being findable, so the Start Menu entry is written and removed
  on the same paths as the `Run` value.

## Impact

- `src/Bubbles/Session/IdleClock.cs` -- a ceiling on what `Elapsed` returns.
- `src/Bubbles/Session/Startup.cs` -- the shortcut written and deleted alongside the `Run` value.
- `src/Bubbles/Interop/MediaSessions.cs` -- what was measured about Edge, recorded where the
  next person to reach for this signal will read it.
- `src/Bubbles/Session/UserBusy.cs` -- the media mapping lifted out where it can be tested.
- `src/Bubbles/Zone/BubbleArt.cs` -- the tray artwork rendered to a multi-size `.ico` as well.
- `src/Bubbles/Program.cs`, `src/Bubbles/Bubbles.csproj` -- a switch to write the icon, and the
  committed icon given to the binary.
- `tests/Bubbles.Tests/` -- the new ceiling, the startup registration, the media
  classification, and the existing hold-off behaviour proved unchanged by any of it.
- `openspec/specs/idle-hold-off/spec.md`, `openspec/specs/tray-menu/spec.md` -- two requirements
  added, one extended. Nothing about the media signal changes.
- No change to `IdleController`, the stage machine, the overlay, the Emission, or any setting.

## Open Questions

Recorded here, to be settled in `design.md`:

1. **Whether the ceiling belongs in `IdleClock` or in `IdleController`.** The clock is the
   thing that answers "how long have you been away", which argues for it; the controller is
   what owns the process lifetime.
2. **What the Start Menu entry is tied to.** Writing it with the `Run` value couples being
   findable to starting at login, which are not the same wish -- somebody may want one and not
   the other.
3. **Whether an existing installation gets the entry without asking.** Startup is already on
   for these users, so the entry would appear on their next run without them requesting it.
