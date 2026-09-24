using System.Globalization;

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

    /// <summary>
    /// Formats a cent amount as a dollar string, e.g. 350 → "$3.50".
    /// Uses invariant culture so output is stable regardless of server locale.
    /// </summary>
    /// <param name="cents">The amount in cents. May be negative.</param>
    /// <returns>The formatted price, e.g. "$3.50" or "-$0.50".</returns>
    public static string FormatPrice(int cents)
    {
        var sign = cents < 0 ? "-" : string.Empty;
        var absolute = Math.Abs(cents);
        var dollars = absolute / 100;
        var remainder = absolute % 100;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}${dollars}.{remainder:D2}");
    }
}
