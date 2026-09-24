using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Domain.Tests;

/// <summary>
/// Exhaustive verification of the greedy change calculator against a
/// dynamic-programming reference implementation.
/// </summary>
public class ChangeCalculatorTests
{
    /// <summary>Every amount from 0 to 4000 cents in 5¢ steps.</summary>
    public static TheoryData<int> AllPayableAmounts()
    {
        var data = new TheoryData<int>();
        for (var amount = 0; amount <= 4000; amount += 5)
        {
            data.Add(amount);
        }

        return data;
    }

    private static IReadOnlyDictionary<Denomination, int> UnlimitedStock() =>
        ChangeCalculator.ChangeDenominations.ToDictionary(denomination => denomination, _ => 1_000_000);

    /// <summary>
    /// Reference minimum coin count via dynamic programming over the same
    /// denominations with unlimited supply. Used to prove the greedy result
    /// is optimal (which it is, because AUD denominations are canonical).
    /// </summary>
    private static int MinimumCoinCountByDp(int amountCents)
    {
        var denominations = new[] { 5, 10, 20, 50, 100, 200 };
        const int infinity = int.MaxValue / 2;
        var dp = new int[amountCents + 1];

        for (var amount = 1; amount <= amountCents; amount++)
        {
            var best = infinity;
            foreach (var denomination in denominations)
            {
                if (denomination <= amount)
                {
                    best = Math.Min(best, dp[amount - denomination] + 1);
                }
            }

            dp[amount] = best;
        }

        return dp[amountCents];
    }

    [Theory]
    [MemberData(nameof(AllPayableAmounts))]
    public void MakeChange_exhaustive_is_exact_minimal_and_never_uses_one_or_two_cents(int amountCents)
    {
        var change = ChangeCalculator.MakeChange(amountCents, UnlimitedStock());

        Assert.NotNull(change);

        // (a) The returned coins sum to the requested amount.
        var total = change!.Sum(pair => pair.Key.ValueCents() * pair.Value);
        Assert.Equal(amountCents, total);

        // (b) 1¢ and 2¢ coins are never dispensed.
        Assert.DoesNotContain(change.Keys, denomination => denomination is Denomination.Cent1 or Denomination.Cent2);

        // (c) The coin count is minimal — greedy equals the DP optimum.
        var greedyCount = change.Values.Sum();
        Assert.Equal(MinimumCoinCountByDp(amountCents), greedyCount);
    }

    [Fact]
    public void MakeChange_zero_returns_an_empty_breakdown()
    {
        var change = ChangeCalculator.MakeChange(0, UnlimitedStock());

        Assert.NotNull(change);
        Assert.Empty(change!);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    public void MakeChange_returns_null_for_amounts_that_cannot_be_made_exactly(int amountCents)
    {
        var change = ChangeCalculator.MakeChange(amountCents, UnlimitedStock());

        Assert.Null(change);
    }

    [Fact]
    public void MakeChange_returns_null_when_stock_cannot_cover_the_amount()
    {
        var stock = new Dictionary<Denomination, int> { [Denomination.Dollar2] = 2 };

        Assert.Null(ChangeCalculator.MakeChange(100, stock));
    }

    [Fact]
    public void MakeChange_returns_null_when_stock_is_empty_and_amount_is_positive()
    {
        Assert.Null(ChangeCalculator.MakeChange(50, new Dictionary<Denomination, int>()));
    }

    [Fact]
    public void MakeChange_returns_null_when_stock_total_is_below_the_amount()
    {
        var stock = new Dictionary<Denomination, int> { [Denomination.Dollar1] = 2, [Denomination.Cent50] = 1 };

        Assert.Null(ChangeCalculator.MakeChange(300, stock));
    }

    [Fact]
    public void MakeChange_uses_the_full_stock_exactly_when_it_fits()
    {
        var stock = new Dictionary<Denomination, int> { [Denomination.Dollar1] = 2, [Denomination.Cent50] = 1 };

        var change = ChangeCalculator.MakeChange(250, stock);

        Assert.NotNull(change);
        Assert.Equal(2, change![Denomination.Dollar1]);
        Assert.Equal(1, change[Denomination.Cent50]);
    }

    [Fact]
    public void MakeChange_descends_to_smaller_denominations_when_larger_ones_are_missing()
    {
        var stock = new Dictionary<Denomination, int> { [Denomination.Cent50] = 3 };

        var change = ChangeCalculator.MakeChange(100, stock);

        Assert.NotNull(change);
        Assert.Equal(2, change![Denomination.Cent50]);
    }

    [Fact]
    public void MakeChange_prefers_the_single_largest_coin()
    {
        var stock = new Dictionary<Denomination, int> { [Denomination.Dollar2] = 1, [Denomination.Dollar1] = 2 };

        var change = ChangeCalculator.MakeChange(200, stock);

        Assert.NotNull(change);
        Assert.Equal(1, change![Denomination.Dollar2]);
        Assert.DoesNotContain(change.Keys, denomination => denomination == Denomination.Dollar1);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    [InlineData(int.MinValue)]
    public void MakeChange_throws_for_negative_amounts(int amountCents)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChangeCalculator.MakeChange(amountCents, UnlimitedStock()));
    }

    [Fact]
    public void MakeChange_throws_for_null_stock()
    {
        Assert.Throws<ArgumentNullException>(() => ChangeCalculator.MakeChange(100, null!));
    }

    [Fact]
    public void CanMakeChange_returns_true_for_zero_even_with_empty_stock()
    {
        Assert.True(ChangeCalculator.CanMakeChange(0, new Dictionary<Denomination, int>()));
    }

    [Fact]
    public void CanMakeChange_returns_false_when_stock_is_insufficient()
    {
        var stock = new Dictionary<Denomination, int> { [Denomination.Dollar2] = 2 };

        Assert.False(ChangeCalculator.CanMakeChange(100, stock));
    }

    [Fact]
    public void CanMakeChange_returns_true_when_stock_covers_the_amount()
    {
        var stock = new Dictionary<Denomination, int> { [Denomination.Dollar1] = 3 };

        Assert.True(ChangeCalculator.CanMakeChange(300, stock));
    }
}
