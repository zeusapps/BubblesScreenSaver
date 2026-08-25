using Bubbles.Session;

namespace Bubbles.Tests;

/// <summary>The settings window's startup control, which is the one control in that window
/// whose value does not live in `settings.json`.
///
/// That makes it the one control the window's own promises were not written for. The snapshot
/// taken on opening cannot hold it, so Cancel has to put it back by itself; and the defaults
/// action must not reach it at all. Both rules are asserted here, because neither is visible
/// from the window's other machinery.</summary>
public sealed class StartupControlTests
{
    /// <summary>The machine, as the control sees it. Counts its writes, because "wrote nothing"
    /// is the assertion that matters most and the end state cannot show it.</summary>
    private sealed class FakeMachine(bool enabled)
    {
        public bool Enabled { get; private set; } = enabled;

        public int Writes { get; private set; }

        public void Set(bool on)
        {
            Writes++;
            Enabled = on;
        }
    }

    private static (StartupControl, FakeMachine) Open(bool enabled)
    {
        var machine = new FakeMachine(enabled);
        return (new StartupControl(() => machine.Enabled, machine.Set), machine);
    }

    // ---- what the control does ------------------------------------------------------------

    [Fact]
    public void TurningItOnRegisters()
    {
        var (control, machine) = Open(enabled: false);

        control.Toggle(true);

        Assert.True(machine.Enabled);
        Assert.True(control.Current);
    }

    [Fact]
    public void TurningItOffUnregisters()
    {
        var (control, machine) = Open(enabled: true);

        control.Toggle(false);

        Assert.False(machine.Enabled);
        Assert.False(control.Current);
    }

    /// <summary>Read every time rather than cached. Startup can be turned off from Task Manager
    /// while the window sits open, and a control claiming otherwise is the exact failure the
    /// tray entry's rule was written to prevent.</summary>
    [Fact]
    public void ItReadsTheMachineRatherThanRememberingIt()
    {
        var (control, machine) = Open(enabled: true);

        machine.Set(false);

        Assert.False(control.Current);
    }

    // ---- Cancel ---------------------------------------------------------------------------

    [Fact]
    public void CancellingAfterTurningItOnPutsItBackOff()
    {
        var (control, machine) = Open(enabled: false);

        control.Toggle(true);

        Assert.True(control.Cancel());
        Assert.False(machine.Enabled);
    }

    [Fact]
    public void CancellingAfterTurningItOffPutsItBackOn()
    {
        var (control, machine) = Open(enabled: true);

        control.Toggle(false);

        Assert.True(control.Cancel());
        Assert.True(machine.Enabled);
    }

    /// <summary>The end state is identical whether or not this writes, so the write is what has
    /// to be asserted. Cancelling a window in which the box was never touched must not put a
    /// value in somebody's registry and a shortcut in their Start Menu on its way out.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellingWithoutTouchingItWritesNothing(bool enabled)
    {
        var (control, machine) = Open(enabled);

        Assert.False(control.Cancel());
        Assert.Equal(0, machine.Writes);
        Assert.Equal(enabled, machine.Enabled);
    }

    /// <summary>Toggled twice back to where it started is not a change to undo either.</summary>
    [Fact]
    public void CancellingAfterTogglingBackWritesNothingMore()
    {
        var (control, machine) = Open(enabled: true);

        control.Toggle(false);
        control.Toggle(true);

        Assert.False(control.Cancel());
        Assert.Equal(2, machine.Writes);
        Assert.True(machine.Enabled);
    }

    // ---- restoring defaults ----------------------------------------------------------------

    /// <summary>Restoring defaults resets what the screensaver looks like, and startup is not
    /// one of the screensaver's defaults -- it is a property of the installation, and the action
    /// is reached by somebody who dislikes what is on screen.
    ///
    /// The guarantee is structural rather than a branch somebody has to remember: the defaults
    /// path copies a fresh <see cref="Settings"/> over the current one, and startup is not in
    /// Settings at all. If it ever gains a key there, this fails.</summary>
    [Fact]
    public void RestoringDefaultsCannotReachStartup()
    {
        var keys = typeof(Settings).GetProperties().Select(p => p.Name).ToList();

        Assert.DoesNotContain(keys, name => name.Contains("Startup", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(keys, name => name.Contains("StartWithWindows", StringComparison.OrdinalIgnoreCase));
    }
}
