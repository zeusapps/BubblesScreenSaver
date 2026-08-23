## 1. The renderer

- [x] 1.1 Add `ThemePreview` in `src/Bubbles/Session/`: given an `OverlayTheme`, returns a
      frozen `BitmapSource` of that theme, with a `switch` over the enum that a new member would
      break rather than silently fall through
- [x] 1.2 Compose the layers the overlay uses, in order: a stand-in desktop gradient, a dimming
      scrim over it, then the artwork -- so the preview shows that the overlay composites
      through to what is underneath
- [x] 1.3 Zone artwork: a seeded selection of `ArtifactVisual`s across the families, each in a
      `Viewbox`, laid out from a `Random` with a fixed seed
- [x] 1.4 Soap artwork: `BubbleArt.Skins` sprites, placed from the same fixed seed
- [x] 1.5 Measure and arrange before rendering -- an unmeasured tree renders blank, which fails
      silently rather than throwing
- [x] 1.6 Render to `RenderTargetBitmap` at the preview's size, `Freeze()`, and return
- [x] 1.7 Cache per theme so switching back and forth renders once each

## 2. Wiring it into the window

- [x] 2.1 Give the theme row an `Image` in its trailing column, sized in DIP so it scales with
      the display
- [x] 2.2 Swap the `Image.Source` from `RefreshAll`, so changing the theme changes the picture
      without reopening
- [x] 2.3 Confirm the picture does not change when any other control is touched
- [x] 2.4 Wrap the render in a try/catch: on failure leave `Source` null, log through
      `Diagnostics`, and let the window open normally
- [x] 2.5 Check the Theme group still reads well -- the five Zone checkboxes stay under the
      dropdown that governs them

## 3. Tests

- [x] 3.1 Test that both themes render a non-null, frozen bitmap of the expected size
- [x] 3.2 Test determinism: rendering the same theme twice produces identical pixels
- [x] 3.3 Test that the two themes produce *different* pixels, so a wiring mistake that returned
      one for both is caught
- [x] 3.4 Test the cache: a second request for the same theme returns the same instance
- [x] 3.5 Test that the renderer ignores settings -- rendering with `Dim` at maximum and
      `Opacity` at minimum gives the same bitmap as with defaults
- [x] 3.6 Use the existing STA harness (`tests/Bubbles.Tests/Sta.cs`), since WPF visuals cannot
      be built on the test runner's default thread

## 4. Verify

- [x] 4.1 `dotnet build` clean and `dotnet test` green
- [x] 4.2 Run with `--settings` and capture the window; confirm both previews look like their
      theme and that the desktop reads through the dimming
- [x] 4.3 Switch the theme in the running window and confirm the picture follows
- [x] 4.4 Confirm `--export` still regenerates the README images unchanged, since `Export.cs`
      was deliberately left alone
      -- CONFIRMED not caused by this change. `detector.png`, `hero.png` and `screens.png`
      differ between two consecutive runs of the *same* binary: `BubbleField._rng` is
      unseeded, and all three compose a `BubbleField`. Pre-existing, and CI tolerates it
      because the step named "Fail if the committed images are stale" only prints a
      warning and never exits non-zero. Left alone; worth its own change.
- [x] 4.5 Update the README's settings section if it describes the Theme group
