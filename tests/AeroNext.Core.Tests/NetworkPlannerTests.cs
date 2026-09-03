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
}
