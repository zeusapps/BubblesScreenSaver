using System.Drawing;
using System.Windows;
using System.Windows.Media;

using Bubbles.Interop;

namespace Bubbles.Overlay;

/// <summary>Answers the one question nothing in-process can: does the desktop actually come
/// through the overlay?
///
/// The layer opacities can be perfectly correct while the window still paints opaque, because
/// that failure is in the compositor rather than in WPF. BUBBLES_SNAP=1 already dumps what WPF
/// believes it is drawing; this looks at what ended up on the glass. Between them the two halves
/// of an opaque overlay are told apart in one run:
///
///   snap.png shows the artifacts over a dim desktop, and this reports black
///       -> WPF is right and the DWM frame extension is gone
///   snap.png is itself black
///       -> a layer is stuck and the compositor is innocent
///
/// It paints a known colour, puts the overlay over it at the artifacts stage, captures the
/// screen and samples. A known colour rather than the real desktop, because a desktop that
/// happens to be dark is indistinguishable from an overlay that is opaque.
///
/// This joins --dim-test, --hold-test and --inputs: every signal this application relies on is
/// one that Windows reports unreliably, and each has to be answerable on the machine where it is
/// going wrong.</summary>
internal static class GlassTest
{
    // Magenta. Nothing on a desktop is this colour by accident, and it puts signal in two
    // channels while leaving the third at zero, so a partial result is still readable.
    private const byte BackdropRed = 255;
    private const byte BackdropGreen = 0;
    private const byte BackdropBlue = 255;

    /// <summary>Below this in every channel is black as far as this test is concerned.</summary>
    private const int Black = 24;

    public static void Run()
    {
        // The process manifest already declares PerMonitorV2, so the capture below sees physical
        // pixels and the screen bounds agree with it. Getting that wrong silently captures the
        // top-left corner of the desktop and puts every coordinate out by the DPI scale --
        // which, per the README, once cost hours chasing a rendering bug that did not exist.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };

        app.Startup += (_, _) => Begin(app);
        app.Run();
    }

    private static void Begin(Application app)
    {
        // Built here rather than loaded, so the result does not depend on what the user has
        // configured, and so nothing is ever written back to settings.json.
        var settings = new Settings
        {
            Dim = 0.55,
            Opacity = 0.85,
            BubbleCount = 1,
            FadeInSeconds = 0,
            HideCursor = false,
            ShowDetector = false,
            Lightning = false,
            MaxFps = 30,
        }.Clamped();

        var screen = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                     ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);

        var backdrop = ShowBackdrop(screen);
        var overlay = new OverlayWindow(settings);

        overlay.Show();
        overlay.ShowBubbles();

        // Long enough for the fade and for DWM to have composited a frame or two.
        var settle = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1200),
        };

        settle.Tick += (_, _) =>
        {
            settle.Stop();

            try
            {
                Report(Sample(screen), settings.Dim);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"could not capture the screen: {ex.Message}");
            }
            finally
            {
                overlay.HideBubbles(immediate: true);
                overlay.Close();
                backdrop.Close();
                app.Shutdown();
            }
        };

        settle.Start();
    }

    /// <summary>A plain window in a known colour, filling the primary screen. Not topmost: the
    /// overlay is, and that is what puts it on top of this.</summary>
    private static Window ShowBackdrop(System.Drawing.Rectangle screen)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            Topmost = false,
            Background = new SolidColorBrush(Color.FromRgb(BackdropRed, BackdropGreen, BackdropBlue)),
            Title = "Bubbles glass test backdrop",
        };

        window.Show();

        // Placed in physical pixels, the same way the overlay places itself, so the two line up
        // on a scaled display.
        var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        Native.SetWindowPos(hwnd, IntPtr.Zero, screen.X, screen.Y, screen.Width, screen.Height,
            Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);

        return window;
    }

    private sealed record Sampled(
        int Points,
        int NotBlack,
        System.Drawing.Color Brightest,
        double MeanRed,
        double MeanBlue);

    /// <summary>Captures the primary screen and looks at a grid of points across it. A grid
    /// rather than one pixel, because an artifact may be sitting on any given spot.</summary>
    private static Sampled Sample(System.Drawing.Rectangle screen)
    {
        using var shot = new Bitmap(
            screen.Width, screen.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var canvas = Graphics.FromImage(shot))
        {
            canvas.CopyFromScreen(
                screen.X, screen.Y, 0, 0, screen.Size, CopyPixelOperation.SourceCopy);
        }

        const int Steps = 12;
        var points = 0;
        var notBlack = 0;
        var brightest = System.Drawing.Color.FromArgb(0, 0, 0);
        double red = 0, blue = 0;

        for (var iy = 1; iy < Steps; iy++)
        {
            for (var ix = 1; ix < Steps; ix++)
            {
                var pixel = shot.GetPixel(screen.Width * ix / Steps, screen.Height * iy / Steps);

                points++;
                red += pixel.R;
                blue += pixel.B;

                if (pixel.R > Black || pixel.G > Black || pixel.B > Black) notBlack++;
                if (pixel.R + pixel.G + pixel.B > brightest.R + brightest.G + brightest.B) brightest = pixel;
            }
        }

        return new Sampled(points, notBlack, brightest, red / points, blue / points);
    }

    private static void Report(Sampled sampled, double dim)
    {
        // What the backdrop should look like once the scrim has dimmed it. Approximate: the
        // artifacts add light of their own and WPF composites in its own colour space.
        var expected = (int)Math.Round(255 * (1 - dim));

        Console.WriteLine($"backdrop           #{BackdropRed:X2}{BackdropGreen:X2}{BackdropBlue:X2} (magenta)");
        Console.WriteLine($"scrim              Dim {dim:N2}, so roughly {expected} of 255 should survive in red and blue");
        Console.WriteLine($"sampled points     {sampled.Points}");
        Console.WriteLine($"not black          {sampled.NotBlack}");
        Console.WriteLine($"brightest sample   #{sampled.Brightest.R:X2}{sampled.Brightest.G:X2}{sampled.Brightest.B:X2}");
        Console.WriteLine($"mean red / blue    {sampled.MeanRed:N1} / {sampled.MeanBlue:N1}");
        Console.WriteLine();

        // Magenta coming through means red and blue survive together. An artifact glowing on an
        // otherwise black screen would lift one channel or the brightest sample alone, so the
        // verdict is taken from the means across the whole grid.
        var throughput = Math.Min(sampled.MeanRed, sampled.MeanBlue);
        var mostlyLit = sampled.NotBlack > sampled.Points / 2;

        if (throughput > expected / 3.0 && mostlyLit)
        {
            Console.WriteLine("PASS: the desktop is coming through the overlay.");
            return;
        }

        Console.WriteLine("FAIL: the overlay is opaque -- the colour beneath did not come through.");
        Console.WriteLine();
        Console.WriteLine(@"next: run the app with BUBBLES_SNAP=1 and look at %APPDATA%\Bubbles\snap.png.");
        Console.WriteLine("  artifacts over a dim desktop  -> WPF is right; the DWM frame extension is gone");
        Console.WriteLine("  a black image                 -> a layer is stuck at the wrong opacity");
    }
}
