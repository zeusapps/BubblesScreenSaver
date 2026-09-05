namespace Bubbles.Interop;

/// <summary>The moments when something may have changed underneath this application.
///
/// Nothing here knows what happened to the hardware, only that the machine passed through one of
/// the transitions on which other software tends to reassert itself: the session locking or
/// unlocking, the power source changing or a resume completing, the displays being reconfigured.
///
/// It exists because the keyboard cannot be read. There is no way to ask an Aura keyboard what
/// colour it is showing, so a lighting layer that wants to keep the keys black cannot wait to be
/// told it has been repainted -- it writes constantly, on a flat interval, and that is what
/// actually keeps the keys dark.
///
/// This does not change that interval. It only says when not to wait out the one in hand: these
/// are the moments other software tends to reassert itself, so they are the moments worth writing
/// at immediately rather than up to an interval late. A small saving, and the honest size of the
/// claim -- an earlier version of this comment offered these as an alternative to writing
/// constantly, which they never were.
///
/// A single subscription for the life of the process, taken once. <c>SystemEvents</c> raises on
/// its own thread, so anything hung off this must be safe to call from one.</summary>
internal static class MachineEvents
{
    private static bool _watching;

    /// <summary>Something happened that may have disturbed the hardware. The argument says what,
    /// for the log; nothing behaves differently on it.</summary>
    public static event Action<string>? Disturbed;

    /// <summary>Starts listening. Safe to call more than once; only the first does anything.</summary>
    public static void Watch()
    {
        if (_watching) return;
        _watching = true;

        Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) => Raise($"the session ({e.Reason})");
        Microsoft.Win32.SystemEvents.PowerModeChanged += (_, e) => Raise($"power ({e.Mode})");
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += (_, _) => Raise("the displays");
    }

    /// <summary>Raised with a subscriber's failure contained. This runs on the SystemEvents
    /// thread, which the framework shares with everything else listening; an exception escaping
    /// here would take that thread down for the whole process.</summary>
    private static void Raise(string what)
    {
        try
        {
            Disturbed?.Invoke(what);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"machine events: {what} subscriber threw: {ex.Message}");
        }
    }
}
