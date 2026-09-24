namespace CoffeeMachine.Domain.Money;

/// <summary>
/// Australian coin denominations, expressed in cents.
/// The raw integer values are the canonical representation used throughout
/// the domain: all arithmetic (balance, price, change) happens in cents so no
/// floating-point rounding can ever occur.
/// </summary>
/// <remarks>
/// <see cref="Cent1"/> and <see cref="Cent2"/> exist because the assignment
/// brief says the machine accepts them physically, but the coin validator
/// rejects them and they are never accepted into the balance or the stock.
/// </remarks>
public enum Denomination
{
    /// <summary>1 cent coin. Rejected by the machine.</summary>
    Cent1 = 1,

    /// <summary>2 cent coin. Rejected by the machine.</summary>
    Cent2 = 2,

    /// <summary>5 cent coin.</summary>
    Cent5 = 5,

    /// <summary>10 cent coin.</summary>
    Cent10 = 10,

    /// <summary>20 cent coin.</summary>
    Cent20 = 20,

    /// <summary>50 cent coin.</summary>
    Cent50 = 50,

    /// <summary>1 dollar coin (100 cents).</summary>
    Dollar1 = 100,

    /// <summary>2 dollar coin (200 cents).</summary>
    Dollar2 = 200,
}

/// <summary>
/// Static metadata and lookup helpers for <see cref="Denomination"/>.
/// C# does not allow static members on enums, so the "enum-level" helpers the
/// machine needs live here as a companion class.
/// </summary>
public static class Denominations
{
    /// <summary>
    /// Denominations the machine accepts, smallest first: 5¢ through $2.
    /// This is the exact list the coin validator works from.
    /// </summary>
    public static readonly IReadOnlyList<Denomination> Accepted =
        new[] { Denomination.Cent5, Denomination.Cent10, Denomination.Cent20, Denomination.Cent50, Denomination.Dollar1, Denomination.Dollar2 };

    /// <summary>
    /// Every defined denomination, smallest first, including the rejected 1¢ and 2¢.
    /// </summary>
    public static readonly IReadOnlyList<Denomination> All =
        new[] { Denomination.Cent1, Denomination.Cent2, Denomination.Cent5, Denomination.Cent10, Denomination.Cent20, Denomination.Cent50, Denomination.Dollar1, Denomination.Dollar2 };

    /// <summary>
    /// Attempts to map a cent value to its <see cref="Denomination"/>.
    /// Returns false for values that do not correspond to a real coin
    /// (0, 3, 25, 250, negative numbers, ...).
    /// </summary>
    /// <param name="cents">The cent value to look up.</param>
    /// <param name="denomination">The matched denomination, or 0 when unmatched.</param>
    /// <returns>True when <paramref name="cents"/> is a defined coin value.</returns>
    public static bool TryParse(int cents, out Denomination denomination)
    {
        foreach (var candidate in All)
        {
            if ((int)candidate == cents)
            {
                denomination = candidate;
                return true;
            }
        }

        denomination = default;
        return false;
    }

    /// <summary>
    /// Maps a cent value to its <see cref="Denomination"/>.
    /// </summary>
    /// <param name="cents">The cent value to look up.</param>
    /// <returns>The matching denomination.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="cents"/> is not a defined coin value.
    /// </exception>
    public static Denomination FromCents(int cents) =>
        TryParse(cents, out var denomination)
            ? denomination
            : throw new ArgumentOutOfRangeException(nameof(cents), cents, $"'{cents}' cents is not a defined coin denomination.");
}

/// <summary>
/// Convenience extensions over <see cref="Denomination"/>.
/// </summary>
public static class DenominationExtensions
{
    /// <summary>
    /// Gets the numeric value of the denomination in cents.
    /// </summary>
    /// <param name="denomination">The denomination.</param>
    /// <returns>The value in cents, e.g. 200 for <see cref="Denomination.Dollar2"/>.</returns>
    public static int ValueCents(this Denomination denomination) => (int)denomination;

    /// <summary>
    /// Gets a short human-readable label for the denomination, e.g. "50¢" or "$2".
    /// </summary>
    /// <param name="denomination">The denomination.</param>
    /// <returns>The display label.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="denomination"/> is not a defined coin value.
    /// </exception>
    public static string DisplayName(this Denomination denomination) => denomination switch
    {
        Denomination.Cent1 => "1¢",
        Denomination.Cent2 => "2¢",
        Denomination.Cent5 => "5¢",
        Denomination.Cent10 => "10¢",
        Denomination.Cent20 => "20¢",
        Denomination.Cent50 => "50¢",
        Denomination.Dollar1 => "$1",
        Denomination.Dollar2 => "$2",
        _ => throw new ArgumentOutOfRangeException(nameof(denomination), denomination, "Unknown coin denomination."),
    };
}
