namespace CoffeeMachine.Domain.Menu;

/// <summary>
/// A product the machine sells.
/// </summary>
/// <param name="Id">Stable string identifier, e.g. "cappuccino".</param>
/// <param name="Name">Display name, e.g. "Cappuccino".</param>
/// <param name="PriceCents">Price in cents, e.g. 350 for $3.50. Must be &gt;= 0.</param>
public sealed record MenuItem(string Id, string Name, int PriceCents)
{
    /// <summary>
    /// The price formatted as a currency string, e.g. "$3.50".
    /// </summary>
    public string PriceDisplay => CoffeeMenu.FormatPrice(PriceCents);
}
