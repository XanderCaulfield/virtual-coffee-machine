using CoffeeMachine.Api.Data;
using CoffeeMachine.Domain.Machine;
using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Api.Tests;

/// <summary>Verifies the JSON stock column serialization and its corrupt-input tolerance.</summary>
public class StockJsonTests
{
    [Fact]
    public void Coins_round_trip_through_json()
    {
        var stock = new Dictionary<Denomination, int>
        {
            [Denomination.Cent5] = 3,
            [Denomination.Cent50] = 0,
            [Denomination.Dollar2] = 12,
        };

        var json = StockJson.SerializeCoins(stock);
        var restored = StockJson.DeserializeCoins(json);

        Assert.Equal(3, restored[Denomination.Cent5]);
        Assert.Equal(0, restored[Denomination.Cent50]); // zero counts round-trip too
        Assert.Equal(12, restored[Denomination.Dollar2]);
    }

    [Fact]
    public void Items_round_trip_through_json()
    {
        var stock = new Dictionary<string, int> { ["latte"] = 7, ["decaf"] = 2 };

        var restored = StockJson.DeserializeItems(StockJson.SerializeItems(stock));

        Assert.Equal(stock, restored);
    }

    [Fact]
    public void DeserializeCoins_drops_negative_and_unknown_entries()
    {
        var coins = StockJson.DeserializeCoins("""{"5":3,"10":-2,"25":4,"200":1}""");

        Assert.Equal(3, coins[Denomination.Cent5]);
        Assert.Equal(1, coins[Denomination.Dollar2]);
        Assert.Equal(2, coins.Count);
    }

    [Theory]
    [InlineData("{not valid json")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"a\":1}")]
    [InlineData("42")]
    public void Corrupt_coin_json_never_throws(string json)
    {
        Assert.False(StockJson.TryDeserializeCoins(json, out _));
        Assert.Empty(StockJson.DeserializeCoins(json));
    }

    [Theory]
    [InlineData("{not valid json")]
    [InlineData("[1,2,3]")]
    public void Corrupt_item_json_never_throws(string json)
    {
        Assert.False(StockJson.TryDeserializeItems(json, out _));
        Assert.Empty(StockJson.DeserializeItems(json));
    }

    [Fact]
    public void Blank_json_deserializes_to_empty_maps()
    {
        Assert.True(StockJson.TryDeserializeCoins("  ", out var coins));
        Assert.Empty(coins);
        Assert.True(StockJson.TryDeserializeItems("", out var items));
        Assert.Empty(items);
    }

    [Theory]
    [InlineData("Idle", MachineState.Idle)]
    [InlineData("AwaitingSelection", MachineState.AwaitingSelection)]
    [InlineData("Dispensing", MachineState.Dispensing)]
    [InlineData("not-a-state", MachineState.Idle)]
    [InlineData("", MachineState.Idle)]
    public void State_deserializes_with_an_idle_fallback(string stored, MachineState expected)
    {
        Assert.Equal(expected, StockJson.DeserializeState(stored));
    }
}
