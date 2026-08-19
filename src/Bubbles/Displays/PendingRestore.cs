using System.IO;
using System.Text.Json;

namespace Bubbles.Displays;

/// <summary>The outcome of one attempt to give hardware back what it is owed.</summary>
/// <param name="Restored">How many entries were verifiably put back and can be forgotten.</param>
/// <param name="StillOwed">Keys of everything that could not be reached, in the order they
/// were recorded.</param>
public readonly record struct Settlement(int Restored, IReadOnlyList<string> StillOwed)
{
    public bool Complete => StillOwed.Count == 0;
}

/// <summary>A set of original values owed back to hardware that may not be there to receive
/// them, and which must survive both a disconnection and the process dying.
///
/// This exists because getting it wrong is silent and nasty. A monitor unplugged while its
/// backlight is down comes back dark, forever, with nothing left to say what it should have
/// been -- and the same is true of a display whose HDR was switched off, because Windows
/// persists that setting per display. Both went wrong in exactly the same way, separately,
/// which is the argument for one implementation instead of two.
///
/// The rules that make it safe:
///
/// An entry is only forgotten once the change has been *verified* applied. Not attempted --
/// verified. A restore that quietly found nothing to restore used to clear the record, which
/// is precisely how a monitor ends up stranded at zero.
///
/// Re-recording an already-owed key keeps the *first* original. A monitor that reconnects at
/// zero brightness must not have zero written down as the value to go back to.
///
/// Everything is written to disk before the change it describes, so a session that ends badly
/// is undone on the next launch. A state file that cannot be parsed is deleted rather than
/// retried, since it would otherwise fail on every launch forever with nothing to gain.</summary>
public sealed class PendingRestore<T> where T : class
{
    private readonly string _stateFile;
    private readonly Func<T, string> _key;
    private readonly Func<T, string> _describe;
    private readonly string _what;
    private readonly object _gate = new();

    private List<T> _owed = new();

    /// <param name="stateFile">Where the record survives a crash.</param>
    /// <param name="key">A stable identity for an entry. Must not depend on enumeration order,
    /// or unplugging one display will hand another display's value back to the wrong one.</param>
    /// <param name="what">Named in the log lines, e.g. "backlight".</param>
    /// <param name="describe">How an entry is named in the log, when the key itself is not
    /// something a person would recognise. Defaults to the key.</param>
    public PendingRestore(string stateFile, Func<T, string> key, string what, Func<T, string>? describe = null)
    {
        _stateFile = stateFile;
        _key = key;
        _what = what;
        _describe = describe ?? key;
    }

    /// <summary>Everything still owed, oldest first.</summary>
    public IReadOnlyList<T> Owed
    {
        get { lock (_gate) return _owed.ToList(); }
    }

    public int Count
    {
        get { lock (_gate) return _owed.Count; }
    }

    /// <summary>Records originals for anything not already owed. Returns how many entries were
    /// genuinely new, so a caller can tell "nothing to do" from "already pending".</summary>
    public int Remember(IEnumerable<T> originals)
    {
        var added = 0;

        lock (_gate)
        {
            foreach (var entry in originals)
            {
                // First value wins. A later reading of the same display is taken while it is
                // already changed, so trusting it would record the dimmed value as the original.
                if (_owed.Any(existing => _key(existing) == _key(entry))) continue;

                _owed.Add(entry);
                added++;
            }

            Persist(_owed);
        }

        return added;
    }

    /// <summary>Drops entries that turned out not to be owed after all: recorded ahead of a
    /// change that was then refused. This is not a restore -- nothing was put back, because
    /// nothing had moved -- so it says nothing in the log.</summary>
    public void Forget(IEnumerable<T> entries)
    {
        var drop = entries.Select(_key).ToHashSet();

        lock (_gate)
        {
            _owed = _owed.Where(entry => !drop.Contains(_key(entry))).ToList();
            Persist(_owed);
        }
    }

    /// <summary>Hands the owed entries to <paramref name="restore"/>, which returns the keys it
    /// verified. Those are forgotten; everything else stays owed and is retried later.</summary>
    public Settlement Settle(Func<IReadOnlyList<T>, IEnumerable<string>> restore, string why)
    {
        List<T> owed;
        lock (_gate)
        {
            if (_owed.Count == 0) return new Settlement(0, Array.Empty<string>());
            owed = _owed.ToList();
        }

        var done = restore(owed).ToHashSet();

        List<T> left;
        lock (_gate)
        {
            _owed = _owed.Where(entry => !done.Contains(_key(entry))).ToList();
            left = _owed;
            Persist(left);
        }

        var restored = owed.Count - left.Count;

        if (restored > 0) Diagnostics.Log($"{_what}: restored {restored} ({why})");
        if (left.Count > 0)
            Diagnostics.Log($"{_what}: still owed to {left.Count} not attached: " +
                            string.Join(", ", left.Select(_describe)));

        return new Settlement(restored, left.Select(_key).ToList());
    }

    /// <summary>Reads back anything a previous run left behind. Safe to call when there is no
    /// file, and safe to call when the file is rubbish.</summary>
    public void Load()
    {
        try
        {
            if (!File.Exists(_stateFile)) return;

            var saved = JsonSerializer.Deserialize<List<T>>(File.ReadAllText(_stateFile));

            if (saved is { Count: > 0 })
            {
                lock (_gate) _owed = saved;
                Diagnostics.Log($"{_what}: {saved.Count} left owed by a previous run");
            }
            else
            {
                Delete();
            }
        }
        catch (Exception ex)
        {
            // Unreadable is worse than absent: it would be retried, and fail, on every launch.
            Diagnostics.Log($"{_what}: discarding an unreadable record: {ex.Message}");
            Delete();
        }
    }

    private void Persist(List<T> entries)
    {
        try
        {
            if (entries.Count == 0)
            {
                Delete();
                return;
            }

            var directory = Path.GetDirectoryName(_stateFile);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllText(_stateFile, JsonSerializer.Serialize(entries));
        }
        catch
        {
            // Crash recovery is a nicety; the restore in this session still works without it.
        }
    }

    private void Delete()
    {
        try
        {
            if (File.Exists(_stateFile)) File.Delete(_stateFile);
        }
        catch
        {
        }
    }
}
