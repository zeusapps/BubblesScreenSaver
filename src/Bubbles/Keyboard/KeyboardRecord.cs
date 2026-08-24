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
    /// attached, or none this knows how to talk to.</summary>
    KeyboardRecord? Open();

    /// <summary>Sets the whole keyboard to one colour.</summary>
    bool Show(KeyColor colour);

    /// <summary>Takes the lighting off.</summary>
    bool GoDark();

    /// <summary>Gives the keyboard back. False leaves the debt standing, to be retried on the
    /// next path that ends an Emission.</summary>
    bool Restore(KeyboardRecord record);
}
