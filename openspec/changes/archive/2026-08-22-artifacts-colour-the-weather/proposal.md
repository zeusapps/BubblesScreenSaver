## Why

Weather and artifacts share a screen and ignore each other. The sky cycles through fog, rain
and storms while the artifacts drift underneath, and nothing in either one gives any sign that
the other is there. Two independent screensavers running at once.

The Zone already sorts its artifacts into four anomaly families -- Chemical, Electrical,
Thermic, Gravitational -- each with its own palette, and there are four weather states. That is
a mapping the setting hands over for nothing, and it is currently unused.

There is also a claim to make good on. The README and `zone-weather`'s design both say rain is
lit by the strikes. It is not: lightning is drawn behind the artifacts and the weather in front
of them, so a bolt passes behind the rain without touching it.

## What Changes

- **Rain is lit by strikes.** A bolt, ambient or Emission, brightens the precipitation for the
  length of the strike. This is the sentence already in the README made true.
- **Weather takes its colour from what is drifting in it.** Whichever anomaly family holds the
  most artifacts on screen tints the rain and the fog's glow: Chemical acid-green, Electrical
  cold blue-white, Thermic warm amber, Gravitational a muted violet. The tint follows the
  census as artifacts are collected and replaced, and changes by the same cross-fade a weather
  change uses.
- **Collecting an artifact disturbs the weather.** The detector picking one up produces a
  short, local flourish keyed to its family: Thermic burns a clearing in the fog, Electrical
  draws an ambient strike, Chemical stains the rain briefly, Gravitational pulls the fog inward.
  A few seconds, then it settles.
- The tint and the flourishes are visual only. Nothing here changes where artifacts go, how
  fast they drift, or which ones the Zone sends in.

Explicitly out of scope, and both asked about:

- **Weather deciding which artifacts spawn.** The Zone sends what it sends; making the weather
  pick would turn a backdrop into a slot machine.
- **Per-artifact local weather** -- rain curving around a Gravitational artifact, fog thinning
  in a halo around a Thermic one. It is the most striking version of this and the most
  expensive: the weather sheets are one tiled brush per screen, so holes around moving artifacts
  mean a mask that changes every frame, which is the per-frame cost `zone-weather` spent a
  release removing. It wants a prototype before it wants a specification.

## Capabilities

### New Capabilities

- `artifact-weather-interplay`: How the artifacts on screen colour and disturb the weather
  around them -- which family the weather takes its cue from, how a strike reaches the
  precipitation, and what a collection does to the sky.

### Modified Capabilities

None as a delta. `zone-weather` is implemented but **not archived**, so there is no
`openspec/specs/zone-weather/` to write a delta against; its requirements are extended by the
new capability instead. What is being extended, so it is on the record: rain and fog stop being
a fixed palette, and precipitation gains a lit state it did not have. If `zone-weather` is
archived before this lands, those two become a delta on it.

## Impact

- **Depends on `zone-weather`, which is implemented but not archived.** Its spec is the one
  being modified.
- `src/Bubbles/Zone/WeatherBrushes.cs` -- tiles gain a family tint. The bitmaps are rasterised
  once per sheet today; a tint per family multiplies that, so this decides either a tinted
  `ImageBrush` over one grey bitmap, or four bitmaps per sheet.
- `src/Bubbles/Zone/WeatherLayer.cs` -- the tint, the lit state, and the flourishes.
- `src/Bubbles/Zone/BubbleField.cs` -- the family census, and an event carrying what was
  collected rather than only that something was.
- `src/Bubbles/Zone/Artifacts.cs` -- a representative colour per family, if the palettes do not
  already supply one.
- `src/Bubbles/Overlay/OverlayWindow.cs` -- feeding the census and the collection to the layer,
  and telling the weather when a bolt is on screen.
- `src/Bubbles/Export.cs` -- a strip showing each family's weather.
- Per-frame cost must not move. The whole point of the current design is that weather is
  composited rather than repainted, and a tint that repaints a tile would undo it.
