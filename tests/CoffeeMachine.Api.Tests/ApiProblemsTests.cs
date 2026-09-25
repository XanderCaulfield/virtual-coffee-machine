using CoffeeMachine.Api;
using CoffeeMachine.Domain.Machine;
using Microsoft.AspNetCore.Mvc;

namespace CoffeeMachine.Api.Tests;

/// <summary>Verifies the RFC 7807 problem shapes the controllers return.</summary>
public class ApiProblemsTests
{
    [Theory]
    [InlineData(SelectionFailureReason.InsufficientFunds, "Insufficient funds", "insufficient-funds")]
    [InlineData(SelectionFailureReason.OutOfStock, "Out of stock", "out-of-stock")]
    [InlineData(SelectionFailureReason.ExactChangeOnly, "Exact change only", "exact-change-only")]
    [InlineData(SelectionFailureReason.UnknownItem, "Unknown item", "unknown-item")]
    public void SelectionConflict_maps_each_reason_to_a_409_problem_with_an_error_code(
        SelectionFailureReason reason,
        string expectedTitle,
        string expectedErrorCode)
    {
        var result = ApiProblems.SelectionConflict(reason, "latte");

        Assert.Equal(409, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal(expectedTitle, problem.Title);
        Assert.Equal(expectedErrorCode, problem.Extensions["errorCode"]);
    }

    [Fact]
    public void SelectionConflict_uses_the_item_name_in_the_detail()
    {
        var result = ApiProblems.SelectionConflict(SelectionFailureReason.InsufficientFunds, "latte");

        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Contains("Latte", problem.Detail);
    }

    [Fact]
    public void InvalidMachineId_is_a_400_problem()
    {
        var result = ApiProblems.InvalidMachineId("bad id!");

        Assert.Equal(400, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("Invalid machine id", problem.Title);
        Assert.Contains("bad id!", problem.Detail);
    }

    [Fact]
    public void InvalidCoin_is_a_400_problem()
    {
        var result = ApiProblems.InvalidCoin(25);

        Assert.Equal(400, result.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(result.Value);
        Assert.Equal("Invalid coin", problem.Title);
    }
}
