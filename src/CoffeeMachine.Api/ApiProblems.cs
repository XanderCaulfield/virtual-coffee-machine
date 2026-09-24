using CoffeeMachine.Domain.Machine;
using CoffeeMachine.Domain.Menu;
using Microsoft.AspNetCore.Mvc;

namespace CoffeeMachine.Api;

/// <summary>
/// Builds the RFC 7807 ProblemDetails responses the controllers return.
/// </summary>
internal static class ApiProblems
{
    /// <summary>
    /// 400 for a machine id that fails the
    /// <see cref="Data.MachineRegistry.IsValidId"/> rule.
    /// </summary>
    public static ObjectResult InvalidMachineId(string id) => Problem(
        StatusCodes.Status400BadRequest,
        "Invalid machine id",
        $"Machine id '{id}' is invalid: it must be 1-64 characters from [A-Za-z0-9-].");

    /// <summary>400 for a coin value that is not a recognised denomination.</summary>
    public static ObjectResult InvalidCoin(int cents) => Problem(
        StatusCodes.Status400BadRequest,
        "Invalid coin",
        $"{cents} cents is not a recognised coin denomination.");

    /// <summary>400 for a malformed admin refill request.</summary>
    public static ObjectResult InvalidRefill(string detail) => Problem(
        StatusCodes.Status400BadRequest,
        "Invalid refill request",
        detail);

    /// <summary>
    /// 409 for a failed selection, carrying the machine-readable
    /// <c>errorCode</c> extension the client switches on.
    /// </summary>
    public static ObjectResult SelectionConflict(SelectionFailureReason reason, string itemId)
    {
        var itemName = CoffeeMenu.Find(itemId)?.Name ?? itemId;
        var (title, detail, errorCode) = reason switch
        {
            SelectionFailureReason.InsufficientFunds => (
                "Insufficient funds",
                $"The current credit does not cover the price of {itemName}.",
                "insufficient-funds"),
            SelectionFailureReason.OutOfStock => (
                "Out of stock",
                $"{itemName} is out of stock.",
                "out-of-stock"),
            SelectionFailureReason.ExactChangeOnly => (
                "Exact change only",
                "The machine cannot make exact change for this selection; press coin return to get your credit back.",
                "exact-change-only"),
            SelectionFailureReason.UnknownItem => (
                "Unknown item",
                $"'{itemId}' is not on the menu.",
                "unknown-item"),
            _ => ("Selection failed", "The selection could not be completed.", "selection-failed"),
        };

        return Problem(StatusCodes.Status409Conflict, title, detail, errorCode);
    }

    private static ObjectResult Problem(int status, string title, string detail, string? errorCode = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
        };

        if (errorCode is not null)
        {
            problem.Extensions["errorCode"] = errorCode;
        }

        return new ObjectResult(problem) { StatusCode = status };
    }
}
