using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class BaselineAiTests
{
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, false, null);

    private static City MakeGateway(string id, double lat, double lon, int weight, int gateCapacity = 3) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, true, gateCapacity);

    private static AirlineFleet FleetWithIdle(AircraftType type, int quantity = 1) =>
        FleetLedger.ApplyDelivery(AirlineFleet.Empty, type, quantity, FleetOwnershipMode.Owned);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly AircraftType Aircraft = new("t", "Test", Seats: 140);
    private static readonly AirlineFleet FleetWithAircraft = FleetWithIdle(Aircraft);

    [Fact]
    public void DecideOrders_EmptyNetwork_EntersSingleHighestDemandPair()
    {
        var cities = new[]
        {
            MakeCity("a", 0, 0, 100),
            MakeCity("b", 1, 1, 90), // closest + heaviest pair with a -> highest demand
            MakeCity("c", 30, 30, 20),
        };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft);

        var order = Assert.Single(orders);
        var routeOrder = Assert.IsType<RouteOrder>(order);
        Assert.Equal(("a", "b"), (routeOrder.Origin.Id, routeOrder.Destination.Id));
    }

    [Fact]
    public void DecideOrders_WithExistingRoute_AlsoGrowsItWithAnotherAircraft()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var c = MakeCity("c", 30, 30, 20);
        var cities = new[] { a, b, c };
        var existing = new[] { new Route(a, c, Aircraft, AircraftCount: 1) };
        var fleet = FleetWithIdle(Aircraft, quantity: 2); // one to grow a-c, one to open a-b

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, fleet);

        Assert.Equal(2, orders.Count);
        var routeOrders = orders.Cast<RouteOrder>().ToList();
        Assert.Contains(routeOrders, o => o.Origin.Id == "a" && o.Destination.Id == "c"); // grows the existing route
        Assert.Contains(routeOrders, o => o.Origin.Id == "a" && o.Destination.Id == "b"); // new pair
    }

    [Fact]
    public void DecideOrders_NeverProposesAPairItAlreadyServes()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90); // objectively the best pair in this set
        var c = MakeCity("c", 30, 30, 20);
        var cities = new[] { a, b, c };
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) }; // already serves the best pair

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft);

        var newRouteOrder = orders.OfType<RouteOrder>().Single(o => (o.Origin.Id, o.Destination.Id) != ("a", "b"));
        Assert.DoesNotContain((newRouteOrder.Origin.Id, newRouteOrder.Destination.Id), new[] { ("a", "b") });
    }

    [Fact]
    public void DecideOrders_ZeroActionCap_ProducesNoOrders()
    {
        // M32: growing an existing route now costs an action too, so a
        // zero-action-cap quarter produces nothing at all - there's no
        // longer a free frequency-bump variant to fall back on.
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var cities = new[] { a, b };
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };

        var orders = BaselineAi.DecideOrders(existing, actionCap: 0, cities, DemandModel, new[] { Aircraft }, AirlineFleet.Empty);

        Assert.Empty(orders);
    }

    [Fact]
    public void DecideOrders_EmptyNetworkAndNoCities_ProducesNoOrders()
    {
        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, Array.Empty<City>(), DemandModel, new[] { Aircraft }, AirlineFleet.Empty);

        Assert.Empty(orders);
    }

    [Fact]
    public void DecideOrders_GrowsTheMostCapacityConstrainedRoute_NotJustAnyRoute()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 0, 1, 100);  // close + heavy: demand swamps a tiny aircraft -> capacity-constrained
        var c = MakeCity("c", 60, 60, 1);  // far + light: demand is tiny -> nowhere near capacity-constrained
        var cities = new[] { a, b, c };
        var tinyAircraft = new AircraftType("tiny", "Tiny", Seats: 5);
        var existing = new[]
        {
            new Route(a, b, tinyAircraft, AircraftCount: 1), // high load factor
            new Route(a, c, tinyAircraft, AircraftCount: 1), // low load factor
        };
        var fleet = FleetWithIdle(tinyAircraft); // exactly enough for the one growth action this budget allows

        var orders = BaselineAi.DecideOrders(existing, actionCap: 1, cities, DemandModel, new[] { tinyAircraft }, fleet);

        var growth = Assert.IsType<RouteOrder>(Assert.Single(orders));
        Assert.Equal(("a", "b"), (growth.Origin.Id, growth.Destination.Id));
    }

    [Fact]
    public void DecideOrders_IsDeterministic_RegardlessOfCityInputOrder()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var c = MakeCity("c", 30, 30, 20);
        var existing = new[] { new Route(a, c, Aircraft, AircraftCount: 1) };

        var original = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b, c }, DemandModel, new[] { Aircraft }, FleetWithAircraft);
        var shuffled = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { c, b, a }, DemandModel, new[] { Aircraft }, FleetWithAircraft);

        // Compares every order, not just RouteOrders - a fleet this tight
        // (one idle unit) means the new-pair step falls back to leasing,
        // and that should be just as deterministic as the routes are.
        static string Describe(QuarterOrder order) => order switch
        {
            RouteOrder r => $"route:{r.Origin.Id}-{r.Destination.Id}@{r.Aircraft.Id}",
            LeaseAircraftOrder l => $"lease:{l.Type.Id}x{l.Quantity}",
            NegotiationOrder n => $"negotiate:{n.TargetCity.Id}@{n.Bid}",
            _ => order.GetType().Name,
        };

        Assert.Equal(original.Select(Describe), shuffled.Select(Describe));
    }

    [Fact]
    public void DecideOrders_ProducesLegalOrders_WhenRunThroughQuarterEngine()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var c = MakeCity("c", 30, 30, 20);
        var cities = new[] { a, b, c };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft);
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, DemandModel, FleetWithAircraft);

        Assert.Empty(result.RejectedOrders); // the AI never proposes more than it can afford or fly
    }

    [Fact]
    public void DecideOrders_IncludesNegotiation_ForBestUnservedGateway()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft);

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
        var existing = new[] { new Route(a, gateway, Aircraft, AircraftCount: 1) }; // already touches "gw"
        var fleet = FleetWithIdle(Aircraft, quantity: 2); // grow a-gw and open a-gw2 without spilling into a lease, leaving budget for the negotiation

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, fleet);

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

        // Cap 1, no existing route to grow: only one of the two cost-1
        // candidates (new pair vs. negotiation) can fit, and new-pair wins.
        // Needs an idle unit already on hand - leasing costs 2 actions
        // (lease + route), which wouldn't fit a cap-1 budget at all.
        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), actionCap: 1, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft);

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

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft);
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, DemandModel, FleetWithAircraft);

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

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { shortRangeAircraft }, FleetWithIdle(shortRangeAircraft));

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
        var fleet = FleetWithIdle(shortRangeAircraft);

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { shortRangeAircraft }, fleet);
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, DemandModel, fleet);

        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void DecideOrders_NothingIdle_LeasesLongestRangeAircraft_FromEraAvailableTypes()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var cities = new[] { a, b };
        var shortRange = new AircraftType("short", "Short", Seats: 50, RangeKm: 2000);
        var longRange = new AircraftType("long", "Long", Seats: 300, RangeKm: 15000);
        var eraAvailableTypes = new[] { shortRange, longRange };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, eraAvailableTypes, AirlineFleet.Empty);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal("long", routeOrder.Aircraft.Id);
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>());
        Assert.Equal("long", lease.Type.Id);
    }

    [Fact]
    public void DecideOrders_IdleUnitAvailable_PrefersItOverLeasingSomethingNew()
    {
        // Even though a longer-range type exists in the era-available
        // catalog, an idle unit already on hand should always be used
        // first - it's free, leasing isn't.
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var cities = new[] { a, b };
        var shortRange = new AircraftType("short", "Short", Seats: 50, RangeKm: 2000);
        var longRange = new AircraftType("long", "Long", Seats: 300, RangeKm: 15000);

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { shortRange, longRange }, FleetWithIdle(shortRange));

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal("short", routeOrder.Aircraft.Id);
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_NothingIdleAndNothingEraAvailable_SkipsNewRoute_ButStillConsidersNegotiation()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, Array.Empty<AircraftType>(), AirlineFleet.Empty);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Single(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_NothingIdle_NotEnoughBudgetForLeasePlusRoute_SkipsBoth()
    {
        // Cap 1: leasing (1 action) + opening a route with it (1 more
        // action) needs 2, and this AI never leases speculatively without
        // also using it the same quarter.
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var cities = new[] { a, b };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), actionCap: 1, cities, DemandModel, new[] { Aircraft }, AirlineFleet.Empty);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
    }
}
