using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class BaselineAiTests
{
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.Asia, lat, lon, weight, false, null);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly AircraftType Aircraft = new("t", "Test", Seats: 140);

    [Fact]
    public void DecideOrders_EmptyNetwork_EntersSingleHighestDemandPair()
    {
        var cities = new[]
        {
            MakeCity("a", 0, 0, 100),
            MakeCity("b", 1, 1, 90), // closest + heaviest pair with a -> highest demand
            MakeCity("c", 30, 30, 20),
        };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, Aircraft);

        var order = Assert.Single(orders);
        var routeOrder = Assert.IsType<RouteOrder>(order);
        Assert.Equal(("a", "b"), (routeOrder.Origin.Id, routeOrder.Destination.Id));
    }

    [Fact]
    public void DecideOrders_WithExistingRoute_AlsoBumpsItsFrequency()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var c = MakeCity("c", 30, 30, 20);
        var cities = new[] { a, b, c };
        var existing = new[] { new Route(a, c, Aircraft, WeeklyRoundTrips: 3) };

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, cities, DemandModel, Aircraft);

        Assert.Equal(2, orders.Count);
        var routeOrders = orders.Cast<RouteOrder>().ToList();
        Assert.Contains(routeOrders, o => o.Origin.Id == "a" && o.Destination.Id == "c" && o.WeeklyRoundTrips == 5); // 3 + increment(2)
        Assert.Contains(routeOrders, o => o.Origin.Id == "a" && o.Destination.Id == "b"); // new pair
    }

    [Fact]
    public void DecideOrders_NeverProposesAPairItAlreadyServes()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90); // objectively the best pair in this set
        var c = MakeCity("c", 30, 30, 20);
        var cities = new[] { a, b, c };
        var existing = new[] { new Route(a, b, Aircraft, WeeklyRoundTrips: 3) }; // already serves the best pair

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, cities, DemandModel, Aircraft);

        var newRouteOrder = orders.Cast<RouteOrder>().Single(o => o.WeeklyRoundTrips != 5);
        Assert.DoesNotContain((newRouteOrder.Origin.Id, newRouteOrder.Destination.Id), new[] { ("a", "b") });
    }

    [Fact]
    public void DecideOrders_ZeroActionCap_SkipsNewRoute_StillBumpsFrequency()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var cities = new[] { a, b };
        var existing = new[] { new Route(a, b, Aircraft, WeeklyRoundTrips: 3) };

        var orders = BaselineAi.DecideOrders(existing, actionCap: 0, cities, DemandModel, Aircraft);

        var order = Assert.Single(orders);
        var routeOrder = Assert.IsType<RouteOrder>(order);
        Assert.Equal(5, routeOrder.WeeklyRoundTrips); // frequency bump only, no new route
    }

    [Fact]
    public void DecideOrders_EmptyNetworkAndNoCities_ProducesNoOrders()
    {
        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, Array.Empty<City>(), DemandModel, Aircraft);

        Assert.Empty(orders);
    }

    [Fact]
    public void DecideOrders_BumpsTheMostCapacityConstrainedRoute_NotJustAnyRoute()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 0, 1, 100);  // close + heavy: demand swamps a tiny aircraft -> capacity-constrained
        var c = MakeCity("c", 60, 60, 1);  // far + light: demand is tiny -> nowhere near capacity-constrained
        var cities = new[] { a, b, c };
        var tinyAircraft = new AircraftType("tiny", "Tiny", Seats: 5);
        var existing = new[]
        {
            new Route(a, b, tinyAircraft, WeeklyRoundTrips: 1), // high load factor
            new Route(a, c, tinyAircraft, WeeklyRoundTrips: 1), // low load factor
        };

        var orders = BaselineAi.DecideOrders(existing, actionCap: 0, cities, DemandModel, tinyAircraft);

        var bump = Assert.IsType<RouteOrder>(Assert.Single(orders));
        Assert.Equal(("a", "b"), (bump.Origin.Id, bump.Destination.Id));
    }

    [Fact]
    public void DecideOrders_IsDeterministic_RegardlessOfCityInputOrder()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var c = MakeCity("c", 30, 30, 20);
        var existing = new[] { new Route(a, c, Aircraft, WeeklyRoundTrips: 3) };

        var original = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b, c }, DemandModel, Aircraft);
        var shuffled = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { c, b, a }, DemandModel, Aircraft);

        Assert.Equal(
            original.Cast<RouteOrder>().Select(o => (o.Origin.Id, o.Destination.Id, o.WeeklyRoundTrips)),
            shuffled.Cast<RouteOrder>().Select(o => (o.Origin.Id, o.Destination.Id, o.WeeklyRoundTrips)));
    }

    [Fact]
    public void DecideOrders_ProducesLegalOrders_WhenRunThroughQuarterEngine()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var c = MakeCity("c", 30, 30, 20);
        var cities = new[] { a, b, c };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, Aircraft);
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, DemandModel);

        Assert.Empty(result.RejectedOrders); // the AI never proposes more than it can afford
    }
}
