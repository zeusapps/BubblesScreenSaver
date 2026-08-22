using System.Windows.Threading;

namespace Bubbles.Tests;

/// <summary>Runs a test body on the STA thread.
///
/// Constructing a FrameworkElement builds WPF's input manager, which refuses to exist on an MTA
/// thread, and xunit's threads are MTA. Nothing here puts anything on screen -- the layers under
/// test are asked what they would draw, not asked to draw it -- but they are still
/// FrameworkElements and still need the apartment.
///
/// One thread for the whole run, not one per test. A WPF object belongs to the thread that made
/// it, and the weather brushes are shared statics that cannot be frozen because their scroll
/// transforms are animated -- so the first test to touch them owns them, and on a fresh thread
/// every later test throws. The lightning's brushes are frozen and would have been fine either
/// way, which is exactly why this was invisible until weather arrived.</summary>
internal static class Sta
{
    private static readonly Lazy<Dispatcher> Thread = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void Run(Action body) => Thread.Value.Invoke(body);

    private static Dispatcher Start()
    {
        var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;

        var thread = new System.Threading.Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            // Background, so a test run is never held open by a dispatcher nobody is using.
            IsBackground = true,
            Name = "Bubbles.Tests STA",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        return dispatcher!;
    }
}
