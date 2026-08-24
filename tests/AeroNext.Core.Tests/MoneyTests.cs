using AeroNext.Core;
using Xunit;

namespace AeroNext.Core.Tests;

public class MoneyTests
{
    [Fact]
    public void FromDollars_ConvertsToCents()
    {
        Assert.Equal(150_000, Money.FromDollars(1_500).Cents);
    }

    [Fact]
    public void Addition_IsExact()
    {
        var a = Money.FromDollars(10);
        var b = Money.FromDollars(5);
        Assert.Equal(Money.FromDollars(15), a + b);
    }
}
