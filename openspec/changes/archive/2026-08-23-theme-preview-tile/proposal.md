## Why

The Theme group in the settings window is a dropdown reading "The Zone -- S.T.A.L.K.E.R.
artifacts" or "Soap bubbles -- the original", and five checkboxes that grey out when the answer
is not the Zone. Nothing in it shows what either theme looks like.

That is the one setting in the whole window where a name is a poor substitute for the thing.
Every other setting is a quantity or a yes-or-no -- a delay of two minutes means two minutes --
but a theme is a picture, and the only way to see which one is selected is to close the window,
wait out the idle delay, and look. The screensaver is held off while the window is open, so
that is not a small ask.

The application already renders every image in its documentation from the drawing code itself,
through `--export`. The artwork to show is already there and already public: `ArtifactVisual`
draws one artifact at a given time, and `BubbleArt.Skins` holds the six soap sprites.

## What Changes

- **Add a preview to the Theme group.** A small still image showing the selected theme's
  artwork, sitting with the theme dropdown so that choosing a theme and seeing it are the same
  glance.
- **Render it from the drawing code, not from a stored picture.** The preview composes
  `ArtifactVisual` and `BubbleArt.Skins` -- the same types the overlay draws with -- so it
  cannot fall out of date with the artwork. This follows the rule `--export` already
  establishes for the README images.
- **Swap it when the theme changes**, without closing and reopening the window.
- **Keep it still.** No animation and no timer: the window is a form, and `Animated` is itself
  one of the settings on offer in it.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `settings-dialog`: gains a requirement that the theme is shown as well as named, and that
  what is shown is rendered from the drawing code rather than stored alongside it. The existing
  requirement about settings the current theme ignores is untouched -- the preview is about the
  theme that *is* selected, not the settings that are inert.

## Impact

- `src/Bubbles/Session/SettingsWindow.xaml.cs` -- the `Themes()` row gains the preview beside
  the dropdown, and `RefreshAll` swaps it.
- **New** a small renderer, composing `ArtifactVisual`, `Artifacts` and `BubbleArt.Skins`.
  All three are already public; nothing in `Zone` needs opening up.
- No change to `Settings.cs`, to `settings.json`, or to any overlay code.
- `Export.cs` is left alone. It builds comparable pictures for the README, but its helpers are
  private to it and sized for documentation rather than for a form, and reaching into it would
  couple the settings window to the documentation build.

## Open Questions

Recorded here, to be settled in `design.md`:

1. **What the preview should be a picture of.** A row of artifacts against a dark ground is the
   obvious answer for the Zone, and the soap sprites for the other -- but the themes also
   differ in what surrounds them (the Zone has weather, lightning, the detector), and a preview
   that shows only the artifacts undersells it.
2. **Whether the preview reflects other settings.** It could honour `Dim` and `Opacity`, which
   would make it a small live preview -- something the previous change explicitly declined to
   build. The boundary needs stating rather than drifting across.
3. **What it costs.** `ArtifactVisual` is vector content that WPF re-rasterises on every
   composition pass, which is the documented reason `Animated` exists as a setting at all.
   A still preview should be rendered once to a bitmap rather than left live in the tree.
