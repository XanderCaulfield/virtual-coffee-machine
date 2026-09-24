namespace CoffeeMachine.Domain.Ledger;

/// <summary>
/// Append-only store of <see cref="Transaction"/> rows.
/// The domain core depends only on this abstraction; the API layer provides
/// an EF Core/SQLite-backed implementation with the same semantics.
/// </summary>
public interface ITransactionLedger
{
    /// <summary>
    /// Records a transaction.
    /// </summary>
    /// <param name="transaction">The transaction to record.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transaction"/> is null.</exception>
    void Append(Transaction transaction);

    /// <summary>
    /// Lists a machine's transactions, most recent first.
    /// </summary>
    /// <param name="machineId">The machine whose transactions to list.</param>
    /// <param name="limit">Maximum number of rows to return; rows beyond the limit are discarded.</param>
    /// <returns>The matching transactions, newest first.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="machineId"/> is null.</exception>
    IEnumerable<Transaction> List(string machineId, int limit);
}
