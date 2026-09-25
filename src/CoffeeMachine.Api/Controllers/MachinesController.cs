using CoffeeMachine.Api.Data;
using CoffeeMachine.Contracts;
using CoffeeMachine.Domain.Menu;
using CoffeeMachine.Domain.Money;
using Microsoft.AspNetCore.Mvc;

namespace CoffeeMachine.Api.Controllers;

/// <summary>Per-visitor vending machines: state, coins, selections and cancel.</summary>
[ApiController]
[Route("api/v1/machines")]
public sealed class MachinesController : ControllerBase
{
    private readonly MachineRegistry _machines;

    /// <summary>Creates the controller.</summary>
    /// <param name="machines">The singleton machine registry.</param>
    public MachinesController(MachineRegistry machines) => _machines = machines;

    /// <summary>
    /// Gets the full observable state of a machine, creating it (with default
    /// stock, or hydrated from SQLite) on first touch.
    /// </summary>
    /// <param name="id">The machine id.</param>
    /// <returns>The machine state DTO, or 400 for an invalid id.</returns>
    [HttpGet("{id}")]
    public ActionResult<MachineStateDto> GetState(string id)
    {
        if (!MachineRegistry.IsValidId(id))
        {
            return ApiProblems.InvalidMachineId(id);
        }

        var snapshot = _machines.GetSnapshot(id);
        var menu = CoffeeMenu.All
            .Select(item => new MenuItemDto(
                item.Id,
                item.Name,
                item.PriceCents,
                item.PriceDisplay,
                snapshot.ItemStock.GetValueOrDefault(item.Id) > 0))
            .ToList();

        var inventory = new InventoryDto(
            snapshot.CoinStock.ToDictionary(pair => pair.Key.ValueCents(), pair => pair.Value),
            snapshot.ItemStock);

        return Ok(new MachineStateDto(
            snapshot.Id,
            snapshot.State.ToString(),
            snapshot.BalanceCents,
            MoneyFormat.Format(snapshot.BalanceCents),
            menu,
            inventory,
            MachineStatus.For(snapshot)));
    }

    /// <summary>
    /// Inserts a coin. Recognised-but-rejected 1¢/2¢ coins return 200 with
    /// <c>Accepted=false</c> so the client can animate the coin-return tray;
    /// only values that are not real coins at all are a 400.
    /// </summary>
    /// <param name="id">The machine id.</param>
    /// <param name="request">The coin to insert.</param>
    /// <returns>The insertion outcome.</returns>
    [HttpPost("{id}/coins")]
    public ActionResult<InsertCoinResponse> InsertCoin(string id, InsertCoinRequest request)
    {
        if (!MachineRegistry.IsValidId(id))
        {
            return ApiProblems.InvalidMachineId(id);
        }

        if (!Denominations.TryParse(request.DenominationCents, out var denomination))
        {
            return ApiProblems.InvalidCoin(request.DenominationCents);
        }

        var result = _machines.InsertCoin(id, denomination);
        return Ok(new InsertCoinResponse(
            result.Accepted,
            result.RejectionReason,
            result.BalanceCents,
            MoneyFormat.Format(result.BalanceCents)));
    }

    /// <summary>
    /// Attempts to purchase a drink with the current credit. Failures return
    /// 409 with an <c>errorCode</c> extension:
    /// <c>insufficient-funds</c>, <c>out-of-stock</c>, <c>exact-change-only</c>
    /// or <c>unknown-item</c>.
    /// </summary>
    /// <param name="id">The machine id.</param>
    /// <param name="request">The drink to buy.</param>
    /// <returns>The purchase, including change broken into coins.</returns>
    [HttpPost("{id}/selections")]
    public ActionResult<PurchaseResponse> SelectItem(string id, SelectItemRequest request)
    {
        if (!MachineRegistry.IsValidId(id))
        {
            return ApiProblems.InvalidMachineId(id);
        }

        var result = _machines.SelectItem(id, request.ItemId);
        if (!result.Success)
        {
            return ApiProblems.SelectionConflict(result.FailureReason!.Value, request.ItemId);
        }

        return Ok(new PurchaseResponse(
            result.Item!.Id,
            result.Item.Name,
            result.PriceCents,
            result.PaidCents,
            result.ChangeTotalCents,
            ToChangeCoins(result.Change!)));
    }

    /// <summary>
    /// Cancels any pending credit: refunds the full balance from the coin
    /// stock and zeroes it. Cancelling with no credit is a 200 with zeroes.
    /// </summary>
    /// <param name="id">The machine id.</param>
    /// <returns>The refund, broken into coins.</returns>
    [HttpPost("{id}/cancel")]
    public ActionResult<CancelResponse> Cancel(string id)
    {
        if (!MachineRegistry.IsValidId(id))
        {
            return ApiProblems.InvalidMachineId(id);
        }

        var result = _machines.Cancel(id);
        return Ok(new CancelResponse(result.RefundedCents, ToChangeCoins(result.Refund)));
    }

    private static IReadOnlyList<ChangeCoinDto> ToChangeCoins(IReadOnlyDictionary<Denomination, int> change) =>
        change
            .OrderByDescending(pair => pair.Key.ValueCents())
            .Select(pair => new ChangeCoinDto(pair.Key.ValueCents(), pair.Value))
            .ToList();
}
