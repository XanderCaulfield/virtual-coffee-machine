namespace CoffeeMachine.Domain.Ledger;

/// <summary>
/// One immutable, append-only ledger row recording money that left or entered
/// the machine's credit path.
/// </summary>
/// <remarks>
/// <para>Semantics by <see cref="Status"/>:</para>
/// <list type="bullet">
/// <item><description><c>Purchased</c>: <see cref="ItemId"/> is the sold item,
/// <see cref="PaidCents"/> is the amount tendered and <see cref="ChangeCents"/>
/// the change returned.</description></item>
/// <item><description><c>Cancelled</c>: <see cref="ItemId"/> is
/// <see cref="string.Empty"/>, <see cref="PaidCents"/> is the amount refunded
/// and <see cref="ChangeCents"/> is 0.</description></item>
/// </list>
/// </remarks>
/// <param name="Id">Unique transaction identifier.</param>
/// <param name="Timestamp">When the transaction was recorded.</param>
/// <param name="MachineId">The machine the transaction belongs to.</param>
/// <param name="ItemId">Item id for purchases; empty string for cancellations.</param>
/// <param name="PaidCents">Tendered amount for purchases; refunded amount for cancellations.</param>
/// <param name="ChangeCents">Change returned for purchases; 0 for cancellations.</param>
/// <param name="Status">One of the <see cref="TransactionStatus"/> constants.</param>
public sealed record Transaction(
    Guid Id,
    DateTimeOffset Timestamp,
    string MachineId,
    string ItemId,
    int PaidCents,
    int ChangeCents,
    string Status);
