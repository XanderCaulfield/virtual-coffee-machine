using System.Text.Json;
using CoffeeMachine.Domain.Machine;
using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Api.Data;

/// <summary>
/// Serializes machine stock maps to and from the JSON text columns of
/// <see cref="MachineEntity"/>. Coin stock is keyed by denomination cents
/// (the raw int value), item stock by item id.
/// </summary>
internal static class StockJson
{
    /// <summary>Serializes a denomination → count map keyed by cent value.</summary>
    public static string SerializeCoins(IReadOnlyDictionary<Denomination, int> coins) =>
        JsonSerializer.Serialize(coins.ToDictionary(pair => pair.Key.ValueCents(), pair => pair.Value));

    /// <summary>
    /// Deserializes a coin stock JSON map. Unknown or negative entries are
    /// dropped defensively so a hand-edited row can never poison the machine.
    /// </summary>
    public static IReadOnlyDictionary<Denomination, int> DeserializeCoins(string json)
    {
        var result = new Dictionary<Denomination, int>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }

        var raw = JsonSerializer.Deserialize<Dictionary<int, int>>(json) ?? new Dictionary<int, int>();
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

        return result;
    }

    /// <summary>Serializes an item id → count map.</summary>
    public static string SerializeItems(IReadOnlyDictionary<string, int> items) =>
        JsonSerializer.Serialize(items);

    /// <summary>Deserializes an item stock JSON map; negative counts are dropped.</summary>
    public static IReadOnlyDictionary<string, int> DeserializeItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, int>();
        }

        var raw = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new Dictionary<string, int>();
        return raw.Where(pair => pair.Value >= 0).ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>Serializes a machine state name.</summary>
    public static string SerializeState(MachineState state) => state.ToString();

    /// <summary>Deserializes a machine state name, falling back to Idle.</summary>
    public static MachineState DeserializeState(string value) =>
        Enum.TryParse<MachineState>(value, out var state) ? state : MachineState.Idle;
}
