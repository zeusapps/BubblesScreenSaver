namespace Bubbles;

internal static class Program
{
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

            var displays = new MonitorBacklight();
            Console.WriteLine($"DDC/CI capable monitor found: {displays.Available}");

            displays.RecoverFromCrash();

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
