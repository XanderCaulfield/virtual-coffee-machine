using CoffeeMachine.Domain.Menu;
using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Domain.Machine;

/// <summary>
/// Outcome of <see cref="VendingMachine.SelectItem"/>.
/// </summary>
/// <remarks>
/// On success the change, item, amounts and transaction id are populated and
/// <see cref="FailureReason"/> is null. On failure <see cref="FailureReason"/>
/// explains what went wrong and the other properties are left at defaults.
/// </remarks>
/// <param name="FailureReason">The failure code, or null on success.</param>
/// <param name="Item">The purchased item, or null on failure.</param>
/// <param name="PriceCents">The item price in cents (0 on failure).</param>
/// <param name="PaidCents">The total amount tendered in cents (0 on failure).</param>
/// <param name="ChangeTotalCents">Total change returned in cents (0 on failure).</param>
/// <param name="Change">Change broken into coins, largest first (null on failure).</param>
/// <param name="TransactionId">Id of the recorded ledger row (null on failure).</param>
public sealed record ItemSelectionResult(
    SelectionFailureReason? FailureReason,
    MenuItem? Item,
    int PriceCents,
    int PaidCents,
    int ChangeTotalCents,
    IReadOnlyDictionary<Denomination, int>? Change,
    Guid? TransactionId)
{
    /// <summary>True when the purchase went through.</summary>
    public bool Success => FailureReason is null;

    /// <summary>Creates a success result.</summary>
    internal static ItemSelectionResult Succeeded(
        MenuItem item,
        int paidCents,
        int changeTotalCents,
        IReadOnlyDictionary<Denomination, int> change,
        Guid transactionId) =>
        new(null, item, item.PriceCents, paidCents, changeTotalCents, change, transactionId);

    /// <summary>Creates a failure result.</summary>
    internal static ItemSelectionResult Failed(SelectionFailureReason reason) =>
        new(reason, null, 0, 0, 0, null, null);
}
