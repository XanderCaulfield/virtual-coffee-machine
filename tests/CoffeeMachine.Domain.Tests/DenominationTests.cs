using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Domain.Tests;

/// <summary>Verifies denomination values and the companion lookup/display helpers.</summary>
public class DenominationTests
{
    [Theory]
    [InlineData(Denomination.Cent1, 1)]
    [InlineData(Denomination.Cent2, 2)]
    [InlineData(Denomination.Cent5, 5)]
    [InlineData(Denomination.Cent10, 10)]
    [InlineData(Denomination.Cent20, 20)]
    [InlineData(Denomination.Cent50, 50)]
    [InlineData(Denomination.Dollar1, 100)]
    [InlineData(Denomination.Dollar2, 200)]
    public void ValueCents_exposes_the_cent_value(Denomination denomination, int expectedCents)
    {
        Assert.Equal(expectedCents, denomination.ValueCents());
    }

    [Fact]
    public void Accepted_contains_exactly_the_six_accepted_denominations_in_order()
    {
        Assert.Equal(
            new[] { Denomination.Cent5, Denomination.Cent10, Denomination.Cent20, Denomination.Cent50, Denomination.Dollar1, Denomination.Dollar2 },
            Denominations.Accepted);
    }

    [Fact]
    public void All_contains_every_defined_denomination_including_one_and_two_cent()
    {
        Assert.Equal(8, Denominations.All.Count);
        Assert.Contains(Denomination.Cent1, Denominations.All);
        Assert.Contains(Denomination.Cent2, Denominations.All);
    }

    [Theory]
    [InlineData(Denomination.Cent1, "1¢")]
    [InlineData(Denomination.Cent2, "2¢")]
    [InlineData(Denomination.Cent5, "5¢")]
    [InlineData(Denomination.Cent10, "10¢")]
    [InlineData(Denomination.Cent20, "20¢")]
    [InlineData(Denomination.Cent50, "50¢")]
    [InlineData(Denomination.Dollar1, "$1")]
    [InlineData(Denomination.Dollar2, "$2")]
    public void DisplayName_labels_each_denomination(Denomination denomination, string expected)
    {
        Assert.Equal(expected, denomination.DisplayName());
    }

    [Theory]
    [InlineData(1, Denomination.Cent1)]
    [InlineData(2, Denomination.Cent2)]
    [InlineData(5, Denomination.Cent5)]
    [InlineData(10, Denomination.Cent10)]
    [InlineData(20, Denomination.Cent20)]
    [InlineData(50, Denomination.Cent50)]
    [InlineData(100, Denomination.Dollar1)]
    [InlineData(200, Denomination.Dollar2)]
    public void FromCents_maps_cent_values_to_denominations(int cents, Denomination expected)
    {
        Assert.Equal(expected, Denominations.FromCents(cents));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(25)]
    [InlineData(99)]
    [InlineData(250)]
    [InlineData(-5)]
    public void FromCents_throws_for_values_that_are_not_real_coins(int cents)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Denominations.FromCents(cents));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    public void TryParse_succeeds_for_every_defined_coin(int cents)
    {
        Assert.True(Denominations.TryParse(cents, out var denomination));
        Assert.Equal(cents, denomination.ValueCents());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(25)]
    [InlineData(250)]
    [InlineData(-5)]
    public void TryParse_fails_for_values_that_are_not_real_coins(int cents)
    {
        Assert.False(Denominations.TryParse(cents, out _));
    }
}
