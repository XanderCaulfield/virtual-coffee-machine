using System.Globalization;

namespace CoffeeMachine.Domain.Money;

/// <summary>
/// Formats cent amounts as dollar strings for display. This is the single
/// source of truth for money formatting: the API and the Blazor client both
/// call <see cref="Format"/>, so a price or balance renders identically on
/// both sides of the wire.
/// </summary>
public static class MoneyFormat
{
    /// <summary>
    /// Formats a cent amount as a dollar string, e.g. 350 → "$3.50".
    /// Uses the invariant culture so output is stable regardless of locale.
    /// </summary>
    /// <param name="cents">The amount in cents. May be negative.</param>
    /// <returns>The formatted amount, e.g. "$3.50" or "-$0.50".</returns>
    public static string Format(int cents)
    {
        var sign = cents < 0 ? "-" : string.Empty;
        var absolute = Math.Abs(cents);
        var dollars = absolute / 100;
        var remainder = absolute % 100;
        return string.Create(CultureInfo.InvariantCulture, $"{sign}${dollars}.{remainder:D2}");
    }
}
