using System.Text.Json;

namespace Bubbles.Session;

/// <summary>Owns the one <see cref="Settings"/> instance the process has, and tells everybody
/// that cares when it changes.
///
/// There is deliberately no way to swap the instance. Settings are handed to components by
/// reference and read later -- <c>App</c> reads <c>LockAfterBlackout</c> from a closure when the
/// blackout ends, minutes after anything last touched the menu -- so replacing the object leaves
/// every one of those holders reading a copy that has stopped changing. That is exactly what the
/// old <c>TrayIcon.ReloadSettings</c> did, and after one reload the PIN prompt was decided by a
/// stale object for the rest of the run.
///
/// So: one instance, mutated in place, and a fan-out that runs afterwards.</summary>
public sealed class SettingsHost
{
    private readonly List<Action<Settings>> _listeners = new();

    public SettingsHost(Settings settings) => Current = settings;

    /// <summary>The one instance. Mutate it through <see cref="Edit"/>, never by assignment.</summary>
    public Settings Current { get; }

    /// <summary>Registers something that needs to hear about every change.</summary>
    public void Listen(Action<Settings> listener) => _listeners.Add(listener);

    /// <summary>Applies a change: mutate, clamp, then tell everyone.
    ///
    /// The clamp runs here rather than in each caller because <see cref="Settings.Clamped"/> is
    /// the only definition of what a legal value is, and a caller that forgot it would push a
    /// value past the bounds the rest of the app assumes.</summary>
    public void Edit(Action<Settings> change)
    {
        change(Current);
        Current.Clamped();
        foreach (var listener in _listeners) listener(Current);
    }

    /// <summary>A detached copy of the settings as they stand, for something that may need to
    /// put them back -- the settings window applies edits as they are made, so this is the only
    /// record of what "before" was.
    ///
    /// Taken through the same serializer that writes the file, so it captures exactly the
    /// properties that persist. A field marked <c>[JsonIgnore]</c> is computed from the others
    /// and would be wrong to carry across.</summary>
    public Settings Snapshot() =>
        JsonSerializer.Deserialize<Settings>(
            JsonSerializer.Serialize(Current, Settings.JsonOptions), Settings.JsonOptions)
        ?? new Settings();

    /// <summary>Puts a snapshot back, through the ordinary edit path so that everything hears
    /// about it exactly as it would for any other change.</summary>
    public void Restore(Settings snapshot) => Edit(current => snapshot.CopyTo(current));

    /// <summary>Saves the current settings to disk.</summary>
    public void Save() => Current.Save();
}
