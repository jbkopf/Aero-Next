using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
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

    /// <summary>M37: reproduces BaselineAi's own RaisedFare/CutFare rounding independently, for tests that need to construct a route already at the target rather than assert against it.</summary>
    private static Money RaisedFareTarget(Route route) =>
        new((long)Math.Round(FareCalculator.SuggestedFarePerPassenger(route).Cents * 1.15, MidpointRounding.AwayFromZero));

    private static Money CutFareTarget(Route route) =>
        new((long)Math.Round(FareCalculator.SuggestedFarePerPassenger(route).Cents * 0.80, MidpointRounding.AwayFromZero));

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

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(("a", "b"), (routeOrder.Origin.Id, routeOrder.Destination.Id));
        Assert.Single(orders.OfType<HireExecutiveOrder>()); // M31: every quarter, unconditionally
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

        var routeOrders = orders.OfType<RouteOrder>().ToList();
        Assert.Equal(2, routeOrders.Count);
        Assert.Contains(routeOrders, o => o.Origin.Id == "a" && o.Destination.Id == "c"); // grows the existing route
        Assert.Contains(routeOrders, o => o.Origin.Id == "a" && o.Destination.Id == "b"); // new pair
        Assert.Single(orders.OfType<HireExecutiveOrder>()); // M31: every quarter, unconditionally
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
    public void DecideOrders_ZeroActionCap_ProducesNoActionConsumingOrders_ButStillHires()
    {
        // M32: growing an existing route now costs an action too, so a
        // zero-action-cap quarter produces no RouteOrder/NegotiationOrder/
        // etc - there's no longer a free frequency-bump variant to fall
        // back on. M31: HireExecutiveOrder is unconditional and costs 0
        // actions, so it's still proposed even here - cap 0 isn't "no
        // orders at all" anymore, just "no action-consuming ones."
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var cities = new[] { a, b };
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };

        var orders = BaselineAi.DecideOrders(existing, actionCap: 0, cities, DemandModel, new[] { Aircraft }, AirlineFleet.Empty);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<NegotiationOrder>());
        Assert.Single(orders.OfType<HireExecutiveOrder>());
    }

    [Fact]
    public void DecideOrders_EmptyNetworkAndNoCities_ProducesNoActionConsumingOrders_ButStillHires()
    {
        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, Array.Empty<City>(), DemandModel, new[] { Aircraft }, AirlineFleet.Empty);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<NegotiationOrder>());
        Assert.Single(orders.OfType<HireExecutiveOrder>());
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

        var growth = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(("a", "b"), (growth.Origin.Id, growth.Destination.Id));
        Assert.Single(orders.OfType<HireExecutiveOrder>()); // M31: free, doesn't compete for the cap-1 budget
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

    // --- M33: gate-slot awareness ---

    [Fact]
    public void DecideOrders_NewPairTouchesAGatewayWithNoAvailableSlot_SkipsIt_DoesNotAttemptAnyway()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 1, 1, 90); // the only reachable pair goes through here
        var cities = new[] { a, gateway };
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 0 };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft, availableSlotsByCity);

        Assert.Empty(orders.OfType<RouteOrder>());
        // Still bids on it via negotiation - that's how a slot gets won in the first place.
        Assert.Single(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_NewPairTouchesAGatewayWithAnAvailableSlot_StillProposesIt()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 1, 1, 90);
        var cities = new[] { a, gateway };
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 1 };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft, availableSlotsByCity);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal("gw", routeOrder.Destination.Id);
    }

    [Fact]
    public void DecideOrders_GrowingExistingRouteAtAGatewayWithNoAvailableSlot_SkipsGrowth()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 1, 1, 90);
        var cities = new[] { a, gateway };
        var existing = new[] { new Route(a, gateway, Aircraft, AircraftCount: 1) };
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 0 }; // this route's own unit already used the only slot

        var orders = BaselineAi.DecideOrders(existing, actionCap: 1, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft, availableSlotsByCity);

        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_ProducesLegalOrders_WhenRunThroughQuarterEngine_WithSlotGatingToo()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, b, gateway };
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 1 };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft, availableSlotsByCity);
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, DemandModel, FleetWithAircraft, availableSlotsByCity: availableSlotsByCity);

        Assert.Empty(result.RejectedOrders);
    }

    // --- M28: cash-aware negotiation ---

    [Fact]
    public void DecideOrders_NoCashPassed_NegotiationBehaviorUnchanged()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft);

        Assert.Single(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_CashBelowBidAmount_SkipsNegotiation()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft,
            availableSlotsByCity: null, availableCash: Money.FromDollars(1_999_999));

        Assert.Empty(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_CashExactlyMatchingBidAmount_StillProposesNegotiation()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft,
            availableSlotsByCity: null, availableCash: Money.FromDollars(2_000_000));

        Assert.Single(orders.OfType<NegotiationOrder>());
    }

    // --- M31: unconditional executive hiring (docs/BASELINE_AI_ROADMAP.md) ---

    [Fact]
    public void DecideOrders_FullBudgetSpentOnEverythingElse_StillHiresAnExecutiveToo()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, b, gateway };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft);

        // Every action-consuming candidate this AI can propose here (new
        // pair + negotiation) already fills the 3-action budget - hiring
        // still shows up on top, since it never competes for that budget.
        Assert.Single(orders.OfType<RouteOrder>());
        Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Single(orders.OfType<HireExecutiveOrder>());
    }

    [Fact]
    public void DecideOrders_HireExecutiveOrder_NeverCountsAgainstTheActionCap_WhenRunThroughQuarterEngine()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var cities = new[] { a, b };

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), actionCap: 1, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft);
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), currentActionCap: 1, orders, cities, DemandModel, FleetWithAircraft);

        Assert.Empty(result.RejectedOrders); // the one action-consuming order and the free hire both fit a cap-1 budget
        Assert.Equal(2, result.ActionCap); // the hire raises next quarter's cap
    }

    [Fact]
    public void DecideOrders_ActionCapGrowsQuarterOverQuarter_WhenHiringIsAppliedEachTime()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var cities = new[] { a, b };
        IReadOnlyList<Route> routes = Array.Empty<Route>();
        var cap = QuarterEngine.BaseActionCap;

        for (var quarter = 1; quarter <= 3; quarter++)
        {
            var orders = BaselineAi.DecideOrders(routes, cap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft);
            var result = QuarterEngine.RunQuarter(quarter, routes, cap, orders, cities, DemandModel, FleetWithAircraft);
            routes = result.Routes;
            cap = result.ActionCap;
        }

        Assert.Equal(QuarterEngine.BaseActionCap + 3, cap);
    }

    // --- M35: fleet/route lifecycle - retire and close (docs/BASELINE_AI_ROADMAP.md) ---

    [Fact]
    public void DecideOrders_OverBuiltRoute_LoadFactorBelowFloor_MultipleAircraft_GetsShrunk()
    {
        var a = MakeCity("a", 0, 0, 1);
        var b = MakeCity("b", 60, 60, 1); // far + light: negligible demand relative to capacity
        var bigAircraft = new AircraftType("big", "Big", Seats: 400);
        var existing = new[] { new Route(a, b, bigAircraft, AircraftCount: 2) };

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b }, DemandModel, new[] { bigAircraft }, AirlineFleet.Empty);

        var close = Assert.Single(orders.OfType<CloseRouteOrder>());
        Assert.Equal(("a", "b"), (close.Origin.Id, close.Destination.Id));
    }

    [Fact]
    public void DecideOrders_OverBuiltRoute_LoadFactorBelowFloor_SingleAircraft_NeverFullyClosed()
    {
        // The AircraftCount > 1 guard: closing the last aircraft on a
        // route would remove it from currentRoutes entirely, and a
        // high-demand-but-oversupplied pair would very plausibly get
        // reopened next quarter by the new-pair step - ping-ponging for
        // free. This milestone only ever shrinks, never fully abandons.
        var a = MakeCity("a", 0, 0, 1);
        var b = MakeCity("b", 60, 60, 1);
        var bigAircraft = new AircraftType("big", "Big", Seats: 400);
        var existing = new[] { new Route(a, b, bigAircraft, AircraftCount: 1) };

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b }, DemandModel, new[] { bigAircraft }, AirlineFleet.Empty);

        Assert.Empty(orders.OfType<CloseRouteOrder>());
    }

    [Fact]
    public void DecideOrders_HealthyRoute_LoadFactorAtOrAboveFloor_NeverShrunk()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90); // close + heavy: demand swamps capacity -> full load factor
        var aircraft = new AircraftType("t", "Test", Seats: 10);
        var existing = new[] { new Route(a, b, aircraft, AircraftCount: 2) };

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b }, DemandModel, new[] { aircraft }, AirlineFleet.Empty);

        Assert.Empty(orders.OfType<CloseRouteOrder>());
    }

    [Fact]
    public void DecideOrders_CloseRouteOrder_IsFreeAction()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);

        Assert.Equal(0, QuarterEngine.Cost(new CloseRouteOrder(a, b), Array.Empty<Route>()));
    }

    [Fact]
    public void DecideOrders_StrandedIdleUnit_NoRouteUsesItsType_NoUnservedPairExists_GetsRetired()
    {
        var a = MakeCity("a", 0, 0, 100); // only city - no pair can ever exist
        var strandedType = new AircraftType("stranded", "Stranded", Seats: 50);
        var fleet = FleetWithIdle(strandedType);

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { a }, DemandModel, new[] { strandedType }, fleet);

        var retire = Assert.Single(orders.OfType<RetireAircraftOrder>());
        Assert.Equal(fleet.Units.Single().Id, retire.UnitId);
    }

    [Fact]
    public void DecideOrders_IdleUnit_TypeStillUsedByAServedRoute_NotRetired()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var cities = new[] { a, b }; // no third city, so no unserved pair exists for this type either
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) }; // already uses Aircraft's type
        var fleet = FleetWithIdle(Aircraft, quantity: 2); // one grows a-b, one genuinely spare - but its type is still in use

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, fleet);

        Assert.Empty(orders.OfType<RetireAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_IdleUnit_CouldStillOpenAPositiveDemandPair_NotRetired()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var c = MakeCity("c", 2, 2, 20);
        var cities = new[] { a, b, c };
        var otherType = new AircraftType("other", "Other", Seats: 140);
        var fleet = FleetWithIdle(otherType, quantity: 2); // one opens the best pair (a-b), one remains idle but a-c/b-c are still open

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { otherType }, fleet);

        Assert.Empty(orders.OfType<RetireAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_RetireAircraftOrder_IsFreeAction()
    {
        Assert.Equal(0, QuarterEngine.Cost(new RetireAircraftOrder(1), Array.Empty<Route>()));
    }

    [Fact]
    public void DecideOrders_CloseAndRetire_AreFree_CoexistWithAFullBudgetOfOtherOrders()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var overBuiltDest = MakeCity("far", 60, 60, 1); // over-built route target, negligible demand
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, b, overBuiltDest, gateway };
        var bigAircraft = new AircraftType("big", "Big", Seats: 400);
        var existing = new[]
        {
            new Route(a, b, bigAircraft, AircraftCount: 1),            // healthy - highest load factor, gets grown
            new Route(a, overBuiltDest, bigAircraft, AircraftCount: 2), // over-built - gets shrunk
        };
        var fleet = FleetWithIdle(bigAircraft, quantity: 2); // grows a-b AND opens a new pair without needing to lease

        var orders = BaselineAi.DecideOrders(
            existing, QuarterEngine.BaseActionCap, cities, DemandModel, new[] { bigAircraft }, fleet,
            availableSlotsByCity: null, availableCash: Money.FromDollars(2_000_000));

        Assert.Single(orders.OfType<CloseRouteOrder>()); // shrinks the over-built a-far route
        Assert.Empty(orders.OfType<LeaseAircraftOrder>()); // two idle units cover both growth steps
        Assert.Equal(2, orders.OfType<RouteOrder>().Count()); // grow a-b + open the best new pair
        Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Single(orders.OfType<HireExecutiveOrder>());
    }

    [Fact]
    public void DecideOrders_CloseRouteOrder_ProducesLegalOrders_WhenRunThroughQuarterEngine()
    {
        // Worth documenting, not hiding: with only one existing route and
        // nothing else to grow, the "grow best route" step (unchanged
        // since M32) re-grows this exact route the same quarter the new
        // M35 step shrinks it - a real, visible illustration of the gap
        // M39 exists to close (M35 is reactive/uncoordinated by design).
        // Both orders are still individually legal and both apply; the
        // net AircraftCount is unchanged, not reduced, this quarter.
        var a = MakeCity("a", 0, 0, 1);
        var b = MakeCity("b", 60, 60, 1);
        var bigAircraft = new AircraftType("big", "Big", Seats: 400);
        var existing = new[] { new Route(a, b, bigAircraft, AircraftCount: 2) };
        var withUnits = FleetLedger.ApplyDelivery(AirlineFleet.Empty, bigAircraft, quantity: 2, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.Assign(FleetLedger.Assign(withUnits, withUnits.Units[0].Id, "a-b"), withUnits.Units[1].Id, "a-b");

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b }, DemandModel, new[] { bigAircraft }, fleet);
        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, orders, new[] { a, b }, DemandModel, fleet);

        Assert.Empty(result.RejectedOrders);
        Assert.Contains(orders, o => o is CloseRouteOrder);
        Assert.Single(result.Routes);
        Assert.Equal(2, result.Routes.Single().AircraftCount); // grow (+1) and close (-1) cancel out this quarter
    }

    [Fact]
    public void DecideOrders_RetireAircraftOrder_ProducesLegalOrders_WhenRunThroughQuarterEngine()
    {
        var a = MakeCity("a", 0, 0, 100);
        var strandedType = new AircraftType("stranded", "Stranded", Seats: 50);
        var fleet = FleetWithIdle(strandedType);

        var orders = BaselineAi.DecideOrders(Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { a }, DemandModel, new[] { strandedType }, fleet);
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { a }, DemandModel, fleet);

        Assert.Empty(result.RejectedOrders);
        Assert.Empty(result.UpdatedFleet!.Units);
    }

    // --- M36: debt tools (docs/BASELINE_AI_ROADMAP.md) ---

    [Fact]
    public void DecideOrders_CashShortOfBid_RealTargetExists_CeilingHasRoom_BorrowsTheGap_NotTheSameQuarterAsNegotiating()
    {
        // Borrowing and negotiating the same quarter would still get the
        // NegotiationOrder rejected by QuarterEngine - a same-quarter
        // RefinanceOrder's cash isn't visible to its affordability gate
        // (established back in M29). So: borrow now, negotiate next
        // quarter once availableCash actually reflects the top-up.
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft,
            availableSlotsByCity: null, availableCash: Money.FromDollars(500_000), currentDebtBalance: Money.Zero);

        var refinance = Assert.Single(orders.OfType<RefinanceOrder>());
        Assert.Equal(Money.FromDollars(1_500_000), refinance.AdditionalBorrowing); // 2,000,000 bid - 500,000 cash
        Assert.Empty(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_BorrowingTheGap_WouldBreachDebtCeiling_NeverBorrows()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };
        var debtAtCeiling = Money.FromDollars(FinancialResolver.DebtCeilingDollars); // zero headroom left

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft,
            availableSlotsByCity: null, availableCash: Money.FromDollars(500_000), currentDebtBalance: debtAtCeiling);

        Assert.Empty(orders.OfType<RefinanceOrder>());
        Assert.Empty(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_CashShortOfBid_NoDebtBalancePassed_NeverBorrows()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft,
            availableSlotsByCity: null, availableCash: Money.FromDollars(500_000)); // currentDebtBalance stays null

        Assert.Empty(orders.OfType<RefinanceOrder>());
        Assert.Empty(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_CashActuallyCoversTheBid_NeverBorrows()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft,
            availableSlotsByCity: null, availableCash: Money.FromDollars(2_000_000), currentDebtBalance: Money.Zero);

        Assert.Empty(orders.OfType<RefinanceOrder>());
        Assert.Single(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_BorrowThenNegotiate_ProducesLegalOrders_WhenRunThroughQuarterEngine()
    {
        var a = MakeCity("a", 0, 0, 100);
        var gateway = MakeGateway("gw", 30, 30, 80);
        var cities = new[] { a, gateway };

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, cities, DemandModel, new[] { Aircraft }, FleetWithAircraft,
            availableSlotsByCity: null, availableCash: Money.FromDollars(500_000), currentDebtBalance: Money.Zero);
        var result = QuarterEngine.RunQuarter(
            1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, DemandModel, FleetWithAircraft,
            availableCash: Money.FromDollars(500_000), currentDebtBalance: Money.Zero);

        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void DecideOrders_CashExceedsBuffer_DebtSmallerThanAffordablePaydown_PaysDownFullDebt()
    {
        var a = MakeCity("a", 0, 0, 1);

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { a }, DemandModel, new[] { Aircraft }, AirlineFleet.Empty,
            availableSlotsByCity: null, availableCash: Money.FromDollars(15_000_000), currentDebtBalance: Money.FromDollars(1_000_000));

        var payDown = Assert.Single(orders.OfType<PayDownDebtOrder>());
        Assert.Equal(Money.FromDollars(1_000_000), payDown.Amount);
    }

    [Fact]
    public void DecideOrders_CashExceedsBuffer_DebtLargerThanAffordablePaydown_PaysDownOnlyWhatsAffordable()
    {
        var a = MakeCity("a", 0, 0, 1);

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { a }, DemandModel, new[] { Aircraft }, AirlineFleet.Empty,
            availableSlotsByCity: null, availableCash: Money.FromDollars(15_000_000), currentDebtBalance: Money.FromDollars(50_000_000));

        var payDown = Assert.Single(orders.OfType<PayDownDebtOrder>());
        Assert.Equal(Money.FromDollars(5_000_000), payDown.Amount); // 15,000,000 cash - 10,000,000 buffer
    }

    [Fact]
    public void DecideOrders_CashExceedsBuffer_DebtIsZero_NeverPaysDown()
    {
        var a = MakeCity("a", 0, 0, 1);

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { a }, DemandModel, new[] { Aircraft }, AirlineFleet.Empty,
            availableSlotsByCity: null, availableCash: Money.FromDollars(15_000_000), currentDebtBalance: Money.Zero);

        Assert.Empty(orders.OfType<PayDownDebtOrder>());
    }

    [Fact]
    public void DecideOrders_CashAtOrBelowBuffer_NeverPaysDown_EvenWithNonzeroDebt()
    {
        var a = MakeCity("a", 0, 0, 1);

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { a }, DemandModel, new[] { Aircraft }, AirlineFleet.Empty,
            availableSlotsByCity: null, availableCash: Money.FromDollars(10_000_000), currentDebtBalance: Money.FromDollars(1_000_000));

        Assert.Empty(orders.OfType<PayDownDebtOrder>());
    }

    [Fact]
    public void DecideOrders_PayDownDebtOrder_ProducesLegalOrders_WhenRunThroughQuarterEngine()
    {
        var a = MakeCity("a", 0, 0, 1);

        var orders = BaselineAi.DecideOrders(
            Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { a }, DemandModel, new[] { Aircraft }, AirlineFleet.Empty,
            availableSlotsByCity: null, availableCash: Money.FromDollars(15_000_000), currentDebtBalance: Money.FromDollars(1_000_000));
        var result = QuarterEngine.RunQuarter(
            1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { a }, DemandModel, fleet: null,
            availableCash: Money.FromDollars(15_000_000), currentDebtBalance: Money.FromDollars(1_000_000));

        Assert.Empty(result.RejectedOrders);
    }

    // --- M37: fares, raise on saturated and cut on underperforming (docs/BASELINE_AI_ROADMAP.md) ---

    [Fact]
    public void DecideOrders_SaturatedRoute_LoadFactorAtCeiling_FareRaisedAboveDefault()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90); // close + heavy: demand swamps capacity -> full load factor
        var aircraft = new AircraftType("t", "Test", Seats: 10);
        var existing = new[] { new Route(a, b, aircraft, AircraftCount: 1) };

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b }, DemandModel, new[] { aircraft }, AirlineFleet.Empty);

        var fareOrder = Assert.Single(orders.OfType<SetFareOrder>());
        Assert.Equal(("a", "b"), (fareOrder.Origin.Id, fareOrder.Destination.Id));
        Assert.Equal(RaisedFareTarget(existing[0]), fareOrder.FarePerPassenger);
        Assert.True(fareOrder.FarePerPassenger > FareCalculator.SuggestedFarePerPassenger(existing[0]));
    }

    [Fact]
    public void DecideOrders_SaturatedRoute_FareAlreadyAtOrAboveRaisedTarget_NotRaisedAgain()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);
        var aircraft = new AircraftType("t", "Test", Seats: 10);
        var baseline = new Route(a, b, aircraft, AircraftCount: 1);
        var existing = new[] { baseline with { FareOverride = RaisedFareTarget(baseline) } };

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b }, DemandModel, new[] { aircraft }, AirlineFleet.Empty);

        Assert.Empty(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void DecideOrders_UnderperformingRoute_LoadFactorBelowFloor_FareCutBelowDefault()
    {
        var a = MakeCity("a", 0, 0, 1);
        var b = MakeCity("b", 60, 60, 1); // far + light: negligible demand relative to capacity
        var bigAircraft = new AircraftType("big", "Big", Seats: 400);
        var existing = new[] { new Route(a, b, bigAircraft, AircraftCount: 1) }; // single aircraft - never closed by M35, isolates the fare-cut behavior

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b }, DemandModel, new[] { bigAircraft }, AirlineFleet.Empty);

        var fareOrder = Assert.Single(orders.OfType<SetFareOrder>());
        Assert.Equal(("a", "b"), (fareOrder.Origin.Id, fareOrder.Destination.Id));
        Assert.Equal(CutFareTarget(existing[0]), fareOrder.FarePerPassenger);
        Assert.True(fareOrder.FarePerPassenger < FareCalculator.SuggestedFarePerPassenger(existing[0]));
    }

    [Fact]
    public void DecideOrders_UnderperformingRoute_FareAlreadyAtOrBelowCutTarget_NotCutAgain()
    {
        var a = MakeCity("a", 0, 0, 1);
        var b = MakeCity("b", 60, 60, 1);
        var bigAircraft = new AircraftType("big", "Big", Seats: 400);
        var baseline = new Route(a, b, bigAircraft, AircraftCount: 1);
        var existing = new[] { baseline with { FareOverride = CutFareTarget(baseline) } };

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b }, DemandModel, new[] { bigAircraft }, AirlineFleet.Empty);

        Assert.Empty(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void DecideOrders_MidRangeLoadFactor_NeitherSaturatedNorUnderperforming_FareUntouched()
    {
        var a = MakeCity("a", 0, 0, 60);
        var b = MakeCity("b", 3, 3, 60);
        var aircraft = new AircraftType("t", "Test", Seats: 280);
        var route = new Route(a, b, aircraft, AircraftCount: 1);
        var loadFactor = RouteEvaluator.Evaluate(route, DemandModel).LoadFactor;
        Assert.InRange(loadFactor, 0.5, 0.9499); // precondition: in the "safe zone" between the two thresholds

        var orders = BaselineAi.DecideOrders(new[] { route }, QuarterEngine.BaseActionCap, new[] { a, b }, DemandModel, new[] { aircraft }, AirlineFleet.Empty);

        Assert.Empty(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void DecideOrders_FareRaiseAndCut_AreFreeActions()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);

        Assert.Equal(0, QuarterEngine.Cost(new SetFareOrder(a, b, Money.FromDollars(100)), Array.Empty<Route>()));
    }

    [Fact]
    public void DecideOrders_FareRaiseAndCut_ProduceLegalOrders_WhenRunThroughQuarterEngine()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90);   // saturated
        var c = MakeCity("c", 60, 60, 1);  // far + light -> underperforming
        var cities = new[] { a, b, c };
        var saturatedAircraft = new AircraftType("sat", "Saturated", Seats: 10);
        var underperformingAircraft = new AircraftType("under", "Underperforming", Seats: 400);
        var existing = new[]
        {
            new Route(a, b, saturatedAircraft, AircraftCount: 1),
            new Route(a, c, underperformingAircraft, AircraftCount: 1),
        };

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, cities, DemandModel, new[] { saturatedAircraft, underperformingAircraft }, AirlineFleet.Empty);
        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, orders, cities, DemandModel);

        Assert.Equal(2, orders.OfType<SetFareOrder>().Count());
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void DecideOrders_ProposedFareRaise_WhenApplied_MeasurablyIncreasesRevenue_ForTheSameCarriedPassengers()
    {
        // Ties together the roadmap's own "Done when" bar: the fare
        // BaselineAi proposes, once actually applied, has a directly
        // observable revenue effect - same carried volume (fare doesn't
        // affect demand, M34's own scope), strictly more revenue.
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 90); // close + heavy -> saturated, full load factor
        var aircraft = new AircraftType("t", "Test", Seats: 10);
        var existing = new[] { new Route(a, b, aircraft, AircraftCount: 1) };
        var airline = new Airline("A", existing);
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, b }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var (_, baselineStatement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        var orders = BaselineAi.DecideOrders(existing, QuarterEngine.BaseActionCap, new[] { a, b }, DemandModel, new[] { aircraft }, AirlineFleet.Empty);
        var fareOrder = Assert.Single(orders.OfType<SetFareOrder>());
        var raisedRoutes = new[] { existing[0] with { FareOverride = fareOrder.FarePerPassenger } };
        var raisedAirline = new Airline("A", raisedRoutes);
        var raisedMarket = CompetitiveMarket.Resolve(new[] { raisedAirline }, new[] { a, b }, DemandModel);
        var (_, raisedStatement) = FinancialResolver.Resolve(state, raisedAirline, raisedMarket, Array.Empty<QuarterOrder>());

        Assert.Equal(market.TotalCarried(airline), raisedMarket.TotalCarried(raisedAirline)); // same carried volume
        Assert.True(raisedStatement.Revenue > baselineStatement.Revenue);
    }
}
