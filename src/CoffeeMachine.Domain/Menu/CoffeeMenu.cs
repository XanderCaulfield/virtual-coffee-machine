namespace CoffeeMachine.Domain.Menu;

/// <summary>
/// The fixed product catalogue of the machine: three coffees with stable
/// string identifiers and prices in cents (Cappuccino $3.50, Latte $3.00,
/// Decaf $4.00).
/// </summary>
public static class CoffeeMenu
{
    /// <summary>Cappuccino, $3.50.</summary>
    public static readonly MenuItem Cappuccino = new("cappuccino", "Cappuccino", 350);

    /// <summary>Latte, $3.00.</summary>
    public static readonly MenuItem Latte = new("latte", "Latte", 300);

    /// <summary>Decaf, $4.00.</summary>
    public static readonly MenuItem Decaf = new("decaf", "Decaf", 400);

    /// <summary>
    /// All menu items in catalogue order: Cappuccino, Latte, Decaf.
    /// </summary>
    public static readonly IReadOnlyList<MenuItem> All = new[] { Cappuccino, Latte, Decaf };

    /// <summary>
    /// Looks up a menu item by its stable id, ignoring case.
    /// </summary>
    /// <param name="itemId">The item id to look up, e.g. "latte".</param>
    /// <returns>The matching item, or null when unknown.</returns>
    public static MenuItem? Find(string itemId)
    {
        foreach (var item in All)
        {
            if (string.Equals(item.Id, itemId, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }
}
