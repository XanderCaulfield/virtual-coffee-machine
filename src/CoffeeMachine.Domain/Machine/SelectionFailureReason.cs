namespace CoffeeMachine.Domain.Machine;

/// <summary>
/// Reasons a <see cref="VendingMachine.SelectItem"/> call can fail.
/// The numeric-free enum names are the canonical failure codes surfaced by the
/// API (e.g. "InsufficientFunds" → HTTP 409 problem detail "insufficient-funds").
/// </summary>
public enum SelectionFailureReason
{
    /// <summary>The item id is not on the menu.</summary>
    UnknownItem,

    /// <summary>The item is out of stock.</summary>
    OutOfStock,

    /// <summary>The current balance does not cover the price.</summary>
    InsufficientFunds,

    /// <summary>The machine cannot make the required change from its coin stock.</summary>
    ExactChangeOnly,
}
