using AeroNext.Core.Airlines;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class RouteEvaluatorTests
{
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, false, null);

    [Fact]
    public void Route_SeatsPerQuarter_MultipliesSeatsByRoundTripsByTwoByThirteenWeeks()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var route = new Route(MakeCity("a", 0, 0, 50), MakeCity("b", 0, 1, 50), aircraft, AircraftCount: 2);

        Assert.Equal(100L * route.WeeklyRoundTrips * 2 * 13, route.SeatsPerQuarter);
    }

    [Fact]
    public void Evaluate_CapacityConstrained_CarriesUpToSeatsOnly()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 0, 1, 100); // close + heavy => huge demand
        var tinyAircraft = new AircraftType("t", "Test", Seats: 10);
        var route = new Route(a, b, tinyAircraft, AircraftCount: 1);

        var load = RouteEvaluator.Evaluate(route, demandModel);

        Assert.True(load.DemandPassengers > load.SeatsPerQuarter);
        Assert.Equal(load.SeatsPerQuarter, load.PassengersCarried);
        Assert.Equal(1.0, load.LoadFactor, precision: 6);
    }

    [Fact]
    public void Evaluate_DemandConstrained_CarriesOnlyDemand_LoadFactorBelowOne()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 1);
        var b = MakeCity("b", 30, 30, 1); // far + light => tiny demand
        var bigAircraft = new AircraftType("t", "Test", Seats: 400);
        var route = new Route(a, b, bigAircraft, AircraftCount: 1);

        var load = RouteEvaluator.Evaluate(route, demandModel);

        Assert.True(load.DemandPassengers < load.SeatsPerQuarter);
        Assert.Equal(load.DemandPassengers, load.PassengersCarried);
        Assert.True(load.LoadFactor < 1.0);
    }

    [Fact]
    public void Evaluate_ZeroCapacityRoute_LoadFactorIsZero_NotNaN()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 0, 1, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var route = new Route(a, b, aircraft, AircraftCount: 0); // suspended route: zero capacity

        var load = RouteEvaluator.Evaluate(route, demandModel);

        Assert.Equal(0, load.SeatsPerQuarter);
        Assert.Equal(0.0, load.LoadFactor);
    }

    [Fact]
    public void EvaluateNetwork_IsDeterministic_SortedByOriginThenDestination()
    {
        var demandModel = new GravityDemandModel();
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var a = MakeCity("a", 0, 0, 10);
        var b = MakeCity("b", 1, 1, 10);
        var c = MakeCity("c", 2, 2, 10);

        var routes = new[]
        {
            new Route(c, a, aircraft, 1),
            new Route(a, b, aircraft, 1),
            new Route(a, c, aircraft, 1),
        };

        var network = RouteEvaluator.EvaluateNetwork(routes, demandModel);

        Assert.Equal(
            new[] { ("a", "b"), ("a", "c"), ("c", "a") },
            network.Select(l => (l.Route.Origin.Id, l.Route.Destination.Id)));
    }
}
