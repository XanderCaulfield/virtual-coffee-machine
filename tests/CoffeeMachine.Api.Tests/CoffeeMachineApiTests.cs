using System.Net;
using System.Text;
using System.Text.Json;
using CoffeeMachine.Api.Data;
using CoffeeMachine.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CoffeeMachine.Api.Tests;

/// <summary>
/// End-to-end tests against the real HTTP pipeline (WebApplicationFactory),
/// exercising every endpoint, every selection failure mode, persistence and
/// the RFC 7807 problem details contract.
/// </summary>
public sealed class CoffeeMachineApiTests : IClassFixture<CoffeeMachineApiFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly CoffeeMachineApiFactory _factory;
    private readonly HttpClient _client;

    public CoffeeMachineApiTests(CoffeeMachineApiFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ------------------------------------------------------------------ menu

    [Fact]
    public async Task Menu_Returns_Three_Items_With_Expected_Prices_And_Displays()
    {
        var response = await _client.GetAsync("/api/v1/menu");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var menu = await ReadAsync<List<MenuItemDto>>(response);
        Assert.Equal(
            new[] { "cappuccino", "latte", "decaf" },
            menu.Select(item => item.Id).ToArray());
        Assert.Equal(
            new[] { "Cappuccino", "Latte", "Decaf" },
            menu.Select(item => item.Name).ToArray());
        Assert.Equal(
            new[] { 350, 300, 400 },
            menu.Select(item => item.PriceCents).ToArray());
        Assert.Equal(
            new[] { "$3.50", "$3.00", "$4.00" },
            menu.Select(item => item.PriceDisplay).ToArray());
        Assert.All(menu, item => Assert.True(item.InStock));
    }

    // --------------------------------------------------------------- healthz

    [Fact]
    public async Task Healthz_Returns_200_Ok()
    {
        var response = await _client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("OK", await response.Content.ReadAsStringAsync());
    }

    // ----------------------------------------------------------- coin insert

    [Fact]
    public async Task InsertCoin_Accepted_Updates_Balance_And_Status()
    {
        var id = NewMachineId();

        var initial = await GetStateAsync(id);
        Assert.Equal(0, initial.BalanceCents);
        Assert.Equal("$0.00", initial.BalanceDisplay);
        Assert.Equal("Idle", initial.State);
        Assert.Equal("INSERT COIN", initial.StatusMessage);

        var response = await InsertCoinAsync(id, 100);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsync<InsertCoinResponse>(response);
        Assert.True(result.Accepted);
        Assert.Null(result.RejectionReason);
        Assert.Equal(100, result.BalanceCents);
        Assert.Equal("$1.00", result.BalanceDisplay);

        var state = await GetStateAsync(id);
        Assert.Equal(100, state.BalanceCents);
        Assert.Equal("$1.00", state.BalanceDisplay);
        Assert.Equal("AwaitingSelection", state.State);
        Assert.Equal("SELECT DRINK", state.StatusMessage);
    }

    [Fact]
    public async Task InsertCoin_OneCent_Is_Rejected_But_Not_An_Http_Error()
    {
        var id = NewMachineId();

        var response = await InsertCoinAsync(id, 1);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await ReadAsync<InsertCoinResponse>(response);
        Assert.False(result.Accepted);
        Assert.Equal("1 cent coins are not accepted", result.RejectionReason);
        Assert.Equal(0, result.BalanceCents);
        Assert.Equal("$0.00", result.BalanceDisplay);

        // The rejected coin never touches the balance or the state.
        var state = await GetStateAsync(id);
        Assert.Equal(0, state.BalanceCents);
        Assert.Equal("INSERT COIN", state.StatusMessage);
    }

    [Fact]
    public async Task InsertCoin_TwoCents_Is_Rejected_But_Not_An_Http_Error()
    {
        var id = NewMachineId();

        var response = await InsertCoinAsync(id, 2);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await ReadAsync<InsertCoinResponse>(response);
        Assert.False(result.Accepted);
        Assert.Equal("2 cent coins are not accepted", result.RejectionReason);
        Assert.Equal(0, result.BalanceCents);
    }

    [Fact]
    public async Task InsertCoin_Unknown_Denomination_Returns_400_ProblemDetails()
    {
        var id = NewMachineId();

        var response = await InsertCoinAsync(id, 25);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Invalid coin", problem.RootElement.GetProperty("title").GetString());
    }

    // ------------------------------------------------------------- purchases

    [Fact]
    public async Task Purchase_Latte_With_Exact_Payment_Succeeds_With_No_Change()
    {
        var id = NewMachineId();
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        await InsertCoinAndAssertAcceptedAsync(id, 100);

        var purchase = await SelectAndAssertOkAsync(id, "latte");
        Assert.Equal("latte", purchase.ItemId);
        Assert.Equal("Latte", purchase.ItemName);
        Assert.Equal(300, purchase.PriceCents);
        Assert.Equal(300, purchase.PaidCents);
        Assert.Equal(0, purchase.ChangeTotalCents);
        Assert.Empty(purchase.Change);

        // The dispense completes server-side: the machine is Idle again.
        var state = await GetStateAsync(id);
        Assert.Equal(0, state.BalanceCents);
        Assert.Equal("Idle", state.State);
        Assert.Equal("INSERT COIN", state.StatusMessage);
    }

    [Theory]
    [InlineData("latte", new[] { 200, 200 }, 400, 100, new[] { 100 })]            // $3.00 paid with 2x$2 → one $1 coin
    [InlineData("decaf", new[] { 200, 200, 200 }, 600, 200, new[] { 200 })]       // $4.00 paid with 3x$2 → one $2 coin
    public async Task Purchase_Overspend_Returns_Change_Broken_Into_Coins(
        string itemId,
        int[] coins,
        int expectedPaidCents,
        int expectedChangeCents,
        int[] expectedDenominations)
    {
        var id = NewMachineId();
        foreach (var coin in coins)
        {
            await InsertCoinAndAssertAcceptedAsync(id, coin);
        }

        var purchase = await SelectAndAssertOkAsync(id, itemId);
        Assert.Equal(itemId, purchase.ItemId);
        Assert.Equal(expectedPaidCents, purchase.PaidCents);
        Assert.Equal(expectedChangeCents, purchase.ChangeTotalCents);
        Assert.Equal(expectedDenominations, purchase.Change.Select(coin => coin.DenominationCents).ToArray());
        Assert.All(purchase.Change, coin => Assert.Equal(1, coin.Count));
        Assert.Equal(expectedChangeCents, purchase.Change.Sum(coin => coin.DenominationCents * coin.Count));
        Assert.Equal(0, (await GetStateAsync(id)).BalanceCents);
    }

    [Fact]
    public async Task Purchase_Insufficient_Funds_Returns_409_With_ErrorCode()
    {
        var id = NewMachineId();
        await InsertCoinAndAssertAcceptedAsync(id, 100);

        var response = await SelectAsync(id, "latte");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("insufficient-funds", await GetErrorCodeAsync(response));

        // The balance survives so the customer can add coins or cancel.
        Assert.Equal(100, (await GetStateAsync(id)).BalanceCents);
    }

    [Fact]
    public async Task Purchase_Unknown_Item_Returns_409_With_ErrorCode()
    {
        var id = NewMachineId();

        var response = await SelectAsync(id, "espresso");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("unknown-item", await GetErrorCodeAsync(response));
    }

    [Fact]
    public async Task Purchase_Depletes_Stock_Then_Returns_409_OutOfStock()
    {
        var id = NewMachineId();

        // Fresh machines carry 10 of each drink; buy every latte.
        for (var i = 0; i < 10; i++)
        {
            await InsertCoinAndAssertAcceptedAsync(id, 100);
            await InsertCoinAndAssertAcceptedAsync(id, 100);
            await InsertCoinAndAssertAcceptedAsync(id, 100);
            await SelectAndAssertOkAsync(id, "latte");
        }

        var state = await GetStateAsync(id);
        Assert.False(state.Menu.Single(item => item.Id == "latte").InStock);
        Assert.True(state.Menu.Single(item => item.Id == "cappuccino").InStock);

        // The eleventh attempt fails with out-of-stock.
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        var response = await SelectAsync(id, "latte");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("out-of-stock", await GetErrorCodeAsync(response));
    }

    [Fact]
    public async Task Purchase_When_Change_Is_Unpayable_Returns_409_ExactChangeOnly()
    {
        var id = NewMachineId();

        // Craft a machine whose coin stock holds ONLY $2 coins: the $3 change
        // for a latte paid with 3x$2 (600 - 300 = 300 = $2 + $1) can never be
        // made because no $1 (or smaller) coins exist. The row is inserted
        // directly so the registry hydrates from SQLite on first touch.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Machines.Add(new MachineEntity
            {
                Id = id,
                State = "Idle",
                BalanceCents = 0,
                CoinStockJson = "{\"200\":20}",
                ItemStockJson = "{\"latte\":10}",
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        // Hydration check: only latte is in stock, as written to the database.
        var hydrated = await GetStateAsync(id);
        Assert.True(hydrated.Menu.Single(item => item.Id == "latte").InStock);
        Assert.False(hydrated.Menu.Single(item => item.Id == "decaf").InStock);

        await InsertCoinAndAssertAcceptedAsync(id, 200);
        await InsertCoinAndAssertAcceptedAsync(id, 200);
        await InsertCoinAndAssertAcceptedAsync(id, 200);

        // A latte is affordable but its change is unpayable: the LED says so.
        var state = await GetStateAsync(id);
        Assert.Equal("EXACT CHANGE ONLY", state.StatusMessage);

        var response = await SelectAsync(id, "latte");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("exact-change-only", await GetErrorCodeAsync(response));

        // The credit is held until the customer cancels.
        Assert.Equal(600, (await GetStateAsync(id)).BalanceCents);

        // Cancelling returns the coins and clears the exact-change condition.
        var cancel = await CancelAndAssertOkAsync(id);
        Assert.Equal(600, cancel.RefundedCents);
        Assert.Equal("INSERT COIN", (await GetStateAsync(id)).StatusMessage);
    }

    // ----------------------------------------------------------------- cancel

    [Fact]
    public async Task Cancel_Refunds_Full_Balance_And_Zeroes_It()
    {
        var id = NewMachineId();
        await InsertCoinAndAssertAcceptedAsync(id, 200);
        await InsertCoinAndAssertAcceptedAsync(id, 200);

        var cancel = await CancelAndAssertOkAsync(id);
        Assert.Equal(400, cancel.RefundedCents);
        Assert.Equal(new[] { 200 }, cancel.Refund.Select(coin => coin.DenominationCents).ToArray());
        Assert.Equal(2, cancel.Refund.Single().Count);
        Assert.Equal(400, cancel.Refund.Sum(coin => coin.DenominationCents * coin.Count));

        var state = await GetStateAsync(id);
        Assert.Equal(0, state.BalanceCents);
        Assert.Equal("Idle", state.State);

        // The cancellation is recorded in the ledger.
        var transactions = await GetTransactionsAsync(id);
        var cancelled = Assert.Single(transactions, row => row.Status == "Cancelled");
        Assert.Equal(string.Empty, cancelled.ItemId);
        Assert.Equal(400, cancelled.PaidCents);
        Assert.Equal(0, cancelled.ChangeCents);

        // Cancelling with no credit is a harmless 200 with zeroes.
        var second = await CancelAndAssertOkAsync(id);
        Assert.Equal(0, second.RefundedCents);
        Assert.Empty(second.Refund);
    }

    // ---------------------------------------------------------- transactions

    [Fact]
    public async Task Transactions_Are_Returned_Newest_First_And_Respect_Limit()
    {
        var id = NewMachineId();

        // First purchase: latte, exact payment.
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        await SelectAndAssertOkAsync(id, "latte");

        // Give the clock room so the two rows carry distinct timestamps.
        await Task.Delay(25);

        // Second purchase: cappuccino with $1 change.
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        await InsertCoinAndAssertAcceptedAsync(id, 100);
        var second = await SelectAndAssertOkAsync(id, "cappuccino");
        Assert.Equal(50, second.ChangeTotalCents);

        var transactions = await GetTransactionsAsync(id);
        Assert.Equal(2, transactions.Count);
        Assert.All(transactions, row => Assert.Equal(id, row.MachineId));
        Assert.All(transactions, row => Assert.Equal("Purchased", row.Status));
        Assert.True(transactions[0].Timestamp >= transactions[1].Timestamp, "Rows must be newest first.");
        Assert.Equal("cappuccino", transactions[0].ItemId);
        Assert.Equal("latte", transactions[1].ItemId);
        Assert.Equal(350, transactions[0].PaidCents - transactions[0].ChangeCents);

        // limit=1 returns only the newest row.
        var limited = await GetTransactionsAsync(id, 1);
        var newest = Assert.Single(limited);
        Assert.Equal("cappuccino", newest.ItemId);
    }

    // ------------------------------------------------------------- persistence

    [Fact]
    public async Task Machine_State_Is_Persisted_To_Sqlite_Across_Gets()
    {
        var id = NewMachineId();
        await InsertCoinAndAssertAcceptedAsync(id, 100);

        // Two independent GETs observe the same persisted state.
        var first = await GetStateAsync(id);
        var second = await GetStateAsync(id);
        Assert.Equal(100, first.BalanceCents);
        Assert.Equal(100, second.BalanceCents);
        Assert.Equal("AwaitingSelection", second.State);

        // The SQLite row carries the state, balance and stock JSON.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.Machines.AsNoTracking().SingleAsync(machine => machine.Id == id);
        Assert.Equal("AwaitingSelection", row.State);
        Assert.Equal(100, row.BalanceCents);
        Assert.Equal(21, JsonSerializer.Deserialize<Dictionary<int, int>>(row.CoinStockJson)![100]);
    }

    [Fact]
    public async Task Fresh_Machine_Is_Seeded_With_Default_Stock()
    {
        var id = NewMachineId();
        var state = await GetStateAsync(id);

        Assert.Equal(20, state.Inventory.Coins[5]);
        Assert.Equal(20, state.Inventory.Coins[10]);
        Assert.Equal(20, state.Inventory.Coins[20]);
        Assert.Equal(20, state.Inventory.Coins[50]);
        Assert.Equal(20, state.Inventory.Coins[100]);
        Assert.Equal(20, state.Inventory.Coins[200]);
        Assert.Equal(10, state.Inventory.Items["cappuccino"]);
        Assert.Equal(10, state.Inventory.Items["latte"]);
        Assert.Equal(10, state.Inventory.Items["decaf"]);
    }

    [Fact]
    public async Task Machine_With_Corrupt_Stock_Json_Hydrates_With_Reseeded_Defaults_Instead_Of_500()
    {
        var id = NewMachineId();

        // A hand-edited row whose coin stock column is not valid JSON. The
        // balance column is a plain integer and stays trusted; the stock is
        // reseeded to the factory defaults so the machine stays usable.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Machines.Add(new MachineEntity
            {
                Id = id,
                State = "AwaitingSelection",
                BalanceCents = 150,
                CoinStockJson = "{not valid json",
                ItemStockJson = "{\"latte\":10}",
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        var state = await GetStateAsync(id);
        Assert.Equal(150, state.BalanceCents);
        Assert.Equal("AwaitingSelection", state.State);
        Assert.Equal(20, state.Inventory.Coins[5]);
        Assert.Equal(20, state.Inventory.Coins[200]);
        Assert.Equal(10, state.Inventory.Items["latte"]);
    }

    [Fact]
    public async Task Machine_With_Corrupt_Item_Stock_Json_Hydrates_With_Reseeded_Defaults_Instead_Of_500()
    {
        var id = NewMachineId();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Machines.Add(new MachineEntity
            {
                Id = id,
                State = "Idle",
                BalanceCents = 0,
                CoinStockJson = "{\"200\":20}",
                ItemStockJson = "[1,2,3]",
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        var state = await GetStateAsync(id);
        Assert.Equal(20, state.Inventory.Coins[200]);
        Assert.Equal(20, state.Inventory.Coins[5]); // reseeded: the row only carried $2 coins
        Assert.Equal(10, state.Inventory.Items["cappuccino"]);
        Assert.Equal(10, state.Inventory.Items["latte"]); // reseeded: the row carried no latte
    }

    // ------------------------------------------------------------ admin refill

    [Fact]
    public async Task Admin_Refill_Restocks_Coins_And_Items()
    {
        var id = NewMachineId();
        var before = await GetStateAsync(id);
        Assert.Equal(20, before.Inventory.Coins[100]);
        Assert.Equal(10, before.Inventory.Items["latte"]);

        var response = await PostJsonAsync(
            _client,
            $"/api/v1/admin/refill?machineId={id}",
            new
            {
                coinDenominations = new[] { 100, 50 },
                coinCounts = new[] { 7, 3 },
                items = new Dictionary<string, int> { ["latte"] = 5 },
            });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var after = await GetStateAsync(id);
        Assert.Equal(27, after.Inventory.Coins[100]);
        Assert.Equal(23, after.Inventory.Coins[50]);
        Assert.Equal(15, after.Inventory.Items["latte"]);
    }

    [Fact]
    public async Task Admin_Refill_With_Mismatched_Arrays_Returns_400()
    {
        var id = NewMachineId();
        var response = await PostJsonAsync(
            _client,
            $"/api/v1/admin/refill?machineId={id}",
            new { coinDenominations = new[] { 100 }, coinCounts = Array.Empty<int>() });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // -------------------------------------------------------------- machine id

    [Theory]
    [InlineData("bad id!")]
    [InlineData("has space")]
    [InlineData("under_score")]
    public async Task Invalid_Machine_Id_Returns_400_ProblemDetails(string invalidId)
    {
        var response = await _client.GetAsync($"/api/v1/machines/{invalidId}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Machine_Id_Longer_Than_64_Characters_Returns_400()
    {
        var tooLong = new string('a', 65);
        var response = await _client.GetAsync($"/api/v1/machines/{tooLong}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Machine_Id_At_Max_Length_Is_Accepted()
    {
        var id = new string('a', 64);
        var response = await _client.GetAsync($"/api/v1/machines/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ------------------------------------------------------------- unknown routes

    [Fact]
    public async Task Unknown_Api_Route_Returns_404_ProblemDetails_Not_The_Spa_Shell()
    {
        var response = await _client.GetAsync("/api/v1/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("Not found", problem.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Unknown_Api_Route_With_Post_Also_Returns_404_ProblemDetails()
    {
        var response = await _client.PostAsync("/api/v1/does-not-exist/coins", content: null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    // ----------------------------------------------------------------- helpers

    private static string NewMachineId() => $"test-{Guid.NewGuid():N}";

    private static async Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string url, object body)
    {
        var content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
        return await client.PostAsync(url, content);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(), Json)
        ?? throw new InvalidOperationException($"Response body did not deserialize to {typeof(T).Name}.");

    private static async Task<string> GetErrorCodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errorCode").GetString() ?? string.Empty;
    }

    private async Task<MachineStateDto> GetStateAsync(string id)
    {
        var response = await _client.GetAsync($"/api/v1/machines/{id}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<MachineStateDto>(response);
    }

    private Task<HttpResponseMessage> InsertCoinAsync(string id, int denominationCents) =>
        PostJsonAsync(_client, $"/api/v1/machines/{id}/coins", new { denominationCents });

    private async Task InsertCoinAndAssertAcceptedAsync(string id, int denominationCents)
    {
        var response = await InsertCoinAsync(id, denominationCents);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ReadAsync<InsertCoinResponse>(response)).Accepted);
    }

    private Task<HttpResponseMessage> SelectAsync(string id, string itemId) =>
        PostJsonAsync(_client, $"/api/v1/machines/{id}/selections", new { itemId });

    private async Task<PurchaseResponse> SelectAndAssertOkAsync(string id, string itemId)
    {
        var response = await SelectAsync(id, itemId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<PurchaseResponse>(response);
    }

    private async Task<CancelResponse> CancelAndAssertOkAsync(string id)
    {
        var response = await _client.PostAsync($"/api/v1/machines/{id}/cancel", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<CancelResponse>(response);
    }

    private async Task<List<TransactionDto>> GetTransactionsAsync(string machineId, int? limit = null)
    {
        var url = $"/api/v1/transactions?machineId={machineId}";
        if (limit is not null)
        {
            url += $"&limit={limit}";
        }

        var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadAsync<List<TransactionDto>>(response);
    }
}
