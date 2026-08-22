using System.Windows;

using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>The field's half of the census: the counts the weather colours itself by.
///
/// The property under test is that the running count is always the count -- it is maintained
/// as skins are assigned rather than taken when asked, precisely so that nothing walks the
/// field on the render path, and a running total that drifts is worse than no total at all.
/// </summary>
public sealed class FieldCensusTests
{
    private static readonly Rect Screen = new(0, 0, 1920, 1080);

    private static BubbleField Field(Settings? settings = null)
    {
        var field = new BubbleField((settings ?? new Settings()).Clamped());
        field.SkinCount = Artifacts.Count;
        field.Resize(new Size(Screen.Width, Screen.Height));
        field.SetRegions([Screen]);
        return field;
    }

    /// <summary>What the counts would be if something did walk the field.</summary>
    private static int[] ByHand(BubbleField field)
    {
        var counts = new int[Enum.GetValues<Anomaly>().Length];
        foreach (var b in field.Bubbles) counts[(int)AnomalyTint.FamilyOf(b.Skin)]++;
        return counts;
    }

    private static void AssertTrue(BubbleField field) =>
        Assert.Equal(ByHand(field), field.FamilyCounts.ToArray());

    [Fact]
    public void The_counts_add_up_to_the_field()
    {
        var field = Field();

        Assert.Equal(field.Bubbles.Count, field.FamilyCounts.Sum());
        AssertTrue(field);
    }

    [Fact]
    public void They_survive_the_population_growing_and_shrinking()
    {
        var field = Field(new Settings { BubbleCount = 40 });
        AssertTrue(field);

        field.Apply(new Settings { BubbleCount = 8 }.Clamped());
        AssertTrue(field);

        field.Apply(new Settings { BubbleCount = 60 }.Clamped());
        AssertTrue(field);
    }

    [Fact]
    public void They_survive_a_monitor_arriving()
    {
        var field = Field();
        field.SetRegions([Screen, new Rect(1920, 0, 2560, 1440)]);

        AssertTrue(field);
        Assert.Equal(field.Bubbles.Count, field.FamilyCounts.Sum());
    }

    [Fact]
    public void They_survive_a_run_of_collections()
    {
        var field = Field();
        field.CollectPoint = new Point(Screen.Width / 2, Screen.Height / 2);

        // Long enough for the detector to work through a good many artifacts, each of which
        // takes one family out of the census and puts another in.
        for (var i = 0; i < 4000; i++) field.Update(1.0 / 60);

        Assert.True(field.Collected > 0, "nothing was collected, so this asserts nothing");
        AssertTrue(field);
        Assert.Equal(field.Bubbles.Count, field.FamilyCounts.Sum());
    }

    [Fact]
    public void A_collection_says_which_family_it_was()
    {
        var field = Field();
        field.CollectPoint = new Point(Screen.Width / 2, Screen.Height / 2);

        var heard = new List<Anomaly>();
        field.ArtifactCollected += heard.Add;

        for (var i = 0; i < 2000; i++) field.Update(1.0 / 60);

        Assert.Equal(field.Collected, heard.Count);
        Assert.All(heard, family => Assert.Contains(family, Enum.GetValues<Anomaly>()));
    }

    [Fact]
    public void A_quiet_frame_takes_no_census()
    {
        // The whole point of a running count: a frame in which nothing is collected and
        // nothing respawns leaves the census exactly where it was, without anything having
        // been counted to find that out.
        var field = Field();
        field.CollectPoint = null;

        var before = field.FamilyCounts.ToArray();
        for (var i = 0; i < 600; i++) field.Update(1.0 / 60);

        Assert.Equal(before, field.FamilyCounts.ToArray());
    }
}
