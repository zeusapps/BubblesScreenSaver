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
            var displays = new ExternalDisplays();
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

        // The mutex is scoped tightly: a relaunch after an update must happen once it has been
        // released, or the new process finds the old one still holding it and exits.
        using (var singleInstance = new Mutex(initiallyOwned: true, "Bubbles.Overlay.SingleInstance", out var isFirst))
        {
            if (!isFirst) return;

            new App().Run();
        }

        Updater.RelaunchIfSwapped();
    }
}
