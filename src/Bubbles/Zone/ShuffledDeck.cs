namespace Bubbles.Zone;

/// <summary>Deals indexes from a shuffled deck rather than rolling a die.
///
/// Independent rolls put obvious duplicates on screen constantly -- with sixteen artifact kinds
/// and twenty-two artifacts on screen, a collision is a certainty rather than bad luck --
/// whereas dealing without replacement shows every kind once before any kind repeats.
///
/// The deck reshuffles when it runs out, and starts over if the number of kinds changes
/// underneath it, which is what switching theme does.</summary>
public sealed class ShuffledDeck
{
    private readonly Random _random;
    private readonly List<int> _remaining = new();

    private int _size = -1;

    /// <param name="random">Supply one to make the order reproducible; the app leaves it be.</param>
    public ShuffledDeck(Random? random = null) => _random = random ?? new Random();

    /// <summary>How many cards are left before the deck reshuffles.</summary>
    public int Remaining => _remaining.Count;

    /// <summary>Deals the next index in [0, <paramref name="size"/>).</summary>
    public int Next(int size)
    {
        size = Math.Max(1, size);

        // A different number of kinds makes the current deck meaningless.
        if (_size != size)
        {
            _size = size;
            _remaining.Clear();
        }

        if (_remaining.Count == 0) Refill(size);

        var card = _remaining[^1];
        _remaining.RemoveAt(_remaining.Count - 1);
        return card;
    }

    private void Refill(int size)
    {
        for (var i = 0; i < size; i++) _remaining.Add(i);

        // Fisher-Yates.
        for (var i = _remaining.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (_remaining[i], _remaining[j]) = (_remaining[j], _remaining[i]);
        }
    }
}
