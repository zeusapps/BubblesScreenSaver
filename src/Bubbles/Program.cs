using System.IO;

using Bubbles.Displays;
using Bubbles.Interop;
using Bubbles.Session;

namespace Bubbles;

internal static class Program
{
    /// <summary>Whether another Bubbles already holds the single-instance lock. Diagnostics
    /// ask so they can keep their hands off state that belongs to a live instance.</summary>
    private static bool AnotherInstanceIsRunning()
    {
        using var mutex = new Mutex(initiallyOwned: false, "Bubbles.Overlay.SingleInstance");

        try
        {
            if (!mutex.WaitOne(0)) return true;
        }
        catch (AbandonedMutexException)
        {
            // Held by something that died without releasing it; nothing is running now.
        }

        mutex.ReleaseMutex();
        return false;
    }

    [STAThread]
    private static void Main(string[] args)
    {
        // `--export <dir>` renders the artwork to PNGs and quits, so the visuals can be
        // reviewed without commandeering a screen.
        if (args.Length >= 2 && args[0] == "--export")
        {
            Export.Run(args[1]);
            return;
        }

        // `--check-update` runs one check and reports, without starting the overlay. Handy
        // for testing the update path, and for anyone who would rather drive it themselves.
        if (args.Length >= 1 && args[0] == "--check-update")
        {
            var settings = Settings.Load();
            var outcome = new Updater(settings).CheckAsync(manual: true).GetAwaiter().GetResult();
            Console.WriteLine(outcome ?? "no update available");
            return;
        }

        // `--dim-test` exercises the external-monitor backlight path: it reports what it
        // finds, dims for a couple of seconds, and puts everything back.
        if (args.Length >= 1 && args[0] == "--dim-test")
        {
            Console.WriteLine("displays:");
            DisplayInfo.Describe().ForEach(Console.WriteLine);
            Console.WriteLine();

            // A running instance owns display-state.json, and RecoverFromCrash claims it:
            // run this while the app is blacked out and the diagnostic restores the monitor to
            // full brightness and deletes the record the app is relying on, leaving the screen
            // lit for the rest of the blackout. So when something else is live, this works on
            // a scratch file and leaves the real bookkeeping alone.
            var live = AnotherInstanceIsRunning();

            var displays = live
                ? new MonitorBacklight(Path.Combine(Path.GetTempPath(), "bubbles-dim-test.json"))
                : new MonitorBacklight();

            Console.WriteLine($"DDC/CI capable monitor found: {displays.Available}");

            if (live)
            {
                Console.WriteLine();
                Console.WriteLine("another Bubbles is running: leaving its saved state alone.");
                Console.WriteLine("if it is mid-blackout, what you see below is not what it did.");
                Console.WriteLine();
            }
            else
            {
                displays.RecoverFromCrash();
            }

            Console.WriteLine("before:");
            displays.Read().ForEach(Console.WriteLine);

            displays.Dim(alsoStandby: false);
            Thread.Sleep(1200);

            Console.WriteLine("while dimmed:");
            displays.Read().ForEach(Console.WriteLine);

            displays.Restore();
            Thread.Sleep(400);

            Console.WriteLine("after restore:");
            displays.Read().ForEach(Console.WriteLine);
            return;
        }

        // `--audio` samples the output level for a few seconds. Silence must read as a flat
        // zero: an endpoint that idles at a trickle of noise would hold the screensaver off
        // permanently, which is the one failure this check must not have.
        if (args.Length >= 1 && args[0] == "--audio")
        {
            Console.WriteLine($"silence threshold: {SoundWatch.Silence}");

            for (var i = 0; i < 20; i++)
            {
                var peak = AudioActivity.Peak();
                Console.WriteLine(peak is { } level
                    ? $"  peak {level:F4}  {(level > SoundWatch.Silence ? "PLAYING" : "silent")}"
                    : "  no reading (no output device?)");
                Thread.Sleep(500);
            }

            return;
        }

        // `--inputs` reports which source each monitor is actually showing. Read-only: it sends
        // no writes at all, so it is safe to run against a live instance and safe to run while
        // another machine is driving the screen.
        if (args.Length >= 1 && args[0] == "--inputs")
        {
            Console.WriteLine("displays, as Windows sees them:");
            DisplayInfo.Describe().ForEach(Console.WriteLine);
            Console.WriteLine();
            Console.WriteLine("monitors, as they describe themselves over DDC/CI:");
            new MonitorBacklight(Path.Combine(Path.GetTempPath(), "bubbles-inputs.json"))
                .ReadInputs().ForEach(Console.WriteLine);
            return;
        }

        // `--hold-test` checks that a blackout survives a monitor putting its own backlight
        // back up, which is a thing monitors do: dim, shove the brightness back to maximum the
        // way the panel would, and see whether the hold notices and undoes it.
        if (args.Length >= 1 && args[0] == "--hold-test")
        {
            // Always a scratch record: this is a synthetic drift, and it must never end up in
            // the state file a running instance is relying on.
            var monitors = new MonitorBacklight(
                Path.Combine(Path.GetTempPath(), "bubbles-hold-test.json"));

            Console.WriteLine("before:");
            monitors.Read().ForEach(Console.WriteLine);

            monitors.Dim(alsoStandby: false);
            Thread.Sleep(600);
            Console.WriteLine("dimmed:");
            monitors.Read().ForEach(Console.WriteLine);

            monitors.SimulateExternalChange();
            Thread.Sleep(600);
            Console.WriteLine("after the monitor raises itself:");
            monitors.Read().ForEach(Console.WriteLine);

            var again = monitors.Reassert();
            Thread.Sleep(600);
            Console.WriteLine($"reassert put back {again.Count}: {string.Join(", ", again)}");
            monitors.Read().ForEach(Console.WriteLine);

            monitors.Restore();
            Thread.Sleep(400);
            Console.WriteLine("restored:");
            monitors.Read().ForEach(Console.WriteLine);
            return;
        }

        // `--hdr on|off` switches HDR on every display that supports it. Only here so the
        // blackout path can be set up and torn down when testing.
        if (args.Length >= 2 && args[0] == "--hdr")
        {
            var wanted = args[1].Equals("on", StringComparison.OrdinalIgnoreCase);
            Console.WriteLine("before:");
            DisplayInfo.Describe().ForEach(Console.WriteLine);

            foreach (var target in DisplayInfo.AllTargets())
                DisplayInfo.SetHdr(target, wanted);

            Thread.Sleep(2500);
            Console.WriteLine("after:");
            DisplayInfo.Describe().ForEach(Console.WriteLine);
            return;
        }

        // `--busy` reports whether anything is currently holding the overlay off.
        if (args.Length >= 1 && args[0] == "--busy")
        {
            var settings = Settings.Load();
            Console.WriteLine(UserBusy.DescribeForeground());
            Console.WriteLine();

            var reason = UserBusy.Reason(settings);
            Console.WriteLine(reason is null
                ? "nothing is holding the screensaver off right now"
                : $"holding off: {reason}");
            return;
        }

        // `--emission-demo` runs one Emission on demand, for previewing or testing.
        var demo = args.Length >= 1 && args[0] == "--emission-demo";
        App.EmissionDemo = demo;

        Updater.SweepOldBinaries();

        // One overlay is plenty. The wait matters: after an update the outgoing process is
        // still shutting down when its replacement starts, and an instant give-up meant the
        // app quietly failed to come back at all. A few seconds of patience costs a manual
        // second launch nothing -- it still exits, just a moment later.
        using (var singleInstance = new Mutex(initiallyOwned: false, "Bubbles.Overlay.SingleInstance"))
        {
            var acquired = false;

            try
            {
                // Generous, because the process this one is replacing may still be shutting
                // down. Giving up here is how an update ends with nothing running at all.
                acquired = singleInstance.WaitOne(TimeSpan.FromSeconds(20));
            }
            catch (AbandonedMutexException)
            {
                // The previous holder died without releasing it, which is ours to take.
                acquired = true;
            }

            if (!acquired)
            {
                // Worth recording: a silent exit here looks exactly like a crash from outside,
                // and that ambiguity cost real time to unpick.
                Diagnostics.Log("another instance holds the single-instance lock; exiting");
                return;
            }

            try
            {
                new App().Run();
            }
            finally
            {
                singleInstance.ReleaseMutex();
            }
        }
    }
}
