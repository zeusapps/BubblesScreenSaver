using Bubbles.Zone;

namespace Bubbles.Tests;

/// <summary>"Spawn them randomly from the remaining list" -- the property that stops four
/// Slugs sitting on screen at once, which independent rolls guarantee at twenty-two artifacts
/// drawn from sixteen kinds.</summary>
public sealed class ShuffledDeckTests
{
    [Fact]
    public void Every_kind_comes_up_once_before_any_kind_repeats()
    {
        var deck = new ShuffledDeck(new Random(1));

        var dealt = Enumerable.Range(0, 16).Select(_ => deck.Next(16)).ToList();

        Assert.Equal(Enumerable.Range(0, 16), dealt.OrderBy(i => i));
    }

    [Fact]
    public void The_deck_reshuffles_and_keeps_dealing_past_its_size()
    {
        var deck = new ShuffledDeck(new Random(2));

        var dealt = Enumerable.Range(0, 48).Select(_ => deck.Next(16)).ToList();

        // Three complete passes: every kind exactly three times, none four.
        Assert.All(dealt.GroupBy(i => i), group => Assert.Equal(3, group.Count()));
    }

    [Fact]
    public void The_order_is_not_simply_the_catalogue_order()
    {
        var deck = new ShuffledDeck(new Random(3));

        var dealt = Enumerable.Range(0, 16).Select(_ => deck.Next(16)).ToList();

        Assert.NotEqual(Enumerable.Range(0, 16), dealt);
    }

    [Fact]
    public void Changing_the_number_of_kinds_starts_a_fresh_deck()
    {
        var deck = new ShuffledDeck(new Random(4));

        deck.Next(16);
        deck.Next(16);

        // Switching theme changes the catalogue underneath it. Nothing from the old deck may
        // survive, or the Soap theme deals indexes that only exist in the Zone one.
        var dealt = Enumerable.Range(0, 4).Select(_ => deck.Next(4)).ToList();

        Assert.Equal(Enumerable.Range(0, 4), dealt.OrderBy(i => i));
    }

    [Fact]
    public void Everything_dealt_is_a_valid_index()
    {
        var deck = new ShuffledDeck(new Random(5));

        Assert.All(Enumerable.Range(0, 200).Select(_ => deck.Next(7)),
            card => Assert.InRange(card, 0, 6));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void An_empty_catalogue_deals_a_usable_index_rather_than_throwing(int size)
    {
        // SkinCount is whatever the theme reports; a zero would otherwise divide by nothing.
        Assert.Equal(0, new ShuffledDeck(new Random(6)).Next(size));
    }
}
