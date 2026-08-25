using AeroNext.Core.Airlines;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class NetworkPlannerTests
{
    private static City MakeCity(string id, double lat, double lon, int weight, Region region = Region.Asia) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, false, null);

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
}
