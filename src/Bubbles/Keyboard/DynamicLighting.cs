using Microsoft.Win32;

using Bubbles.Displays;

namespace Bubbles.Keyboard;

/// <summary>A note that Windows' Dynamic Lighting has been stood down and not yet put back.
///
/// Unlike a keyboard, this one carries a value. The registry says what Dynamic Lighting was
/// before it was touched, so there is something honest to restore *to* -- and there has to be,
/// because handing back a fixed "on" would switch the feature on for somebody who had
/// deliberately turned it off, which is worse than not giving it back at all.
///
/// A class with settable properties because <see cref="PendingRestore{T}"/> round-trips it
/// through System.Text.Json, which needs both.</summary>
internal sealed class DynamicLightingRecord
{
    /// <summary>Which setting this is. There is only ever one, but <see cref="PendingRestore{T}"/>
    /// identifies entries by key and the file is easier to read with a name in it.</summary>
    public string Key { get; set; } = DynamicLightingLoan.RecordKey;

    /// <summary>What Dynamic Lighting was before it was stood down.</summary>
    public bool Enabled { get; set; }
}

/// <summary>Windows' Dynamic Lighting toggle, as the keyboard layer needs to see it.
///
/// An interface because the thing behind it is a registry value in the user's own hive, and a
/// test that exercised it for real would change the machine running the test -- an actual
/// personalization setting, on somebody's actual desktop. Everything worth checking about the
/// borrow is above this line.</summary>
internal interface IAmbientLighting
{
    /// <summary>Whether Dynamic Lighting is on. Null means the question could not be put --
    /// nothing is borrowed on a null, because there would be nothing to give back.</summary>
    bool? Read();

    /// <summary>Sets it. False if the write did not happen, in which case nothing moved and
    /// nothing is owed.</summary>
    bool Write(bool enabled);
}

/// <summary>The Dynamic Lighting toggle where Windows actually keeps it.
///
/// There is no API for this. The Settings app writes one per-user DWORD and the lighting
/// service reads it; that is the whole interface, and it is undocumented. The cost of it moving
/// is bounded: the write silently does nothing and the feature degrades to what it does today,
/// which is what the corrected text already describes.</summary>
internal sealed class AmbientLighting : IAmbientLighting
{
    private const string KeyPath = @"Software\Microsoft\Lighting";
    private const string ValueName = "AmbientLightingEnabled";

    public bool? Read()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath);

            // Absent is not unknown. A machine that has never touched the toggle has Dynamic
            // Lighting running, because that is what Windows ships with -- so absent reads as
            // on, and is given back as on.
            return key?.GetValue(ValueName) is not int value || value != 0;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"dynamic lighting: could not be read ({ex.GetType().Name}: {ex.Message})");
            return null;
        }
    }

    public bool Write(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(KeyPath);

            if (key is null) return false;

            key.SetValue(ValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            return true;
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"dynamic lighting: could not be set to " +
                            $"{(enabled ? "on" : "off")} ({ex.GetType().Name}: {ex.Message})");
            return false;
        }
    }
}

/// <summary>Stands Dynamic Lighting down for the length of a keyboard loan, and puts it back.
///
/// This is the same shape as everything else this application borrows -- read the value, write
/// it down, change it, give back what was written down -- and it uses the same ledger, for the
/// same reason: the record reaches disk before the change it describes, so a run that dies
/// mid-Emission leaves behind the one thing needed to undo it.
///
/// Its own file rather than a corner of the keyboard's, because the two debts are settled
/// independently: a crash can leave Dynamic Lighting off with no keyboard owed, and a keyboard
/// owed with Dynamic Lighting untouched.
///
/// Nothing here consults a setting. Whether to take the loan at all is decided by the caller,
/// once, at the moment of taking it; giving it back never is, because a debt is not conditional
/// on the setting that incurred it still being on.</summary>
internal sealed class DynamicLightingLoan
{
    /// <summary>The one key in the ledger. Named for the registry value it stands for, so the
    /// file on disk says what it is about.</summary>
    internal const string RecordKey = "AmbientLightingEnabled";

    private readonly IAmbientLighting _lighting;
    private readonly PendingRestore<DynamicLightingRecord> _owed;

    public DynamicLightingLoan(string stateFile) : this(new AmbientLighting(), stateFile)
    {
    }

    /// <param name="lighting">How to reach the toggle. Replaced in tests, because the real one
    /// changes the machine the tests are running on.</param>
    internal DynamicLightingLoan(IAmbientLighting lighting, string stateFile)
    {
        _lighting = lighting;
        _owed = new PendingRestore<DynamicLightingRecord>(
            stateFile,
            record => record.Key,
            "dynamic lighting",
            record => record.Enabled ? "on" : "off");
    }

    /// <summary>Whether anything is on the books. For tests and for the log.</summary>
    internal int Owed => _owed.Count;

    /// <summary>Whatever a previous run left switched off. Safe when there is no file, which is
    /// the case on every machine that has never turned this on.</summary>
    public void Load() => _owed.Load();

    /// <summary>Stands Dynamic Lighting down, writing what it found before changing it.
    ///
    /// A machine already at "off" is recorded as "off" and written "off" anyway. That looks
    /// like a wasted write, and it is the point: the record is what makes the release correct,
    /// and a loan that recorded nothing when the value happened to match would hand back "on"
    /// to somebody who never had it.
    ///
    /// A second call while the loan is already out does nothing -- <see cref="PendingRestore{T}"/>
    /// keeps the first value by key, and the second reading would be of a value this already
    /// changed.</summary>
    public void Take()
    {
        if (_owed.Count > 0) return;

        if (_lighting.Read() is not { } found) return;

        var record = new DynamicLightingRecord { Key = RecordKey, Enabled = found };

        _owed.Remember([record]);

        // Recorded ahead of a change that was then refused: nothing moved, so nothing is owed.
        if (!_lighting.Write(false))
        {
            _owed.Forget([record]);
            return;
        }

        Diagnostics.Log($"dynamic lighting: stood down (was {(found ? "on" : "off")})");
    }

    /// <summary>Puts back whatever was found, and forgets it only once the write confirmed.
    /// Does nothing at all when nothing is owed, which is the ordinary case.</summary>
    public void Settle(string why) =>
        _owed.Settle(
            owed => owed.Where(record => _lighting.Write(record.Enabled))
                        .Select(record => record.Key)
                        .ToList(),
            why);
}
