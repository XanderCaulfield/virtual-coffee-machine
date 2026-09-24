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
}
