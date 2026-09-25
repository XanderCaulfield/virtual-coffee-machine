using CoffeeMachine.Api;
using CoffeeMachine.Api.Data;
using CoffeeMachine.Domain.Machine;
using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Api.Tests;

/// <summary>
/// Verifies the LED status text derivation across the affordability and
/// exact-change boundaries.
/// </summary>
public class MachineStatusTests
{
    private static readonly Dictionary<Denomination, int> FullStock = new()
    {
        [Denomination.Cent5] = 20,
        [Denomination.Cent10] = 20,
        [Denomination.Cent20] = 20,
        [Denomination.Cent50] = 20,
        [Denomination.Dollar1] = 20,
        [Denomination.Dollar2] = 20,
    };

    private static readonly Dictionary<string, int> FullItems = new()
    {
        ["cappuccino"] = 10,
        ["latte"] = 10,
        ["decaf"] = 10,
    };

    private static MachineRegistry.MachineSnapshot Snapshot(
        int balanceCents,
        IReadOnlyDictionary<Denomination, int>? coinStock = null,
        IReadOnlyDictionary<string, int>? itemStock = null) =>
        new("machine-1", MachineState.AwaitingSelection, balanceCents,
            coinStock ?? FullStock, itemStock ?? FullItems);

    [Fact]
    public void No_credit_shows_insert_coin()
    {
        Assert.Equal(MachineStatus.InsertCoin, MachineStatus.For(Snapshot(0)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(100)]
    [InlineData(299)]
    public void Credit_below_the_cheapest_drink_shows_select_drink(int balanceCents)
    {
        Assert.Equal(MachineStatus.SelectDrink, MachineStatus.For(Snapshot(balanceCents)));
    }

    [Theory]
    [InlineData(300)]  // exact latte
    [InlineData(350)]  // exact cappuccino
    [InlineData(400)]  // exact decaf
    [InlineData(1000)] // plenty of credit, full stock can pay any change
    public void Affordable_drinks_with_payable_change_show_select_drink(int balanceCents)
    {
        Assert.Equal(MachineStatus.SelectDrink, MachineStatus.For(Snapshot(balanceCents)));
    }

    [Fact]
    public void Affordable_but_unpayable_change_shows_exact_change_only()
    {
        // $6 of credit, only lattes in stock, and the coin stock holds ONLY
        // $2 coins: the $3 change for a latte can never be made.
        var snapshot = Snapshot(600, new Dictionary<Denomination, int> { [Denomination.Dollar2] = 20 },
            new Dictionary<string, int> { ["latte"] = 10 });

        Assert.Equal(MachineStatus.ExactChangeOnly, MachineStatus.For(snapshot));
    }

    [Fact]
    public void Exact_change_boundary_one_selectable_drink_is_enough()
    {
        // Same $2-only stock, but decaf is also in stock and its change
        // ($6.00 - $4.00 = one $2 coin) is payable — so no exact-change mode.
        var snapshot = Snapshot(600, new Dictionary<Denomination, int> { [Denomination.Dollar2] = 20 },
            new Dictionary<string, int> { ["latte"] = 10, ["decaf"] = 10 });

        Assert.Equal(MachineStatus.SelectDrink, MachineStatus.For(snapshot));
    }

    [Fact]
    public void Exact_payment_requires_no_change_so_it_is_always_selectable()
    {
        // Zero change is trivially payable, so an exact payment stays
        // selectable even from a $2-only stock (decaf $4.00 paid with 2×$2).
        var snapshot = Snapshot(400, new Dictionary<Denomination, int> { [Denomination.Dollar2] = 20 },
            new Dictionary<string, int> { ["decaf"] = 10 });

        Assert.Equal(MachineStatus.SelectDrink, MachineStatus.For(snapshot));
    }

    [Fact]
    public void Credit_with_nothing_in_stock_shows_select_drink_not_exact_change()
    {
        // Exact-change mode requires at least one affordable, in-stock drink.
        var snapshot = Snapshot(1000, FullStock, new Dictionary<string, int>());

        Assert.Equal(MachineStatus.SelectDrink, MachineStatus.For(snapshot));
    }
}
