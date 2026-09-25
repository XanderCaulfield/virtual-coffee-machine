using System.Text.Json;
using CoffeeMachine.Domain.Machine;
using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Api.Data;

/// <summary>
/// Serializes machine stock maps to and from the JSON text columns of
/// <see cref="MachineEntity"/>. Coin stock is keyed by denomination cents
/// (the raw int value), item stock by item id.
/// </summary>
/// <remarks>
/// Deserialization is defensive by design: unknown or negative entries are
/// dropped, and structurally invalid JSON is reported as a failed parse
/// instead of throwing, so a hand-edited database row can never crash
/// machine hydration (see <see cref="TryDeserializeCoins"/>).
/// </remarks>
internal static class StockJson
{
    /// <summary>Serializes a denomination → count map keyed by cent value.</summary>
    public static string SerializeCoins(IReadOnlyDictionary<Denomination, int> coins) =>
        JsonSerializer.Serialize(coins.ToDictionary(pair => pair.Key.ValueCents(), pair => pair.Value));

    /// <summary>
    /// Deserializes a coin stock JSON map. Unknown or negative entries are
    /// dropped defensively; invalid JSON yields an empty map.
    /// </summary>
    public static IReadOnlyDictionary<Denomination, int> DeserializeCoins(string json) =>
        TryDeserializeCoins(json, out var coins) ? coins : new Dictionary<Denomination, int>();

    /// <summary>
    /// Attempts to deserialize a coin stock JSON map. Returns false when the
    /// JSON is structurally invalid (never throws).
    /// </summary>
    public static bool TryDeserializeCoins(string json, out IReadOnlyDictionary<Denomination, int> coins)
    {
        coins = new Dictionary<Denomination, int>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        Dictionary<int, int> raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<int, int>>(json) ?? new Dictionary<int, int>();
        }
        catch (JsonException)
        {
            return false;
        }

        var result = new Dictionary<Denomination, int>();
        foreach (var (cents, count) in raw)
        {
            if (count < 0)
            {
                continue;
            }

            if (Denominations.TryParse(cents, out var denomination))
            {
                result[denomination] = count;
            }
        }

        coins = result;
        return true;
    }

    /// <summary>Serializes an item id → count map.</summary>
    public static string SerializeItems(IReadOnlyDictionary<string, int> items) =>
        JsonSerializer.Serialize(items);

    /// <summary>Deserializes an item stock JSON map; negative counts are dropped and invalid JSON yields an empty map.</summary>
    public static IReadOnlyDictionary<string, int> DeserializeItems(string json) =>
        TryDeserializeItems(json, out var items) ? items : new Dictionary<string, int>();

    /// <summary>
    /// Attempts to deserialize an item stock JSON map. Returns false when the
    /// JSON is structurally invalid (never throws).
    /// </summary>
    public static bool TryDeserializeItems(string json, out IReadOnlyDictionary<string, int> items)
    {
        items = new Dictionary<string, int>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        Dictionary<string, int> raw;
        try
        {
            raw = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
        }
        catch (JsonException)
        {
            return false;
        }

        items = raw.Where(pair => pair.Value >= 0).ToDictionary(pair => pair.Key, pair => pair.Value);
        return true;
    }

    /// <summary>Serializes a machine state name.</summary>
    public static string SerializeState(MachineState state) => state.ToString();

    /// <summary>Deserializes a machine state name, falling back to Idle.</summary>
    public static MachineState DeserializeState(string value) =>
        Enum.TryParse<MachineState>(value, out var state) ? state : MachineState.Idle;
}
