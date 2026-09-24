using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Domain.Tests;

/// <summary>
/// Verifies the coin acceptance matrix: 5¢–$2 accepted, 1¢/2¢ rejected with a
/// specific reason, everything else rejected as invalid.
/// </summary>
public class CoinValidatorTests
{
    public static TheoryData<Denomination> AcceptedDenominations => new()
    {
        Denomination.Cent5,
        Denomination.Cent10,
        Denomination.Cent20,
        Denomination.Cent50,
        Denomination.Dollar1,
        Denomination.Dollar2,
    };

    public static TheoryData<int> AcceptedCentValues => new() { 5, 10, 20, 50, 100, 200 };

    [Theory]
    [MemberData(nameof(AcceptedDenominations))]
    public void Validate_accepts_every_accepted_denomination(Denomination denomination)
    {
        var result = CoinValidator.Validate(denomination);

        Assert.True(result.Accepted);
        Assert.Null(result.RejectionReason);
    }

    [Theory]
    [MemberData(nameof(AcceptedCentValues))]
    public void Validate_accepts_every_accepted_cent_value(int cents)
    {
        var result = CoinValidator.Validate(cents);

        Assert.True(result.Accepted);
        Assert.Null(result.RejectionReason);
    }

    [Theory]
    [InlineData(Denomination.Cent1, "1 cent coins are not accepted")]
    [InlineData(Denomination.Cent2, "2 cent coins are not accepted")]
    public void Validate_rejects_one_and_two_cent_denominations_with_a_reason(
        Denomination denomination,
        string expectedReason)
    {
        var result = CoinValidator.Validate(denomination);

        Assert.False(result.Accepted);
        Assert.Equal(expectedReason, result.RejectionReason);
    }

    [Theory]
    [InlineData(1, "1 cent coins are not accepted")]
    [InlineData(2, "2 cent coins are not accepted")]
    public void Validate_rejects_one_and_two_cent_values_with_a_reason(int cents, string expectedReason)
    {
        var result = CoinValidator.Validate(cents);

        Assert.False(result.Accepted);
        Assert.Equal(expectedReason, result.RejectionReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(25)]
    [InlineData(99)]
    [InlineData(101)]
    [InlineData(250)]
    [InlineData(-5)]
    public void Validate_rejects_values_that_are_not_real_coins(int cents)
    {
        var result = CoinValidator.Validate(cents);

        Assert.False(result.Accepted);
        Assert.Equal("Invalid coin", result.RejectionReason);
    }

    [Fact]
    public void Validate_rejects_undefined_enum_values_as_invalid()
    {
        var result = CoinValidator.Validate((Denomination)7);

        Assert.False(result.Accepted);
        Assert.Equal("Invalid coin", result.RejectionReason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(250)]
    public void Rejection_reasons_are_never_empty(int cents)
    {
        var result = CoinValidator.Validate(cents);

        Assert.False(result.Accepted);
        Assert.False(string.IsNullOrWhiteSpace(result.RejectionReason));
    }
}
