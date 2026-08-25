using Bubbles.Session;

namespace Bubbles.Tests;

/// <summary>Starting with Windows, and being findable, with the machine taken out.
///
/// Both halves of this write somewhere real and permanent -- the user's own Run key and the
/// user's own Start Menu -- so the seam is not a nicety. A test suite that exercised the real
/// thing would register Bubbles to start at login on whatever machine ran it, and leave a
/// shortcut behind afterwards.</summary>
public sealed class StartupRegistrationTests
{
    private const string Exe = @"C:\Users\somebody\AppData\Local\Programs\Bubbles\Bubbles.exe";
    private const string Lnk = @"C:\Users\somebody\AppData\Roaming\...\Start Menu\Programs\Bubbles.lnk";

    private sealed class FakeRunKey(string? initial = null) : IRunKey
    {
        public string? Value { get; private set; } = initial;

        public void Set(string value) => Value = value;

        public void Delete() => Value = null;
    }

    /// <summary>A Start Menu that can be made to refuse, because a real one can: a locked-down
    /// profile, a roaming folder that is not there yet, a shell object that will not create.</summary>
    private sealed class FakeShortcuts(bool present = false, bool writable = true) : IShortcuts
    {
        private string? _target = present ? Exe : null;

        public int Writes { get; private set; }
        public int Removals { get; private set; }
        public string? Target => _target;

        public bool Exists(string path) => _target is not null;

        public void Write(string path, string target)
        {
            Writes++;
            if (!writable) throw new UnauthorizedAccessException("no");
            _target = target;
        }

        public void Remove(string path)
        {
            Removals++;
            if (!writable) throw new UnauthorizedAccessException("no");
            _target = null;
        }
    }

    private static StartupRegistration Registration(FakeRunKey run, FakeShortcuts shortcuts) =>
        new(run, shortcuts, () => Exe, Lnk);

    // ---- the two halves are written together ----------------------------------------------

    [Fact]
    public void TurningStartupOnWritesBothHalves()
    {
        var run = new FakeRunKey();
        var shortcuts = new FakeShortcuts();

        Registration(run, shortcuts).Set(true);

        Assert.Contains("Bubbles.exe", run.Value);
        Assert.Equal(Exe, shortcuts.Target);
    }

    [Fact]
    public void TurningStartupOffRemovesBothHalves()
    {
        var run = new FakeRunKey($"\"{Exe}\"");
        var shortcuts = new FakeShortcuts(present: true);

        Registration(run, shortcuts).Set(false);

        Assert.Null(run.Value);
        Assert.Null(shortcuts.Target);
    }

    // ---- the Run value is the authority ---------------------------------------------------

    /// <summary>The tray tick reflects the operating system, and the operating system acts on
    /// the Run value. A shortcut somebody deleted by hand must not make the menu claim the
    /// application no longer starts with Windows, because it still does.</summary>
    [Fact]
    public void AMissingShortcutDoesNotMakeStartupLookDisabled()
    {
        var run = new FakeRunKey($"\"{Exe}\"");

        Assert.True(Registration(run, new FakeShortcuts(present: false)).IsEnabled);
    }

    [Fact]
    public void NothingRegisteredReadsAsDisabled()
    {
        Assert.False(Registration(new FakeRunKey(), new FakeShortcuts()).IsEnabled);
    }

    // ---- reconciling an installation that predates the entry --------------------------------

    [Fact]
    public void AnInstallationAlreadyRegisteredGetsTheEntryItNeverHad()
    {
        var shortcuts = new FakeShortcuts(present: false);

        Registration(new FakeRunKey($"\"{Exe}\""), shortcuts).Reconcile();

        Assert.Equal(Exe, shortcuts.Target);
    }

    [Fact]
    public void ReconcilingTwiceChangesNothingTheSecondTime()
    {
        var shortcuts = new FakeShortcuts(present: false);
        var registration = Registration(new FakeRunKey($"\"{Exe}\""), shortcuts);

        registration.Reconcile();
        registration.Reconcile();
        registration.Reconcile();

        Assert.Equal(1, shortcuts.Writes);
    }

    /// <summary>Reconciling hands out what was missed. It does not decide that somebody who
    /// turned startup off should have a Start Menu entry anyway.</summary>
    [Fact]
    public void AnInstallationNotRegisteredGetsNothing()
    {
        var shortcuts = new FakeShortcuts(present: false);

        Registration(new FakeRunKey(), shortcuts).Reconcile();

        Assert.Equal(0, shortcuts.Writes);
        Assert.Null(shortcuts.Target);
    }

    // ---- the entry is the half that is allowed to fail ---------------------------------------

    [Fact]
    public void AShortcutThatCannotBeWrittenStillLeavesStartupRegistered()
    {
        var run = new FakeRunKey();
        var shortcuts = new FakeShortcuts(writable: false);
        var registration = Registration(run, shortcuts);

        registration.Set(true);

        Assert.Contains("Bubbles.exe", run.Value);
        Assert.True(registration.IsEnabled);
        Assert.Null(shortcuts.Target);
    }

    [Fact]
    public void AShortcutThatCannotBeRemovedStillLeavesStartupUnregistered()
    {
        var run = new FakeRunKey($"\"{Exe}\"");
        var registration = Registration(run, new FakeShortcuts(present: true, writable: false));

        registration.Set(false);

        Assert.Null(run.Value);
        Assert.False(registration.IsEnabled);
    }

    [Fact]
    public void AReconcileThatCannotWriteThrowsNothing()
    {
        var registration = Registration(
            new FakeRunKey($"\"{Exe}\""), new FakeShortcuts(writable: false));

        registration.Reconcile();

        Assert.True(registration.IsEnabled);
    }
}
