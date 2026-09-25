using CoffeeMachine.Domain.Money;

namespace CoffeeMachine.Domain.Tests;

/// <summary>
/// Verifies the single shared money formatter used by both the API and the
/// Blazor client.
/// </summary>
public class MoneyFormatTests
{
    [Theory]
    [InlineData(0, "$0.00")]
    [InlineData(5, "$0.05")]
    [InlineData(50, "$0.50")]
    [InlineData(100, "$1.00")]
    [InlineData(350, "$3.50")]
    [InlineData(4095, "$40.95")]
    [InlineData(12345, "$123.45")]
    [InlineData(-50, "-$0.50")]
    [InlineData(-305, "-$3.05")]
    public void Format_renders_cent_amounts_as_dollar_strings(int cents, string expected)
    {
        Assert.Equal(expected, MoneyFormat.Format(cents));
    }

    [Fact]
    public void Format_is_stable_across_cultures()
    {
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE"); // comma decimal separator
            Assert.Equal("$3.50", MoneyFormat.Format(350));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = before;
        }
    }
}
