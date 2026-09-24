using CoffeeMachine.Domain.Machine;
using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Domain.Tests;

/// <summary>Verifies stock bookkeeping and change dispensing for the machine inventory.</summary>
public class InventoryTests
{
    [Fact]
    public void AddCoin_and_RemoveCoin_track_counts_and_value()
    {
        var inventory = new Inventory();

        inventory.AddCoin(Denomination.Dollar1, 2);
        inventory.AddCoin(Denomination.Cent50);

        Assert.Equal(3, inventory.CoinCount);
        Assert.Equal(250, inventory.TotalValueCents);
        Assert.Equal(2, inventory.CoinCountOf(Denomination.Dollar1));
        Assert.Equal(1, inventory.CoinCountOf(Denomination.Cent50));
        Assert.Equal(0, inventory.CoinCountOf(Denomination.Dollar2));

        Assert.True(inventory.RemoveCoin(Denomination.Dollar1));
        Assert.Equal(1, inventory.CoinCountOf(Denomination.Dollar1));
        Assert.Equal(150, inventory.TotalValueCents);
    }

    [Fact]
    public void RemoveCoin_fails_without_mutating_when_not_enough_coins_are_held()
    {
        var inventory = new Inventory();
        inventory.AddCoin(Denomination.Cent5, 1);

        var removed = inventory.RemoveCoin(Denomination.Cent5, 2);

        Assert.False(removed);
        Assert.Equal(1, inventory.CoinCountOf(Denomination.Cent5));
    }

    [Fact]
    public void RemoveCoin_removes_the_key_when_the_last_coin_goes()
    {
        var inventory = new Inventory();
        inventory.AddCoin(Denomination.Cent10);

        Assert.True(inventory.RemoveCoin(Denomination.Cent10));
        Assert.False(inventory.CoinStock.ContainsKey(Denomination.Cent10));
        Assert.Equal(0, inventory.CoinCount);
    }

    [Fact]
    public void AddItem_and_RemoveItem_track_counts()
    {
        var inventory = new Inventory();

        inventory.AddItem("latte", 2);
        inventory.AddItem("decaf");

        Assert.Equal(2, inventory.ItemCountOf("latte"));
        Assert.Equal(1, inventory.ItemCountOf("decaf"));
        Assert.Equal(0, inventory.ItemCountOf("cappuccino"));

        Assert.True(inventory.RemoveItem("latte"));
        Assert.Equal(1, inventory.ItemCountOf("latte"));
    }

    [Fact]
    public void RemoveItem_fails_without_mutating_when_not_enough_items_are_held()
    {
        var inventory = new Inventory();
        inventory.AddItem("latte", 1);

        var removed = inventory.RemoveItem("latte", 3);

        Assert.False(removed);
        Assert.Equal(1, inventory.ItemCountOf("latte"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AddCoin_throws_for_negative_counts(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Inventory().AddCoin(Denomination.Cent5, count));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void RemoveCoin_throws_for_negative_counts(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Inventory().RemoveCoin(Denomination.Cent5, count));
    }

    [Fact]
    public void AddItem_throws_for_negative_counts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Inventory().AddItem("latte", -1));
    }

    [Fact]
    public void RemoveItem_throws_for_negative_counts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Inventory().RemoveItem("latte", -1));
    }

    [Fact]
    public void CanMakeChange_is_true_when_stock_covers_the_amount()
    {
        var inventory = new Inventory();
        inventory.AddCoin(Denomination.Dollar1, 3);

        Assert.True(inventory.CanMakeChange(300));
        Assert.True(inventory.CanMakeChange(200));
        Assert.True(inventory.CanMakeChange(0));
        Assert.False(inventory.CanMakeChange(50));
        Assert.False(inventory.CanMakeChange(305));
    }

    [Fact]
    public void CanMakeChange_throws_for_negative_amounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Inventory().CanMakeChange(-5));
    }

    [Fact]
    public void MakeChange_dispenses_the_minimal_breakdown_and_mutates_the_stock()
    {
        var inventory = new Inventory();
        inventory.AddCoin(Denomination.Dollar1, 5);
        inventory.AddCoin(Denomination.Cent50, 2);

        var change = inventory.MakeChange(250);

        Assert.NotNull(change);
        Assert.Equal(2, change![Denomination.Dollar1]);
        Assert.Equal(1, change[Denomination.Cent50]);
        Assert.Equal(3, inventory.CoinCountOf(Denomination.Dollar1));
        Assert.Equal(1, inventory.CoinCountOf(Denomination.Cent50));
    }

    [Fact]
    public void MakeChange_returns_null_and_keeps_stock_when_unpayable()
    {
        var inventory = new Inventory();
        inventory.AddCoin(Denomination.Dollar2, 1);

        var change = inventory.MakeChange(100);

        Assert.Null(change);
        Assert.Equal(1, inventory.CoinCountOf(Denomination.Dollar2));
    }

    [Fact]
    public void MakeChange_of_zero_returns_empty_and_mutates_nothing()
    {
        var inventory = new Inventory();
        inventory.AddCoin(Denomination.Cent5, 2);

        var change = inventory.MakeChange(0);

        Assert.NotNull(change);
        Assert.Empty(change!);
        Assert.Equal(2, inventory.CoinCountOf(Denomination.Cent5));
    }

    [Fact]
    public void Refill_adds_coins_and_items_in_bulk()
    {
        var inventory = new Inventory();
        inventory.AddCoin(Denomination.Cent20, 1);
        inventory.AddItem("latte", 1);

        inventory.Refill(
            new Dictionary<Denomination, int> { [Denomination.Cent20] = 4, [Denomination.Dollar2] = 2 },
            new Dictionary<string, int> { ["latte"] = 2, ["decaf"] = 1 });

        Assert.Equal(5, inventory.CoinCountOf(Denomination.Cent20));
        Assert.Equal(2, inventory.CoinCountOf(Denomination.Dollar2));
        Assert.Equal(3, inventory.ItemCountOf("latte"));
        Assert.Equal(1, inventory.ItemCountOf("decaf"));
    }

    [Fact]
    public void Stock_snapshots_are_defensive_copies()
    {
        var inventory = new Inventory();
        inventory.AddCoin(Denomination.Dollar1);

        var coinSnapshot = inventory.CoinStock;
        ((Dictionary<Denomination, int>)coinSnapshot)[Denomination.Dollar1] = 99;

        Assert.Equal(1, inventory.CoinCountOf(Denomination.Dollar1));
    }
}
