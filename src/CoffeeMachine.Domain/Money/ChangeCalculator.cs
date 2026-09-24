namespace CoffeeMachine.Domain.Money;

/// <summary>
/// Computes change broken down into the coins the machine can dispense.
/// </summary>
/// <remarks>
/// <para>
/// A simple greedy algorithm (always take the largest coin that fits) is used,
/// iterating over the denominations in descending order
/// [200, 100, 50, 20, 10, 5].
/// </para>
/// <para>
/// Why greedy is optimal here: Australian denominations form a
/// <em>canonical</em> coin system — every denomination is a multiple of the
/// next smaller one (200 = 2×100 = 4×50 = 10×20 = 20×10 = 40×5). In a
/// canonical system the greedy algorithm provably uses the minimum possible
/// number of coins for every amount, and because 1¢ and 2¢ coins are excluded
/// the system remains canonical and every multiple of 5¢ remains exactly
/// representable. Given that all prices (300, 350, 400) and all accepted coins
/// are multiples of 5¢, greedy change is always both exact and minimal — the
/// same result a dynamic-programming solver would give, but in O(1) work.
/// </para>
/// <para>
/// The algorithm never emits 1¢ or 2¢ coins even if the stock contains them,
/// which keeps the "rejected coins" story consistent: those coins leave via
/// the coin return, not as change.
/// </para>
/// </remarks>
public static class ChangeCalculator
{
    /// <summary>
    /// The denominations change is dispensed from, largest first. This is the
    /// order the greedy pass iterates in.
    /// </summary>
    public static readonly IReadOnlyList<Denomination> ChangeDenominations =
        new[] { Denomination.Dollar2, Denomination.Dollar1, Denomination.Cent50, Denomination.Cent20, Denomination.Cent10, Denomination.Cent5 };

    /// <summary>
    /// Computes the change for <paramref name="amountCents"/> given the
    /// available <paramref name="coinStock"/>, without touching the stock.
    /// </summary>
    /// <param name="amountCents">The amount to break into coins. Must be &gt;= 0.</param>
    /// <param name="coinStock">Available coins, denomination to count.</param>
    /// <returns>
    /// A dictionary mapping denomination to count whose total value equals
    /// <paramref name="amountCents"/> (an empty dictionary for 0), or null
    /// when the stock cannot cover the amount exactly.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="coinStock"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amountCents"/> is negative.</exception>
    public static IReadOnlyDictionary<Denomination, int>? MakeChange(
        int amountCents,
        IReadOnlyDictionary<Denomination, int> coinStock)
    {
        ArgumentNullException.ThrowIfNull(coinStock);

        if (amountCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountCents), amountCents, "Change amount cannot be negative.");
        }

        var change = new Dictionary<Denomination, int>();
        var remaining = amountCents;

        foreach (var denomination in ChangeDenominations)
        {
            if (remaining == 0)
            {
                break;
            }

            var available = coinStock.TryGetValue(denomination, out var count) ? count : 0;
            var needed = remaining / denomination.ValueCents();
            var taken = Math.Min(available, needed);

            if (taken > 0)
            {
                change[denomination] = taken;
                remaining -= taken * denomination.ValueCents();
            }
        }

        return remaining == 0 ? change : null;
    }

    /// <summary>
    /// Determines whether the stock can make exact change for
    /// <paramref name="amountCents"/>.
    /// </summary>
    /// <param name="amountCents">The amount to break into coins. Must be &gt;= 0.</param>
    /// <param name="coinStock">Available coins, denomination to count.</param>
    /// <returns>True when exact change can be made, including the trivial case of 0.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="coinStock"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amountCents"/> is negative.</exception>
    public static bool CanMakeChange(int amountCents, IReadOnlyDictionary<Denomination, int> coinStock) =>
        MakeChange(amountCents, coinStock) is not null;
}
