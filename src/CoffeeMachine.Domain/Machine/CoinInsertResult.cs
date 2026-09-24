namespace CoffeeMachine.Domain.Machine;

/// <summary>
/// Outcome of <see cref="VendingMachine.InsertCoin"/>.
/// </summary>
/// <param name="Accepted">True when the coin was accepted and credited.</param>
/// <param name="RejectionReason">
/// Human-readable reason the coin was rejected; non-null exactly when
/// <paramref name="Accepted"/> is false.
/// </param>
/// <param name="BalanceCents">The machine's balance after the operation.</param>
public sealed record CoinInsertResult(bool Accepted, string? RejectionReason, int BalanceCents)
{
    /// <summary>Creates an accepting result.</summary>
    /// <param name="balanceCents">The updated balance.</param>
    /// <returns>The result.</returns>
    internal static CoinInsertResult Ok(int balanceCents) => new(true, null, balanceCents);

    /// <summary>Creates a rejecting result.</summary>
    /// <param name="reason">Why the coin was rejected.</param>
    /// <param name="balanceCents">The unchanged balance.</param>
    /// <returns>The result.</returns>
    internal static CoinInsertResult Rejected(string reason, int balanceCents) => new(false, reason, balanceCents);
}
