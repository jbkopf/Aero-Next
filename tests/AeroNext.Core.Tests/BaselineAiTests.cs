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

    private static City MakeGateway(string id, double lat, double lon, int weight, int gateCapacity = 3) =>
        new(id, id.ToUpperInvariant(), Region.Asia, lat, lon, weight, true, gateCapacity);

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

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft });

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

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft });

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

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft });

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

        var orders = BaselineAi.DecideOrders(existing, actionCap: 0, cities, DemandModel, new[] { Aircraft });

        var order = Assert.Single(orders);
        var routeOrder = Assert.IsType<RouteOrder>(order);
        Assert.Equal(5, routeOrder.WeeklyRoundTrips); // frequency bump only, no new route
    }

    [Fact]
    public void DecideOrders_EmptyNetworkAndNoCities_ProducesNoOrders()
    {
        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, Array.Empty<City>(), DemandModel, new[] { Aircraft });

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

        var orders = BaselineAi.DecideOrders(existing, actionCap: 0, cities, DemandModel, new[] { tinyAircraft });

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

        var original = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b, c }, DemandModel, new[] { Aircraft });
        var shuffled = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { c, b, a }, DemandModel, new[] { Aircraft });

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

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft });
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, DemandModel);

        Assert.Empty(result.RejectedOrders); // the AI never proposes more than it can afford
    }

    [Fact]
    public void DecideOrders_IncludesNegotiation_ForBestUnservedGateway()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft });

        var negotiation = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("gw", negotiation.TargetCity.Id);
        Assert.True(negotiation.Bid.Cents > 0);
    }

    [Fact]
    public void DecideOrders_NeverTargetsAGatewayItAlreadyTouches()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var otherGateway = MakeGateway("gw2", 40, 40, 10);
        var cities = new[] { a, gateway, otherGateway };
        var existing = new[] { new Route(a, gateway, Aircraft, WeeklyRoundTrips: 3) }; // already touches "gw"

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft });

        var negotiation = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("gw2", negotiation.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_TightBudget_PrioritizesNewRouteOverNegotiation()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, b, gateway };

        // Cap 1: only the frequency-free bump plus exactly one of the two
        // cost-1 candidates can fit.
        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), actionCap: 1, cities, DemandModel, new[] { Aircraft });

        Assert.Single(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_WithGateways_StillProducesLegalOrders_WhenRunThroughQuarterEngine()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, b, gateway };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft });
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, DemandModel);

        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void DecideOrders_SkipsCandidatePair_BeyondAircraftRange()
    {
        var a = MakeCity("a", 0, 0, 100);
        var farButHighDemand = MakeCity("far", 40, 40, 100); // best demand, but out of reach
        var near = MakeCity("near", 0.5, 0.5, 5); // lower demand, but reachable
        var cities = new[] { a, farButHighDemand, near };
        var shortRangeAircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 500);

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { shortRangeAircraft });

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal("near", routeOrder.Destination.Id);
    }

    [Fact]
    public void DecideOrders_WithShortRangeAircraft_NeverProducesRejectedOrders()
    {
        var a = MakeCity("a", 0, 0, 100);
        var far = MakeCity("far", 40, 40, 100);
        var cities = new[] { a, far };
        var shortRangeAircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 500);

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { shortRangeAircraft });
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, DemandModel);

        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void DecideOrders_PicksLongestRangeAircraft_FromAvailableFleet()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var cities = new[] { a, b };
        var shortRange = new AircraftType("short", "Short", Seats: 50, RangeKm: 2000);
        var longRange = new AircraftType("long", "Long", Seats: 300, RangeKm: 15000);
        var fleet = new[] { shortRange, longRange };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, fleet);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal("long", routeOrder.Aircraft.Id);
    }

    [Fact]
    public void DecideOrders_EmptyFleet_SkipsNewRoute_ButStillConsidersNegotiation()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, Array.Empty<AircraftType>());

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Single(orders.OfType<NegotiationOrder>());
    }
}
