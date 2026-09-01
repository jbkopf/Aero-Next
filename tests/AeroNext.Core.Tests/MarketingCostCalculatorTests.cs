using AeroNext.Core.Finance;
using Xunit;

namespace AeroNext.Core.Tests;

public class MarketingCostCalculatorTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 100_000)]
    [InlineData(5, 500_000)]
    public void NetworkCampaignCost_ScalesWithRouteCount(int routeCount, long expectedDollars)
    {
        Assert.Equal(Money.FromDollars(expectedDollars), MarketingCostCalculator.NetworkCampaignCost(routeCount));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1_000, 50_000)]
    [InlineData(10_000, 500_000)]
    public void RouteCampaignCost_ScalesWithDemand(long demandPassengers, long expectedDollars)
    {
        Assert.Equal(Money.FromDollars(expectedDollars), MarketingCostCalculator.RouteCampaignCost(demandPassengers));
    }
}
