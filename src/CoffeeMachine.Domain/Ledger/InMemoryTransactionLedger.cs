namespace CoffeeMachine.Domain.Ledger;

/// <summary>
/// Thread-safe in-memory <see cref="ITransactionLedger"/> used by tests and
/// as a fallback when a machine is constructed without a persisted ledger.
/// </summary>
public sealed class InMemoryTransactionLedger : ITransactionLedger
{
    private readonly object _gate = new();
    private readonly List<Transaction> _transactions = new();

    /// <inheritdoc />
    public void Append(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        lock (_gate)
        {
            _transactions.Add(transaction);
        }
    }

    /// <inheritdoc />
    public IEnumerable<Transaction> List(string? machineId, int limit)
    {
        if (limit <= 0)
        {
            return Array.Empty<Transaction>();
        }

        lock (_gate)
        {
            var filtered = string.IsNullOrWhiteSpace(machineId)
                ? _transactions
                : _transactions.Where(transaction => transaction.MachineId == machineId);

            // The index tie-break keeps the "newest first" guarantee strict
            // even when consecutive rows share the same timestamp (the
            // clock resolution on some platforms is coarser than the time
            // between two quick transactions).
            return filtered
                .Select((transaction, index) => (transaction, index))
                .OrderByDescending(entry => entry.transaction.Timestamp)
                .ThenByDescending(entry => entry.index)
                .Select(entry => entry.transaction)
                .Take(limit)
                .ToList();
        }
    }
}
