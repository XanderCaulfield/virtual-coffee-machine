using CoffeeMachine.Api.Data;
using CoffeeMachine.Domain.Menu;
using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Api;

/// <summary>
/// Computes the LED status text shown on the machine display.
/// </summary>
internal static class MachineStatus
{
    /// <summary>Shown when the machine holds no credit.</summary>
    public const string InsertCoin = "INSERT COIN";

    /// <summary>Shown when there is credit and at least one drink can be bought.</summary>
    public const string SelectDrink = "SELECT DRINK";

    /// <summary>
    /// Shown when there is credit but every affordable, in-stock drink would
    /// require change the coin stock cannot pay.
    /// </summary>
    public const string ExactChangeOnly = "EXACT CHANGE ONLY";

    /// <summary>
    /// Derives the status message from a machine snapshot.
    /// </summary>
    /// <remarks>
    /// A drink is <em>selectable</em> when it is in stock, the balance covers
    /// its price, and the coin stock can pay the exact difference. The machine
    /// shows <see cref="ExactChangeOnly"/> only when at least one drink is
    /// affordable and in stock but none of those drinks is selectable — i.e.
    /// every purchase the customer could attempt would fail with the
    /// "exact-change-only" error. Otherwise, with any credit at all, it shows
    /// <see cref="SelectDrink"/> (more coins may still be needed).
    /// </remarks>
    /// <param name="snapshot">The machine state to evaluate.</param>
    /// <returns>The status text.</returns>
    public static string For(MachineRegistry.MachineSnapshot snapshot)
    {
        if (snapshot.BalanceCents == 0)
        {
            return InsertCoin;
        }

        var affordableInStock = CoffeeMenu.All
            .Where(item =>
                snapshot.ItemStock.GetValueOrDefault(item.Id) > 0 &&
                snapshot.BalanceCents >= item.PriceCents)
            .ToList();

        if (affordableInStock.Count == 0)
        {
            return SelectDrink;
        }

        var anySelectable = affordableInStock.Any(item =>
            ChangeCalculator.CanMakeChange(snapshot.BalanceCents - item.PriceCents, snapshot.CoinStock));

        return anySelectable ? SelectDrink : ExactChangeOnly;
    }
}
