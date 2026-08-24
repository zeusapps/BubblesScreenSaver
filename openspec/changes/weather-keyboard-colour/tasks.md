## 1. The ambient colour

- [x] 1.1 Add `WeatherLight` in `src/Bubbles/Keyboard/`: a pure function from a weather state,
      an intensity and an anomaly tint to a `KeyColor`. `Clear` is black; `Fog`, `Rain` and
      `Storm` are the tint at their own weight.
- [x] 1.2 Give it an `AmbientCeiling` constant -- the fraction of the Emission's deepest red
      that ambient weather may reach -- and scale every state through it.
- [x] 1.3 Add a summing entry point that takes `WeatherCycle` and an `Anomaly` and folds every
      state's `IntensityOf` into one colour, so a cross-fade blends without a fade of its own.
- [x] 1.4 Add `WeatherLightTests`: `Clear` is black, a tint carries through, intensity scales,
      the same inputs give the same colour, and a two-state blend lands between its endpoints.
- [x] 1.5 Assert the ceiling holds: the brightest ambient colour any state and tint can produce
      is dimmer than the Emission's colour a second in. This is the test that keeps the
      Emission an event, so name it so nobody deletes it casually.

## 2. Rationing a slower sky

- [x] 2.1 Give `SendPolicy` a second floor for ambient colour, long enough that settled weather
      costs a handful of writes a minute. One class, two floors -- not a second policy.
- [x] 2.2 Keep the strike exemption on both paths, and scale an ambient flash below an
      Emission's.
- [x] 2.3 Extend `SendPolicyTests`: a minute of settled weather costs under a dozen writes, a
      six-second cross-fade produces several, and an ambient strike still gets through.

## 3. Telling the keyboard what the sky is doing

- [x] 3.1 Add an event to `OverlayWindow` beside `EmissionFrame`, raised from the weather tick,
      carrying the cycle, the dominant anomaly and the ambient strike already in hand at line
      990. Do not call `HasStrike` again.
- [x] 3.2 Raise it only while the artifacts are on screen and no Emission is running, mirroring
      the existing `_cycle.Suspended = _emitting`.
- [x] 3.3 Wire it in `App.cs` to a new `KeyboardLighting.Weather(...)`, beside the Emission
      wiring.

## 4. Two sources, one keyboard

- [x] 4.1 Give `KeyboardLighting` an ambient path that reuses the open, the record, the
      rationing and the hand-back unchanged -- only the colour source is new.
- [x] 4.2 Make an Emission outrank ambient: while emitting, ambient colours are dropped rather
      than queued, so there is one rule about precedence.
- [x] 4.3 Hold the device through `Clear` rather than releasing it, and confirm the existing
      release paths -- `LeftDark`, exit, startup recovery -- are untouched.
- [x] 4.4 Extend `KeyboardLightingTests`: ambient lights the keys, an Emission takes over
      mid-weather, weather resumes after a blackout, and `Clear` leaves the device held but the
      keys dark.

## 5. The setting

- [x] 5.1 Add one key to `Settings.cs`, defaulting off, with window text saying the keyboard is
      held for as long as the screensaver is up rather than for an Emission's twelve seconds.
- [x] 5.2 Make it a no-op unless `KeyboardLighting` is also on, and show that in the settings
      window the way other dependent settings are shown.
- [x] 5.3 Add tests: default off, off for an older `settings.json`, and nothing sent when the
      master switch is off.

## 6. Finding the number

- [x] 6.1 Run the full test suite and the build with warnings as errors.
- [x] 6.2 Watch it on the hardware, in a dark room: confirm a storm reads as weather and not as
      an Emission, that a cross-fade is a fade, and that ambient strikes flicker rather than
      slap. Adjust `AmbientCeiling` and report what it ended at and why.
- [x] 6.3 Trigger a real Emission straight after ambient weather and confirm it still lands as
      an event.
- [x] 6.4 Record in the change what was settled by looking, since the ceiling is the one value
      no test can choose.

## 7. What looking at it changed

Added after the first hardware run; every one of these came from watching the keys.

- [x] 7.1 Take the colour from `WeatherTint.Rain`/`Fog` -- what the sheets are drawn in -- rather
      than `AnomalyTint`, which is only what they are derived from.
- [x] 7.2 Split `WeatherTint` out of `WeatherBrushes`, so asking for a colour does not claim the
      WPF brush cache for the calling thread.
- [x] 7.3 Raise `AmbientCeiling` from 0.22 to 0.50; at a fifth the backlight read as switched off.
- [x] 7.4 Give rain and storms a shimmer, and leave fog still. This is what made the connection
      to the screen legible.
- [x] 7.5 Shorten the weather send floor to 0.2s so a shimmer is a shimmer, leaving the
      visible-step rule to keep a still sky cheap.
- [x] 7.6 Stop dimming ambient lightning: a bolt is a bolt, and it is the clearest signal here.
- [x] 7.7 Drop the storm's invented cold cast; on screen a storm is rain with bolts behind it.
