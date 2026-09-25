using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CoffeeMachine.Domain.Ledger;
using CoffeeMachine.Domain.Machine;
using CoffeeMachine.Domain.Menu;
using CoffeeMachine.Domain.Money;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeMachine.Api.Data;

/// <summary>
/// Singleton cache of live <see cref="VendingMachine"/> instances, hydrated
/// from (and persisted to) SQLite after every mutation.
/// </summary>
/// <remarks>
/// <para>
/// Access to each machine is serialised with a per-machine
/// <see cref="SemaphoreSlim"/> because the domain model is not thread-safe by
/// design. Every mutation opens a short-lived <see cref="AppDbContext"/> scope,
/// runs the domain operation, writes the machine row and saves — so the
/// persisted snapshot always reflects the last completed mutation.
/// </para>
/// </remarks>
public sealed class MachineRegistry
{
    /// <summary>Coin stock for a brand-new machine: 20 of every accepted denomination.</summary>
    private const int DefaultCoinStock = 20;

    /// <summary>Item stock for a brand-new machine: 10 of every drink.</summary>
    private const int DefaultItemStock = 10;

    private static readonly Regex IdPattern = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.Compiled);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly EfTransactionLedger _ledger;
    private readonly ConcurrentDictionary<string, MachineEntry> _machines = new(StringComparer.Ordinal);
    private readonly object _creationGate = new();

    /// <summary>Creates the registry.</summary>
    /// <param name="scopeFactory">Used to reach the scoped <see cref="AppDbContext"/>.</param>
    /// <param name="ledger">The singleton ledger machines record transactions into.</param>
    public MachineRegistry(IServiceScopeFactory scopeFactory, EfTransactionLedger ledger)
    {
        _scopeFactory = scopeFactory;
        _ledger = ledger;
    }

    /// <summary>
    /// Validates a machine id: non-empty, at most 64 characters, and only
    /// letters, digits and hyphens.
    /// </summary>
    /// <param name="id">The candidate identifier.</param>
    /// <returns>True when the id may be used as a machine id.</returns>
    public static bool IsValidId(string? id) => id is not null && IdPattern.IsMatch(id);

    /// <summary>
    /// Gets an immutable snapshot of a machine's observable state, creating
    /// (and hydrating from SQLite where possible) the machine on first touch.
    /// </summary>
    /// <param name="id">The machine id; must already be validated.</param>
    /// <returns>The current state snapshot.</returns>
    public MachineSnapshot GetSnapshot(string id)
    {
        var entry = GetOrCreate(id);
        entry.Gate.Wait();
        try
        {
            var machine = entry.Machine;
            return new MachineSnapshot(
                machine.Id,
                machine.State,
                machine.BalanceCents,
                machine.Inventory.CoinStock,
                machine.Inventory.ItemStock);
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    /// <summary>
    /// Inserts a coin into a machine and persists the result.
    /// </summary>
    /// <param name="id">The machine id; must already be validated.</param>
    /// <param name="denomination">The inserted coin (any defined denomination; the machine rejects 1¢/2¢).</param>
    /// <returns>The insertion outcome.</returns>
    public CoinInsertResult InsertCoin(string id, Denomination denomination) =>
        Mutate(id, machine => machine.InsertCoin(denomination));

    /// <summary>
    /// Attempts a purchase. On success the dispense completes immediately
    /// (the brew animation is purely client-side) so the persisted state is
    /// Idle with a zero balance.
    /// </summary>
    /// <param name="id">The machine id; must already be validated.</param>
    /// <param name="itemId">The requested item id.</param>
    /// <returns>The selection outcome.</returns>
    public ItemSelectionResult SelectItem(string id, string itemId) =>
        Mutate(id, machine =>
        {
            var result = machine.SelectItem(itemId);
            if (result.Success)
            {
                machine.CompleteDispense();
            }

            return result;
        });

    /// <summary>
    /// Cancels any pending credit and persists the result.
    /// </summary>
    /// <param name="id">The machine id; must already be validated.</param>
    /// <returns>The refund, broken into coins (empty when no credit was pending).</returns>
    public CancelResult Cancel(string id) => Mutate(id, machine => machine.Cancel());

    /// <summary>
    /// Restocks a machine from the admin service panel and persists the result.
    /// </summary>
    /// <param name="id">The machine id; must already be validated.</param>
    /// <param name="coins">Denomination → count to add.</param>
    /// <param name="items">Item id → count to add.</param>
    public void Refill(string id, IReadOnlyDictionary<Denomination, int> coins, IReadOnlyDictionary<string, int> items) =>
        Mutate(id, machine =>
        {
            machine.Inventory.Refill(coins, items);
            return true;
        });

    /// <summary>
    /// Runs one mutation on a machine under its gate, persisting the machine
    /// row (and any ledger rows the mutation appended) in a single save.
    /// </summary>
    private T Mutate<T>(string id, Func<VendingMachine, T> action)
    {
        var entry = GetOrCreate(id);
        entry.Gate.Wait();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using (_ledger.Capture(db))
            {
                var result = action(entry.Machine);
                Persist(db, entry.Machine);
                db.SaveChanges();
                return result;
            }
        }
        finally
        {
            entry.Gate.Release();
        }
    }

    /// <summary>
    /// Gets (or lazily creates) the cached entry for a machine id. Creation is
    /// guarded by a dedicated lock so a machine is never built twice.
    /// </summary>
    private MachineEntry GetOrCreate(string id)
    {
        if (_machines.TryGetValue(id, out var existing))
        {
            return existing;
        }

        lock (_creationGate)
        {
            return _machines.GetOrAdd(id, CreateEntry);
        }
    }

    /// <summary>
    /// Hydrates a machine from its SQLite row, or builds a fresh one with the
    /// default stock and persists it immediately. A row whose stock JSON is
    /// corrupt is treated as untrusted: the persisted balance is kept but the
    /// stock is reseeded to the factory defaults, so the machine hydrates
    /// usable instead of failing every request with a 500.
    /// </summary>
    private MachineEntry CreateEntry(string id)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entity = db.Machines.Find(id);
        VendingMachine machine;
        if (entity is null)
        {
            machine = new VendingMachine(id, SeedDefaultInventory(), _ledger);
            Persist(db, machine);
            db.SaveChanges();
        }
        else
        {
            var state = StockJson.DeserializeState(entity.State);
            var coinStockOk = StockJson.TryDeserializeCoins(entity.CoinStockJson, out var coins);
            var itemStockOk = StockJson.TryDeserializeItems(entity.ItemStockJson, out var items);

            machine = coinStockOk && itemStockOk
                ? VendingMachine.Restore(id, state, entity.BalanceCents, coins, items, _ledger)
                : RestoreWithDefaultStock(id, state, entity.BalanceCents);
        }

        return new MachineEntry(machine);
    }

    /// <summary>
    /// Builds a machine from a row whose stock JSON cannot be trusted: keep
    /// the persisted state and balance, but reseed the factory-default stock
    /// so the machine stays operable (the admin can restock via the service
    /// panel).
    /// </summary>
    private VendingMachine RestoreWithDefaultStock(string id, MachineState state, int balanceCents)
    {
        var seed = SeedDefaultInventory();
        return VendingMachine.Restore(id, state, balanceCents, seed.CoinStock, seed.ItemStock, _ledger);
    }

    /// <summary>Builds the factory-default inventory: 20 of every accepted denomination and 10 of every drink.</summary>
    private static Inventory SeedDefaultInventory()
    {
        var inventory = new Inventory();
        foreach (var denomination in Denominations.Accepted)
        {
            inventory.AddCoin(denomination, DefaultCoinStock);
        }

        foreach (var item in CoffeeMenu.All)
        {
            inventory.AddItem(item.Id, DefaultItemStock);
        }

        return inventory;
    }

    /// <summary>Writes the machine's current state into its SQLite row.</summary>
    private static void Persist(AppDbContext db, VendingMachine machine)
    {
        var entity = db.Machines.Find(machine.Id);
        if (entity is null)
        {
            entity = new MachineEntity { Id = machine.Id };
            db.Machines.Add(entity);
        }

        entity.State = StockJson.SerializeState(machine.State);
        entity.BalanceCents = machine.BalanceCents;
        entity.CoinStockJson = StockJson.SerializeCoins(machine.Inventory.CoinStock);
        entity.ItemStockJson = StockJson.SerializeItems(machine.Inventory.ItemStock);
        entity.UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>An immutable view of a machine's observable state.</summary>
    public sealed record MachineSnapshot(
        string Id,
        MachineState State,
        int BalanceCents,
        IReadOnlyDictionary<Denomination, int> CoinStock,
        IReadOnlyDictionary<string, int> ItemStock);

    /// <summary>A cached machine plus the gate that serialises access to it.</summary>
    private sealed class MachineEntry
    {
        public MachineEntry(VendingMachine machine) => Machine = machine;

        public VendingMachine Machine { get; }

        public SemaphoreSlim Gate { get; } = new(1, 1);
    }
}
