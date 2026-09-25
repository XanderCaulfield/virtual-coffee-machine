using CoffeeMachine.Api.Data;
using CoffeeMachine.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace CoffeeMachine.Api.Controllers;

/// <summary>The append-only transaction ledger.</summary>
[ApiController]
[Route("api/v1/transactions")]
public sealed class TransactionsController : ControllerBase
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    private readonly EfTransactionLedger _ledger;

    /// <summary>Creates the controller.</summary>
    /// <param name="ledger">The singleton EF-backed ledger.</param>
    public TransactionsController(EfTransactionLedger ledger) => _ledger = ledger;

    /// <summary>
    /// Lists ledger rows, newest first, optionally filtered to one machine.
    /// </summary>
    /// <param name="machineId">Optional machine filter; omit for every machine.</param>
    /// <param name="limit">Maximum rows to return; defaults to 20, clamped to 1..100.</param>
    /// <returns>The matching transactions, newest first.</returns>
    [HttpGet]
    public ActionResult<IReadOnlyList<TransactionDto>> Get(
        [FromQuery] string? machineId = null,
        [FromQuery] int? limit = null)
    {
        var effectiveLimit = Math.Clamp(limit ?? DefaultLimit, 1, MaxLimit);

        var rows = _ledger.List(machineId, effectiveLimit)
            .Select(t => new TransactionDto(t.Id, t.Timestamp, t.MachineId, t.ItemId, t.PaidCents, t.ChangeCents, t.Status))
            .ToList();

        return Ok(rows);
    }
}
