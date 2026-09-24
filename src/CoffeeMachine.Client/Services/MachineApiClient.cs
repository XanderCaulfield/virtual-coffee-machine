using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CoffeeMachine.Contracts;
using Microsoft.JSInterop;

namespace CoffeeMachine.Client.Services;

/// <summary>
/// Thrown when the REST API answers with a non-success status (RFC 7807 ProblemDetails).
/// </summary>
public sealed class ApiException : Exception
{
    public HttpStatusCode StatusCode { get; }
    public string? Code { get; }

    /// <summary>Refund breakdown when the Api includes one (e.g. exact-change-only), null otherwise.</summary>
    public IReadOnlyList<ChangeCoinDto>? Refund { get; }

    public ApiException(HttpStatusCode statusCode, string? code, string message, IReadOnlyList<ChangeCoinDto>? refund = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Refund = refund;
    }
}

/// <summary>
/// Typed client for the Coffee Machine REST API. All endpoints live under
/// <c>/api/v1</c> on the same origin as the app, and every request is tagged
/// with the per-visitor machine id (a GUID persisted in localStorage so a
/// refresh keeps the same machine, balance and ledger).
/// </summary>
public sealed class MachineApiClient
{
    private const string MachineIdStorageKey = "cm-machine-id";

    private readonly HttpClient _http;
    private readonly IJSRuntime _js;
    private Task<Guid>? _machineId;

    public MachineApiClient(HttpClient http, IJSRuntime js)
    {
        _http = http;
        _js = js;
    }

    /// <summary>Stable per-visitor machine id (generated once, reused forever).</summary>
    public Task<Guid> MachineIdAsync => _machineId ??= LoadOrCreateMachineIdAsync();

    public async Task<IReadOnlyList<MenuItemDto>> GetMenuAsync(CancellationToken ct = default)
    {
        var menu = await GetAsync<List<MenuItemDto>>("menu", ct).ConfigureAwait(false);
        return menu;
    }

    public Task<MachineStateDto> GetMachineAsync(CancellationToken ct = default) =>
        SendForMachineAsync(async (id, c) => await GetAsync<MachineStateDto>($"machines/{id}", c).ConfigureAwait(false), ct);

    public Task<InsertCoinResponse> InsertCoinAsync(int denominationCents, CancellationToken ct = default) =>
        SendForMachineAsync(async (id, c) => await PostAsync<InsertCoinResponse>(
            $"machines/{id}/coins", new InsertCoinRequest(denominationCents), c).ConfigureAwait(false), ct);

    public Task<PurchaseResponse> SelectItemAsync(string itemId, CancellationToken ct = default) =>
        SendForMachineAsync(async (id, c) => await PostAsync<PurchaseResponse>(
            $"machines/{id}/selections", new SelectItemRequest(itemId), c).ConfigureAwait(false), ct);

    public Task<CancelResponse> CancelAsync(CancellationToken ct = default) =>
        SendForMachineAsync(async (id, c) => await PostAsync<CancelResponse>($"machines/{id}/cancel", null, c).ConfigureAwait(false), ct);

    public Task<IReadOnlyList<TransactionDto>> GetTransactionsAsync(int limit = 10, CancellationToken ct = default) =>
        SendForMachineAsync(async (id, c) =>
        {
            IReadOnlyList<TransactionDto> list = await GetAsync<List<TransactionDto>>($"transactions?machineId={id}&limit={limit}", c).ConfigureAwait(false);
            return list;
        }, ct);

    public Task RefillAsync(int[] coinDenominations, int[] coinCounts, Dictionary<string, int>? items = null, CancellationToken ct = default) =>
        SendForMachineAsync(async (id, c) =>
        {
            // The refill endpoint scopes the restock to one machine via the
            // machineId query parameter.
            using var response = await _http.PostAsJsonAsync($"admin/refill?machineId={id}", new RefillRequest(coinDenominations, coinCounts, items), JsonOptions, c).ConfigureAwait(false);
            await EnsureSuccessAsync(response, c).ConfigureAwait(false);
            return true;
        }, ct);

    // ------------------------------------------------------------------ core

    private async Task<T> SendForMachineAsync<T>(Func<Guid, CancellationToken, Task<T>> operation, CancellationToken ct)
    {
        var id = await MachineIdAsync.ConfigureAwait(false);
        return await operation(id, ct).ConfigureAwait(false);
    }

    private async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false))!;
    }

    private async Task<T> PostAsync<T>(string path, object? body, CancellationToken ct)
    {
        using var response = body is null
            ? await _http.PostAsync(path, null, ct).ConfigureAwait(false)
            : await _http.PostAsJsonAsync(path, body, JsonOptions, ct).ConfigureAwait(false);
        await EnsureSuccessAsync(response, ct).ConfigureAwait(false);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false))!;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        // RFC 7807 ProblemDetails. The Api adds the machine-readable extension
        // "errorCode" ("insufficient-funds", "out-of-stock", ...); the demo
        // backend historically used "code", so accept both.
        string? code = null;
        string message = $"The machine reported an error ({response.StatusCode}).";
        List<ChangeCoinDto>? refund = null;
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = document.RootElement;
            if (root.TryGetProperty("errorCode", out var errorCodeElement))
            {
                code = errorCodeElement.GetString();
            }
            else if (root.TryGetProperty("code", out var codeElement))
            {
                code = codeElement.GetString();
            }

            if (root.TryGetProperty("detail", out var detailElement) && detailElement.ValueKind == JsonValueKind.String)
            {
                message = detailElement.GetString() ?? message;
            }
            else if (root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
            {
                message = titleElement.GetString() ?? message;
            }

            // Optional extension (the demo backend and the Api may include the
            // coins refunded to the customer, e.g. on exact-change-only).
            if (root.TryGetProperty("refund", out var refundElement) && refundElement.ValueKind == JsonValueKind.Array)
            {
                refund = refundElement.Deserialize<List<ChangeCoinDto>>(JsonOptions);
            }
        }
        catch (JsonException)
        {
            // Fall back to the generic message — the status code alone is still useful.
        }

        throw new ApiException(response.StatusCode, code, message, refund);
    }

    private async Task<Guid> LoadOrCreateMachineIdAsync()
    {
        try
        {
            var stored = await _js.InvokeAsync<string?>("sessionStorage.getItem", MachineIdStorageKey).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(stored) && Guid.TryParse(stored, out var existing))
            {
                return existing;
            }
        }
        catch (JSException)
        {
            // localStorage unavailable (private mode?) — fall through and use an in-memory id.
        }

        var fresh = Guid.NewGuid();
        try
        {
            await _js.InvokeVoidAsync("localStorage.setItem", MachineIdStorageKey, fresh.ToString()).ConfigureAwait(false);
        }
        catch (JSException)
        {
            // Non-fatal: the id simply won't survive a refresh in this session.
        }

        return fresh;
    }
}
