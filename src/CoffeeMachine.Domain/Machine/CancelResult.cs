using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Domain.Machine;

/// <summary>
/// Outcome of <see cref="VendingMachine.Cancel"/>.
/// </summary>
/// <param name="RefundedCents">Total amount refunded in cents.</param>
/// <param name="Refund">The refund broken into coins, largest first.</param>
public sealed record CancelResult(int RefundedCents, IReadOnlyDictionary<Denomination, int> Refund)
{
    /// <summary>A cancellation that returned nothing (no credit was pending).</summary>
    internal static readonly CancelResult Nothing = new(0, new Dictionary<Denomination, int>());
}
