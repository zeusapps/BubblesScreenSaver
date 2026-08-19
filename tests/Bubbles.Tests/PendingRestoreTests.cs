using System.IO;

using Bubbles.Displays;

namespace Bubbles.Tests;

/// <summary>The rules that keep a display from being stranded.
///
/// Every one of these corresponds to something that actually went wrong on a real desk: a
/// monitor unplugged mid-blackout that came back at zero brightness and stayed there, and a
/// display that kept HDR switched off across a reboot because the record was cleared on a
/// restore that had reached nothing. The whole point of the extraction is that these can now
/// be provoked in a millisecond instead of by pulling a cable at the wrong moment.</summary>
public sealed class PendingRestoreTests : IDisposable
{
    private sealed record Entry(string Device, uint Brightness);

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bubbles-tests", Guid.NewGuid().ToString("n"));

    private string StateFile => Path.Combine(_directory, "state.json");

    private PendingRestore<Entry> New() =>
        new(StateFile, entry => entry.Device, "test");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
        catch
        {
        }
    }

    // ---- the stranded-monitor bug ------------------------------------------------------

    [Fact]
    public void An_entry_the_restore_could_not_reach_stays_owed()
    {
        var owed = New();
        owed.Remember([new Entry(@"\\.\DISPLAY1", 100)]);

        // The monitor is gone, so the restore reaches nothing and verifies nothing.
        var settled = owed.Settle(_ => Array.Empty<string>(), "restore");

        Assert.Equal(0, settled.Restored);
        Assert.False(settled.Complete);
        Assert.Equal([@"\\.\DISPLAY1"], settled.StillOwed);
        Assert.Equal(1, owed.Count);
    }

    [Fact]
    public void An_entry_the_restore_verified_is_forgotten()
    {
        var owed = New();
        owed.Remember([new Entry(@"\\.\DISPLAY1", 100)]);

        var settled = owed.Settle(entries => entries.Select(e => e.Device), "restore");

        Assert.Equal(1, settled.Restored);
        Assert.True(settled.Complete);
        Assert.Equal(0, owed.Count);
    }

    [Fact]
    public void A_partial_restore_keeps_only_what_it_missed()
    {
        var owed = New();
        owed.Remember([new Entry("A", 100), new Entry("B", 80), new Entry("C", 60)]);

        // Only B was attached.
        var settled = owed.Settle(_ => ["B"], "restore");

        Assert.Equal(1, settled.Restored);
        Assert.Equal(["A", "C"], settled.StillOwed);
        Assert.Equal(["A", "C"], owed.Owed.Select(e => e.Device));
    }

    // ---- the reconnected-at-zero bug ---------------------------------------------------

    [Fact]
    public void Re_recording_a_pending_key_keeps_the_first_original()
    {
        var owed = New();
        owed.Remember([new Entry("A", 100)]);

        // A second blackout begins while A is still owed. It now reads as dim, and writing
        // *that* down as the original is how a monitor gets stranded at zero permanently.
        var added = owed.Remember([new Entry("A", 0)]);

        Assert.Equal(0, added);
        Assert.Equal(100u, Assert.Single(owed.Owed).Brightness);
    }

    [Fact]
    public void Remember_reports_only_genuinely_new_entries()
    {
        var owed = New();
        owed.Remember([new Entry("A", 100)]);

        Assert.Equal(1, owed.Remember([new Entry("A", 50), new Entry("B", 70)]));
        Assert.Equal(2, owed.Count);
    }

    // ---- recorded ahead of a change that was refused ------------------------------------

    [Fact]
    public void Forget_drops_an_entry_without_claiming_it_was_restored()
    {
        var owed = New();
        owed.Remember([new Entry("A", 100), new Entry("B", 80)]);

        // B refused the change, so nothing about it moved and nothing is owed back.
        owed.Forget([new Entry("B", 80)]);

        Assert.Equal(["A"], owed.Owed.Select(e => e.Device));
    }

    // ---- surviving the process ----------------------------------------------------------

    [Fact]
    public void What_is_owed_survives_a_restart()
    {
        New().Remember([new Entry("A", 100), new Entry("B", 80)]);

        var reloaded = New();
        reloaded.Load();

        Assert.Equal(["A", "B"], reloaded.Owed.Select(e => e.Device));
        Assert.Equal([100u, 80u], reloaded.Owed.Select(e => e.Brightness));
    }

    [Fact]
    public void A_failed_restore_still_leaves_the_record_on_disk_for_the_next_run()
    {
        var owed = New();
        owed.Remember([new Entry("A", 100)]);
        owed.Settle(_ => Array.Empty<string>(), "restore");

        Assert.True(File.Exists(StateFile));

        var reloaded = New();
        reloaded.Load();
        Assert.Equal(100u, Assert.Single(reloaded.Owed).Brightness);
    }

    [Fact]
    public void The_record_is_removed_once_nothing_is_owed()
    {
        var owed = New();
        owed.Remember([new Entry("A", 100)]);
        Assert.True(File.Exists(StateFile));

        owed.Settle(_ => ["A"], "restore");

        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public void An_unreadable_record_is_discarded_rather_than_retried_forever()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StateFile, "{ this is not the file you are looking for");

        var owed = New();
        owed.Load();

        Assert.Equal(0, owed.Count);
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public void Loading_when_there_is_nothing_to_load_is_harmless()
    {
        var owed = New();
        owed.Load();

        Assert.Equal(0, owed.Count);
    }

    // ---- identity that outlives the thing identifying it -------------------------------

    [Fact]
    public void Entries_can_be_re_identified_after_a_reload()
    {
        // An adapter LUID is reissued at every boot, so a reloaded entry names something that
        // no longer resolves. Without a way to adopt the current identity it could never be
        // settled, and the display would stay changed for good.
        New().Remember([new Entry("stale-id", 100)]);

        var reloaded = New();
        reloaded.Load();
        reloaded.Remap(entry => entry with { Device = "current-id" });

        Assert.Equal("current-id", Assert.Single(reloaded.Owed).Device);
        Assert.Equal(100u, Assert.Single(reloaded.Owed).Brightness);
    }

    [Fact]
    public void A_re_identified_entry_settles_under_its_new_key()
    {
        var owed = New();
        owed.Remember([new Entry("stale-id", 100)]);
        owed.Remap(entry => entry with { Device = "current-id" });

        var settled = owed.Settle(_ => ["current-id"], "restore");

        Assert.Equal(1, settled.Restored);
        Assert.Equal(0, owed.Count);
    }

    [Fact]
    public void Re_identifying_survives_another_restart()
    {
        var owed = New();
        owed.Remember([new Entry("stale-id", 100)]);
        owed.Remap(entry => entry with { Device = "current-id" });

        var reloaded = New();
        reloaded.Load();

        Assert.Equal("current-id", Assert.Single(reloaded.Owed).Device);
    }

    // ---- counting -------------------------------------------------------------------------

    [Fact]
    public void The_restored_count_is_what_was_settled_not_the_change_in_length()
    {
        var owed = New();
        owed.Remember([new Entry("A", 100)]);

        // The restore callback runs outside the lock, and for HDR it provokes display changes
        // that come back through this same object. Deriving the count from list lengths would
        // under-report here, and can go negative.
        var settled = owed.Settle(entries =>
        {
            owed.Remember([new Entry("B", 80)]);
            return entries.Select(e => e.Device);
        }, "restore");

        Assert.Equal(1, settled.Restored);
    }

    [Fact]
    public void A_default_settlement_does_not_throw_when_asked_whether_it_is_complete()
    {
        Assert.True(default(Settlement).Complete);
    }

    // ---- degenerate cases ----------------------------------------------------------------

    [Fact]
    public void Settling_an_empty_record_does_not_call_the_restore()
    {
        var called = false;

        var settled = New().Settle(_ =>
        {
            called = true;
            return Array.Empty<string>();
        }, "restore");

        Assert.False(called);
        Assert.Equal(0, settled.Restored);
        Assert.True(settled.Complete);
    }

    [Fact]
    public void A_key_the_restore_invents_is_ignored()
    {
        var owed = New();
        owed.Remember([new Entry("A", 100)]);

        owed.Settle(_ => ["something else entirely"], "restore");

        Assert.Equal(1, owed.Count);
    }
}
