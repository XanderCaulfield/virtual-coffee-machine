using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Domain.Machine;

/// <summary>
/// The physical stock of the machine: coins held for change and items held
/// for sale. Inserted coins feed the coin stock; change is drawn from it.
/// </summary>
/// <remarks>
/// Not thread-safe by design: the machine registry (API layer) serialises
/// access per machine, keeping the domain free of locking concerns.
/// </remarks>
public sealed class Inventory
{
    private readonly Dictionary<Denomination, int> _coins = new();
    private readonly Dictionary<string, int> _items = new();

    /// <summary>
    /// The total number of coins held across all denominations.
    /// </summary>
    public int CoinCount => _coins.Values.Sum();

    /// <summary>
    /// The total monetary value of the coin stock, in cents.
    /// </summary>
    public int TotalValueCents => _coins.Sum(pair => pair.Key.ValueCents() * pair.Value);

    /// <summary>
    /// A snapshot of the coin stock (denomination → count).
    /// </summary>
    public IReadOnlyDictionary<Denomination, int> CoinStock => new Dictionary<Denomination, int>(_coins);

    /// <summary>
    /// A snapshot of the item stock (item id → count).
    /// </summary>
    public IReadOnlyDictionary<string, int> ItemStock => new Dictionary<string, int>(_items);

    /// <summary>
    /// Adds coins of a denomination to the stock.
    /// </summary>
    /// <param name="denomination">The denomination to add.</param>
    /// <param name="count">How many coins to add. Defaults to 1.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is negative.</exception>
    public void AddCoin(Denomination denomination, int count = 1)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Coin count cannot be negative.");
        }

        _coins[denomination] = _coins.GetValueOrDefault(denomination) + count;
    }

    /// <summary>
    /// Removes coins of a denomination from the stock.
    /// </summary>
    /// <param name="denomination">The denomination to remove.</param>
    /// <param name="count">How many coins to remove. Defaults to 1.</param>
    /// <returns>True when the coins were removed; false when fewer than <paramref name="count"/> were available.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is negative.</exception>
    public bool RemoveCoin(Denomination denomination, int count = 1)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Coin count cannot be negative.");
        }

        var available = _coins.GetValueOrDefault(denomination);
        if (available < count)
        {
            return false;
        }

        if (available == count)
        {
            _coins.Remove(denomination);
        }
        else
        {
            _coins[denomination] = available - count;
        }

        return true;
    }

    /// <summary>
    /// Gets the number of coins of a denomination in stock.
    /// </summary>
    /// <param name="denomination">The denomination to count.</param>
    /// <returns>The number held; 0 when none.</returns>
    public int CoinCountOf(Denomination denomination) => _coins.GetValueOrDefault(denomination);

    /// <summary>
    /// Adds items to the stock.
    /// </summary>
    /// <param name="itemId">The item id to add.</param>
    /// <param name="count">How many items to add. Defaults to 1.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="itemId"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is negative.</exception>
    public void AddItem(string itemId, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(itemId);

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Item count cannot be negative.");
        }

        _items[itemId] = _items.GetValueOrDefault(itemId) + count;
    }

    /// <summary>
    /// Removes items from the stock.
    /// </summary>
    /// <param name="itemId">The item id to remove.</param>
    /// <param name="count">How many items to remove. Defaults to 1.</param>
    /// <returns>True when the items were removed; false when fewer than <paramref name="count"/> were available.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="itemId"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is negative.</exception>
    public bool RemoveItem(string itemId, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(itemId);

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Item count cannot be negative.");
        }

        var available = _items.GetValueOrDefault(itemId);
        if (available < count)
        {
            return false;
        }

        if (available == count)
        {
            _items.Remove(itemId);
        }
        else
        {
            _items[itemId] = available - count;
        }

        return true;
    }

    /// <summary>
    /// Gets the number of items of an id in stock.
    /// </summary>
    /// <param name="itemId">The item id to count.</param>
    /// <returns>The number held; 0 when none.</returns>
    public int ItemCountOf(string itemId) => _items.GetValueOrDefault(itemId);

    /// <summary>
    /// Determines whether the coin stock can make exact change for
    /// <paramref name="amountCents"/>.
    /// </summary>
    /// <param name="amountCents">The amount in cents. Must be &gt;= 0.</param>
    /// <returns>True when exact change can be made, including the trivial case of 0.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amountCents"/> is negative.</exception>
    public bool CanMakeChange(int amountCents) =>
        ChangeCalculator.CanMakeChange(amountCents, _coins);

    /// <summary>
    /// Dispenses change by removing the computed coins from the stock.
    /// </summary>
    /// <param name="amountCents">The amount to dispense in cents. Must be &gt;= 0.</param>
    /// <returns>
    /// A dictionary mapping denomination to count whose total value equals
    /// <paramref name="amountCents"/> (empty for 0), or null when the stock
    /// cannot cover the amount. The stock is only mutated on success.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="amountCents"/> is negative.</exception>
    public IReadOnlyDictionary<Denomination, int>? MakeChange(int amountCents)
    {
        var change = ChangeCalculator.MakeChange(amountCents, _coins);
        if (change is null)
        {
            return null;
        }

        foreach (var (denomination, count) in change)
        {
            RemoveCoin(denomination, count);
        }

        return change;
    }

    /// <summary>
    /// Restocks coins and items in bulk (the admin service panel).
    /// </summary>
    /// <param name="coins">Denomination → count to add.</param>
    /// <param name="items">Item id → count to add.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is null.</exception>
    public void Refill(IReadOnlyDictionary<Denomination, int> coins, IReadOnlyDictionary<string, int> items)
    {
        ArgumentNullException.ThrowIfNull(coins);
        ArgumentNullException.ThrowIfNull(items);

        foreach (var (denomination, count) in coins)
        {
            AddCoin(denomination, count);
        }

        foreach (var (itemId, count) in items)
        {
            AddItem(itemId, count);
        }
    }
}
