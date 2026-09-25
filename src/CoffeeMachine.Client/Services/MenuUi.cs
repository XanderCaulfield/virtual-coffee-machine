using CoffeeMachine.Contracts;

namespace CoffeeMachine.Client.Services;

/// <summary>
/// Shared affordability checks for the machine UI. The server stays
/// authoritative (it owns the exact-change stock logic); these helpers only
/// drive the instant client-side feedback — drink-button glow and LED text —
/// between requests.
/// </summary>
internal static class MenuUi
{
    /// <summary>True when the drink is in stock and the balance covers it.</summary>
    public static bool CanAfford(MenuItemDto item, int balanceCents) =>
        item.InStock && balanceCents >= item.PriceCents;

    /// <summary>True when at least one menu item can currently be bought.</summary>
    public static bool CanAffordAny(IReadOnlyList<MenuItemDto> menu, int balanceCents) =>
        menu.Any(item => CanAfford(item, balanceCents));
}
