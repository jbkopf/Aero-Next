using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class QuarterEngineTests
{
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.Asia, lat, lon, weight, false, null);

    [Fact]
    public void RunQuarter_WithNoOrdersAndNoRoutes_ProducesEmptyNetwork()
    {
        var demandModel = new GravityDemandModel();
        var cities = new[] { MakeCity("a", 0, 0, 50), MakeCity("b", 1, 1, 50) };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), Array.Empty<Order>(), cities, demandModel);

        Assert.Empty(result.Routes);
        Assert.Equal(0, result.Plan.TotalCarried);
    }

    [Fact]
    public void RunQuarter_AppliesNewOrder_AddsRoute()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var order = new Order(a, b, aircraft, WeeklyRoundTrips: 3);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.Routes);
        Assert.Equal(("a", "b"), (result.Routes[0].Origin.Id, result.Routes[0].Destination.Id));
    }

    [Fact]
    public void RunQuarter_OrderForExistingPair_ReplacesRatherThanDuplicates()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var existing = new[] { new Route(a, b, aircraft, WeeklyRoundTrips: 1) };
        var order = new Order(a, b, aircraft, WeeklyRoundTrips: 7);

        var result = QuarterEngine.RunQuarter(2, existing, new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.Routes);
        Assert.Equal(7, result.Routes[0].WeeklyRoundTrips);
    }

    [Fact]
    public void RunQuarter_MultipleOrdersInOneBatch_AllApply()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var orders = new[]
        {
            new Order(a, b, aircraft, WeeklyRoundTrips: 3),
            new Order(a, c, aircraft, WeeklyRoundTrips: 2),
        };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), orders, new[] { a, b, c }, demandModel);

        Assert.Equal(2, result.Routes.Count);
    }

    [Fact]
    public void RunQuarter_UnchangedInputs_ProducesIdenticalResult()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var routes = new[] { new Route(a, b, aircraft, WeeklyRoundTrips: 3) };

        var first = QuarterEngine.RunQuarter(1, routes, Array.Empty<Order>(), new[] { a, b }, demandModel);
        var second = QuarterEngine.RunQuarter(1, routes, Array.Empty<Order>(), new[] { a, b }, demandModel);

        Assert.Equal(first.Plan.TotalCarried, second.Plan.TotalCarried);
    }
}
