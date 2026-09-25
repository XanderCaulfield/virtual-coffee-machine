namespace CoffeeMachine.Contracts;

/// <summary>
/// Canonical data-transfer contracts shared by the API and the Blazor client.
/// These records are the single source of truth for the HTTP wire format:
/// field names, types and property order are part of the public contract and
/// must not be changed without bumping both the Api and Client projects.
/// </summary>

/// <summary>Describes a single menu item the machine can sell.</summary>
/// <param name="Id">Stable string identifier, e.g. "cappuccino".</param>
/// <param name="Name">Human-readable name, e.g. "Cappuccino".</param>
/// <param name="PriceCents">Price in cents, e.g. 350 for $3.50.</param>
/// <param name="PriceDisplay">Pre-formatted price, e.g. "$3.50".</param>
/// <param name="InStock">Whether the machine currently has stock of this item.</param>
public sealed record MenuItemDto(string Id, string Name, int PriceCents, string PriceDisplay, bool InStock);

/// <summary>Snapshot of the machine's physical inventory.</summary>
/// <param name="Coins">Map of denomination cents (5..200) to count.</param>
/// <param name="Items">Map of item id to stock count.</param>
public sealed record InventoryDto(IReadOnlyDictionary<int, int> Coins, IReadOnlyDictionary<string, int> Items);

/// <summary>Full observable state of a machine, as rendered by the client.</summary>
/// <param name="MachineId">Stable machine identifier (per-visitor GUID).</param>
/// <param name="State">Machine state name: Idle, AwaitingSelection or Dispensing.</param>
/// <param name="BalanceCents">Current credit in cents.</param>
/// <param name="BalanceDisplay">Pre-formatted credit, e.g. "$2.50".</param>
/// <param name="Menu">The menu with per-item stock flags.</param>
/// <param name="Inventory">Current coin and item stock.</param>
/// <param name="StatusMessage">LED status text, e.g. "INSERT COIN", or null when nothing to show.</param>
public sealed record MachineStateDto(string MachineId, string State, int BalanceCents, string BalanceDisplay,
                                     IReadOnlyList<MenuItemDto> Menu, InventoryDto Inventory, string? StatusMessage);

/// <summary>Request body for inserting a coin.</summary>
/// <param name="DenominationCents">Denomination of the inserted coin in cents.</param>
public sealed record InsertCoinRequest(int DenominationCents);

/// <summary>Response body for inserting a coin.</summary>
/// <param name="Accepted">True when the coin was accepted and credited.</param>
/// <param name="RejectionReason">Human-readable reason when rejected, null when accepted.</param>
/// <param name="BalanceCents">Current balance in cents after the operation.</param>
/// <param name="BalanceDisplay">Pre-formatted balance, e.g. "$1.00".</param>
public sealed record InsertCoinResponse(bool Accepted, string? RejectionReason, int BalanceCents, string BalanceDisplay);

/// <summary>Request body for selecting a menu item.</summary>
/// <param name="ItemId">Stable item identifier, e.g. "latte".</param>
public sealed record SelectItemRequest(string ItemId);

/// <summary>One bucket of change coins returned to the customer.</summary>
/// <param name="DenominationCents">Denomination in cents, e.g. 50.</param>
/// <param name="Count">Number of coins of that denomination.</param>
public sealed record ChangeCoinDto(int DenominationCents, int Count);

/// <summary>Response body for a successful purchase.</summary>
/// <param name="ItemId">Identifier of the purchased item.</param>
/// <param name="ItemName">Display name of the purchased item.</param>
/// <param name="PriceCents">Item price in cents.</param>
/// <param name="PaidCents">Total amount tendered by the customer in cents.</param>
/// <param name="ChangeTotalCents">Total change returned in cents.</param>
/// <param name="Change">Change broken down into coins, ordered largest first.</param>
public sealed record PurchaseResponse(string ItemId, string ItemName, int PriceCents, int PaidCents,
                                      int ChangeTotalCents, IReadOnlyList<ChangeCoinDto> Change);

/// <summary>Response body for cancelling a transaction.</summary>
/// <param name="RefundedCents">Total amount refunded in cents.</param>
/// <param name="Refund">Refund broken down into coins, ordered largest first.</param>
public sealed record CancelResponse(int RefundedCents, IReadOnlyList<ChangeCoinDto> Refund);

/// <summary>One ledger row, as served by the transactions endpoint.</summary>
/// <param name="Id">Transaction identifier.</param>
/// <param name="Timestamp">When the transaction was recorded.</param>
/// <param name="MachineId">Machine the transaction belongs to.</param>
/// <param name="ItemId">Item id for purchases; empty string for cancellations.</param>
/// <param name="PaidCents">Amount tendered for purchases; amount refunded for cancellations.</param>
/// <param name="ChangeCents">Change returned for purchases; 0 for cancellations.</param>
/// <param name="Status">"Purchased" or "Cancelled".</param>
public sealed record TransactionDto(Guid Id, DateTimeOffset Timestamp, string MachineId, string ItemId,
                                    int PaidCents, int ChangeCents, string Status);

/// <summary>Request body for the admin refill endpoint.</summary>
/// <param name="CoinDenominations">Parallel array of denomination cents to restock.</param>
/// <param name="CoinCounts">Parallel array of counts to add for each denomination.</param>
/// <param name="Items">Optional map of item id to count to add.</param>
public sealed record RefillRequest(int[] CoinDenominations, int[] CoinCounts, Dictionary<string,int>? Items);
