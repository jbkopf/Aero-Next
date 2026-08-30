using AeroNext.Core.Finance;
using Xunit;

namespace AeroNext.Core.Tests;

public class FinancialStateTests
{
    [Fact]
    public void CanAfford_CostBelowAvailable_True()
    {
        Assert.True(FinancialState.CanAfford(Money.FromDollars(1_000), Money.FromDollars(999)));
    }

    [Fact]
    public void CanAfford_CostExactlyMatchesAvailable_True()
    {
        Assert.True(FinancialState.CanAfford(Money.FromDollars(1_000), Money.FromDollars(1_000)));
    }

    [Fact]
    public void CanAfford_CostExceedsAvailable_False()
    {
        Assert.False(FinancialState.CanAfford(Money.FromDollars(1_000), Money.FromDollars(1_001)));
    }

    [Fact]
    public void CanAfford_ZeroCost_AlwaysTrue_EvenWithZeroAvailable()
    {
        Assert.True(FinancialState.CanAfford(Money.Zero, Money.Zero));
    }
}
