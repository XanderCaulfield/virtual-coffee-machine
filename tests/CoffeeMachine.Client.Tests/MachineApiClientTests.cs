using System.Net;
using System.Text;
using CoffeeMachine.Client.Services;

namespace CoffeeMachine.Client.Tests;

/// <summary>
/// Regression tests for the API client wiring, covering the bugs found when
/// exercising the real (non-demo) backend.
/// </summary>
public sealed class MachineApiClientTests
{
    private static (MachineApiClient Client, RecordingJsRuntime Js, StubHttpHandler Handler) Create(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        var js = new RecordingJsRuntime();
        var handler = new StubHttpHandler(responder);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/api/v1/") };
        return (new MachineApiClient(http, js), js, handler);
    }

    private static Task<HttpResponseMessage> Problem(int status, string body) => Task.FromResult(
        new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/problem+json"),
        });

    // ---------------------------------------------------- admin refill wiring

    [Fact]
    public async Task Refill_Sends_MachineId_As_Query_Parameter()
    {
        var (client, js, handler) = Create(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));

        await client.RefillAsync(new[] { 100, 50 }, new[] { 5, 3 }, new Dictionary<string, int> { ["latte"] = 2 });

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("/admin/refill", request.RequestUri!.AbsolutePath, StringComparison.Ordinal);

        var query = request.RequestUri.Query.TrimStart('?');
        Assert.StartsWith("machineId=", query, StringComparison.Ordinal);
        var machineId = query["machineId=".Length..].Split('&')[0];
        Assert.Equal((await client.MachineIdAsync).ToString(), machineId);
        Assert.True(Guid.TryParse(machineId, out _));

        var body = await request.Content!.ReadAsStringAsync();
        Assert.Contains("\"coinDenominations\":[100,50]", body);
        Assert.Contains("\"coinCounts\":[5,3]", body);
        Assert.Contains("\"latte\":2", body);
    }

    // -------------------------------------------------- errorCode extension

    [Fact]
    public async Task ApiException_Code_Is_Parsed_From_ErrorCode_Extension()
    {
        var (client, _, _) = Create(_ => Problem(409,
            """{"title":"Insufficient funds","status":409,"detail":"Not enough credit.","errorCode":"insufficient-funds"}"""));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.SelectItemAsync("latte"));

        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
        Assert.Equal("insufficient-funds", ex.Code);
    }

    [Fact]
    public async Task ApiException_Code_Falls_Back_To_Legacy_Code_Extension()
    {
        // The demo backend historically used "code"; the client must still read it.
        var (client, _, _) = Create(_ => Problem(409,
            """{"title":"Out of stock","status":409,"detail":"Sold out.","code":"out-of-stock"}"""));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.SelectItemAsync("latte"));

        Assert.Equal("out-of-stock", ex.Code);
    }

    [Fact]
    public async Task ExactChangeOnly_Refund_Breakdown_Is_Parsed_From_Extension()
    {
        var (client, _, _) = Create(_ => Problem(409,
            """{"title":"Exact change only","status":409,"detail":"Cannot make change.","errorCode":"exact-change-only","refund":[{"denominationCents":200,"count":2},{"denominationCents":50,"count":1}]}"""));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.SelectItemAsync("latte"));

        Assert.Equal("exact-change-only", ex.Code);
        Assert.Equal(2, ex.Refund!.Count);
        Assert.Equal(200, ex.Refund[0].DenominationCents);
        Assert.Equal(2, ex.Refund[0].Count);
        Assert.Equal(50, ex.Refund[1].DenominationCents);
    }
}
