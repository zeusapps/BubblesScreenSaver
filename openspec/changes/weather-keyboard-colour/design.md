## Context

Everything this needs is already computed every frame and thrown away.

**The weather is a pure cycle.** `WeatherCycle` holds `Current`, `Outgoing` and a `Progress`,
and `IntensityOf(state)` folds all three into one number per state, counting both sides of a
cross-fade. `WeatherLayer` and the ambient lightning already share that one call rather than
each deciding for themselves whether a storm is overhead -- the comment on it says why. A
keyboard is a third caller of the same kind, and must be the same kind of caller.

**The sky's colour is not per-state, it is per-anomaly.** `WeatherLayer` tints fog and rain
with `AnomalyTint.Of(family)`, where the family is `FamilyCensus.Dominant` -- whatever is
actually drifting up there, re-taken only when the field changes rather than per frame. So
"match the screen" means reading those two inputs, not sampling anything.

**The Emission already outranks the weather.** `_cycle.Suspended = _emitting` at
`OverlayWindow.cs:885`. The keyboard rule is not a new precedence; it is the existing one
extended to one more consumer.

**The strike is already in hand.** `TickAmbientLightning` writes `_strikeOnScreen` from the
`HasStrike` it just made at line 990, exactly as the Emission does at 1139, because the comment
at line 72 asks for one asker.

**The keyboard layer is transport-shaped, not Emission-shaped.** `KeyboardLighting` owns the
once-per-session open, the send rationing, the record on disk and the hand-back. None of it
knows what an Emission is; it takes colours. Adding a second source of colours is the shape it
was already built for.

## Goals / Non-Goals

**Goals:**

- The keys carry the weather that is on screen, in the colour it is on screen.
- The Emission stays an event: ambient light must never be mistaken for it.
- Cross-fades are inherited rather than reimplemented.
- Off by default, and subordinate to the switch that already exists.
- No new cost when there is no weather, no keyboard, or no setting.

**Non-Goals:**

- Per-key weather. The board shows one colour; rain falling across the keys is a different
  feature on different hardware.
- Reacting to individual artifacts, the detector, or collection events. The sky only.
- Weather on the keyboard while the screen is black. The blackout owns the keys then, and it
  owns them by being dark.
- Sampling the rendered frame. Same objection as before: it is the approximation this whole
  line of work replaced.

## Decisions

### The keyboard reads the weather's own state, not its picture

`WeatherLight` is a pure function of `(Weather state, double intensity, Color tint)` to a
`KeyColor`, sitting beside `EmissionLight` and testable the same way -- give it a state and a
number, assert a colour.

Because `IntensityOf` already reports a cross-fade as two intensities that sum to one, the
keyboard fades between states by summing the two contributions. It cannot fall out of step with
the sky during a transition, because it is doing the same arithmetic on the same numbers.

*Alternative considered:* a colour per weather state, fixed. Rejected -- the screen's fog is not
one colour, it is the dominant anomaly's tint, and a keyboard that stayed grey while the sky
went green would look like a bug rather than a choice.

### Ambient is capped at a fraction of the Emission

The Emission's deepest red is `#C43018`. Ambient weather is scaled to a small fraction of that,
so that the brightest possible storm is still obviously quieter than the dimmest part of an
Emission's buildup.

This is the decision the whole change turns on. The reason `keyboard-lighting` ruled ambient
light out was that it would dilute the Emission, and that objection is only answered by a
number. It is a named constant with a test asserting the relationship -- not a value somebody
can nudge upward without noticing what it costs.

*Open, for implementation:* the exact fraction. It has to be found by looking at the keyboard in
a dark room, which is a thing this project can now actually do.

### `Clear` means unlit, and the device stays held

Two questions from the proposal, answered together because they are the same question.

`Clear` shows black. The Zone with an empty sky is a dark room, and dark keys are what that
looks like.

The device is held for the whole artifacts stage rather than released between states. Releasing
would hand the keyboard back to the vendor's software, which would light it with the user's own
profile -- so "release on `Clear`" produces *more* light during the calmest weather, which is
backwards. It would also mean surrendering and re-acquiring the device roughly once a minute,
since `Clear` comes up about a third of the time, and every one of those would be a visible pop
back to whatever Armoury Crate is set to.

*The cost, stated plainly:* while the screensaver is up with this enabled, the keyboard is ours
and the vendor's lighting does not run. That is a longer loan than the Emission's twelve
seconds and the setting says so.

### Ambient lightning flashes, at ambient scale

A storm without strikes is just blue. The flash reuses the Emission's own decaying,
edge-triggered strike -- the mechanism that had to be built anyway when a held bolt pinned the
keys white -- but scaled to ambient brightness, so an ambient strike is a flicker rather than
the white slap an Emission's strike is.

### A slower floor for a slower sky

The send policy rations by visible change and a minimum interval. The Emission's floor is 0.12s
because its colour moves over twelve seconds; ambient weather moves over a minute and a
cross-fade takes six, so the same visible-step rule with a much longer floor produces a handful
of writes per minute.

The strike exemption stays. Everything else about the policy is reused rather than duplicated:
one class, two floors.

### The Emission takes over, and gives back

While `_emitting`, ambient colour is not computed and not sent. This mirrors
`_cycle.Suspended = _emitting`, so there is one rule about precedence rather than two that
could disagree.

Coming out of a blackout, the artifacts return and so does the weather, through the same event
that drives it normally. Nothing special-cases the transition.

## Risks / Trade-offs

- **It dilutes the Emission** → The only real risk, and the reason this was ruled out before.
  Answered by the cap, and by a test that pins ambient below the Emission's floor rather than
  leaving it to judgement.
- **The keyboard is ours for hours, not seconds** → Stated in the setting. Off by default, and
  it needs the master switch on as well, so nobody meets it without two deliberate acts.
- **A lit keyboard at three in the morning** → The blackout still takes it dark, and the
  blackout is what a machine left alone reaches. This lights the keys during the artifacts
  stage, which is the stage somebody chose to have on screen.
- **Holding a HID handle for hours** → Nothing known says this costs anything; the handle is
  idle between writes. Worth watching for the device disappearing under us on sleep or
  reconnect, which the existing "a refused write means the device has gone" path already
  handles by standing down.
- **More writes than the Emission over a night** → Fewer, in fact: a handful a minute against a
  hundred in twelve seconds. The arithmetic is in the tests.

## Migration Plan

None. One new setting, defaulting off, subordinate to a setting that also defaults off.
`settings.json` keeps its shape and version. A user who never enables it cannot tell this
change happened.

Ordered so the untestable part is last: the colour function with tests, then the policy floor,
then the event, then the wiring, then an hour in a dark room deciding whether the number is
right.

## Open Questions

- Whether `Storm` should differ from `Rain` by more than brightness -- a colder cast, say --
  given both are drawn with the same anomaly tint and a keyboard cannot show falling rain.
- Whether the fog's tint should be dimmed further than the rain's. On screen fog is a haze in
  *front* of the artifacts and rain is behind them, which the keys have no way to express.

## What the hardware settled

The ceiling, the colour source and the lightning were all decided by looking at the keyboard,
which is what the plan said would happen. All three moved.

**The ceiling went from 0.22 to 0.50 of the Emission's deepest red.** At 0.22 the verdict was
that the backlight "reads as turned off most of the time". Keys are diffused through plastic and
a fifth of a dark red is nothing on them. The bar in the spec moved with it, from a third to a
half -- the original third was stricter than the goal required, since the Emission's wavefront
is roughly three times its own deep red again and that is what makes it an event.

**The colour comes from the sheets, not the artifacts.** The first version used
`AnomalyTint.Of(family)` -- the colour the rain is *derived* from. The rain is actually drawn
from a pale cold drop colour pulled 85% toward the family, and the fog from a grey-green haze
the same way, so the keys were a near miss: the artifacts' hue while the screen showed pale
rain. Those two are now read from `WeatherTint`, split out of `WeatherBrushes` for the purpose.

That split was forced by something worth recording. `WeatherBrushes` caches WPF brushes and
transforms, and a WPF object belongs to the thread that built it -- so a test asking it for a
colour claimed the whole cache for a non-STA thread and thirty-three unrelated weather tests
began throwing. The comment in `Sta.cs` had predicted exactly this. The tint maths has no thread
affinity and now lives where that cannot happen.

**Rain shimmers; fog does not.** On screen rain is three sheets scrolling past one another at
different speeds, which reads as movement. One zone of backlight cannot scroll, so it wobbles
instead. This is the change that made the feature legible: before it, the answer was "I don't
see how the keyboard colours are connected to what happens on the screen". It cost the send
floor, which went from 1.5s to 0.2s -- the visible-step rule now does the saving, so still fog
is still about one write a minute while rain earns its extra traffic.

**Lightning is not dimmed.** It was scaled to half at first, on the theory that a distant strike
should be a flicker rather than a slap. That was wrong for a reason worth keeping: lightning is
the one part of ambient weather tied to something unmistakable on screen, so halving it threw
away the clearest signal the feature has. A bolt is a bolt.

**The storm's invented cold cast was removed.** On screen a storm *is* rain -- the same
precipitation with bolts behind it -- so tinting the keys colder was the keyboard disagreeing
with the screen about what was happening. Rain and storm now share a colour and are told apart
by the flashes.

**A finding, recorded rather than fixed:** once an anomaly family is dominant it pulls both
sheets 85% toward its own colour, so fog and rain arrive at nearly the same hue. On the keys
they are separated by level, not colour. There is a test saying so, so it is not rediscovered
as a bug.

**The Emission still lands as an event** at the higher ceiling, confirmed on the hardware after
the weather was tuned. That was the whole thing the cap existed to protect.
