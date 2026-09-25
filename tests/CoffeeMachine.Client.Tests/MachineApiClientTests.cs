using System.Net;
using System.Text;
using CoffeeMachine.Client.Services;

namespace CoffeeMachine.Client.Tests;

/// <summary>
/// Unit tests for the API client wiring (request shapes, error parsing and
/// the per-tab machine id store).
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
    public async Task ApiException_Uses_Detail_As_Message_When_ErrorCode_Is_Absent()
    {
        var (client, _, _) = Create(_ => Problem(409,
            """{"title":"Out of stock","status":409,"detail":"Sold out."}"""));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.SelectItemAsync("latte"));

        Assert.Null(ex.Code);
        Assert.Equal("Sold out.", ex.Message);
    }

    [Fact]
    public async Task ApiException_Tolerates_Non_Json_Error_Bodies()
    {
        // A misbehaving proxy or gateway may answer with plain text; the
        // client must fall back to a generic message rather than throw.
        var (client, _, _) = Create(_ => Problem(502, "<html>Bad Gateway</html>"));

        var ex = await Assert.ThrowsAsync<ApiException>(() => client.GetMenuAsync());

        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Null(ex.Code);
        Assert.StartsWith("The machine reported an error", ex.Message, StringComparison.Ordinal);
    }

    // --------------------------------------------------------- machine id store

    [Fact]
    public async Task Machine_Id_Is_Generated_Once_And_Stored_In_SessionStorage()
    {
        var (client, js, _) = Create(_ => Problem(404, "{}"));

        var first = await client.MachineIdAsync;
        var second = await client.MachineIdAsync;

        Assert.Equal(first, second);
        Assert.Contains(js.Calls, call => call.Identifier == "sessionStorage.getItem");

        var set = Assert.Single(js.Calls.Where(call => call.Identifier == "sessionStorage.setItem"));
        Assert.Equal("cm-machine-id", set.Args![0]);
        Assert.Equal(first.ToString(), set.Args[1]);

        // SessionStorage (not localStorage) keeps tabs independent while
        // still surviving a refresh within the same tab.
        Assert.DoesNotContain(js.Calls, call => call.Identifier == "localStorage.getItem");
    }

    [Fact]
    public async Task Machine_Id_Is_Reused_From_SessionStorage_On_Refresh()
    {
        var (client, js, _) = Create(_ => Problem(404, "{}"));
        var existing = Guid.NewGuid().ToString();
        js.Results["sessionStorage.getItem"] = existing;

        var id = await client.MachineIdAsync;

        Assert.Equal(existing, id.ToString());
        Assert.DoesNotContain(js.Calls, call => call.Identifier == "sessionStorage.setItem");
    }
}
