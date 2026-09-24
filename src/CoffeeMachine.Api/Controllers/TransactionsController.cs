using CoffeeMachine.Api.Data;
using CoffeeMachine.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoffeeMachine.Api.Controllers;

/// <summary>The append-only transaction ledger.</summary>
[ApiController]
[Route("api/v1/transactions")]
public sealed class TransactionsController : ControllerBase
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    private readonly AppDbContext _db;

    /// <summary>Creates the controller.</summary>
    /// <param name="db">The request-scoped database context.</param>
    public TransactionsController(AppDbContext db) => _db = db;

    /// <summary>
    /// Lists ledger rows, newest first, optionally filtered to one machine.
    /// </summary>
    /// <param name="machineId">Optional machine filter.</param>
    /// <param name="limit">Maximum rows to return; defaults to 20, capped at 100.</param>
    /// <returns>The matching transactions, newest first.</returns>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TransactionDto>>> Get(
        [FromQuery] string? machineId = null,
        [FromQuery] int? limit = null)
    {
        var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        var query = _db.Transactions.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(machineId))
        {
            query = query.Where(t => t.MachineId == machineId);
        }

        var rows = await query
            .OrderByDescending(t => t.Timestamp)
            .Take(effectiveLimit)
            .ToListAsync();

        return Ok(rows
            .Select(t => new TransactionDto(t.Id, t.Timestamp, t.MachineId, t.ItemId, t.PaidCents, t.ChangeCents, t.Status))
            .ToList());
    }
}
