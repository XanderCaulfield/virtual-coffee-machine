namespace CoffeeMachine.Domain.Ledger;

/// <summary>
/// Canonical status values recorded on <see cref="Transaction"/> rows.
/// </summary>
public static class TransactionStatus
{
    /// <summary>A drink was sold and dispensed.</summary>
    public const string Purchased = "Purchased";

    /// <summary>A pending balance was refunded to the customer.</summary>
    public const string Cancelled = "Cancelled";
}
