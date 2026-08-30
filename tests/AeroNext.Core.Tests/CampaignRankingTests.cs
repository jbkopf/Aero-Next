using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class CampaignRankingTests
{
    private static readonly City A = new("a", "A", Region.EastAsia, 0, 0, 50, false, null);
    private static readonly City B = new("b", "B", Region.EastAsia, 5, 5, 50, false, null);
    private static readonly AircraftType Aircraft = new("t", "Test", Seats: 1000);
    private static readonly Route Route = new(A, B, Aircraft, 10);
    private static readonly CompetitiveMarketResult EmptyMarket = new(Array.Empty<CompetitivePairResult>(), Array.Empty<CompetitiveRouteUsage>());

    [Fact]
    public void Rank_OrdersByRegionsLedDescending()
    {
        var state = new RegionalDominanceState(new Dictionary<(string, Region), int>
        {
            [("A", Region.NorthAmerica)] = 2,
            [("A", Region.Europe)] = 1,
            [("B", Region.EastAsia)] = 1,
        });
        var netWorth = new Dictionary<string, Money> { ["A"] = Money.Zero, ["B"] = Money.Zero };

        var ranked = CampaignRanking.Rank(state, EmptyMarket, netWorth);

        Assert.Equal(new[] { "A", "B" }, ranked.Select(r => r.AirlineName));
        Assert.Equal(2, ranked[0].RegionsLed);
        Assert.Equal(1, ranked[1].RegionsLed);
    }

    [Fact]
    public void Rank_TiedRegionsLed_BrokenByTotalPassengersCarried()
    {
        var state = new RegionalDominanceState(new Dictionary<(string, Region), int>
        {
            [("A", Region.NorthAmerica)] = 1,
            [("B", Region.Europe)] = 1,
        });
        var market = new CompetitiveMarketResult(Array.Empty<CompetitivePairResult>(), new[]
        {
            new CompetitiveRouteUsage(new Airline("A", new[] { Route }), Route, 1000, 800),
            new CompetitiveRouteUsage(new Airline("B", new[] { Route }), Route, 1000, 200),
        });
        var netWorth = new Dictionary<string, Money> { ["A"] = Money.Zero, ["B"] = Money.Zero };

        var ranked = CampaignRanking.Rank(state, market, netWorth);

        Assert.Equal("A", ranked[0].AirlineName);
    }

    [Fact]
    public void Rank_TiedRegionsAndPassengers_BrokenByNetWorth()
    {
        var netWorth = new Dictionary<string, Money> { ["A"] = Money.FromDollars(100), ["B"] = Money.FromDollars(200) };

        var ranked = CampaignRanking.Rank(RegionalDominanceState.Empty, EmptyMarket, netWorth);

        Assert.Equal("B", ranked[0].AirlineName);
    }

    [Fact]
    public void Rank_CompleteTie_BrokenAlphabeticallyByName()
    {
        var netWorth = new Dictionary<string, Money> { ["Zulu"] = Money.Zero, ["Alpha"] = Money.Zero };

        var ranked = CampaignRanking.Rank(RegionalDominanceState.Empty, EmptyMarket, netWorth);

        Assert.Equal(new[] { "Alpha", "Zulu" }, ranked.Select(r => r.AirlineName));
    }

    [Fact]
    public void Rank_IncludesEveryAirlineFromNetWorth_EvenWithNoRegionsOrPassengers()
    {
        var netWorth = new Dictionary<string, Money> { ["A"] = Money.Zero, ["B"] = Money.Zero, ["C"] = Money.Zero, ["D"] = Money.Zero };

        var ranked = CampaignRanking.Rank(RegionalDominanceState.Empty, EmptyMarket, netWorth);

        Assert.Equal(4, ranked.Count);
        Assert.All(ranked, r => Assert.Equal(0, r.RegionsLed));
        Assert.All(ranked, r => Assert.Equal(0, r.TotalPassengersCarried));
    }
}
