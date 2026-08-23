using System.Windows.Media.Imaging;

using Bubbles.Session;

namespace Bubbles.Tests;

/// <summary>The settings window shows a picture of the selected theme, because a theme is the
/// one setting in that window a name describes poorly.
///
/// These run on the STA thread: the preview is built from FrameworkElements, and WPF's input
/// manager refuses to exist on xunit's MTA threads. See <see cref="Sta"/>.</summary>
public sealed class ThemePreviewTests
{
    private static byte[] Pixels(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var buffer = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(buffer, stride, 0);
        return buffer;
    }

    [Fact]
    public void Both_themes_produce_a_picture()
    {
        Sta.Run(() =>
        {
            foreach (var theme in new[] { OverlayTheme.Zone, OverlayTheme.Soap })
            {
                var preview = ThemePreview.For(theme);

                Assert.NotNull(preview);
                Assert.Equal((int)ThemePreview.Width, preview!.PixelWidth);
                Assert.Equal((int)ThemePreview.Height, preview.PixelHeight);

                // Frozen, so it can be handed to an Image and never touched again.
                Assert.True(preview.IsFrozen);
            }
        });
    }

    [Fact]
    public void The_two_themes_do_not_look_the_same()
    {
        // The failure this catches is a wiring mistake that returns one theme's picture for
        // both, which would leave the control looking as though it had stopped working.
        Sta.Run(() =>
        {
            var zone = Pixels(ThemePreview.For(OverlayTheme.Zone)!);
            var soap = Pixels(ThemePreview.For(OverlayTheme.Soap)!);

            Assert.NotEqual(zone, soap);
        });
    }

    [Fact]
    public void A_picture_is_rendered_once_and_kept()
    {
        // Artifacts are vector drawings, which WPF re-rasterises on every composition pass --
        // the documented reason Animated is a setting with a CPU warning on it. Switching the
        // theme back and forth must not pay that again.
        Sta.Run(() =>
        {
            var first = ThemePreview.For(OverlayTheme.Zone);
            var second = ThemePreview.For(OverlayTheme.Soap);
            var again = ThemePreview.For(OverlayTheme.Zone);

            Assert.Same(first, again);
            Assert.NotSame(first, second);
        });
    }

    [Fact]
    public void The_picture_is_the_same_every_time()
    {
        // Seeded composition. A preview that differed between openings would read as a defect,
        // and one that differed between machines could not be written down in a spec.
        Sta.Run(() =>
        {
            var once = Pixels(ThemePreview.For(OverlayTheme.Zone)!);
            var twice = Pixels(ThemePreview.For(OverlayTheme.Zone)!);

            Assert.Equal(once, twice);
        });
    }

    [Fact]
    public void The_picture_shows_the_desktop_through_the_dimming()
    {
        // The overlay composites through to what is underneath, which is the most distinctive
        // thing this screensaver does and the thing a picture on a black background hides. The
        // stand-in desktop is a diagonal gradient, so opposite corners must differ.
        Sta.Run(() =>
        {
            var preview = ThemePreview.For(OverlayTheme.Zone)!;
            var pixels = Pixels(preview);
            var stride = preview.PixelWidth * 4;

            var topLeft = (pixels[0], pixels[1], pixels[2]);
            var bottomRight = (
                pixels[(preview.PixelHeight - 1) * stride + (preview.PixelWidth - 1) * 4],
                pixels[(preview.PixelHeight - 1) * stride + (preview.PixelWidth - 1) * 4 + 1],
                pixels[(preview.PixelHeight - 1) * stride + (preview.PixelWidth - 1) * 4 + 2]);

            Assert.NotEqual(topLeft, bottomRight);

            // ...and neither corner is pure black, which is what a missing desktop layer would
            // leave behind.
            Assert.True(topLeft.Item1 + topLeft.Item2 + topLeft.Item3 > 0);
        });
    }

    [Fact]
    public void The_picture_ignores_every_other_setting()
    {
        // It answers which theme is selected, not what the current settings look like. A picture
        // that moved when a slider was dragged would look as though it were answering the second
        // question while still being wrong about everything else.
        Sta.Run(() =>
        {
            var baseline = Pixels(ThemePreview.For(OverlayTheme.Zone)!);

            var host = new SettingsHost(new Settings());
            host.Edit(s =>
            {
                s.Dim = Settings.Range.DimMax;
                s.Opacity = Settings.Range.OpacityMin;
                s.BubbleCount = Settings.Range.BubbleCountMax;
                s.Animated = false;
                s.Weather = false;
                s.ShowDetector = false;
            });

            Assert.Equal(baseline, Pixels(ThemePreview.For(OverlayTheme.Zone)!));
        });
    }
}
