using CoffeeMachine.Domain.Menu;

namespace CoffeeMachine.Domain.Tests;

/// <summary>Verifies the fixed catalogue and the price formatting helper.</summary>
public class CoffeeMenuTests
{
    [Fact]
    public void Menu_contains_the_three_expected_items_with_stable_ids_and_prices()
    {
        Assert.Equal(3, CoffeeMenu.All.Count);

        Assert.Equal("cappuccino", CoffeeMenu.Cappuccino.Id);
        Assert.Equal("Cappuccino", CoffeeMenu.Cappuccino.Name);
        Assert.Equal(350, CoffeeMenu.Cappuccino.PriceCents);

        Assert.Equal("latte", CoffeeMenu.Latte.Id);
        Assert.Equal("Latte", CoffeeMenu.Latte.Name);
        Assert.Equal(300, CoffeeMenu.Latte.PriceCents);

        Assert.Equal("decaf", CoffeeMenu.Decaf.Id);
        Assert.Equal("Decaf", CoffeeMenu.Decaf.Name);
        Assert.Equal(400, CoffeeMenu.Decaf.PriceCents);
    }

    [Fact]
    public void MenuItem_PriceDisplay_formats_dollars_and_cents()
    {
        Assert.Equal("$3.50", CoffeeMenu.Cappuccino.PriceDisplay);
        Assert.Equal("$3.00", CoffeeMenu.Latte.PriceDisplay);
        Assert.Equal("$4.00", CoffeeMenu.Decaf.PriceDisplay);
    }

    [Fact]
    public void Find_looks_items_up_by_id_case_insensitively()
    {
        Assert.Same(CoffeeMenu.Latte, CoffeeMenu.Find("latte"));
        Assert.Same(CoffeeMenu.Latte, CoffeeMenu.Find("LATTE"));
        Assert.Same(CoffeeMenu.Decaf, CoffeeMenu.Find("decaf"));
    }

    [Fact]
    public void Find_returns_null_for_unknown_ids()
    {
        Assert.Null(CoffeeMenu.Find("espresso"));
        Assert.Null(CoffeeMenu.Find(""));
    }
}
