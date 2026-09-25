using CoffeeMachine.Client.Services;
using CoffeeMachine.Contracts;

namespace CoffeeMachine.Client.Tests;

/// <summary>Verifies the shared affordability checks that drive the drink-button glow and LED text.</summary>
public class MenuUiTests
{
    private static MenuItemDto Drink(string id, int priceCents, bool inStock = true) =>
        new(id, id, priceCents, "$0.00", inStock);

    [Fact]
    public void CanAfford_is_true_when_in_stock_and_balance_covers_the_price()
    {
        Assert.True(MenuUi.CanAfford(Drink("latte", 300), 300));
        Assert.True(MenuUi.CanAfford(Drink("latte", 300), 305));
    }

    [Fact]
    public void CanAfford_is_false_when_out_of_stock_even_with_enough_credit()
    {
        Assert.False(MenuUi.CanAfford(Drink("latte", 300, inStock: false), 1000));
    }

    [Fact]
    public void CanAfford_is_false_when_the_balance_is_short()
    {
        Assert.False(MenuUi.CanAfford(Drink("latte", 300), 299));
        Assert.False(MenuUi.CanAfford(Drink("latte", 300), 0));
    }

    [Fact]
    public void CanAffordAny_is_true_when_any_drink_is_affordable()
    {
        var menu = new[] { Drink("cappuccino", 350), Drink("latte", 300), Drink("decaf", 400) };

        Assert.True(MenuUi.CanAffordAny(menu, 300));
        Assert.True(MenuUi.CanAffordAny(menu, 1000));
    }

    [Fact]
    public void CanAffordAny_is_false_with_no_credit_or_nothing_in_stock()
    {
        var menu = new[] { Drink("cappuccino", 350), Drink("latte", 300), Drink("decaf", 400) };

        Assert.False(MenuUi.CanAffordAny(menu, 0));
        Assert.False(MenuUi.CanAffordAny(menu, 299));
        Assert.False(MenuUi.CanAffordAny(
            new[] { Drink("cappuccino", 350, inStock: false) },
            1000));
    }
}
