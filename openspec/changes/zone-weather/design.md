## Context

The Zone theme currently draws, back to front: a dimming scrim, the Emission sky, the
lightning, the artifacts, the shockwave flash, and the detector. Only the middle three
ever change, and only during an Emission. Between Emissions the scene is static apart
from the artifacts drifting.

Two properties of the existing code shape this design.

**Nothing is stored, and alpha is pre-baked.** `LightningLayer` derives its whole
schedule from the time into the Emission and a hash, so a run is reproducible without
state. Its alpha levels are pre-baked into eight frozen pens and brushes with a comment
explaining why: varying intensity with `PushOpacity` forces WPF onto an intermediate
surface, which is expensive across a whole desktop. Weather has to respect both.

**Weather is on screen continuously, and lightning is not.** `LightningLayer` gets away
with per-frame `DrawLine` work because `HasStrike` early-outs on almost every frame -- a
strike is 0.42s inside a 12.5s Emission. Rain and fog have no such gaps. A per-frame
`OnRender` that draws hundreds of streaks across a 5120x1600 desktop, every frame, for
as long as the screensaver is up, is a different cost class from anything in the app
today.

**On the Emission's strike count.** The schedule starts at 0.9s and accumulates gaps of
`(1.5 - 1.05 * progress) * (0.6 + hash * 0.8)`, where `progress = min(1, at / 8)`. Run
forward, the 22nd strike lands at 13.86s while the screen reaches black at
`DarknessAt = 12.5`. Only 18 strikes are ever seen. Raising `Strikes` to 33 moves the
last strike to 18.82s and leaves the visible count at exactly 18 -- the request cannot be
satisfied by changing that constant.

## Goals / Non-Goals

**Goals:**

- Four weather states -- clear, fog, rain, rain with lightning -- that read as distinct
  at a glance.
- A weather cycle of about a minute, changing by cross-fade, that never looks
  metronomic and never repeats the same state twice running.
- Near-zero per-frame CPU cost, since weather runs continuously.
- 50% more lightning actually on screen during an Emission.
- Weather that behaves sensibly around an Emission and stops entirely at black.

**Non-Goals:**

- Weather in the Soap theme.
- Weather influencing artifact motion, the detector, or collection.
- Real forecast data, time-of-day, or seasons.
- Any change to the Emission timeline (`BuildupEnds`, `WaveEnds`, `DarknessAt`) or to
  its palette.

## Decisions

### Weather is composited, not drawn per frame

Fog and rain are built once as frozen brushes and animated with transforms, rather than
redrawn in `OnRender` each frame.

Rain is a `DrawingBrush` of streaks, tiled, scrolled by a `TranslateTransform` animation
running diagonally. Two or three such brushes at different tile scales and speeds give
parallax, and the parallax is also what hides the tiling repeat. Fog is a handful of
large, very low alpha radial gradient blobs on a canvas, each drifting horizontally on
its own slow animation.

Alternative considered: a procedural `OnRender` in the style of `LightningLayer`, with
drop positions derived from time and a hash. It fits the house style and stores nothing,
but it puts several hundred `DrawLine` calls on the UI thread every frame for the entire
time the screensaver is up. The transform approach hands the motion to the compositor
and costs essentially nothing per frame, which is the property that matters most here.

Consequence to accept: the rain tile is a repeating pattern, and on a very wide desktop a
careful observer can find the repeat. Parallax and low contrast make this acceptable.

### Intensity is a baked level, not an opacity

Each weather state renders at an intensity from 0 to 1, quantised to a small number of
pre-baked levels exactly as `LightningLayer` bakes its eight. A cross-fade drives the
outgoing state's level down and the incoming state's level up over the transition.

Alternative considered: cross-fade by animating `Opacity` on two layer elements. This is
the obvious approach and is what the rest of the overlay does for its stage transitions --
but those are brief, whereas a weather cross-fade happens every minute forever, and each
one would put two full-desktop layers onto intermediate surfaces simultaneously. The
baked-level approach keeps the cost flat.

### Both states are live during a transition, and only during a transition

The weather layer holds an outgoing state and an incoming state. Outside a transition the
outgoing slot is empty and exactly one state renders. This bounds the worst case to two
states on screen at once and makes "what is the layer doing right now" answerable from
two fields.

### The cycle is about a minute, jittered, and never repeats

The nominal dwell is 60 seconds with +/-25% jitter, so states last 45 to 75 seconds and
the changes do not land on a beat. The cross-fade is 6 seconds -- long enough that fog
lifting reads as weather rather than as a dissolve.

Selection is a weighted roll that excludes the current state, rather than the
`ShuffledDeck` used for artifact kinds. A deck was considered and rejected: with four
states it would guarantee each appears once per four changes, making storms exactly 25%
of all weather and the sequence predictable in cycles. Weather should be able to stay
fair for a while. Starting weights are clear 35, fog 25, rain 25, storm 15; these are
constants, and are the most likely thing to want tuning after seeing it run.

Excluding the current state means a re-roll always visibly changes something, which is
the point of having a cycle at all.

### Fog and rain sit in front of the artifacts; storm lightning sits behind

Fog that renders behind the artifacts does not fog anything -- the artifacts stay sharp
in front of it and the effect reads as a haze on the desktop instead. So the weather
layer goes above `_canvas` and below `_flash`, where fog softens the artifacts and rain
falls in front of them.

Storm lightning is the exception and reuses the existing `LightningLayer` instance in its
existing position behind the artifacts, on the same reasoning the current code already
records: lightning belongs to the sky, so it silhouettes the artifacts rather than
covering them.

### Storm lightning is an ambient mode on the existing layer, not a second layer

`LightningLayer` gains an ambient mode: a continuous schedule instead of one anchored to
an Emission's start, fewer strikes, more wash and less bolt, so a storm reads as distant
weather and an Emission still reads as the sky tearing open. An Emission takes the layer
over outright while it runs.

Alternative considered: a separate ambient lightning layer. Rejected because two layers
could then strike at once, which would look like two skies, and because the bolt geometry,
the pre-baked pens and the per-region logic would all have to be duplicated or shared
through a base class for no gain.

### More lightning comes from the gap curve, and the schedule ends at darkness

The gap curve is scaled to 70% of its current values: `1.5 - 1.05 * progress` becomes
`1.05 - 0.74 * progress`. Run forward, that puts 27 strikes before 12.5s against today's
18, which is the 50% asked for, and the last of them lands at 12.30s.

The schedule stops being a fixed-length array and is instead built until it passes the
Emission's darkness time. Today's fixed 22 is why four strikes are scheduled where nobody
can see them; a termination condition removes the whole class of problem and means the
count no longer has to be re-derived by hand if the timeline is ever retuned.

Alternatives considered: raising `Strikes` alone does not work at all, as shown above.
Shortening `StrikeLength` would fit more strikes into the same span but makes each one
snappier, which changes the character rather than the quantity.

Note that after `per-monitor-layers` this count is per screen. 27 strikes is what one
monitor sees, on a desktop of any width.

### Weather yields to an Emission but does not vanish

At the start of an Emission the cycle is suspended, so weather cannot change while the
sky is burning. Fog cross-fades out over the buildup, because a full-desktop haze in
front of the artifacts flattens exactly the contrast the Emission is building. Rain
continues and is lit by the strikes. When the Emission ends the cycle resumes and fog
returns if that was the state.

At black, the weather layer is settled to zero with everything else. Nothing is drawn
once the screen is dark, matching the rule `HideLightning` already enforces.

### Weather density is per region

Rain tile density and fog blob count derive from each region's area, through the model
`per-monitor-layers` introduces. A laptop panel and a large external get the same rain per
square inch, and the fog does not thin out on the bigger screen.

The weather *state* is not per region. It is one sky, and two monitors showing different
weather would read as a bug.

## Risks / Trade-offs

- **[Continuous cost, unlike every other layer]** -> Composited brushes and transforms
  rather than per-frame drawing; the layer is settled to zero and stops animating at
  blackout, and the existing `Suspended` path already stops the render loop when hidden or
  drawing into a powered-down panel. Measure against the current build on the widest
  desktop available before this is considered done.

- **[The rain tile repeats visibly]** -> Multiple tile scales at different speeds, low
  contrast, and a tile large enough that the period is long. Accepted as a trade for the
  per-frame cost.

- **[Fog in front of the artifacts dulls the theme]** -> Fog intensity is capped well
  below full and fog fades out during an Emission. If it still reads as a dirty screen,
  the cap is the knob to turn.

- **[A minute is short enough to notice, long enough to miss]** -> Jitter plus
  never-repeat means every change is visible when it happens. This is the decision most
  likely to need revisiting after seeing it run for an evening.

- **[Two layers cross-fading plus an Emission plus artifacts is the worst case]** ->
  The cycle is suspended during an Emission, so a cross-fade and an Emission cannot
  overlap except for one already in flight when the Emission starts. That one is allowed
  to finish.

- **[Ambient lightning is mistaken for an Emission starting]** -> Ambient strikes are
  fewer, weaker, and weighted towards wash rather than bolt. If the distinction does not
  land visually, ambient strikes lose the bolt entirely and become sky flashes only.

## Migration Plan

1. Land the Emission lightning changes first -- the gap curve and the schedule
   termination. They are independent of weather, small, and directly verifiable by
   counting strikes before 12.5s.
2. Add the weather layer with a single state (clear) wired into the z-order, the resting
   values and the blackout path, changing nothing on screen.
3. Add fog, then rain, then storm, each verifiable on its own with the cycle pinned.
4. Turn the cycle on last.

Rollback: the tray toggle turns weather off, and with it off the Zone theme renders as it
does today apart from the denser Emission storm.

## Open Questions

- The state weights (35/25/25/15) are a guess. They want an evening of watching before
  they are settled.
- Should fog and rain be visible in the Soap theme? The proposal says no, on the grounds
  that Soap is a different place, but the rendering would work there unchanged.
- Should ambient storm lightning respect the existing `Lightning` setting, which is
  documented as being about Emissions specifically, or get its own toggle?
