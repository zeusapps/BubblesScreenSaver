## Context

The settings window is a WPF form built from a table in code-behind, with each row bound to a
setting through `SettingsHost.Edit`. The Theme group holds a two-item dropdown and five
checkboxes that `RefreshAll` greys out when the theme is not the Zone.

Everything the preview needs to draw is already public and already used elsewhere:

- `ArtifactVisual` is a `Grid` with `Skin` and `Time`, drawing one artifact at a moment. This
  is the type the overlay uses and the type `Export.Snapshot` wraps in a `Viewbox` for the
  README sheets.
- `Artifacts.All` and `Artifacts.Count` describe the sixteen kinds across four families.
- `BubbleArt.Skins` is six `BitmapSource` soap sprites, rendered once and cached.
- `BubbleArt.CreateWindowIcon` already establishes the pattern of rendering artwork to a frozen
  bitmap for chrome rather than leaving it live.

`Export.cs` builds pictures that are close to what is wanted, but its helpers are private, it
is `internal static` with a directory-writing entry point, and its sizes are chosen for
documentation. It stays untouched.

## Goals / Non-Goals

**Goals:**

- The selected theme is visible in the window, not only named.
- The picture comes from the drawing code, so it cannot drift from what the overlay draws.
- Switching the theme switches the picture, with the window open.
- Negligible cost: rendered once per theme, cached, and not left as live vector content.

**Non-Goals:**

- **A live preview of the other settings.** The previous change declined this deliberately and
  the reasoning has not changed; see the decision below for where the line falls.
- Animation, a timer, or anything that redraws after the window has settled.
- Any change to `Export.cs` or the README images.
- Previewing the Emission, the blackout, or the PIN prompt. Those are events, not themes.

## Decisions

### The preview shows the theme's world, not just its shapes

A row of artifacts on black describes the Zone about as well as a row of circles describes soap
bubbles: technically the subject, but not the thing anybody recognises. The Zone is artifacts
*over a dimmed desktop*, with weather in front of them; Soap is translucent spheres over the
same desktop.

So each preview is composed of the same layers the overlay uses, in the same order: a stand-in
desktop, a dimming scrim, then the theme's artwork. The desktop is a flat gradient rather than
anything resembling a real screen -- the point is to show that the overlay composites through
to what is underneath, which is the single most distinctive thing about this screensaver and is
invisible in a picture with a black background.

*Alternative considered:* artwork on plain black. Simpler, and wrong for the reason above.

### Rendered once to a frozen bitmap, per theme

`ArtifactVisual` is vector content. The class comment on `Animated` records that WPF
re-rasterises vector content on every composition pass, which is why animating the artifacts is
a setting with a CPU warning attached. Leaving two of them live in a settings window would pay
that cost for a picture that never changes.

Each preview is therefore built, measured, rendered into a `RenderTargetBitmap`, frozen, and
cached for the lifetime of the window. Switching themes swaps the `Source` of one `Image`.
Two bitmaps at preview size is a trivial amount of memory and the redraw cost after that is
what any other image costs.

*Alternative considered:* build the visual tree and let WPF draw it live. Rejected on the cost
above, and because a live tree would also animate if `ArtifactVisual` ever gained an internal
clock.

### The preview is fixed, and does not follow the other settings

It uses fixed artwork, a fixed dim, and a fixed selection of shapes. It does **not** read
`Dim`, `Opacity`, `BubbleCount`, `MinRadius`, `Animated` or the weather toggles.

This is the line the previous change drew, and it is worth stating why the preview does not
cross it. The question that change answered was "can you see your settings taking effect" --
and the answer was no, because the window holds the screensaver off, and making it yes means
reworking the cancel-on-any-input guarantee. This preview answers a different and much smaller
question: *which* theme is this. A preview that followed `Dim` and `Opacity` would start
looking like an answer to the first question while still not being one -- it would move when
you dragged a slider, and stay wrong about everything else.

Fixed artwork also keeps the picture legible. A user who has set `Dim` to 0.95 and `Opacity` to
0.05 would otherwise get two near-identical black rectangles to choose between.

### Deterministic composition, seeded

The artifacts shown are picked and placed from a `Random` with a fixed seed, as `Export` does
with `new Random(11)`. A preview that differed between openings would look like a bug, and one
that differed between machines could not be described in a spec.

### It lives beside the dropdown, not above the group

The row helper already lays out label, control, trailing. The preview goes in the trailing
column of the theme row, so choosing and seeing are one glance apart and the group keeps its
shape. A banner across the top of the group would push the five Zone checkboxes further from
the dropdown that governs them.

## Risks / Trade-offs

- **The preview is a promise about what you will see** → It is composed from the same types the
  overlay draws with, so it cannot drift in the way a stored screenshot would. It can still
  mislead about density and dimming, which is what the fixed-artwork decision accepts openly.
- **Rendering at window construction adds to open time** → Two small bitmaps, built once. If it
  is measurable, the second theme's preview can be built on first use rather than up front.
- **`RenderTargetBitmap` needs a measured, arranged element** → `Export.Measure` does this and
  the preview builder must do the same; an unmeasured tree renders as nothing at all, which is
  a silent failure rather than an exception.
- **A theme added later gets no preview** → The renderer switches on `OverlayTheme`, so a new
  member is a compile-time gap only if the switch is exhaustive. Write it so it is.

## Migration Plan

None. No stored setting, no file format, and no behaviour outside the window changes. The
preview is additive: if it fails to render, the dropdown still says which theme is selected.

## Open Questions

- Whether the Soap preview should show the sprites over the same stand-in desktop as the Zone
  one. It should, for comparability -- but Soap has no weather, so the two previews will differ
  in busyness as well as in palette, and that is honest rather than a flaw.
