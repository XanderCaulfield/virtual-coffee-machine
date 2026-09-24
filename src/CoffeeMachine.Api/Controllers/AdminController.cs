using CoffeeMachine.Api.Data;
using CoffeeMachine.Contracts;
using CoffeeMachine.Domain.Menu;
using CoffeeMachine.Domain.Money;
using Microsoft.AspNetCore.Mvc;

namespace CoffeeMachine.Api.Controllers;

/// <summary>Service-panel operations (the discreet admin surface).</summary>
[ApiController]
[Route("api/v1/admin")]
public sealed class AdminController : ControllerBase
{
    private readonly MachineRegistry _machines;

    /// <summary>Creates the controller.</summary>
    /// <param name="machines">The singleton machine registry.</param>
    public AdminController(MachineRegistry machines) => _machines = machines;

    /// <summary>
    /// Restocks a machine: adds coins and/or items to its inventory.
    /// </summary>
    /// <param name="machineId">The machine to restock (query parameter).</param>
    /// <param name="request">
    /// Parallel <c>coinDenominations</c>/<c>coinCounts</c> arrays and an
    /// optional item id → count map.
    /// </param>
    /// <returns>204 No Content on success.</returns>
    [HttpPost("refill")]
    public ActionResult Refill([FromQuery] string machineId, RefillRequest request)
    {
        if (!MachineRegistry.IsValidId(machineId))
        {
            return ApiProblems.InvalidMachineId(machineId);
        }

        if (request.CoinDenominations.Length != request.CoinCounts.Length)
        {
            return ApiProblems.InvalidRefill("CoinDenominations and CoinCounts must be parallel arrays of the same length.");
        }

        var coins = new Dictionary<Denomination, int>();
        for (var i = 0; i < request.CoinDenominations.Length; i++)
        {
            if (!Denominations.TryParse(request.CoinDenominations[i], out var denomination) ||
                !Denominations.Accepted.Contains(denomination))
            {
                return ApiProblems.InvalidRefill($"{request.CoinDenominations[i]} cents is not an accepted coin denomination (5, 10, 20, 50, 100, 200).");
            }

            if (request.CoinCounts[i] < 0)
            {
                return ApiProblems.InvalidRefill("Coin counts cannot be negative.");
            }

            coins[denomination] = coins.GetValueOrDefault(denomination) + request.CoinCounts[i];
        }

        var items = new Dictionary<string, int>();
        if (request.Items is not null)
        {
            foreach (var (itemId, count) in request.Items)
            {
                if (CoffeeMenu.Find(itemId) is null)
                {
                    return ApiProblems.InvalidRefill($"'{itemId}' is not on the menu.");
                }

                if (count < 0)
                {
                    return ApiProblems.InvalidRefill("Item counts cannot be negative.");
                }

                items[itemId] = items.GetValueOrDefault(itemId) + count;
            }
        }

        _machines.Refill(machineId, coins, items);
        return NoContent();
    }
}
