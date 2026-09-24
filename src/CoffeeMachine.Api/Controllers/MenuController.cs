using CoffeeMachine.Contracts;
using CoffeeMachine.Domain.Menu;
using Microsoft.AspNetCore.Mvc;

namespace CoffeeMachine.Api.Controllers;

/// <summary>The static product catalogue: three coffees at fixed prices.</summary>
[ApiController]
[Route("api/v1/menu")]
public sealed class MenuController : ControllerBase
{
    /// <summary>
    /// Lists the menu. <c>InStock</c> is always true here because stock is a
    /// per-machine concern; callers should read per-machine stock from
    /// <c>GET /api/v1/machines/{id}</c>.
    /// </summary>
    /// <returns>The three menu items in catalogue order.</returns>
    [HttpGet]
    public ActionResult<IReadOnlyList<MenuItemDto>> Get() =>
        Ok(CoffeeMenu.All
            .Select(item => new MenuItemDto(item.Id, item.Name, item.PriceCents, item.PriceDisplay, true))
            .ToList());
}
