using AeroNext.Core.Airlines;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class NetworkPlannerTests
{
    private static City MakeCity(string id, double lat, double lon, int weight, Region region = Region.EastAsia, int hubQuality = 0) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, false, null, AutoCapacity: null, HubQuality: hubQuality);

    [Fact]
    public void Plan_FindsOneStopItinerary_WhenNoNonstopExists()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var hub = MakeCity("hub", 0, 10, 50);
        var b = MakeCity("b", 0, 20, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[]
        {
            new Route(a, hub, aircraft, 5),
            new Route(hub, b, aircraft, 5),
        };

        var plan = NetworkPlanner.Plan(routes, new[] { a, hub, b }, demandModel);

        var aToB = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "b");
        Assert.Equal(ItineraryKind.OneStop, aToB.Itinerary.Kind);
        Assert.Equal(2, aToB.Itinerary.Legs.Count);
        Assert.True(aToB.PassengersCarried > 0);
    }

    [Fact]
    public void Plan_PrefersDirectRoute_OverOneStop()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var hub = MakeCity("hub", 0, 10, 50);
        var b = MakeCity("b", 0, 20, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[]
        {
            new Route(a, hub, aircraft, 5),
            new Route(hub, b, aircraft, 5),
            new Route(a, b, aircraft, 5),
        };

        var plan = NetworkPlanner.Plan(routes, new[] { a, hub, b }, demandModel);

        var aToB = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "b");
        Assert.Equal(ItineraryKind.Nonstop, aToB.Itinerary.Kind);
        Assert.Single(aToB.Itinerary.Legs);
    }

    [Fact]
    public void Plan_MarksUnreachablePairs_Unserved()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 0, 10, 50);
        var isolated = MakeCity("isolated", 40, 40, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[] { new Route(a, b, aircraft, 5) };

        var plan = NetworkPlanner.Plan(routes, new[] { a, b, isolated }, demandModel);

        // "isolated" has no route at all, so it shouldn't appear in any itinerary.
        Assert.DoesNotContain(plan.Itineraries, i => i.Itinerary.Origin.Id == "isolated" || i.Itinerary.Destination.Id == "isolated");
    }

    [Fact]
    public void Plan_OneStopItinerary_IsBottleneckedByEitherLeg()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 100);
        var hub = MakeCity("hub", 0, 10, 100);
        var b = MakeCity("b", 0, 20, 100);
        var bigAircraft = new AircraftType("big", "Big", Seats: 1000);
        var tinyAircraft = new AircraftType("tiny", "Tiny", Seats: 5);
        var routes = new[]
        {
            new Route(a, hub, bigAircraft, 10),  // huge capacity
            new Route(hub, b, tinyAircraft, 1),  // tiny capacity: the bottleneck
        };

        var plan = NetworkPlanner.Plan(routes, new[] { a, hub, b }, demandModel);

        var aToB = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "b");
        var hubToB = routes[1];
        Assert.True(aToB.PassengersCarried <= hubToB.SeatsPerQuarter);
    }

    [Fact]
    public void Plan_NonstopDemand_ClaimsItsOwnLegCapacityBeforeConnections()
    {
        var demandModel = new GravityDemandModel();
        // a-hub is both a local market (a-hub) and a leg for an a-b connection.
        // Its capacity is deliberately tiny relative to the close, heavy a-hub
        // pair's demand, so local nonstop demand alone should fully saturate
        // it and leave nothing for the connecting a-b itinerary.
        var a = MakeCity("a", 0, 0, 100);
        var hub = MakeCity("hub", 0, 5, 100);
        var b = MakeCity("b", 0, 60, 100);
        var tinyAircraft = new AircraftType("tiny", "Tiny", Seats: 1);
        var bigAircraft = new AircraftType("big", "Big", Seats: 400);
        var routes = new[]
        {
            new Route(a, hub, tinyAircraft, 1),
            new Route(hub, b, bigAircraft, 10),
        };

        var plan = NetworkPlanner.Plan(routes, new[] { a, hub, b }, demandModel);

        var aToHub = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "hub");
        var aToB = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "b");
        var legUsage = plan.RouteUsage.Single(u => u.Route == routes[0]);

        Assert.Equal(routes[0].SeatsPerQuarter, aToHub.PassengersCarried); // nonstop fully saturates the shared leg
        Assert.Equal(0, aToB.PassengersCarried); // nothing left for the connection
        Assert.Equal(aToHub.PassengersCarried, legUsage.SeatsUsed);
    }

    [Fact]
    public void Plan_TwoServedButDisconnectedClusters_MarksCrossClusterPairsUnserved()
    {
        var demandModel = new GravityDemandModel();
        // Each city is served (has a route), but no route links the two
        // clusters, and no hub bridges them within one connection either -
        // distinct from the "isolated, zero routes" case already covered
        // above, and exercises Plan()'s null-hub branch plus Allocate()'s
        // unserved-itinerary handling.
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 0, 10, 50);
        var c = MakeCity("c", 40, 40, 50);
        var d = MakeCity("d", 40, 50, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[]
        {
            new Route(a, b, aircraft, 5),
            new Route(c, d, aircraft, 5),
        };

        var plan = NetworkPlanner.Plan(routes, new[] { a, b, c, d }, demandModel);

        var aToC = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "c");
        Assert.Equal(ItineraryKind.Unserved, aToC.Itinerary.Kind);
        Assert.Empty(aToC.Itinerary.Legs);
        Assert.Equal(0, aToC.PassengersCarried);
        Assert.True(aToC.DemandPassengers > 0); // real demand exists; the network just can't reach it
    }

    [Fact]
    public void Plan_SkipsHubCandidateThatDoesNotConnectOnward()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var deadEnd = MakeCity("deadend", 0, 5, 50); // connects to a, goes nowhere else
        var hub = MakeCity("hub", 0, 10, 50);
        var b = MakeCity("b", 0, 20, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[]
        {
            new Route(a, deadEnd, aircraft, 5),
            new Route(a, hub, aircraft, 5),
            new Route(hub, b, aircraft, 5),
        };

        var plan = NetworkPlanner.Plan(routes, new[] { a, deadEnd, hub, b }, demandModel);

        var aToB = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "b");
        Assert.Equal(ItineraryKind.OneStop, aToB.Itinerary.Kind);
        Assert.Equal("hub", aToB.Itinerary.Legs[0].Destination.Id);
    }

    [Fact]
    public void Plan_EquidistantHubCandidates_ResolveDeterministicallyByHubId()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 0, 20, 50);
        // Mirrored around the midpoint meridian (lon=10) between a and b, so
        // reflecting the whole configuration swaps a<->b and hubLow<->hubHigh,
        // making both total detours exactly equal.
        var hubLow = MakeCity("aaa_hub", 5, 5, 50);
        var hubHigh = MakeCity("zzz_hub", 5, 15, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[]
        {
            new Route(a, hubLow, aircraft, 5),
            new Route(hubLow, b, aircraft, 5),
            new Route(a, hubHigh, aircraft, 5),
            new Route(hubHigh, b, aircraft, 5),
        };

        var plan1 = NetworkPlanner.Plan(routes, new[] { a, b, hubLow, hubHigh }, demandModel);
        var plan2 = NetworkPlanner.Plan(routes.Reverse().ToArray(), new[] { hubHigh, hubLow, b, a }, demandModel);

        foreach (var plan in new[] { plan1, plan2 })
        {
            var aToB = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "b");
            Assert.Equal("aaa_hub", aToB.Itinerary.Legs[0].Destination.Id);
        }
    }

    [Fact]
    public void NetworkPlan_TotalDemand_SumsAcrossAllItineraries()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 0, 10, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var plan = NetworkPlanner.Plan(new[] { new Route(a, b, aircraft, 5) }, new[] { a, b }, demandModel);

        var expected = plan.Itineraries.Sum(i => i.DemandPassengers);
        Assert.Equal(expected, plan.TotalDemand);
        Assert.True(plan.TotalDemand > 0);
    }

    [Fact]
    public void NetworkPlan_NonstopAndOneStopCarried_SplitCorrectly_AndSumToTotal()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var hub = MakeCity("hub", 0, 10, 50);
        var b = MakeCity("b", 0, 20, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[] { new Route(a, hub, aircraft, 5), new Route(hub, b, aircraft, 5) };

        var plan = NetworkPlanner.Plan(routes, new[] { a, hub, b }, demandModel);

        Assert.True(plan.NonstopCarried > 0);
        Assert.True(plan.OneStopCarried > 0);
        Assert.Equal(plan.TotalCarried, plan.NonstopCarried + plan.OneStopCarried);
    }

    [Fact]
    public void RouteCapacityUsage_ZeroCapacityRoute_LoadFactorIsZero_NotNaN()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 0, 10, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var route = new Route(a, b, aircraft, AircraftCount: 0); // suspended: zero capacity

        var plan = NetworkPlanner.Plan(new[] { route }, new[] { a, b }, demandModel);

        var usage = plan.RouteUsage.Single();
        Assert.Equal(0, usage.SeatsPerQuarter);
        Assert.Equal(0.0, usage.LoadFactor);
    }

    // --- M62: quality + loyalty + substitution ---

    [Fact]
    public void Plan_HigherHubQuality_PreferredWhenDetourNearlyEqual()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 0, 20, 50);
        // Mirrored around lon=10, so base detourKm is exactly equal (the
        // same fixture Plan_EquidistantHubCandidates_ResolveDeterministicallyByHubId
        // proves ties exactly) - "aaa_hub" would win the plain ordinal
        // tie-break, so a "zzz_hub" win here can only be HubQuality.
        var hubLow = MakeCity("aaa_hub", 5, 5, 50, hubQuality: 0);
        var hubHigh = MakeCity("zzz_hub", 5, 15, 50, hubQuality: 40);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[]
        {
            new Route(a, hubLow, aircraft, 5),
            new Route(hubLow, b, aircraft, 5),
            new Route(a, hubHigh, aircraft, 5),
            new Route(hubHigh, b, aircraft, 5),
        };

        var plan = NetworkPlanner.Plan(routes, new[] { a, b, hubLow, hubHigh }, demandModel);

        var aToB = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "b");
        Assert.Equal("zzz_hub", aToB.Itinerary.Legs[0].Destination.Id);
    }

    [Fact]
    public void Plan_HubQuality_DoesNotOverrideLargeDetourAdvantage()
    {
        var demandModel = new GravityDemandModel();
        // hubNear sits almost exactly on the direct path (tiny detour);
        // hubFar sits nearly on the opposite side of the globe (detour
        // ~37,000km more) - even at the maximum possible combined
        // quality+loyalty bonus (~5,000km, post-M62-retune), nowhere close
        // to overriding a detour delta an order of magnitude larger.
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 0, 5, 50);
        var hubNear = MakeCity("hubnear", 0, 2.5, 50, hubQuality: 0);
        var hubFar = MakeCity("hubfar", 0, 170, 50, hubQuality: 40); // max quality
        var filler = MakeCity("filler", 45, 45, 10);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[]
        {
            new Route(a, hubNear, aircraft, 5),
            new Route(hubNear, b, aircraft, 5),
            new Route(a, hubFar, aircraft, 5),
            new Route(hubFar, b, aircraft, 5),
            // Filler route gives hubFar outsized loyalty too (3/5 vs hubNear's 2/5) - still loses.
            new Route(hubFar, filler, aircraft, 5),
        };

        var plan = NetworkPlanner.Plan(routes, new[] { a, b, hubNear, hubFar, filler }, demandModel);

        var aToB = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "b");
        Assert.Equal("hubnear", aToB.Itinerary.Legs[0].Destination.Id);
    }

    [Fact]
    public void Plan_LoyaltyBonus_PrefersHubAirlineAlreadyUses()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 0, 20, 50);
        // Same exactly-equal-detour fixture as the tie-break test above,
        // but this time both hubs have zero HubQuality - only an extra
        // filler route (far away, so it can't trigger substitution) gives
        // "zzz_hub" a higher loyalty share (3/5 vs "aaa_hub"'s 2/5),
        // which should flip the plain ordinal tie-break on its own.
        var hubLow = MakeCity("aaa_hub", 5, 5, 50);
        var hubHigh = MakeCity("zzz_hub", 5, 15, 50);
        var filler = MakeCity("filler", 80, 80, 10);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[]
        {
            new Route(a, hubLow, aircraft, 5),
            new Route(hubLow, b, aircraft, 5),
            new Route(a, hubHigh, aircraft, 5),
            new Route(hubHigh, b, aircraft, 5),
            new Route(hubHigh, filler, aircraft, 5),
        };

        var plan = NetworkPlanner.Plan(routes, new[] { a, b, hubLow, hubHigh, filler }, demandModel);

        var aToB = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "b");
        Assert.Equal("zzz_hub", aToB.Itinerary.Legs[0].Destination.Id);
    }

    [Fact]
    public void Plan_SubstitutionPenalty_PenalizesHubNearAnAlreadyLoyalHub()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 0, 40, 50);
        // hubEstablished is already loyal (lots of filler routes, none
        // touching a/b) and sits right next to hubDuplicate (~56km apart,
        // well inside the substitution radius). hubDuplicate has the
        // genuinely BETTER raw detour (it's closer to the direct a-b
        // path than hubAlternate) - without substitution it would win;
        // with it, the penalty for duplicating hubEstablished's coverage
        // should hand the connection to hubAlternate instead.
        var hubEstablished = MakeCity("hubestablished", 0, 20, 50);
        var hubDuplicate = MakeCity("hubduplicate", 0, 20.5, 50);
        var hubAlternate = MakeCity("hubalternate", 10, 20, 50);
        var filler1 = MakeCity("filler1", 60, 60, 10);
        var filler2 = MakeCity("filler2", 60, 70, 10);
        var filler3 = MakeCity("filler3", 60, 80, 10);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[]
        {
            new Route(a, hubDuplicate, aircraft, 5),
            new Route(hubDuplicate, b, aircraft, 5),
            new Route(a, hubAlternate, aircraft, 5),
            new Route(hubAlternate, b, aircraft, 5),
            new Route(hubEstablished, filler1, aircraft, 5),
            new Route(hubEstablished, filler2, aircraft, 5),
            new Route(hubEstablished, filler3, aircraft, 5),
        };
        var cities = new[] { a, b, hubEstablished, hubDuplicate, hubAlternate, filler1, filler2, filler3 };

        var plan = NetworkPlanner.Plan(routes, cities, demandModel);

        var aToB = plan.Itineraries.Single(i => i.Itinerary.Origin.Id == "a" && i.Itinerary.Destination.Id == "b");
        Assert.Equal("hubalternate", aToB.Itinerary.Legs[0].Destination.Id);
    }

    [Fact]
    public void RealDataset_CombinedHubBonusStaysBelowASingleIntercontinentalLeg()
    {
        // The whole point of bounding these three terms (see
        // NetworkPlanner.FindBestHub's own doc comment): quality/loyalty/
        // substitution can tilt a genuinely close choice, never make a
        // transcontinental detour look cheaper than a nearby direct hub.
        // Concretely: even the maximum possible combined quality+loyalty
        // bonus (post-M62-retune) stays under a single real transatlantic
        // leg - Plan_HubQuality_DoesNotOverrideLargeDetourAdvantage above
        // is the dedicated, much wider-margin (~37,000km) proof this can
        // never override a genuinely distant hub; this test is a tighter,
        // real-data sanity bound on top of that.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var jfk = cities.Single(c => c.Id == "jfk");
        var lhr = cities.Single(c => c.Id == "lhr");
        var transatlanticKm = GreatCircle.DistanceKm(jfk, lhr);

        var maxCombinedBonusKm = NetworkPlanner.HubQualityKmPerPoint * CityCatalog.MaxAuthoredHubQuality + NetworkPlanner.LoyaltyBonusMaxKm;

        Assert.True(maxCombinedBonusKm < transatlanticKm,
            $"Combined bonus ({maxCombinedBonusKm}) should stay under a real transatlantic distance ({transatlanticKm}).");
    }

    [Fact]
    public void Plan_IsDeterministic_RegardlessOfRouteOrCityInputOrder()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var hub = MakeCity("hub", 0, 10, 50);
        var b = MakeCity("b", 0, 20, 50);
        var c = MakeCity("c", 5, 5, 30);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[]
        {
            new Route(a, hub, aircraft, 5),
            new Route(hub, b, aircraft, 5),
            new Route(hub, c, aircraft, 3),
        };
        var cities = new[] { a, hub, b, c };

        var original = NetworkPlanner.Plan(routes, cities, demandModel);
        var shuffled = NetworkPlanner.Plan(
            new[] { routes[2], routes[0], routes[1] },
            new[] { c, b, hub, a },
            demandModel);

        Assert.Equal(
            original.Itineraries.Select(i => (i.Itinerary.Origin.Id, i.Itinerary.Destination.Id, i.Itinerary.Kind, i.PassengersCarried)),
            shuffled.Itineraries.Select(i => (i.Itinerary.Origin.Id, i.Itinerary.Destination.Id, i.Itinerary.Kind, i.PassengersCarried)));
        Assert.Equal(
            original.RouteUsage.Select(u => (u.Route.Origin.Id, u.Route.Destination.Id, u.SeatsUsed)),
            shuffled.RouteUsage.Select(u => (u.Route.Origin.Id, u.Route.Destination.Id, u.SeatsUsed)));
    }

    // --- M64: ComputeRouteShareByCityId, extracted from Plan()'s own M62 precompute as a shared, reusable helper ---

    [Fact]
    public void ComputeRouteShareByCityId_EmptyRoutes_ReturnsEmpty()
    {
        Assert.Empty(NetworkPlanner.ComputeRouteShareByCityId(Array.Empty<Route>()));
    }

    [Fact]
    public void ComputeRouteShareByCityId_MatchesRouteCountShareOfTotal()
    {
        var a = MakeCity("a", 0, 0, 50);
        var hub = MakeCity("hub", 0, 10, 50);
        var b = MakeCity("b", 0, 20, 50);
        var c = MakeCity("c", 5, 5, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var routes = new[]
        {
            new Route(a, hub, aircraft, 5),
            new Route(hub, b, aircraft, 5),
            new Route(hub, c, aircraft, 5),
        };

        var shareByCityId = NetworkPlanner.ComputeRouteShareByCityId(routes);

        Assert.Equal(3.0 / 3.0, shareByCityId["hub"]); // touches all 3 routes
        Assert.Equal(1.0 / 3.0, shareByCityId["a"]);
        Assert.Equal(1.0 / 3.0, shareByCityId["b"]);
        Assert.Equal(1.0 / 3.0, shareByCityId["c"]);
    }
}
