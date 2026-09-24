using CoffeeMachine.Domain.Ledger;
using CoffeeMachine.Domain.Machine;
using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Domain.Tests;

/// <summary>
/// Behavioural tests for the vending machine state machine: coin acceptance,
/// purchases, change, exact-change-only, cancel, state transitions, ledger
/// recording and state rehydration.
/// </summary>
public class VendingMachineTests
{
    private const string MachineId = "machine-1";

    private static (VendingMachine Machine, InMemoryTransactionLedger Ledger) CreateMachine(
        string id = MachineId,
        Inventory? inventory = null)
    {
        var ledger = new InMemoryTransactionLedger();
        var machine = inventory is null
            ? new VendingMachine(id, ledger)
            : new VendingMachine(id, inventory, ledger);
        return (machine, ledger);
    }

    private static Inventory SeededInventory(
        Dictionary<string, int>? items = null,
        Dictionary<Denomination, int>? coins = null)
    {
        var inventory = new Inventory();
        inventory.Refill(
            coins ?? new Dictionary<Denomination, int>(),
            items ?? new Dictionary<string, int>());
        return inventory;
    }

    private static void Insert(VendingMachine machine, params Denomination[] coins)
    {
        foreach (var coin in coins)
        {
            var result = machine.InsertCoin(coin);
            Assert.True(result.Accepted, $"Coin {coin} should have been accepted: {result.RejectionReason}");
        }
    }

    // ---------- Coin insertion ----------

    [Fact]
    public void InsertCoin_accepts_valid_coin_adds_to_balance_and_stock_and_moves_to_awaiting_selection()
    {
        var (machine, _) = CreateMachine();

        Assert.Equal(MachineState.Idle, machine.State);

        var result = machine.InsertCoin(Denomination.Dollar2);

        Assert.True(result.Accepted);
        Assert.Null(result.RejectionReason);
        Assert.Equal(200, result.BalanceCents);
        Assert.Equal(200, machine.BalanceCents);
        Assert.Equal(1, machine.Inventory.CoinCountOf(Denomination.Dollar2));
        Assert.Equal(MachineState.AwaitingSelection, machine.State);
    }

    [Fact]
    public void InsertCoin_rejects_one_cent_without_touching_balance_or_stock()
    {
        var (machine, _) = CreateMachine();

        var result = machine.InsertCoin(Denomination.Cent1);

        Assert.False(result.Accepted);
        Assert.Equal("1 cent coins are not accepted", result.RejectionReason);
        Assert.Equal(0, result.BalanceCents);
        Assert.Equal(0, machine.BalanceCents);
        Assert.Equal(0, machine.Inventory.CoinCountOf(Denomination.Cent1));
        Assert.Equal(0, machine.Inventory.CoinCount);
        Assert.Equal(MachineState.Idle, machine.State);
    }

    [Fact]
    public void InsertCoin_rejects_two_cent_without_touching_balance_or_stock()
    {
        var (machine, _) = CreateMachine();
        Insert(machine, Denomination.Dollar1);

        var result = machine.InsertCoin(Denomination.Cent2);

        Assert.False(result.Accepted);
        Assert.Equal("2 cent coins are not accepted", result.RejectionReason);
        Assert.Equal(100, result.BalanceCents);
        Assert.Equal(100, machine.BalanceCents);
        Assert.Equal(0, machine.Inventory.CoinCountOf(Denomination.Cent2));
        Assert.Equal(MachineState.AwaitingSelection, machine.State);
    }

    [Fact]
    public void InsertCoin_rejects_coins_while_dispensing()
    {
        var (machine, _) = CreateMachine(inventory: SeededInventory(items: new Dictionary<string, int> { ["latte"] = 3 }));
        Insert(machine, Denomination.Dollar1, Denomination.Dollar1, Denomination.Dollar1);
        machine.SelectItem("latte");
        Assert.Equal(MachineState.Dispensing, machine.State);

        var result = machine.InsertCoin(Denomination.Dollar2);

        Assert.False(result.Accepted);
        Assert.Equal(VendingMachine.BusyRejectionReason, result.RejectionReason);
        Assert.Equal(0, machine.BalanceCents);
        Assert.Equal(3, machine.Inventory.CoinCountOf(Denomination.Dollar1));
        Assert.Equal(0, machine.Inventory.CoinCountOf(Denomination.Dollar2));
    }

    // ---------- Purchases ----------

    [Fact]
    public void SelectItem_exact_payment_purchase_succeeds_with_no_change()
    {
        var (machine, ledger) = CreateMachine(inventory: SeededInventory(items: new Dictionary<string, int> { ["latte"] = 3 }));
        Insert(machine, Denomination.Dollar1, Denomination.Dollar1, Denomination.Dollar1);

        var result = machine.SelectItem("latte");

        Assert.True(result.Success);
        Assert.Null(result.FailureReason);
        Assert.Equal("latte", result.Item!.Id);
        Assert.Equal("Latte", result.Item.Name);
        Assert.Equal(300, result.PriceCents);
        Assert.Equal(300, result.PaidCents);
        Assert.Equal(0, result.ChangeTotalCents);
        Assert.Empty(result.Change!);
        Assert.NotNull(result.TransactionId);

        Assert.Equal(0, machine.BalanceCents);
        Assert.Equal(MachineState.Dispensing, machine.State);
        Assert.Equal(2, machine.Inventory.ItemCountOf("latte"));
        // The three paid dollars stay in the stock as change for the next customer.
        Assert.Equal(3, machine.Inventory.CoinCountOf(Denomination.Dollar1));

        var rows = ledger.List(MachineId, 100).ToList();
        Assert.Single(rows);
        Assert.Equal(TransactionStatus.Purchased, rows[0].Status);
        Assert.Equal("latte", rows[0].ItemId);
        Assert.Equal(300, rows[0].PaidCents);
        Assert.Equal(0, rows[0].ChangeCents);
        Assert.Equal(result.TransactionId, rows[0].Id);
    }

    [Fact]
    public void SelectItem_overspend_returns_change_as_the_single_largest_coin()
    {
        var (machine, _) = CreateMachine(inventory: SeededInventory(items: new Dictionary<string, int> { ["decaf"] = 3 }));
        Insert(machine, Denomination.Dollar2, Denomination.Dollar2, Denomination.Dollar2); // $6.00

        var result = machine.SelectItem("decaf"); // $4.00

        Assert.True(result.Success);
        Assert.Equal(400, result.PriceCents);
        Assert.Equal(600, result.PaidCents);
        Assert.Equal(200, result.ChangeTotalCents);
        Assert.Equal(1, result.Change![Denomination.Dollar2]);
        Assert.Single(result.Change);

        Assert.Equal(0, machine.BalanceCents);
        // 3×$2 inserted, 1×$2 dispensed as change.
        Assert.Equal(2, machine.Inventory.CoinCountOf(Denomination.Dollar2));
    }

    [Fact]
    public void SelectItem_unknown_item_fails_and_keeps_balance()
    {
        var (machine, ledger) = CreateMachine();
        Insert(machine, Denomination.Dollar1);

        var result = machine.SelectItem("espresso");

        Assert.False(result.Success);
        Assert.Equal(SelectionFailureReason.UnknownItem, result.FailureReason);
        Assert.Equal(100, machine.BalanceCents);
        Assert.Equal(MachineState.AwaitingSelection, machine.State);
        Assert.Empty(ledger.List(MachineId, 100));
    }

    [Fact]
    public void SelectItem_insufficient_funds_fails_and_keeps_balance()
    {
        var (machine, ledger) = CreateMachine(inventory: SeededInventory(items: new Dictionary<string, int> { ["latte"] = 3 }));
        Insert(machine, Denomination.Dollar2); // $2.00 < $3.00

        var result = machine.SelectItem("latte");

        Assert.False(result.Success);
        Assert.Equal(SelectionFailureReason.InsufficientFunds, result.FailureReason);
        Assert.Equal(200, machine.BalanceCents);
        Assert.Equal(3, machine.Inventory.ItemCountOf("latte"));
        Assert.Empty(ledger.List(MachineId, 100));
    }

    [Fact]
    public void SelectItem_fails_with_out_of_stock_after_the_last_item_is_sold()
    {
        var (machine, ledger) = CreateMachine(inventory: SeededInventory(items: new Dictionary<string, int> { ["cappuccino"] = 1 }));
        Insert(machine, Denomination.Dollar2, Denomination.Dollar1, Denomination.Cent50); // $3.50
        var first = machine.SelectItem("cappuccino");
        Assert.True(first.Success);
        machine.CompleteDispense();

        Insert(machine, Denomination.Dollar2, Denomination.Dollar1, Denomination.Cent50); // $3.50 again
        var second = machine.SelectItem("cappuccino");

        Assert.False(second.Success);
        Assert.Equal(SelectionFailureReason.OutOfStock, second.FailureReason);
        Assert.Equal(350, machine.BalanceCents);
        Assert.Equal(0, machine.Inventory.ItemCountOf("cappuccino"));
        Assert.Single(ledger.List(MachineId, 100));
    }

    [Fact]
    public void SelectItem_fails_with_exact_change_only_when_stock_cannot_make_the_change()
    {
        // Stock holds exactly the two $2 coins the customer just inserted:
        // $4.00 credit for a $3.00 latte needs 100¢ change, but {200, 200}
        // cannot make 100¢ (needs two 50¢ coins) — so the sale is refused.
        var (machine, ledger) = CreateMachine(inventory: SeededInventory(items: new Dictionary<string, int> { ["latte"] = 3 }));
        Insert(machine, Denomination.Dollar2, Denomination.Dollar2);

        var result = machine.SelectItem("latte");

        Assert.False(result.Success);
        Assert.Equal(SelectionFailureReason.ExactChangeOnly, result.FailureReason);
        Assert.Equal(400, machine.BalanceCents);
        Assert.Equal(3, machine.Inventory.ItemCountOf("latte"));
        Assert.Equal(MachineState.AwaitingSelection, machine.State);
        Assert.Empty(ledger.List(MachineId, 100));

        // The customer can still get their money back.
        var refund = machine.Cancel();
        Assert.Equal(400, refund.RefundedCents);
        Assert.Equal(2, refund.Refund[Denomination.Dollar2]);
    }

    [Fact]
    public void SelectItem_while_dispensing_with_zero_balance_fails_with_insufficient_funds()
    {
        var (machine, _) = CreateMachine(inventory: SeededInventory(items: new Dictionary<string, int> { ["latte"] = 3 }));
        Insert(machine, Denomination.Dollar1, Denomination.Dollar1, Denomination.Dollar1);
        Assert.True(machine.SelectItem("latte").Success);

        var result = machine.SelectItem("latte");

        Assert.False(result.Success);
        Assert.Equal(SelectionFailureReason.InsufficientFunds, result.FailureReason);
        Assert.Equal(MachineState.Dispensing, machine.State);
    }

    // ---------- State machine ----------

    [Fact]
    public void State_transitions_cover_idle_awaiting_selection_dispensing_idle()
    {
        var (machine, _) = CreateMachine(inventory: SeededInventory(items: new Dictionary<string, int> { ["latte"] = 3 }));

        Assert.Equal(MachineState.Idle, machine.State);

        Insert(machine, Denomination.Dollar1, Denomination.Dollar1, Denomination.Dollar1);
        Assert.Equal(MachineState.AwaitingSelection, machine.State);

        machine.SelectItem("latte");
        Assert.Equal(MachineState.Dispensing, machine.State);

        machine.CompleteDispense();
        Assert.Equal(MachineState.Idle, machine.State);
    }

    [Fact]
    public void CompleteDispense_is_idempotent_outside_dispensing()
    {
        var (machine, _) = CreateMachine();

        machine.CompleteDispense();

        Assert.Equal(MachineState.Idle, machine.State);
    }

    // ---------- Cancel ----------

    [Fact]
    public void Cancel_refunds_the_full_balance_and_zeroes_it()
    {
        var (machine, ledger) = CreateMachine();
        Insert(machine, Denomination.Dollar2, Denomination.Dollar2, Denomination.Cent50); // $4.50

        var result = machine.Cancel();

        Assert.Equal(450, result.RefundedCents);
        Assert.Equal(2, result.Refund[Denomination.Dollar2]);
        Assert.Equal(1, result.Refund[Denomination.Cent50]);
        Assert.Equal(0, machine.BalanceCents);
        Assert.Equal(0, machine.Inventory.CoinCount);
        Assert.Equal(MachineState.Idle, machine.State);

        var rows = ledger.List(MachineId, 100).ToList();
        Assert.Single(rows);
        Assert.Equal(TransactionStatus.Cancelled, rows[0].Status);
        Assert.Equal(string.Empty, rows[0].ItemId);
        Assert.Equal(450, rows[0].PaidCents);
        Assert.Equal(0, rows[0].ChangeCents);
    }

    [Fact]
    public void Cancel_with_no_credit_is_a_noop_and_records_nothing()
    {
        var (machine, ledger) = CreateMachine();

        var result = machine.Cancel();

        Assert.Equal(0, result.RefundedCents);
        Assert.Empty(result.Refund);
        Assert.Equal(MachineState.Idle, machine.State);
        Assert.Empty(ledger.List(MachineId, 100));
    }

    [Fact]
    public void Cancel_while_dispensing_is_a_noop_and_keeps_dispensing_state()
    {
        var (machine, _) = CreateMachine(inventory: SeededInventory(items: new Dictionary<string, int> { ["latte"] = 3 }));
        Insert(machine, Denomination.Dollar1, Denomination.Dollar1, Denomination.Dollar1);
        machine.SelectItem("latte");

        var result = machine.Cancel();

        Assert.Equal(0, result.RefundedCents);
        Assert.Equal(MachineState.Dispensing, machine.State);
    }

    // ---------- Ledger ----------

    [Fact]
    public void Ledger_records_one_purchased_row_per_sale_and_one_cancelled_row_per_cancel()
    {
        var (machine, ledger) = CreateMachine(inventory: SeededInventory(items: new Dictionary<string, int> { ["latte"] = 3, ["cappuccino"] = 2 }));

        Insert(machine, Denomination.Dollar1, Denomination.Dollar1, Denomination.Dollar1);
        machine.SelectItem("latte"); // Purchased #1
        machine.CompleteDispense();

        Insert(machine, Denomination.Dollar2);
        machine.Cancel(); // Cancelled

        Insert(machine, Denomination.Dollar2, Denomination.Dollar1, Denomination.Cent50);
        machine.SelectItem("cappuccino"); // Purchased #2

        var rows = ledger.List(MachineId, 100).ToList();

        Assert.Equal(3, rows.Count);
        // Newest first.
        Assert.Equal(TransactionStatus.Purchased, rows[0].Status);
        Assert.Equal("cappuccino", rows[0].ItemId);
        Assert.Equal(TransactionStatus.Cancelled, rows[1].Status);
        Assert.Equal(TransactionStatus.Purchased, rows[2].Status);
        Assert.Equal("latte", rows[2].ItemId);
    }

    // ---------- Restore ----------

    [Fact]
    public void Restore_rebuilds_state_balance_and_stock_and_the_machine_can_continue()
    {
        var (original, _) = CreateMachine(inventory: SeededInventory(items: new Dictionary<string, int> { ["latte"] = 2 }, coins: new Dictionary<Denomination, int> { [Denomination.Dollar1] = 5 }));
        Insert(original, Denomination.Dollar2); // balance 200, state AwaitingSelection

        var restoredLedger = new InMemoryTransactionLedger();
        var rebuilt = VendingMachine.Restore(
            original.Id,
            original.State,
            original.BalanceCents,
            original.Inventory.CoinStock,
            original.Inventory.ItemStock,
            restoredLedger);

        Assert.Equal(original.Id, rebuilt.Id);
        Assert.Equal(MachineState.AwaitingSelection, rebuilt.State);
        Assert.Equal(200, rebuilt.BalanceCents);
        Assert.Equal(5, rebuilt.Inventory.CoinCountOf(Denomination.Dollar1));
        Assert.Equal(1, rebuilt.Inventory.CoinCountOf(Denomination.Dollar2));
        Assert.Equal(2, rebuilt.Inventory.ItemCountOf("latte"));

        // The rebuilt machine is fully functional.
        Insert(rebuilt, Denomination.Dollar1); // 300
        var purchase = rebuilt.SelectItem("latte");
        Assert.True(purchase.Success);
        Assert.Equal(0, purchase.ChangeTotalCents);
        Assert.Equal(MachineState.Dispensing, rebuilt.State);

        var rows = restoredLedger.List(original.Id, 100).ToList();
        Assert.Single(rows);
        Assert.Equal(TransactionStatus.Purchased, rows[0].Status);
        Assert.Equal(original.Id, rows[0].MachineId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Restore_rejects_a_negative_balance(int balanceCents)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VendingMachine.Restore(
            MachineId,
            MachineState.Idle,
            balanceCents,
            new Dictionary<Denomination, int>(),
            new Dictionary<string, int>()));
    }

    [Fact]
    public void Restore_rejects_negative_stock_counts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VendingMachine.Restore(
            MachineId,
            MachineState.Idle,
            0,
            new Dictionary<Denomination, int> { [Denomination.Dollar1] = -2 },
            new Dictionary<string, int>()));
    }
}
