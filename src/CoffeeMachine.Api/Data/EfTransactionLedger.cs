using CoffeeMachine.Domain.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeMachine.Api.Data;

/// <summary>
/// SQLite-backed <see cref="ITransactionLedger"/>, registered as a singleton.
/// </summary>
/// <remarks>
/// <para>
/// The machine registry executes every machine mutation inside a single
/// <see cref="AppDbContext"/> scope and calls <see cref="Capture"/> so that
/// <see cref="Append"/> rows land in that same context's change tracker. The
/// registry then saves once, committing the machine row and its ledger rows
/// atomically. When <see cref="Append"/> is called outside a registry
/// mutation (no ambient context), it opens its own short-lived scope and
/// saves immediately.
/// </para>
/// </remarks>
public sealed class EfTransactionLedger : ITransactionLedger
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AsyncLocal<AppDbContext?> _ambient = new();

    /// <summary>Creates the ledger.</summary>
    /// <param name="scopeFactory">Used to resolve scoped <see cref="AppDbContext"/> instances.</param>
    public EfTransactionLedger(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>
    /// Binds the ledger to a DbContext for the duration of one machine mutation.
    /// Rows appended while bound are flushed by the registry's single
    /// <c>SaveChanges</c> call, keeping the machine row and its ledger rows in
    /// one transaction.
    /// </summary>
    /// <param name="dbContext">The context the current mutation writes through.</param>
    /// <returns>A scope that restores the previous binding on dispose.</returns>
    public IDisposable Capture(AppDbContext dbContext)
    {
        var previous = _ambient.Value;
        _ambient.Value = dbContext;
        return new AmbientScope(this, previous);
    }

    /// <inheritdoc />
    public void Append(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var ambient = _ambient.Value;
        if (ambient is not null)
        {
            // Flushed with the machine row by the registry's SaveChanges call.
            ambient.Transactions.Add(ToEntity(transaction));
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Transactions.Add(ToEntity(transaction));
        db.SaveChanges();
    }

    /// <inheritdoc />
    public IEnumerable<Transaction> List(string machineId, int limit)
    {
        ArgumentNullException.ThrowIfNull(machineId);

        if (limit <= 0)
        {
            return Array.Empty<Transaction>();
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.Transactions
            .AsNoTracking()
            .Where(t => t.MachineId == machineId)
            .OrderByDescending(t => t.Timestamp)
            .Take(limit)
            .ToList()
            .Select(ToDomain)
            .ToList();
    }

    private static TransactionEntity ToEntity(Transaction transaction) => new()
    {
        Id = transaction.Id,
        Timestamp = transaction.Timestamp,
        MachineId = transaction.MachineId,
        ItemId = transaction.ItemId,
        PaidCents = transaction.PaidCents,
        ChangeCents = transaction.ChangeCents,
        Status = transaction.Status,
    };

    private static Transaction ToDomain(TransactionEntity entity) => new(
        entity.Id,
        entity.Timestamp,
        entity.MachineId,
        entity.ItemId,
        entity.PaidCents,
        entity.ChangeCents,
        entity.Status);

    private sealed class AmbientScope : IDisposable
    {
        private readonly EfTransactionLedger _owner;
        private readonly AppDbContext? _previous;

        public AmbientScope(EfTransactionLedger owner, AppDbContext? previous)
        {
            _owner = owner;
            _previous = previous;
        }

        public void Dispose() => _owner._ambient.Value = _previous;
    }
}
