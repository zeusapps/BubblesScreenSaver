namespace Bubbles.Keyboard;

/// <summary>A note that a keyboard has been borrowed and not yet given back.
///
/// Written to disk before the first colour is sent, so a process that dies mid-Emission leaves
/// a record behind. It names the device but not a colour: the Aura protocol only listens, so
/// there is no way to ask a keyboard what it was showing before, and a value invented here
/// would be a lie stored durably. Giving it back means letting go of it -- see
/// <see cref="AuraKeyboard.Restore"/>.
///
/// A class with settable properties because <see cref="Displays.PendingRestore{T}"/>
/// round-trips it through System.Text.Json, which needs both.</summary>
internal sealed class KeyboardRecord
{
    /// <summary>Which device this is. Stable across a restart, unlike anything positional.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>How the keyboard is named in the log, so a line about it means something to
    /// somebody reading it.</summary>
    public string Name { get; set; } = string.Empty;
}

/// <summary>A keyboard, as the lighting layer needs to see one.
///
/// An interface because everything below it is Win32 talking to hardware most machines do not
/// have, and everything above it -- when to open, what to send, what is owed back -- is logic
/// that should not need a keyboard to be exercised.</summary>
internal interface IKeyboardDevice : IDisposable
{
    /// <summary>Finds a keyboard and takes hold of it. Returns null if there is no keyboard
    /// attached, or none this knows how to talk to.
    ///
    /// May be called again on a device that has let go, which is the ordinary way a second
    /// Emission gets a keyboard back after the first handed it in.</summary>
    KeyboardRecord? Open();

    /// <summary>Whether the device is in hand right now.
    ///
    /// Asked rather than remembered, because this changes without anyone above being told: a
    /// hand-back, a refused write and an error all let go of the device. A caller that cached
    /// the answer from <see cref="Open"/> would go on writing to a handle that has been closed,
    /// and every one of those writes fails in silence.</summary>
    bool IsOpen { get; }

    /// <summary>Sets the whole keyboard to one colour.</summary>
    bool Show(KeyColor colour);

    /// <summary>Takes the lighting off.</summary>
    bool GoDark();

    /// <summary>Gives the keyboard back. False leaves the debt standing, to be retried on the
    /// next path that ends an Emission.
    ///
    /// Giving the keyboard back is letting go of it, so a device that has been restored is no
    /// longer open and must be opened again before anything else is sent to it.</summary>
    bool Restore(KeyboardRecord record);
}
