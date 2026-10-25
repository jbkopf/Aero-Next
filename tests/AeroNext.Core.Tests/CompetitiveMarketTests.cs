using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
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
    public void Resolve_MarketingCampaign_UncontestedPair_LowLoadFactor_GeneratesRealAddedDemand()
    {
        // M47 round 2: an uncontested pair now DOES benefit from
        // marketing - real added demand, not a Quality weight (there's
        // no rival share to win here). Plenty of spare capacity in this
        // setup, so the campaign realizes its full RouteCampaignBoost
        // fraction of the pair's own DemandPassengers.
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

        var demand = withoutCampaign.Pairs.Single().DemandPassengers;
        var expectedAdditional = (long)Math.Round(demand * CompetitiveMarket.RouteCampaignBoost, MidpointRounding.AwayFromZero);
        Assert.Equal(withoutCampaign.TotalCarried(solo) + expectedAdditional, withCampaign.TotalCarried(solo));
    }

    [Fact]
    public void Resolve_MarketingCampaign_UncontestedPair_AtCapacity_NoGain()
    {
        // The user's own stated intent, directly: "routes at or very near
        // capacity will see little gain." Tiny aircraft, heavy demand -
        // the route is already fully sold out before any campaign, so
        // there's zero spare capacity for the boost to add to.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 100); // close + heavy -> demand vastly exceeds any small fleet
        var tinyAircraft = new AircraftType("t", "Test", Seats: 10);
        var solo = new Airline("Solo", new[] { new Route(a, b, tinyAircraft, AircraftCount: 1) });
        var marketing = new Dictionary<string, AirlineMarketingCampaign>
        {
            ["Solo"] = new(NetworkWide: false, RouteKeys: new HashSet<(string, string)> { QuarterEngine.NormalizedPairKey(a, b) }),
        };

        var withCampaign = CompetitiveMarket.Resolve(new[] { solo }, new[] { a, b }, demandModel, marketing);
        var withoutCampaign = CompetitiveMarket.Resolve(new[] { solo }, new[] { a, b }, demandModel);

        Assert.Equal(withoutCampaign.TotalCarried(solo), withCampaign.TotalCarried(solo));
    }

    [Fact]
    public void Resolve_MarketingCampaign_UncontestedPair_NetworkCampaign_SmallerAddedDemandThanRouteCampaign()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routeCampaigner = new Airline("RouteCampaigner", new[] { new Route(a, b, aircraft, 5) });
        var networkCampaigner = new Airline("NetworkCampaigner", new[] { new Route(a, b, aircraft, 5) });
        var marketing = new Dictionary<string, AirlineMarketingCampaign>
        {
            ["RouteCampaigner"] = new(NetworkWide: false, RouteKeys: new HashSet<(string, string)> { QuarterEngine.NormalizedPairKey(a, b) }),
            ["NetworkCampaigner"] = new(NetworkWide: true, RouteKeys: new HashSet<(string, string)>()),
        };

        var routeResult = CompetitiveMarket.Resolve(new[] { routeCampaigner }, new[] { a, b }, demandModel, marketing);
        var networkResult = CompetitiveMarket.Resolve(new[] { networkCampaigner }, new[] { a, b }, demandModel, marketing);

        Assert.True(routeResult.TotalCarried(routeCampaigner) > networkResult.TotalCarried(networkCampaigner));
    }

    [Fact]
    public void Resolve_MarketingCampaign_UncontestedPair_SharedHubLeg_NeverDoubleClaimsSpareCapacity()
    {
        // A network campaign covers two one-stop pairs sharing the same
        // hub leg. Each pair's own boost is capped by that leg's real
        // remaining spare capacity, decremented as the first pair's own
        // boost consumes it - the two combined must never exceed the
        // hub leg's own actual spare capacity.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 20);
        var hub = MakeCity("hub", 0, 5, 20);
        var b = MakeCity("b", 0, 10, 20);
        var c = MakeCity("c", 0, 15, 20);
        var spokeAircraft = new AircraftType("t", "Test", Seats: 300);
        var hubLegAircraft = new AircraftType("h", "HubLeg", Seats: 50); // tight bottleneck leg shared by both one-stop pairs
        var airline = new Airline("A", new[]
        {
            new Route(a, hub, hubLegAircraft, AircraftCount: 1),
            new Route(hub, b, spokeAircraft, AircraftCount: 1),
            new Route(hub, c, spokeAircraft, AircraftCount: 1),
        });
        var marketing = new Dictionary<string, AirlineMarketingCampaign>
        {
            ["A"] = new(NetworkWide: true, RouteKeys: new HashSet<(string, string)>()),
        };

        var withoutCampaign = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, hub, b, c }, demandModel);
        var withCampaign = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, hub, b, c }, demandModel, marketing);

        var hubLegRoute = airline.Routes.Single(r => r.Origin.Id == "a" && r.Destination.Id == "hub");
        var hubLegUsageWithout = withoutCampaign.RouteUsage.Single(u => u.Route == hubLegRoute).SeatsUsed;
        var hubLegUsageWith = withCampaign.RouteUsage.Single(u => u.Route == hubLegRoute).SeatsUsed;
        var hubLegSpareCapacity = hubLegRoute.SeatsPerQuarter - hubLegUsageWithout;

        Assert.True(hubLegUsageWith - hubLegUsageWithout <= hubLegSpareCapacity);
        Assert.True(hubLegUsageWith <= hubLegRoute.SeatsPerQuarter); // never oversold
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

    // --- M48: fare's own demand effect ---

    [Fact]
    public void Resolve_UncontestedPair_NoFareOverride_NoEffect()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var solo = new Airline("Solo", new[] { new Route(a, b, aircraft, 5) }); // no FareOverride

        var result = CompetitiveMarket.Resolve(new[] { solo }, new[] { a, b }, demandModel);
        var monopoly = NetworkPlanner.Plan(solo.Routes, new[] { a, b }, demandModel).TotalCarried;

        Assert.Equal(monopoly, result.TotalCarried(solo));
    }

    [Fact]
    public void Resolve_UncontestedPair_LowLoadFactor_Discount_IncreasesCarried()
    {
        // Compared against NetworkPlanner's own unboosted monopoly figure
        // directly (the same technique Resolve_SoleCarrier_... above
        // uses) rather than a second Resolve call with a differently-
        // identified Airline object - Airline is a record, so two
        // separately-constructed instances (even with the same name)
        // are different dictionary keys and TotalCarried would silently
        // read back zero for one never actually passed into that call.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var defaultRoute = new Route(a, b, aircraft, 5);
        var discountFare = new Money(defaultRoute.FarePerPassenger.Cents * 9 / 10); // -10%
        var discounted = new Airline("Solo", new[] { new Route(a, b, aircraft, 5, discountFare) });

        var result = CompetitiveMarket.Resolve(new[] { discounted }, new[] { a, b }, demandModel);
        var unboostedMonopoly = NetworkPlanner.Plan(new[] { defaultRoute }, new[] { a, b }, demandModel).TotalCarried;

        Assert.True(result.TotalCarried(discounted) > unboostedMonopoly);
    }

    [Fact]
    public void Resolve_UncontestedPair_AtCapacity_Premium_NeverForcesADecrease()
    {
        // The user's own direct instruction: raising fares must not force
        // carried passengers down while the (now-lower) demand is still
        // above capacity.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 100); // close + heavy -> demand vastly exceeds a tiny fleet
        var tinyAircraft = new AircraftType("t", "Test", Seats: 10);
        var defaultRoute = new Route(a, b, tinyAircraft, AircraftCount: 1);
        var premiumFare = new Money(defaultRoute.FarePerPassenger.Cents * 12 / 10); // +20%
        var expensive = new Airline("Solo", new[] { new Route(a, b, tinyAircraft, AircraftCount: 1, premiumFare) });

        var result = CompetitiveMarket.Resolve(new[] { expensive }, new[] { a, b }, demandModel);
        var unboostedMonopoly = NetworkPlanner.Plan(new[] { defaultRoute }, new[] { a, b }, demandModel).TotalCarried;

        Assert.Equal(unboostedMonopoly, result.TotalCarried(expensive));
    }

    [Fact]
    public void Resolve_UncontestedPair_NotAtCapacity_Premium_RealDecrease()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var bigAircraft = new AircraftType("t", "Test", Seats: 1000); // far from saturating - genuinely demand-bound
        var defaultRoute = new Route(a, b, bigAircraft, 5);
        var premiumFare = new Money(defaultRoute.FarePerPassenger.Cents * 12 / 10); // +20%
        var expensive = new Airline("Solo", new[] { new Route(a, b, bigAircraft, 5, premiumFare) });

        var result = CompetitiveMarket.Resolve(new[] { expensive }, new[] { a, b }, demandModel);
        var unboostedMonopoly = NetworkPlanner.Plan(new[] { defaultRoute }, new[] { a, b }, demandModel).TotalCarried;

        Assert.True(result.TotalCarried(expensive) < unboostedMonopoly);
    }

    [Fact]
    public void Resolve_UncontestedPair_ExtremePremium_NeverGoesNegative()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 100);
        var tinyAircraft = new AircraftType("t", "Test", Seats: 10);
        var defaultRoute = new Route(a, b, tinyAircraft, AircraftCount: 1);
        var extremeFare = defaultRoute.FarePerPassenger * 10; // +900%, an unrealistically large premium
        var expensive = new Airline("Solo", new[] { new Route(a, b, tinyAircraft, AircraftCount: 1, extremeFare) });

        var result = CompetitiveMarket.Resolve(new[] { expensive }, new[] { a, b }, demandModel);

        Assert.True(result.TotalCarried(expensive) >= 0);
    }

    [Fact]
    public void Resolve_ContestedPair_Discount_CapturesLargerShare()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var defaultRoute = new Route(a, b, aircraft, 10);
        var discountFare = new Money(defaultRoute.FarePerPassenger.Cents * 9 / 10);
        var discounted = new Airline("Discounted", new[] { new Route(a, b, aircraft, 10, discountFare) });
        var rival = new Airline("Rival", new[] { defaultRoute });

        var result = CompetitiveMarket.Resolve(new[] { discounted, rival }, new[] { a, b }, demandModel);

        Assert.True(result.TotalCarried(discounted) > result.TotalCarried(rival));
    }

    [Fact]
    public void Resolve_ContestedPair_Premium_CapturesSmallerShare()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var defaultRoute = new Route(a, b, aircraft, 10);
        var premiumFare = new Money(defaultRoute.FarePerPassenger.Cents * 11 / 10);
        var expensive = new Airline("Expensive", new[] { new Route(a, b, aircraft, 10, premiumFare) });
        var rival = new Airline("Rival", new[] { defaultRoute });

        var result = CompetitiveMarket.Resolve(new[] { expensive, rival }, new[] { a, b }, demandModel);

        Assert.True(result.TotalCarried(expensive) < result.TotalCarried(rival));
    }

    [Fact]
    public void Resolve_UncontestedPair_MarketingAndFareDiscount_Combine()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 5, 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var defaultRoute = new Route(a, b, aircraft, 5);
        var discountFare = new Money(defaultRoute.FarePerPassenger.Cents * 9 / 10);
        var solo = new Airline("Solo", new[] { new Route(a, b, aircraft, 5, discountFare) });
        var marketing = new Dictionary<string, AirlineMarketingCampaign>
        {
            ["Solo"] = new(NetworkWide: false, RouteKeys: new HashSet<(string, string)> { QuarterEngine.NormalizedPairKey(a, b) }),
        };

        var combined = CompetitiveMarket.Resolve(new[] { solo }, new[] { a, b }, demandModel, marketing);
        var fareOnly = CompetitiveMarket.Resolve(new[] { solo }, new[] { a, b }, demandModel);

        Assert.True(combined.TotalCarried(solo) > fareOnly.TotalCarried(solo));
    }

    // --- Seat return (docs/CONNECTING_PASSENGERS_DESIGN.md) ---

    /// <summary>
    /// Hub carrier H flies a-hub (too small for everyone) and hub-b, so its
    /// own plan shares a-hub between local a-hub passengers and the a-b
    /// connection. Rival R flies a-b nonstop with far more seats, so H's
    /// connection loses most of a-b.
    /// </summary>
    private static (Airline Hub, Airline Rival, Route AHub, City A, City HubCity, CompetitiveMarketResult Result) ConnectionLosesToANonstop()
    {
        var demandModel = new GravityDemandModel();
        var a = new City("a", "A", Region.EastAsia, 0, 0, 100, false, null);
        var hub = new City("hub", "HUB", Region.EastAsia, 0, 8, 100, false, null);
        var b = new City("b", "B", Region.EastAsia, 0, 16, 100, false, null);
        var aHub = new Route(a, hub, new AircraftType("small", "Small", Seats: 20), 1);
        var hubB = new Route(hub, b, new AircraftType("big", "Big", Seats: 2000), 1);
        var hubCarrier = new Airline("H", new[] { aHub, hubB });
        var rival = new Airline("R", new[] { new Route(a, b, new AircraftType("huge", "Huge", Seats: 5000), 4) });

        return (hubCarrier, rival, aHub, a, hub, CompetitiveMarket.Resolve(new[] { hubCarrier, rival }, new[] { a, hub, b }, demandModel));
    }

    [Fact]
    public void Resolve_SeatsHeldForAConnectionThatLostItsPair_GoBackToLocalPassengers()
    {
        var (hubCarrier, _, aHub, a, hub, result) = ConnectionLosesToANonstop();
        var plan = NetworkPlanner.Plan(hubCarrier.Routes, new[] { a, hub, result.Pairs.Single(p => p.Destination.Id == "b" && p.Origin.Id == "a").Destination }, new GravityDemandModel());
        var plannedLocal = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "hub").PassengersCarried;
        var local = result.Pairs.Single(p => p.Origin.Id == "a" && p.Destination.Id == "hub").CarriedByAirline[hubCarrier];
        var usage = result.RouteUsage.Single(u => u.Airline == hubCarrier && u.Route == aHub);

        Assert.True(plannedLocal < aHub.SeatsPerQuarter); // the plan held seats for the connection
        Assert.True(local > plannedLocal);                 // the market gave them back to local passengers
        Assert.Equal(aHub.SeatsPerQuarter, usage.SeatsUsed); // so the leg flies full
    }

    [Fact]
    public void Resolve_SeatReturn_NeverExceedsDemand_AndLeavesRivalsAlone()
    {
        var (hubCarrier, rival, _, _, _, result) = ConnectionLosesToANonstop();

        Assert.All(result.Pairs, p => Assert.True(p.CarriedByAirline.Values.Sum() <= p.DemandPassengers));
        Assert.All(result.RouteUsage, u => Assert.True(u.SeatsUsed <= u.SeatsPerQuarter));

        // The rival's own carriage matches a market without seat return's
        // reach: it serves only a-b, where it already took its contested
        // share - nothing it carried came from another airline's seats.
        var rivalAB = result.Pairs.Single(p => p.Origin.Id == "a" && p.Destination.Id == "b").CarriedByAirline[rival];
        Assert.Equal(rivalAB, result.RouteUsage.Single(u => u.Airline == rival).SeatsUsed);
    }

    [Fact]
    public void Resolve_SeatReturn_ALeftoverTooSmallToSplit_StaysUnassigned()
    {
        // Three identical airlines on a pair with demand 4: the contest
        // gives each 1 (4/3 rounded), leaving 1 unserved, and a third of
        // a passenger each rounds to nobody - no airline gains a fractional
        // or phantom passenger.
        var a = MakeCity("a", 0, 0, 1);
        var b = MakeCity("b", 0, 10, 1);
        var demandModel = new GravityDemandModel(scale: 4 * Math.Pow(GreatCircle.DistanceKm(a, b), 2));
        var aircraft = new AircraftType("t", "Test", Seats: 1000);
        var airlines = new[] { "X", "Y", "Z" }.Select(n => new Airline(n, new[] { new Route(a, b, aircraft, 1) })).ToArray();

        var result = CompetitiveMarket.Resolve(airlines, new[] { a, b }, demandModel);

        var pair = result.Pairs.Single();
        Assert.Equal(4, pair.DemandPassengers);
        Assert.All(airlines, airline => Assert.Equal(1, pair.CarriedByAirline[airline]));
    }
}
