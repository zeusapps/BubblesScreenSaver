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

        // One overlay is plenty; a second launch just quietly bows out.
        using var singleInstance = new Mutex(initiallyOwned: true, "Bubbles.Overlay.SingleInstance", out var isFirst);
        if (!isFirst) return;

        new App().Run();
    }
}
