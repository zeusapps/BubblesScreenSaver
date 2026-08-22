using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>Which family colours the weather, and how reluctantly that changes.
///
/// Asserted against the decision itself rather than against anything on screen. Every property
/// worth having here is a property of the sequence of counts -- that a slim lead does not flip
/// the sky, that a field oscillating by one artifact never flaps -- and none of them is
/// observable by watching a tint for twenty minutes.</summary>
public sealed class FamilyCensusTests
{
    /// <summary>Counts indexed by <see cref="Anomaly"/>, in declaration order.</summary>
    private static int[] Counts(int chemical, int electrical, int thermic, int gravitational) =>
        [chemical, electrical, thermic, gravitational];

    /// <summary>A census with its opening dwell already served, so a test that is not about
    /// the dwell does not have to wait one out.</summary>
    private static FamilyCensus Ready()
    {
        var census = new FamilyCensus();
        census.Tick(FamilyCensus.Dwell);
        return census;
    }

    [Fact]
    public void A_clear_lead_takes_over()
    {
        var census = Ready();

        Assert.True(census.Take(Counts(0, 0, 9, 0)));
        Assert.Equal(Anomaly.Thermic, census.Dominant);
    }

    [Fact]
    public void A_one_artifact_lead_is_not_enough()
    {
        var census = Ready();
        census.Take(Counts(0, 0, 9, 0));
        census.Tick(FamilyCensus.Dwell);

        // Gravitational is ahead, but by less than the margin. Two families sit this close
        // most of the time, and one collection should not repaint the sky.
        Assert.False(census.Take(Counts(0, 0, 8, 9)));
        Assert.Equal(Anomaly.Thermic, census.Dominant);
    }

    [Fact]
    public void An_empty_field_changes_nothing()
    {
        var census = Ready();
        census.Take(Counts(0, 0, 9, 0));
        census.Tick(FamilyCensus.Dwell);

        Assert.False(census.Take(Counts(0, 0, 0, 0)));
        Assert.Equal(Anomaly.Thermic, census.Dominant);
    }

    [Fact]
    public void A_tie_changes_nothing()
    {
        var census = Ready();
        census.Take(Counts(0, 0, 9, 0));
        census.Tick(FamilyCensus.Dwell);

        Assert.False(census.Take(Counts(9, 0, 9, 0)));
        Assert.Equal(Anomaly.Thermic, census.Dominant);
    }

    [Fact]
    public void A_tint_holds_for_its_dwell()
    {
        var census = Ready();
        census.Take(Counts(0, 0, 9, 0));

        // A landslide the other way, immediately. The counts say Chemical; the dwell says not
        // yet.
        Assert.False(census.Take(Counts(20, 0, 0, 0)));
        Assert.Equal(Anomaly.Thermic, census.Dominant);

        census.Tick(FamilyCensus.Dwell);

        Assert.True(census.Take(Counts(20, 0, 0, 0)));
        Assert.Equal(Anomaly.Chemical, census.Dominant);
    }

    [Fact]
    public void A_field_oscillating_by_one_never_flaps()
    {
        // Two families a hair apart, swapping the lead every collection, for an hour of
        // ticks. This is the case the hysteresis exists for: without the margin the sky would
        // change on every one of these.
        var census = Ready();
        census.Take(Counts(0, 0, 9, 0));

        var settled = census.Dominant;
        var changes = 0;

        for (var i = 0; i < 2000; i++)
        {
            census.Tick(1.0);

            var counts = i % 2 == 0
                ? Counts(0, 0, 9, 8)
                : Counts(0, 0, 8, 9);

            if (census.Take(counts)) changes++;
        }

        Assert.Equal(0, changes);
        Assert.Equal(settled, census.Dominant);
    }

    [Fact]
    public void Collections_cannot_walk_the_tint_through_the_families()
    {
        // A run of collections handing the lead round the four families as fast as the
        // detector's cooldown allows. The dwell is what stops the sky becoming a carousel.
        var census = Ready();
        var families = Enum.GetValues<Anomaly>();
        var changes = 0;

        for (var i = 0; i < 60; i++)
        {
            census.Tick(1.6);

            var counts = new int[families.Length];
            counts[i % families.Length] = 20;

            if (census.Take(counts)) changes++;
        }

        // 60 collections is 96 seconds. At a 25s dwell that is at most four changes, not sixty.
        Assert.True(changes <= (int)(60 * 1.6 / FamilyCensus.Dwell) + 1,
            $"the tint changed {changes} times in 96 seconds");
    }

    [Fact]
    public void It_starts_on_a_family_rather_than_on_nothing()
    {
        // There is always some weather and it always has a colour. A census that began with no
        // answer would leave the first minute of every run untinted.
        Assert.Contains(new FamilyCensus().Dominant, Enum.GetValues<Anomaly>());
    }
}
