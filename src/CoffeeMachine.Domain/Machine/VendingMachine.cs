using CoffeeMachine.Domain.Ledger;
using CoffeeMachine.Domain.Menu;
using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Domain.Machine;

/// <summary>
/// The coffee vending machine state machine.
/// </summary>
/// <remarks>
/// <para>
/// State flow driven by the server (synchronously):
/// <c>Idle</c> → (coin accepted) <c>AwaitingSelection</c> → (purchase) <c>Dispensing</c>
/// → (<see cref="CompleteDispense"/>) <c>Idle</c>. <see cref="Cancel"/> returns
/// the machine to <c>Idle</c> from any accept state.
/// </para>
/// <para>
/// All money handling is exact: balances, prices and change are tracked in
/// integer cents, and rejected 1¢/2¢ coins never touch the balance or the
/// stock. Inserted coins are held in the <see cref="Inventory"/> and change
/// is drawn from that same stock, which is what makes the "exact change only"
/// rule (insufficient stock to make change → refuse the sale) meaningful.
/// </para>
/// <para>
/// Not thread-safe by design: the API layer serialises access per machine id.
/// </para>
/// </remarks>
public sealed class VendingMachine
{
    /// <summary>Rejection reason returned when a coin arrives while the machine is busy.</summary>
    public const string BusyRejectionReason = "Machine is busy dispensing";

    private readonly ITransactionLedger _ledger;

    /// <summary>
    /// Creates a brand-new machine with empty stock, no credit and
    /// <see cref="MachineState.Idle"/>.
    /// </summary>
    /// <param name="id">Stable machine identifier (per-visitor GUID in production).</param>
    /// <param name="ledger">
    /// The ledger to record transactions in; defaults to a private in-memory
    /// ledger when null.
    /// </param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> is null.</exception>
    public VendingMachine(string id, ITransactionLedger? ledger = null)
        : this(id, new Inventory(), MachineState.Idle, 0, ledger)
    {
    }

    /// <summary>
    /// Creates a new machine pre-stocked with an inventory (e.g. seeded with
    /// change coins by the admin service panel).
    /// </summary>
    /// <param name="id">Stable machine identifier.</param>
    /// <param name="inventory">The initial inventory. May not be null.</param>
    /// <param name="ledger">The ledger to record transactions in; defaults to in-memory when null.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="id"/> or <paramref name="inventory"/> is null.</exception>
    public VendingMachine(string id, Inventory inventory, ITransactionLedger? ledger = null)
        : this(id, inventory, MachineState.Idle, 0, ledger)
    {
    }

    private VendingMachine(string id, Inventory inventory, MachineState state, int balanceCents, ITransactionLedger? ledger)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(inventory);

        Id = id;
        Inventory = inventory;
        State = state;
        BalanceCents = balanceCents;
        _ledger = ledger ?? new InMemoryTransactionLedger();
    }

    /// <summary>
    /// Rebuilds a machine from persisted state (used by the API layer when
    /// rehydrating from SQLite after a restart or refresh).
    /// </summary>
    /// <param name="id">The machine's stable identifier.</param>
    /// <param name="state">The persisted state.</param>
    /// <param name="balanceCents">The persisted balance. Must be &gt;= 0.</param>
    /// <param name="coinStock">The persisted coin stock (denomination → count).</param>
    /// <param name="itemStock">The persisted item stock (item id → count).</param>
    /// <param name="ledger">The ledger future transactions are recorded in; defaults to in-memory when null.</param>
    /// <returns>A machine identical to the persisted snapshot.</returns>
    /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="balanceCents"/> is negative or a stock count is negative.</exception>
    public static VendingMachine Restore(
        string id,
        MachineState state,
        int balanceCents,
        IReadOnlyDictionary<Denomination, int> coinStock,
        IReadOnlyDictionary<string, int> itemStock,
        ITransactionLedger? ledger = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(coinStock);
        ArgumentNullException.ThrowIfNull(itemStock);

        if (balanceCents < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(balanceCents), balanceCents, "Balance cannot be negative.");
        }

        foreach (var (denomination, count) in coinStock)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(coinStock), count, $"Coin stock for {denomination} cannot be negative.");
            }
        }

        foreach (var (itemId, count) in itemStock)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(itemStock), count, $"Item stock for '{itemId}' cannot be negative.");
            }
        }

        var inventory = new Inventory();
        inventory.Refill(coinStock, itemStock);

        return new VendingMachine(id, inventory, state, balanceCents, ledger);
    }

    /// <summary>The machine's stable identifier.</summary>
    public string Id { get; }

    /// <summary>The current credit in cents. Read-only from outside the machine.</summary>
    public int BalanceCents { get; private set; }

    /// <summary>The current state of the state machine.</summary>
    public MachineState State { get; private set; }

    /// <summary>The machine's physical stock (coins for change, items for sale).</summary>
    public Inventory Inventory { get; }

    /// <summary>
    /// Handles a coin being inserted into the slot.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>1¢ and 2¢ coins are rejected without touching the
    /// balance or the stock.</description></item>
    /// <item><description>Accepted coins are credited to the balance AND added
    /// to the coin stock (they become change for the next customer).</description></item>
    /// <item><description>Inserting the first coin moves the machine from
    /// <see cref="MachineState.Idle"/> to <see cref="MachineState.AwaitingSelection"/>.</description></item>
    /// <item><description>While <see cref="MachineState.Dispensing"/> (or
    /// <see cref="MachineState.Brewing"/>) the slot is ignored so credit can
    /// never accumulate mid-dispense.</description></item>
    /// </list>
    /// </remarks>
    /// <param name="denomination">The coin inserted.</param>
    /// <returns>The outcome, including the updated balance.</returns>
    public CoinInsertResult InsertCoin(Denomination denomination)
    {
        if (State is MachineState.Dispensing or MachineState.Brewing)
        {
            return CoinInsertResult.Rejected(BusyRejectionReason, BalanceCents);
        }

        var validation = CoinValidator.Validate(denomination);
        if (!validation.Accepted)
        {
            return CoinInsertResult.Rejected(validation.RejectionReason!, BalanceCents);
        }

        Inventory.AddCoin(denomination);
        BalanceCents += denomination.ValueCents();

        if (State == MachineState.Idle)
        {
            State = MachineState.AwaitingSelection;
        }

        return CoinInsertResult.Ok(BalanceCents);
    }

    /// <summary>
    /// Attempts to purchase an item with the current balance.
    /// </summary>
    /// <remarks>
    /// On success the machine: decrements the item stock, dispenses the change
    /// from the coin stock (minimal coin count, never 1¢/2¢), records a
    /// "Purchased" ledger row, zeroes the balance and moves to
    /// <see cref="MachineState.Dispensing"/>. On failure nothing changes — the
    /// balance stays so the customer can add coins or cancel. The one
    /// exception is the "exact change only" case, where the machine holds the
    /// customer's coins until they cancel (the UI prompts them to press the
    /// coin-return button, which calls <see cref="Cancel"/>).
    /// </remarks>
    /// <param name="itemId">The stable item id, e.g. "latte".</param>
    /// <returns>The purchase outcome, or the failure reason.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="itemId"/> is null.</exception>
    public ItemSelectionResult SelectItem(string itemId)
    {
        ArgumentNullException.ThrowIfNull(itemId);

        var item = CoffeeMenu.Find(itemId);
        if (item is null)
        {
            return ItemSelectionResult.Failed(SelectionFailureReason.UnknownItem);
        }

        if (Inventory.ItemCountOf(item.Id) <= 0)
        {
            return ItemSelectionResult.Failed(SelectionFailureReason.OutOfStock);
        }

        if (BalanceCents < item.PriceCents)
        {
            return ItemSelectionResult.Failed(SelectionFailureReason.InsufficientFunds);
        }

        var changeTotalCents = BalanceCents - item.PriceCents;
        if (!Inventory.CanMakeChange(changeTotalCents))
        {
            return ItemSelectionResult.Failed(SelectionFailureReason.ExactChangeOnly);
        }

        // Commit: this order guarantees the cup and the change leave together
        // and the stock never goes negative.
        if (!Inventory.RemoveItem(item.Id, 1))
        {
            return ItemSelectionResult.Failed(SelectionFailureReason.OutOfStock);
        }

        var change = Inventory.MakeChange(changeTotalCents)!;
        var paidCents = BalanceCents;
        BalanceCents = 0;

        var transactionId = Guid.NewGuid();
        _ledger.Append(new Transaction(
            transactionId,
            DateTimeOffset.UtcNow,
            Id,
            item.Id,
            paidCents,
            changeTotalCents,
            TransactionStatus.Purchased));

        State = MachineState.Dispensing;

        return ItemSelectionResult.Succeeded(item, paidCents, changeTotalCents, change, transactionId);
    }

    /// <summary>
    /// Signals that the customer has taken the drink and change, returning the
    /// machine to <see cref="MachineState.Idle"/>. Called by the API when the
    /// client reports the cup area emptied; the server stays synchronous (the
    /// brew animation is purely client-side).
    /// </summary>
    /// <remarks>Idempotent: calling it outside <see cref="MachineState.Dispensing"/> is a no-op.</remarks>
    public void CompleteDispense()
    {
        if (State == MachineState.Dispensing)
        {
            State = MachineState.Idle;
        }
    }

    /// <summary>
    /// Cancels the pending credit: refunds the full balance from the coin
    /// stock, records a "Cancelled" ledger row, zeroes the balance and returns
    /// the machine to <see cref="MachineState.Idle"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The refund is always payable: every cent of the balance was inserted as
    /// a physical coin that went straight into the stock, and no sale can have
    /// consumed it (a successful sale always zeroes the balance).
    /// </para>
    /// <para>
    /// Cancelling with no credit (or while dispensing) is a no-op and records
    /// nothing.
    /// </para>
    /// </remarks>
    /// <returns>The refund, broken into coins.</returns>
    public CancelResult Cancel()
    {
        if (State is MachineState.Dispensing or MachineState.Brewing)
        {
            // Never touch the state mid-dispense; there is nothing to refund.
            return CancelResult.Nothing;
        }

        if (BalanceCents == 0)
        {
            State = MachineState.Idle;
            return CancelResult.Nothing;
        }

        var refund = Inventory.MakeChange(BalanceCents)
            ?? throw new InvalidOperationException(
                $"Machine '{Id}' could not refund its balance of {BalanceCents}¢ from its coin stock. " +
                "This is an invariant violation: inserted coins always enter the stock.");

        var refundedCents = BalanceCents;
        BalanceCents = 0;

        _ledger.Append(new Transaction(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            Id,
            string.Empty,
            refundedCents,
            0,
            TransactionStatus.Cancelled));

        State = MachineState.Idle;

        return new CancelResult(refundedCents, refund);
    }
}
