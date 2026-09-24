using System.Net;
using System.Text;
using System.Text.Json;
using CoffeeMachine.Contracts;

namespace CoffeeMachine.Client.Services;

/// <summary>
/// Development seam: a fake HTTP pipeline that serves contract-conforming
/// JSON for the whole REST API, so the UI can be exercised standalone
/// (e.g. <c>?demo=1</c>) without the real Api project running.
///
/// Wire format, DTOs, status codes and ProblemDetails bodies mirror the real
/// API exactly (see plan §2), so swapping this handler out is transparent
/// to the rest of the client. State is held in memory per handler instance —
/// each browser tab gets its own machine, exactly like a real session.
/// </summary>
public sealed class FakeBackendHandler : HttpMessageHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly MenuItemDto[] DemoMenu =
    [
        new("cappuccino", "Cappuccino", 350, "$3.50", true),
        new("latte", "Latte", 300, "$3.00", true),
        new("decaf", "Decaf", 400, "$4.00", true),
    ];

    private static readonly int[] DenominationsDesc = [200, 100, 50, 20, 10, 5];

    private readonly object _gate = new();
    private readonly Dictionary<Guid, MachineSession> _machines = [];
    private readonly List<TransactionDto> _ledger = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;
        var method = request.Method.Method;

        try
        {
            return await RouteAsync(method, path, request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Problem(HttpStatusCode.InternalServerError, "internal-error", ex.Message);
        }
    }

    // ------------------------------------------------------------------ routing

    private async Task<HttpResponseMessage> RouteAsync(string method, string path, HttpRequestMessage request, CancellationToken ct)
    {
        // GET /api/v1/menu
        if (method == "GET" && path == "/api/v1/menu")
        {
            return Json(HttpStatusCode.OK, DemoMenu.ToArray());
        }

        // GET /api/v1/machines/{id}
        if (method == "GET" && TryMatch(path, "api/v1/machines/", out var machinePart) && !machinePart.Contains('/'))
        {
            return Json(HttpStatusCode.OK, GetSession(machinePart).Snapshot());
        }

        // POST /api/v1/machines/{id}/coins
        if (method == "POST" && TryMatch(path, "api/v1/machines/", out machinePart) &&
            machinePart.EndsWith("/coins", StringComparison.Ordinal))
        {
            var session = GetSession(machinePart[..^"/coins".Length]);
            var body = await ReadBodyAsync<InsertCoinRequest>(request, ct).ConfigureAwait(false);
            return Json(HttpStatusCode.OK, session.Insert(body.DenominationCents));
        }

        // POST /api/v1/machines/{id}/selections
        if (method == "POST" && TryMatch(path, "api/v1/machines/", out machinePart) &&
            machinePart.EndsWith("/selections", StringComparison.Ordinal))
        {
            var session = GetSession(machinePart[..^"/selections".Length]);
            var body = await ReadBodyAsync<SelectItemRequest>(request, ct).ConfigureAwait(false);
            return session.Select(body.ItemId);
        }

        // POST /api/v1/machines/{id}/cancel
        if (method == "POST" && TryMatch(path, "api/v1/machines/", out machinePart) &&
            machinePart.EndsWith("/cancel", StringComparison.Ordinal))
        {
            var session = GetSession(machinePart[..^"/cancel".Length]);
            return Json(HttpStatusCode.OK, session.Cancel());
        }

        // GET /api/v1/transactions?machineId=&limit=
        if (method == "GET" && path == "/api/v1/transactions")
        {
            var query = ParseQuery(request.RequestUri?.Query ?? string.Empty);
            var machineId = query.GetValueOrDefault("machineId") ?? string.Empty;
            var limit = int.TryParse(query.GetValueOrDefault("limit"), out var parsed) ? Math.Clamp(parsed, 1, 100) : 10;
            List<TransactionDto> rows;
            lock (_gate)
            {
                rows = _ledger
                    .Where(t => string.Equals(t.MachineId, machineId, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(t => t.Timestamp)
                    .Take(limit)
                    .ToList();
            }

            return Json(HttpStatusCode.OK, rows);
        }

        // POST /api/v1/admin/refill
        if (method == "POST" && path == "/api/v1/admin/refill")
        {
            var body = await ReadBodyAsync<RefillRequest>(request, ct).ConfigureAwait(false);
            lock (_gate)
            {
                foreach (var session in _machines.Values)
                {
                    session.Restock(body.CoinDenominations, body.CoinCounts, body.Items);
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        return Problem(HttpStatusCode.NotFound, "not-found", $"No route for {method} {path} (fake backend)");
    }

    private MachineSession GetSession(string machineIdText)
    {
        if (!Guid.TryParse(machineIdText, out var id))
        {
            throw new InvalidOperationException("Malformed machine id in fake backend");
        }

        lock (_gate)
        {
            if (!_machines.TryGetValue(id, out var session))
            {
                session = new MachineSession(id, this);
                _machines[id] = session;
            }

            return session;
        }
    }

    internal void Record(TransactionDto row)
    {
        lock (_gate)
        {
            _ledger.Add(row with { Id = Guid.NewGuid(), Timestamp = DateTimeOffset.UtcNow });
        }
    }

    // ------------------------------------------------------------------ plumbing

    private static bool TryMatch(string path, string prefix, out string rest)
    {
        if (path.StartsWith("/" + prefix, StringComparison.Ordinal))
        {
            rest = path[(prefix.Length + 1)..];
            return rest.Length > 0;
        }

        rest = string.Empty;
        return false;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
        {
            return result;
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            result[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(value);
        }

        return result;
    }

    private static async Task<T> ReadBodyAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Content is null)
        {
            return (T)JsonSerializer.Deserialize("{}", typeof(T), JsonOptions)!;
        }

        var text = await request.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return (T)JsonSerializer.Deserialize(text, typeof(T), JsonOptions)!;
    }

    private static HttpResponseMessage Json<T>(HttpStatusCode status, T payload) => new(status)
    {
        Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Problem(HttpStatusCode status, string code, string detail)
    {
        var problem = new Dictionary<string, object>
        {
            ["type"] = $"https://httpstatuses.com/{(int)status}",
            ["title"] = ReasonPhrase(status),
            ["status"] = (int)status,
            ["detail"] = detail,
            ["code"] = code,
        };
        return Json(status, problem);
    }

    private static string ReasonPhrase(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest => "Bad Request",
        HttpStatusCode.NotFound => "Not Found",
        HttpStatusCode.Conflict => "Conflict",
        HttpStatusCode.InternalServerError => "Internal Server Error",
        _ => status.ToString(),
    };

    // ------------------------------------------------------------------ session

    private sealed class MachineSession(Guid id, FakeBackendHandler backend)
    {
        private readonly Dictionary<int, int> _coins = new()
        {
            [5] = 30, [10] = 30, [20] = 25, [50] = 20, [100] = 15, [200] = 10,
        };

        private readonly Dictionary<string, int> _items = new()
        {
            ["cappuccino"] = 12, ["latte"] = 12, ["decaf"] = 12,
        };

        private int _balance;

        public MachineStateDto Snapshot()
        {
            var menu = DemoMenu.Select(m => m with { InStock = GetStock(m.Id) > 0 }).ToArray();
            var inStock = menu.Where(m => m.InStock).ToArray();
            string? status = null;
            if (inStock.Length == 0)
            {
                status = "OUT OF STOCK";
            }
            else
            {
                var minPrice = inStock.Min(m => m.PriceCents);
                var affordable = inStock.Where(m => _balance >= m.PriceCents).ToArray();
                if (affordable.Length > 0 && affordable.All(m => !CanMakeChange(_balance - m.PriceCents)))
                {
                    status = "EXACT CHANGE ONLY";
                }
            }

            return new MachineStateDto(
                id.ToString(),
                _balance > 0 ? "AwaitingSelection" : "Idle",
                _balance,
                FormatMoney(_balance),
                menu,
                new InventoryDto(new Dictionary<int, int>(_coins), new Dictionary<string, int>(_items)),
                status);
        }

        public InsertCoinResponse Insert(int denominationCents)
        {
            if (denominationCents is 1 or 2)
            {
                return new InsertCoinResponse(
                    false,
                    denominationCents == 1 ? "1¢ coins are not accepted" : "2¢ coins are not accepted",
                    _balance,
                    FormatMoney(_balance));
            }

            if (denominationCents is not (5 or 10 or 20 or 50 or 100 or 200))
            {
                return new InsertCoinResponse(false, "Unrecognised coin", _balance, FormatMoney(_balance));
            }

            _balance += denominationCents;
            _coins[denominationCents] = _coins.GetValueOrDefault(denominationCents) + 1;
            return new InsertCoinResponse(true, null, _balance, FormatMoney(_balance));
        }

        public HttpResponseMessage Select(string itemId)
        {
            var item = DemoMenu.FirstOrDefault(m => m.Id == itemId);
            if (item is null)
            {
                return Problem(HttpStatusCode.NotFound, "unknown-item", $"No menu item '{itemId}'.");
            }

            if (GetStock(itemId) <= 0)
            {
                return Problem(HttpStatusCode.Conflict, "out-of-stock", $"{item.Name} is out of stock.");
            }

            if (_balance < item.PriceCents)
            {
                return Problem(HttpStatusCode.Conflict, "insufficient-funds",
                    $"Insert {FormatMoney(item.PriceCents - _balance)} more to buy a {item.Name}.");
            }

            var changeTotal = _balance - item.PriceCents;
            if (!TryBreak(changeTotal, out var change))
            {
                // Per domain rules: refund the inserted coins and refuse the sale.
                var refund = RefundBalance();
                var problem = new Dictionary<string, object>
                {
                    ["type"] = "https://httpstatuses.com/409",
                    ["title"] = "Conflict",
                    ["status"] = 409,
                    ["detail"] = "Exact change only — your coins have been refunded.",
                    ["code"] = "exact-change-only",
                    ["refund"] = refund.Select(c => new ChangeCoinDto(c.Key, c.Value)).ToArray(),
                };
                return Json(HttpStatusCode.Conflict, problem);
            }

            _items[itemId] = GetStock(itemId) - 1;
            foreach (var (denomination, count) in change)
            {
                _coins[denomination] = _coins.GetValueOrDefault(denomination) - count;
            }

            var paid = item.PriceCents + changeTotal;
            _balance = 0;
            backend.Record(new TransactionDto(
                Guid.Empty, DateTimeOffset.UtcNow, id.ToString(), item.Id,
                paid, changeTotal, "Purchased"));

            return Json(HttpStatusCode.OK, new PurchaseResponse(
                item.Id, item.Name, item.PriceCents, paid,
                changeTotal, change.Select(c => new ChangeCoinDto(c.Key, c.Value)).ToArray()));
        }

        public CancelResponse Cancel()
        {
            var refund = RefundBalance();
            return new CancelResponse(
                refund.Sum(c => c.Key * c.Value),
                refund.Select(c => new ChangeCoinDto(c.Key, c.Value)).ToArray());
        }

        public void Restock(int[] coinDenominations, int[] coinCounts, Dictionary<string, int>? items)
        {
            for (var i = 0; i < coinDenominations.Length && i < coinCounts.Length; i++)
            {
                var denomination = coinDenominations[i];
                if (denomination <= 0)
                {
                    continue;
                }

                _coins[denomination] = _coins.GetValueOrDefault(denomination) + Math.Max(0, coinCounts[i]);
            }

            if (items is not null)
            {
                foreach (var (itemId, count) in items)
                {
                    _items[itemId] = GetStock(itemId) + Math.Max(0, count);
                }
            }
        }

        private List<KeyValuePair<int, int>> RefundBalance()
        {
            var refunded = new List<KeyValuePair<int, int>>();
            var remaining = _balance;
            foreach (var denomination in DenominationsDesc)
            {
                var available = _coins.GetValueOrDefault(denomination);
                var take = Math.Min(available, remaining / denomination);
                if (take > 0)
                {
                    refunded.Add(new KeyValuePair<int, int>(denomination, take));
                    _coins[denomination] = available - take;
                    remaining -= denomination * take;
                }
            }

            _balance = 0;
            return refunded;
        }

        private bool CanMakeChange(int amount) => TryBreak(amount, out _);

        private bool TryBreak(int amount, out List<KeyValuePair<int, int>> coins)
        {
            coins = [];
            var remaining = amount;
            foreach (var denomination in DenominationsDesc)
            {
                var take = Math.Min(_coins.GetValueOrDefault(denomination), remaining / denomination);
                if (take > 0)
                {
                    coins.Add(new KeyValuePair<int, int>(denomination, take));
                    remaining -= denomination * take;
                }
            }

            return remaining == 0;
        }

        private int GetStock(string itemId) => _items.GetValueOrDefault(itemId);

        private static string FormatMoney(int cents) => $"${cents / 100d:0.00}";
    }
}
