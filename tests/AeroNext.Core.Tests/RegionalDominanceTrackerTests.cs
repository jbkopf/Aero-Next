using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class RegionalDominanceTrackerTests
{
    private static City MakeCity(string id, double lat, double lon, int weight, Region region = Region.EastAsia) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, false, null);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly HashSet<string> AB = new(StringComparer.OrdinalIgnoreCase) { "A", "B" };

    [Fact]
    public void Advance_SoleLeader_StartsAStreakOfOne()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 5, 5, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var airline = new Airline("A", new[] { new Route(a, b, aircraft, 10) });
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, b }, DemandModel);

        var state = RegionalDominanceTracker.Advance(RegionalDominanceState.Empty, market, new HashSet<string> { "A" });

        Assert.Equal(1, state.StreakFor("A", Region.EastAsia));
    }

    [Fact]
    public void Advance_SameLeaderAcrossQuarters_StreakKeepsIncrementing()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 5, 5, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var eligible = new HashSet<string> { "A" };

        var airlineQ1 = new Airline("A", new[] { new Route(a, b, aircraft, 10) });
        var marketQ1 = CompetitiveMarket.Resolve(new[] { airlineQ1 }, new[] { a, b }, DemandModel);
        var afterQ1 = RegionalDominanceTracker.Advance(RegionalDominanceState.Empty, marketQ1, eligible);

        var airlineQ2 = new Airline("A", new[] { new Route(a, b, aircraft, 10) });
        var marketQ2 = CompetitiveMarket.Resolve(new[] { airlineQ2 }, new[] { a, b }, DemandModel);
        var afterQ2 = RegionalDominanceTracker.Advance(afterQ1, marketQ2, eligible);

        Assert.Equal(2, afterQ2.StreakFor("A", Region.EastAsia));
    }

    [Fact]
    public void Advance_KeyedByAirlineName_NotByAirlineRecord_SurvivesARebuiltRouteList()
    {
        // The exact correctness trap flagged in the design: Airline has
        // structural equality over its Routes list, and a fresh Airline
        // object gets built every quarter at both real call sites. If the
        // tracker mistakenly kept state keyed by the Airline record itself,
        // this streak would never survive past quarter 1.
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 5, 5, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var eligible = new HashSet<string> { "A" };

        var airlineQ1 = new Airline("A", new[] { new Route(a, b, aircraft, 10) });
        var marketQ1 = CompetitiveMarket.Resolve(new[] { airlineQ1 }, new[] { a, b }, DemandModel);
        var afterQ1 = RegionalDominanceTracker.Advance(RegionalDominanceState.Empty, marketQ1, eligible);

        // A distinctly different Routes list object (extra frequency), same airline name.
        var airlineQ2 = new Airline("A", new[] { new Route(a, b, aircraft, 12) });
        var marketQ2 = CompetitiveMarket.Resolve(new[] { airlineQ2 }, new[] { a, b }, DemandModel);
        var afterQ2 = RegionalDominanceTracker.Advance(afterQ1, marketQ2, eligible);

        Assert.Equal(2, afterQ2.StreakFor("A", Region.EastAsia));
    }

    [Fact]
    public void Advance_NewLeaderOvertakes_StartsFreshStreak_OldLeaderDrops()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 5, 5, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var route = new Route(a, b, aircraft, 10);

        var q1Market = new CompetitiveMarketResult(Array.Empty<CompetitivePairResult>(), new[]
        {
            new CompetitiveRouteUsage(new Airline("A", new[] { route }), route, 1000, 800),
            new CompetitiveRouteUsage(new Airline("B", new[] { route }), route, 1000, 200),
        });
        var afterQ1 = RegionalDominanceTracker.Advance(RegionalDominanceState.Empty, q1Market, AB);
        Assert.Equal(1, afterQ1.StreakFor("A", Region.EastAsia));

        var q2Market = new CompetitiveMarketResult(Array.Empty<CompetitivePairResult>(), new[]
        {
            new CompetitiveRouteUsage(new Airline("A", new[] { route }), route, 1000, 200),
            new CompetitiveRouteUsage(new Airline("B", new[] { route }), route, 1000, 800),
        });
        var afterQ2 = RegionalDominanceTracker.Advance(afterQ1, q2Market, AB);

        Assert.Equal(1, afterQ2.StreakFor("B", Region.EastAsia));
        Assert.Equal(0, afterQ2.StreakFor("A", Region.EastAsia));
    }

    [Fact]
    public void Advance_TiedPassengerCounts_NoLeaderRecorded()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 5, 5, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var route = new Route(a, b, aircraft, 10);

        var tiedMarket = new CompetitiveMarketResult(Array.Empty<CompetitivePairResult>(), new[]
        {
            new CompetitiveRouteUsage(new Airline("A", new[] { route }), route, 1000, 500),
            new CompetitiveRouteUsage(new Airline("B", new[] { route }), route, 1000, 500),
        });

        var state = RegionalDominanceTracker.Advance(RegionalDominanceState.Empty, tiedMarket, AB);

        Assert.Equal(0, state.StreakFor("A", Region.EastAsia));
        Assert.Equal(0, state.StreakFor("B", Region.EastAsia));
    }

    [Fact]
    public void Advance_TieAfterAStreak_BreaksIt()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 5, 5, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var route = new Route(a, b, aircraft, 10);

        var leaderMarket = new CompetitiveMarketResult(Array.Empty<CompetitivePairResult>(), new[]
        {
            new CompetitiveRouteUsage(new Airline("A", new[] { route }), route, 1000, 800),
            new CompetitiveRouteUsage(new Airline("B", new[] { route }), route, 1000, 200),
        });
        var afterFirst = RegionalDominanceTracker.Advance(RegionalDominanceState.Empty, leaderMarket, AB);
        Assert.Equal(1, afterFirst.StreakFor("A", Region.EastAsia));

        var tiedMarket = new CompetitiveMarketResult(Array.Empty<CompetitivePairResult>(), new[]
        {
            new CompetitiveRouteUsage(new Airline("A", new[] { route }), route, 1000, 500),
            new CompetitiveRouteUsage(new Airline("B", new[] { route }), route, 1000, 500),
        });
        var afterSecond = RegionalDominanceTracker.Advance(afterFirst, tiedMarket, AB);

        Assert.Equal(0, afterSecond.StreakFor("A", Region.EastAsia));
    }

    [Fact]
    public void Advance_IneligibleLeader_Excluded_LeadershipFallsToNextEligibleAirline()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 5, 5, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var route = new Route(a, b, aircraft, 10);

        var market = new CompetitiveMarketResult(Array.Empty<CompetitivePairResult>(), new[]
        {
            new CompetitiveRouteUsage(new Airline("Bankrupt", new[] { route }), route, 1000, 900), // most traffic, but not eligible
            new CompetitiveRouteUsage(new Airline("Solvent", new[] { route }), route, 1000, 100),
        });

        var state = RegionalDominanceTracker.Advance(RegionalDominanceState.Empty, market, new HashSet<string> { "Solvent" });

        Assert.Equal(1, state.StreakFor("Solvent", Region.EastAsia));
        Assert.Equal(0, state.StreakFor("Bankrupt", Region.EastAsia));
    }

    [Fact]
    public void Advance_EligibleAirlineTouchesRegionButCarriesZeroPassengers_NoLeaderRecorded()
    {
        // Distinct from the "no eligible airline touches this region at
        // all" case (empty dictionary) - here an airline has a route
        // touching the region, but it's an empty/zero-load-factor route,
        // so nobody actually carried anyone there this quarter.
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 5, 5, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var route = new Route(a, b, aircraft, 10);

        var market = new CompetitiveMarketResult(Array.Empty<CompetitivePairResult>(), new[]
        {
            new CompetitiveRouteUsage(new Airline("A", new[] { route }), route, 1000, 0),
        });

        var state = RegionalDominanceTracker.Advance(RegionalDominanceState.Empty, market, new HashSet<string> { "A" });

        Assert.Empty(state.StreakByLeader);
    }

    [Fact]
    public void Advance_RouteTouchingOnlyAfrica_CreatesARegionEntry()
    {
        // Africa-counts-toward-the-win-condition follow-up: Africa isn't
        // in HomeRegions.All (still never home-eligible - see
        // HomeRegionsTests/RegionalEconomicsTests, both untouched by this
        // change), but it is in DominanceTrackedRegions.All - a route
        // entirely within Africa now creates a real leadership entry,
        // same as any other tracked region.
        var cai = MakeCity("cai", 30.1, 31.4, 50, Region.Africa);
        var dkr = MakeCity("dkr", 14.7, -17.4, 50, Region.Africa);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var airline = new Airline("A", new[] { new Route(cai, dkr, aircraft, 10) });
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { cai, dkr }, DemandModel);

        var state = RegionalDominanceTracker.Advance(RegionalDominanceState.Empty, market, new HashSet<string> { "A" });

        Assert.Equal(1, state.StreakFor("A", Region.Africa));
    }

    [Fact]
    public void Advance_TwoAirlinesContestAfrica_LeaderStreakIncrementsLikeAnyOtherRegion()
    {
        // Real streak accrual, not just a single-quarter entry - "A"
        // wins Africa's first quarter (more seats carried there), loses
        // the next when "B" overtakes it. Mirrors the existing
        // contested-region tests above for a real home region, just
        // targeted at Africa specifically.
        var cai = MakeCity("cai", 30.1, 31.4, 50, Region.Africa);
        var dkr = MakeCity("dkr", 14.7, -17.4, 50, Region.Africa);
        var big = new AircraftType("big", "Big", Seats: 1000);
        var small = new AircraftType("small", "Small", Seats: 10);
        var airlineA = new Airline("A", new[] { new Route(cai, dkr, big, 10) });
        var airlineB = new Airline("B", new[] { new Route(cai, dkr, small, 1) });
        var quarter1Market = CompetitiveMarket.Resolve(new[] { airlineA, airlineB }, new[] { cai, dkr }, DemandModel);
        var eligible = new HashSet<string> { "A", "B" };

        var afterQuarter1 = RegionalDominanceTracker.Advance(RegionalDominanceState.Empty, quarter1Market, eligible);
        Assert.Equal(1, afterQuarter1.StreakFor("A", Region.Africa));
        Assert.Equal(0, afterQuarter1.StreakFor("B", Region.Africa));

        var airlineAShrunk = new Airline("A", new[] { new Route(cai, dkr, small, 1) });
        var airlineBGrown = new Airline("B", new[] { new Route(cai, dkr, big, 10) });
        var quarter2Market = CompetitiveMarket.Resolve(new[] { airlineAShrunk, airlineBGrown }, new[] { cai, dkr }, DemandModel);

        var afterQuarter2 = RegionalDominanceTracker.Advance(afterQuarter1, quarter2Market, eligible);
        Assert.Equal(0, afterQuarter2.StreakFor("A", Region.Africa));
        Assert.Equal(1, afterQuarter2.StreakFor("B", Region.Africa));
    }
}
