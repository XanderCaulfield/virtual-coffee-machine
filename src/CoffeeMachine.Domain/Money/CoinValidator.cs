namespace CoffeeMachine.Domain.Money;

/// <summary>
/// Outcome of a coin validation.
/// </summary>
/// <param name="Accepted">True when the coin may enter the machine.</param>
/// <param name="RejectionReason">
/// Human-readable reason the coin was rejected; always non-null when
/// <paramref name="Accepted"/> is false, always null when accepted.
/// </param>
public sealed record ValidationResult(bool Accepted, string? RejectionReason)
{
    /// <summary>Creates an accepting result.</summary>
    /// <returns>A result with <see cref="Accepted"/> = true and no reason.</returns>
    public static ValidationResult Accept() => new(true, null);

    /// <summary>Creates a rejecting result carrying an explanation.</summary>
    /// <param name="reason">Human-readable rejection reason.</param>
    /// <returns>A result with <see cref="Accepted"/> = false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reason"/> is null.</exception>
    public static ValidationResult Reject(string reason) =>
        new(false, reason ?? throw new ArgumentNullException(nameof(reason)));
}

/// <summary>
/// Validates coins against the machine's acceptance rules.
/// The machine accepts 5¢, 10¢, 20¢, 50¢, $1 and $2 coins. Per the assignment
/// brief it still <em>recognises</em> 1¢ and 2¢ coins (they are rejected with a
/// specific message rather than treated as foreign objects), and rejects
/// everything else as invalid.
/// </summary>
public static class CoinValidator
{
    /// <summary>Rejection reason for a physically recognised but unwanted 1 cent coin.</summary>
    public const string Cent1Rejected = "1 cent coins are not accepted";

    /// <summary>Rejection reason for a physically recognised but unwanted 2 cent coin.</summary>
    public const string Cent2Rejected = "2 cent coins are not accepted";

    /// <summary>Rejection reason for any value that is not a real coin.</summary>
    public const string InvalidCoin = "Invalid coin";

    /// <summary>
    /// Validates a coin given as a <see cref="Denomination"/>.
    /// </summary>
    /// <param name="denomination">The coin to validate.</param>
    /// <returns>The validation outcome.</returns>
    public static ValidationResult Validate(Denomination denomination) => denomination switch
    {
        Denomination.Cent1 => ValidationResult.Reject(Cent1Rejected),
        Denomination.Cent2 => ValidationResult.Reject(Cent2Rejected),
        Denomination.Cent5 or Denomination.Cent10 or Denomination.Cent20 or Denomination.Cent50 or Denomination.Dollar1 or Denomination.Dollar2 => ValidationResult.Accept(),
        _ => ValidationResult.Reject(InvalidCoin),
    };

    /// <summary>
    /// Validates a coin given as a raw cent value.
    /// </summary>
    /// <param name="cents">The coin value in cents.</param>
    /// <returns>The validation outcome.</returns>
    public static ValidationResult Validate(int cents) => cents switch
    {
        1 => ValidationResult.Reject(Cent1Rejected),
        2 => ValidationResult.Reject(Cent2Rejected),
        5 or 10 or 20 or 50 or 100 or 200 => ValidationResult.Accept(),
        _ => ValidationResult.Reject(InvalidCoin),
    };
}
