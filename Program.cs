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
                acquired = singleInstance.WaitOne(TimeSpan.FromSeconds(8));
            }
            catch (AbandonedMutexException)
            {
                // The previous holder died without releasing it, which is ours to take.
                acquired = true;
            }

            if (!acquired) return;

            try
            {
                new App().Run();
            }
            finally
            {
                singleInstance.ReleaseMutex();
            }
        }

        Updater.RelaunchIfSwapped();
    }
}
