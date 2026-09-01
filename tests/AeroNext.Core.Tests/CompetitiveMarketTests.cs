using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class CompetitiveMarketTests
{
    // Light weight + far apart keeps demand modest, so it's easy to build
    // scenarios where combined competitor capacity exceeds it (the regime
    // where competition actually has something to redistribute).
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, false, null);

    [Fact]
    public void Resolve_SoleCarrier_GetsFullMonopolyCarried_UnaffectedByNonCompetitor()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var elsewhere = MakeCity("c", 40, 40, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 400);

        var solo = new Airline("Solo", new[] { new Route(a, b, aircraft, 5) });
        var unrelated = new Airline("Unrelated", new[] { new Route(a, elsewhere, aircraft, 5) }); // doesn't touch a-b

        var result = CompetitiveMarket.Resolve(new[] { solo, unrelated }, new[] { a, b, elsewhere }, demandModel);

        var monopoly = NetworkPlanner.Plan(solo.Routes, new[] { a, b, elsewhere }, demandModel)
            .Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "b")
            .PassengersCarried;

        Assert.Equal(monopoly, result.TotalCarried(solo));
    }

    [Fact]
    public void Resolve_EqualCompetitors_SplitDemandEvenly_WhenDemandConstrained()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var bigAircraft = new AircraftType("t", "Test", Seats: 1000); // both far from saturating alone

        var airlineA = new Airline("A", new[] { new Route(a, b, bigAircraft, 10) });
        var airlineB = new Airline("B", new[] { new Route(a, b, bigAircraft, 10) });

        var result = CompetitiveMarket.Resolve(new[] { airlineA, airlineB }, new[] { a, b }, demandModel);

        var carriedA = result.TotalCarried(airlineA);
        var carriedB = result.TotalCarried(airlineB);

        Assert.True(carriedA > 0);
        Assert.InRange((double)carriedA / carriedB, 0.95, 1.05); // roughly even, allowing for rounding
    }

    [Fact]
    public void Resolve_IncreasingOneAirlinesFrequency_ErodesTheOthers_WhenDemandConstrained()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);

        var airlineB = new Airline("B", new[] { new Route(a, b, aircraft, 5) });

        var baselineA = new Airline("A", new[] { new Route(a, b, aircraft, 5) });
        var boostedA = new Airline("A", new[] { new Route(a, b, aircraft, 20) }); // 4x A's frequency

        var before = CompetitiveMarket.Resolve(new[] { baselineA, airlineB }, new[] { a, b }, demandModel);
        var after = CompetitiveMarket.Resolve(new[] { boostedA, airlineB }, new[] { a, b }, demandModel);

        Assert.True(after.TotalCarried(airlineB) < before.TotalCarried(airlineB));
    }

    [Fact]
    public void Resolve_WhenCombinedCapacityBelowDemand_NoCompetitionEffect_BothHitOwnCeiling()
    {
        var demandModel = new GravityDemandModel();
        // Close + heavy: demand vastly exceeds any realistic small fleet.
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 100);
        var tinyAircraft = new AircraftType("t", "Test", Seats: 10);

        var airlineA = new Airline("A", new[] { new Route(a, b, tinyAircraft, 1) });
        var airlineB = new Airline("B", new[] { new Route(a, b, tinyAircraft, 1) });

        var result = CompetitiveMarket.Resolve(new[] { airlineA, airlineB }, new[] { a, b }, demandModel);

        // Each airline should still hit its own full monopoly ceiling —
        // competition has nothing to redistribute when demand swamps both.
        var soloA = NetworkPlanner.Plan(airlineA.Routes, new[] { a, b }, demandModel).TotalCarried;
        Assert.Equal(soloA, result.TotalCarried(airlineA));
        Assert.Equal(soloA, result.TotalCarried(airlineB));
    }

    [Fact]
    public void Resolve_HigherQuality_CapturesLargerShare_AtEqualFrequency()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);

        var premium = new Airline("Premium", new[] { new Route(a, b, aircraft, 10) }, Quality: 2.0);
        var basic = new Airline("Basic", new[] { new Route(a, b, aircraft, 10) }, Quality: 1.0);

        var result = CompetitiveMarket.Resolve(new[] { premium, basic }, new[] { a, b }, demandModel);

        Assert.True(result.TotalCarried(premium) > result.TotalCarried(basic));
    }

    [Fact]
    public void Resolve_ZeroQualityCompetitors_DoesNotCrash_CarriesNothing()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);

        // Both airlines have zero quality -> total weight is zero on this
        // pair. Must not divide by zero or throw; nobody captures the pair.
        var airlineA = new Airline("A", new[] { new Route(a, b, aircraft, 10) }, Quality: 0.0);
        var airlineB = new Airline("B", new[] { new Route(a, b, aircraft, 10) }, Quality: 0.0);

        var result = CompetitiveMarket.Resolve(new[] { airlineA, airlineB }, new[] { a, b }, demandModel);

        Assert.Equal(0, result.TotalCarried(airlineA));
        Assert.Equal(0, result.TotalCarried(airlineB));
    }

    [Fact]
    public void Resolve_RouteUsage_SumsCarriedAcrossEveryPairThatSharesALeg()
    {
        // Hub scenario: both airlines fly a-hub and hub-b. The hub-b leg
        // carries each airline's own local hub-b demand *and* its share of
        // the a-b one-stop connection - RouteUsage.SeatsUsed for hub-b must
        // equal the sum of both, per airline. This is the per-leg capacity
        // accounting under competition, never directly checked before (only
        // TotalCarried per airline was, which can't catch a bug in how
        // usage is attributed to individual legs).
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 20);
        var hub = MakeCity("hub", 0, 10, 20);
        var b = MakeCity("b", 0, 20, 20);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);

        var airlineA = new Airline("A", new[] { new Route(a, hub, aircraft, 10), new Route(hub, b, aircraft, 10) });
        var airlineB = new Airline("B", new[] { new Route(a, hub, aircraft, 10), new Route(hub, b, aircraft, 10) });

        var result = CompetitiveMarket.Resolve(new[] { airlineA, airlineB }, new[] { a, hub, b }, demandModel);

        foreach (var airline in new[] { airlineA, airlineB })
        {
            var hubToBRoute = airline.Routes.Single(r => r.Origin.Id == "hub" && r.Destination.Id == "b");
            var hubToBUsage = result.RouteUsage.Single(u => u.Airline == airline && u.Route == hubToBRoute);

            // Pairs are keyed in ascending id order ("b" < "hub" ordinally).
            var hubToBPairCarried = result.Pairs
                .Single(p => p.Origin.Id == "b" && p.Destination.Id == "hub")
                .CarriedByAirline.GetValueOrDefault(airline);
            var aToBPairCarried = result.Pairs
                .Single(p => p.Origin.Id == "a" && p.Destination.Id == "b")
                .CarriedByAirline.GetValueOrDefault(airline);

            Assert.Equal(hubToBPairCarried + aToBPairCarried, hubToBUsage.SeatsUsed);
        }
    }

    [Fact]
    public void RouteUsage_NormalCapacityRoute_LoadFactorDividesSeatsUsedBySeatsPerQuarter()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 100);
        var tinyAircraft = new AircraftType("t", "Test", Seats: 10); // capacity-constrained, so LF pins at 1.0
        var airline = new Airline("A", new[] { new Route(a, b, tinyAircraft, AircraftCount: 1) });

        var result = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, b }, demandModel);

        var usage = result.RouteUsage.Single();
        Assert.True(usage.SeatsPerQuarter > 0);
        Assert.Equal((double)usage.SeatsUsed / usage.SeatsPerQuarter, usage.LoadFactor, precision: 9);
    }

    [Fact]
    public void RouteUsage_ZeroCapacityRoute_LoadFactorIsZero_NotNaN()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 20);
        var b = MakeCity("b", 5, 5, 20);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var airline = new Airline("A", new[] { new Route(a, b, aircraft, AircraftCount: 0) });

        var result = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, b }, demandModel);

        var usage = result.RouteUsage.Single();
        Assert.Equal(0, usage.SeatsPerQuarter);
        Assert.Equal(0.0, usage.LoadFactor);
    }

    [Fact]
    public void Resolve_IsDeterministic_RegardlessOfAirlineInputOrder()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 20);
        var hub = MakeCity("hub", 0, 10, 20);
        var b = MakeCity("b", 0, 20, 20);
        var aircraft = new AircraftType("t", "Test", Seats: 500);

        var airlineA = new Airline("A", new[] { new Route(a, hub, aircraft, 5), new Route(hub, b, aircraft, 5) });
        var airlineB = new Airline("B", new[] { new Route(a, hub, aircraft, 3) });
        var airlineC = new Airline("C", new[] { new Route(hub, b, aircraft, 3) });

        var original = CompetitiveMarket.Resolve(new[] { airlineA, airlineB, airlineC }, new[] { a, hub, b }, demandModel);
        var shuffled = CompetitiveMarket.Resolve(new[] { airlineC, airlineA, airlineB }, new[] { b, a, hub }, demandModel);

        Assert.Equal(original.TotalCarried(airlineA), shuffled.TotalCarried(airlineA));
        Assert.Equal(original.TotalCarried(airlineB), shuffled.TotalCarried(airlineB));
        Assert.Equal(original.TotalCarried(airlineC), shuffled.TotalCarried(airlineC));
        Assert.Equal(
            original.RouteUsage.Select(u => (u.Airline.Name, u.Route.Origin.Id, u.Route.Destination.Id, u.SeatsUsed)),
            shuffled.RouteUsage.Select(u => (u.Airline.Name, u.Route.Origin.Id, u.Route.Destination.Id, u.SeatsUsed)));
    }

    // --- M47: marketing campaigns ---

    [Fact]
    public void Resolve_RouteCampaign_CapturesLargerShare_OnTheTargetedPair()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);

        var campaigning = new Airline("Campaigning", new[] { new Route(a, b, aircraft, 10) });
        var rival = new Airline("Rival", new[] { new Route(a, b, aircraft, 10) });
        var marketing = new Dictionary<string, AirlineMarketingCampaign>
        {
            ["Campaigning"] = new(NetworkWide: false, RouteKeys: new HashSet<(string, string)> { QuarterEngine.NormalizedPairKey(a, b) }),
        };

        var result = CompetitiveMarket.Resolve(new[] { campaigning, rival }, new[] { a, b }, demandModel, marketing);

        Assert.True(result.TotalCarried(campaigning) > result.TotalCarried(rival));
    }

    [Fact]
    public void Resolve_NetworkCampaign_BoostsEveryPair_NotJustOne()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var c = MakeCity("c", 10, 10, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);

        var campaigning = new Airline("Campaigning", new[] { new Route(a, b, aircraft, 10), new Route(a, c, aircraft, 10) });
        var rival = new Airline("Rival", new[] { new Route(a, b, aircraft, 10), new Route(a, c, aircraft, 10) });
        var marketing = new Dictionary<string, AirlineMarketingCampaign>
        {
            ["Campaigning"] = new(NetworkWide: true, RouteKeys: new HashSet<(string, string)>()),
        };

        var result = CompetitiveMarket.Resolve(new[] { campaigning, rival }, new[] { a, b, c }, demandModel, marketing);

        var carriedAB = result.Pairs.Single(p => p.Origin.Id == "a" && p.Destination.Id == "b").CarriedByAirline;
        var carriedAC = result.Pairs.Single(p => p.Origin.Id == "a" && p.Destination.Id == "c").CarriedByAirline;
        Assert.True(carriedAB[campaigning] > carriedAB[rival]);
        Assert.True(carriedAC[campaigning] > carriedAC[rival]);
    }

    [Fact]
    public void Resolve_RouteCampaignBoost_BiggerThanNetworkCampaignBoost()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);

        var routeCampaigner = new Airline("RouteCampaigner", new[] { new Route(a, b, aircraft, 10) });
        var networkCampaigner = new Airline("NetworkCampaigner", new[] { new Route(a, b, aircraft, 10) });
        var marketing = new Dictionary<string, AirlineMarketingCampaign>
        {
            ["RouteCampaigner"] = new(NetworkWide: false, RouteKeys: new HashSet<(string, string)> { QuarterEngine.NormalizedPairKey(a, b) }),
            ["NetworkCampaigner"] = new(NetworkWide: true, RouteKeys: new HashSet<(string, string)>()),
        };

        var result = CompetitiveMarket.Resolve(new[] { routeCampaigner, networkCampaigner }, new[] { a, b }, demandModel, marketing);

        Assert.True(result.TotalCarried(routeCampaigner) > result.TotalCarried(networkCampaigner));
    }

    [Fact]
    public void Resolve_MarketingCampaign_UncontestedPair_HasNoEffect()
    {
        // Matches Quality's own pre-existing behavior: the solo-carrier
        // branch never reads Quality (or now, a campaign's boost) at all
        // - a monopoly ceiling is a monopoly ceiling regardless.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var solo = new Airline("Solo", new[] { new Route(a, b, aircraft, 5) });
        var marketing = new Dictionary<string, AirlineMarketingCampaign>
        {
            ["Solo"] = new(NetworkWide: false, RouteKeys: new HashSet<(string, string)> { QuarterEngine.NormalizedPairKey(a, b) }),
        };

        var withCampaign = CompetitiveMarket.Resolve(new[] { solo }, new[] { a, b }, demandModel, marketing);
        var withoutCampaign = CompetitiveMarket.Resolve(new[] { solo }, new[] { a, b }, demandModel);

        Assert.Equal(withoutCampaign.TotalCarried(solo), withCampaign.TotalCarried(solo));
    }

    [Fact]
    public void Resolve_NullMarketingByAirlineName_PreservesPreExistingBehavior()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var airlineA = new Airline("A", new[] { new Route(a, b, aircraft, 10) });
        var airlineB = new Airline("B", new[] { new Route(a, b, aircraft, 10) });

        var withExplicitNull = CompetitiveMarket.Resolve(new[] { airlineA, airlineB }, new[] { a, b }, demandModel, marketingByAirlineName: null);
        var withDefault = CompetitiveMarket.Resolve(new[] { airlineA, airlineB }, new[] { a, b }, demandModel);

        Assert.Equal(withDefault.TotalCarried(airlineA), withExplicitNull.TotalCarried(airlineA));
    }

    [Fact]
    public void Resolve_RouteCampaign_TargetingADifferentPair_NoBoostHere()
    {
        // The airline has a real, active route campaign - just not on
        // this pair. Distinct from having no marketing entry at all.
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var c = MakeCity("c", 10, 10, 5);
        var demandModel = new GravityDemandModel();
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var airlineA = new Airline("A", new[] { new Route(a, b, aircraft, 10), new Route(a, c, aircraft, 10) });
        var airlineB = new Airline("B", new[] { new Route(a, b, aircraft, 10) });
        var marketing = new Dictionary<string, AirlineMarketingCampaign>
        {
            // Campaign targets a-c, not a-b.
            ["A"] = new(NetworkWide: false, RouteKeys: new HashSet<(string, string)> { QuarterEngine.NormalizedPairKey(a, c) }),
        };

        var result = CompetitiveMarket.Resolve(new[] { airlineA, airlineB }, new[] { a, b, c }, demandModel, marketing);

        var carriedOnAB = result.Pairs.Single(p => p.Origin.Id == "a" && p.Destination.Id == "b").CarriedByAirline;
        Assert.InRange((double)carriedOnAB[airlineA] / carriedOnAB[airlineB], 0.95, 1.05); // no boost on a-b specifically
    }

    [Fact]
    public void Resolve_AirlineWithNoEntryInMarketingDictionary_NoBoost()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var airlineA = new Airline("A", new[] { new Route(a, b, aircraft, 10) });
        var airlineB = new Airline("B", new[] { new Route(a, b, aircraft, 10) });
        var marketing = new Dictionary<string, AirlineMarketingCampaign>(); // neither airline has an entry

        var result = CompetitiveMarket.Resolve(new[] { airlineA, airlineB }, new[] { a, b }, demandModel, marketing);

        Assert.InRange((double)result.TotalCarried(airlineA) / result.TotalCarried(airlineB), 0.95, 1.05);
    }
}
