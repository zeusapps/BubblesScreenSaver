## Context

`zone-weather` left the weather layer in a particular shape, and every decision here is
constrained by it.

Rain and fog are **bitmaps**. Each sheet is rasterised once into a `RenderTargetBitmap` and
tiled as an `ImageBrush` scrolled by an animated `TranslateTransform`. That is not incidental:
they were vector `DrawingBrush`es until v1.8.1, and re-rasterising a hundred-odd radial
gradients as the transform moved cost a whole core. The fix is the reason weather is affordable,
and anything that tints a tile by repainting it puts that cost straight back.

Intensity is the brush's own `Opacity`, on a ladder of eight pre-built brushes sharing one
bitmap. `Brush.Opacity` folds into the fill as it rasterises; `UIElement.Opacity` is what forces
WPF onto an intermediate surface. That distinction is what the ladder exists to respect, and
`OpacityMask` falls on the wrong side of it.

The cross-fade holds **at most two sheets live**: an outgoing state and an incoming one, with
`Progress` running 0 to 1 and the outgoing slot emptied when it lands.

The z-order is: scrim, Emission sky, **lightning**, artifacts, **weather**, shockwave flash,
detector. Lightning is below the artifacts because it is the sky and should silhouette them.
Weather is above them because fog behind the artifacts fogs nothing. The consequence nobody
noticed until now is that a bolt is two layers below the rain, so it cannot light it -- while
the shockwave, being above, does.

`BubbleField` already knows what it collected (`LastCollectedSkin`, `Collected`, and a 1.6s
cooldown so a crowded corner does not hoover). `Artifacts.All` already carries `Core`, `Shell`
and `Halo` colours per artifact and an `Anomaly` family per artifact, four of each.

## Goals / Non-Goals

**Goals:**

- The weather looks like it belongs to the artifacts drifting in it.
- A strike reaches the rain.
- Collecting something visibly disturbs the sky, at the place it happened.
- Per-frame cost unchanged. No sheet is ever repainted while it is on screen.

**Non-Goals:**

- Weather choosing which artifacts arrive. The Zone sends what it sends.
- Per-artifact local weather -- rain parting around a Gravitational artifact. Wants a prototype,
  not a specification. See the proposal.
- Anything that moves the artifacts. This change is visual.
- New weather states, or any change to the cycle's timing, weights or cross-fade.

## Decisions

### The tint is baked into the bitmap, rasterised per family and cached

A tinted tile is a different bitmap, produced by rasterising the same drawing with the family's
colour and kept for as long as the process lives.

Alternative considered: one greyscale bitmap as the `Rectangle`'s `OpacityMask`, with a
`SolidColorBrush` fill supplying the colour. It is the elegant version -- the tint becomes a
`Fill` assignment and could vary continuously -- but `OpacityMask` composites through an
intermediate surface, which is the class of cost this layer was rebuilt to avoid. It would trade
the v1.8.1 fix for a nicer API.

Alternative considered: rasterise all four families up front. Simple, and about 10 MB of
bitmaps for tiles most desks will never show. Lazily is the same code with a dictionary in front
of it.

**Corrected after measuring.** This section claimed rasterising "costs a few milliseconds" and
happens "off the render path". Both were wrong, and the change shipped a visible freeze because
of it. A family's tiles take **19-77 ms** to rasterise, and the first frame that needs them is
the frame that builds them -- so the stall lands on the first frame of the tint cross-fade, at
exactly the moment the weather changes colour. Frame logs across a demo run: a 63 ms frame nine
seconds in, precisely when fog first appears.

So the tiles are warmed up front after all, at `DispatcherPriority.ApplicationIdle`, one family
per callback -- the "rasterise all four up front" alternative rejected above, which was rejected
on memory grounds without the freeze being weighed against it. The measured total is about 12 MB
and a few hundred milliseconds, spent at window creation, minutes before the idle timeout draws
anything. Memory traded for a stall, deliberately, because the stall was visible and the memory
is not.

Lazily is still the mechanism -- `Warm` is `For` under another name -- so a family that somehow
arrives unwarmed still works, just slowly.

### A tint change is a state change

The layer already cross-fades between two things and holds at most two live. So the thing being
faded becomes the pair `(state, family)` rather than the state alone, and a tint change goes
through exactly the machinery a weather change does.

Alternative considered: a second, independent cross-fade for the tint. That permits four live
sheets during a simultaneous state and tint change -- twice the fill rate, at the one moment
the layer is already busiest -- for a distinction nobody watching could name.

### The intensity ladder needed to be finer

Not part of the original design, and found the same way: by looking at it. The ladder was eight
rungs, on the inherited reasoning that a six-second cross-fade makes that one step every three
quarters of a second and therefore invisible. One rung is an opacity step of one eighth, which
across a desktop-wide sheet at the fog's own alpha is a **2.7% jump in alpha** -- and a flat area
that size bands at about 1%. Rain hid it, having no flat area to band across; fog is nothing but
flat area.

Thirty-two rungs brings the step to 0.69%. It costs nothing worth counting, because a rung is
another `ImageBrush` over the bitmap the whole ladder already shares -- no extra rasterising and
not one extra pixel. `RainCeiling` and `StrikeLift` are derived from `Levels` so the three
cannot drift out of proportion.

One trap on the way: the "draw nothing" cutoff was a flat `intensity <= 0.02`, which sits below
the bottom rung on an eight-rung ladder and *above* it on a thirty-two-rung one. Left alone, a
finer ladder would have silently deleted its own bottom step and every fade would have begun by
jumping to the second rung -- a new discontinuity at the start of every fade, introduced while
fixing one in the middle. The cutoff is now a fraction of a rung.

### The census is counted on change, never per frame

The dominant family is recomputed when the population changes or something is collected, not
every frame. It is a count over at most a few hundred artifacts, but the render path is the one
place this change must not touch.

### The tint needs hysteresis, or it will flap

Sixteen artifact kinds spread over four families means two families are often within one
artifact of each other, and a single collection would flip the sky. So a challenger has to lead
the incumbent by a margin before it takes over, and a tint has to hold for a minimum dwell
regardless. Weather changes about once a minute; a tint changing faster than that would read as
flickering rather than as a shift.

With no artifacts at all, or a genuine tie under the margin, the tint stays where it is.

### Strike-lit rain moves the rung, not the layer

While a bolt is on screen the rain renders some rungs brighter, which is a `Fill` swap to
another already-built brush. The lightning layer keeps its position below the artifacts -- it is
the sky, and moving it above them to reach the rain would put bolts over the top of everything.

`HasStrike` already exists and is already consulted per frame by the render loop, so the layer
is told rather than asked.

The lift lasts exactly as long as the strike, so this needs no clock of its own.

### A collection is a small element, not a change to the sheets

The flourish is a short-lived radial burst placed at the detector, in the family's colour,
animated by opacity and scale and then removed.

This is the only way to get something *local* out of a layer built from desktop-wide tiles: a
tile cannot be disturbed in one place without repainting it. A single small element, alive for
about a second, is cheap in exactly the way a mask over the whole sheet is not.

Two families additionally nudge the weather itself, because they have somewhere obvious to
reach: Electrical brings an ambient strike forward, and Thermic dips the fog briefly. Both are
parameter changes to machinery that already exists. Chemical and Gravitational get the burst
alone -- inventing a mechanism per family would be four times the surface for a second of
animation.

### Family colours come from the artifacts, not from a new palette

Each family's tint is derived from the `Core` colours already in `Artifacts.All`. Chemical is
acid-green, Electrical near-white blue, Thermic amber, Gravitational muted -- those palettes
were chosen once and should not be chosen again somewhere else, where they can drift.

Gravitational is the awkward one: its artifacts are dark bodies with pale shells, so `Core` is
nearly black and useless as a tint. That family takes its `Shell` instead, which is the part of
it that is actually visible.

## Risks / Trade-offs

- **[A tint per family multiplies the bitmaps]** -> Lazy and cached, so a desk only pays for
  families it actually sees, and each is rasterised once per run.

- **[The tint makes rain hard to see against the desktop]** -> Tints are applied to the tile's
  existing alpha rather than replacing its brightness, so a coloured sheet is no more opaque
  than the grey one. Any family whose colour disappears against a dark desktop is a reason to
  lift that colour, not to lift the alpha.

- **[Hysteresis makes the tint feel unresponsive]** -> That is the intent. The alternative
  flickers. If the margin proves too sticky it is one constant.

- **[Strike-lit rain reads as the rain flashing rather than the sky]** -> The lift is a couple
  of rungs, not full brightness, and lasts a strike. If it still looks like the precipitation is
  the light source, the lift comes down.

- **[The collection burst competes with the detector's own flash]** -> They fire together by
  definition. The burst is behind the detector in z-order and is the larger, dimmer of the two,
  so it should read as the sky answering rather than as a second readout.

- **[Bursts pile up]** -> The 1.6s collection cooldown already bounds the rate, and a burst is
  shorter than the cooldown, so at most one is alive at a time. Any change to that cooldown has
  to keep this true, or the cap has to move into the layer.

## Measured cost against v1.8.1

Nine `--weather-demo` runs, Release builds, same machine, baseline and current interleaved and
order-reversed to cancel drift. CPU from the process's own accumulated processor time; GPU summed
across the Windows GPU engine counters for the pid.

|                          | CPU %  | GPU %  |
|--------------------------|--------|--------|
| v1.8.1 (n=4)             | 2.83   | 33.58  |
| this change, no tint changes (n=2) | 2.80 | 34.50 |
| this change, 4 tint changes (n=3)  | 2.97 | 35.08 |

**CPU does not move.** +0.14 pp, well inside the run-to-run spread.

**GPU is inconclusive.** +1.50 pp looks like a 4.5% rise, but the spread across all nine runs is
sd 1.27 with a range of 32.65-36.52 -- so the effect is about one standard deviation and this
method cannot separate it from noise. Suppressing the tint cross-fades keeps two thirds of the
apparent delta, which argues against the doubled sheet slots being the cause and for it being
measurement noise.

Two caveats worth keeping. The demo is a worst case for the tint: it forces four cross-fades in
sixty seconds, where the dwell alone allows at most one per twenty-five and the census in real
use swings far less often. And the noise floor here is roughly the size of the effect -- settling
it would want PresentMon or a few dozen runs, not five more.

## What was judged, and what was not

The tints were signed off on a live desktop. They can be changed at any time -- the whole palette
is derived from `Artifacts.All` in one place, so moving one is moving one colour.

**The margin and the dwell were never watched in use.** They are asserted directly in
`FamilyCensusTests` -- a one-artifact lead does not flip the sky, a field oscillating by one
never flaps, a run of collections cannot walk the tint through four families -- but no one has
sat and watched the census actually drive a tint change, because the demo *pins* the family in
order to show all four inside a minute, which switches the census off.

That gap is worth naming rather than leaving implicit. Twice in this change a property was
asserted, believed, and wrong: rasterising was "off the render path" and the ladder was
"invisible". Both were found by looking at the screen, not by a test. The margin and the dwell
are in that same category of claim, and the only thing that will confirm them is an evening of
ordinary use where nothing pins anything.

The risk is low -- worst case is a tint that changes more or less often than intended, which is
one constant either way -- but it is not zero, and it is not tested.

## Migration Plan

1. Family colours and the census, both pure and assertable with no window.
2. Tinted tiles behind the existing brush lookup, still always using one family, so nothing
   changes on screen.
3. Turn the census on, with the hysteresis.
4. Strike-lit rain.
5. The collection burst, then the two per-family nudges.

Rollback: the tint falls back to the untinted tile, and the flourish is one element that can
stop being added. Turning weather off removes all of it, as it does today.

## Open Questions

None outstanding. The one that was open in the proposal -- tinted bitmaps or a colour through a
mask -- is settled above, against the mask.
