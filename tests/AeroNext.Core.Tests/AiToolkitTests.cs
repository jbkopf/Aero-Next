using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>M45: direct tests for the new AiToolkit statics the two real strategies wire alongside their existing gateway-slot checks.</summary>
public class AiToolkitTests
{
    private static City MakeCity(string id, Region region, double lat, double lon, int weight, bool isGateway = false, int? autoCapacity = null) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, isGateway, isGateway ? 3 : null, isGateway ? null : autoCapacity);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly AircraftType Aircraft = new("t", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);

    private static StrategyContext MakeContext(
        IReadOnlyList<City> cities, City homeCity,
        System.Collections.Generic.IReadOnlyDictionary<string, int>? idleContestableSlotsByCity = null,
        int actionCap = 3, Money? availableCash = null, IReadOnlyList<Route>? routes = null) =>
        new(routes ?? Array.Empty<Route>(), actionCap, cities, DemandModel, new[] { Aircraft }, AirlineFleet.Empty, homeCity.Region, homeCity,
            IdleContestableSlotsByCity: idleContestableSlotsByCity, AvailableCash: availableCash);

    // --- ComputeCityWideSlotsRemaining ---

    [Fact]
    public void ComputeCityWideSlotsRemaining_NoRoutesAnywhere_ReturnsFullCapacity()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>>
        {
            ["A"] = Array.Empty<Route>(),
        };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy });

        Assert.Equal(3, result["bz"]);
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_SumsAircraftCountAcrossEveryAirline_NotJustOne()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 5);
        var elsewhere = MakeCity("el", Region.NorthAmerica, 1, 1, 50);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>>
        {
            ["A"] = new[] { new Route(busy, elsewhere, Aircraft, AircraftCount: 2) },
            ["B"] = new[] { new Route(elsewhere, busy, Aircraft, AircraftCount: 1) }, // busy is the destination here
        };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy, elsewhere });

        Assert.Equal(2, result["bz"]); // 5 - (2 + 1)
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_UsageExceedsCapacity_FloorsAtZero_NeverNegative()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var elsewhere = MakeCity("el", Region.NorthAmerica, 1, 1, 50);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>>
        {
            ["A"] = new[] { new Route(busy, elsewhere, Aircraft, AircraftCount: 5) },
        };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy });

        Assert.Equal(0, result["bz"]);
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_CityWithNoAutoCapacity_NeverAppearsInResult()
    {
        var plain = MakeCity("pl", Region.NorthAmerica, 0, 0, 50); // AutoCapacity: null
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>> { ["A"] = Array.Empty<Route>() };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { plain });

        Assert.False(result.ContainsKey("pl"));
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_NullCapacityBonus_PreservesPreExistingBehavior()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>> { ["A"] = Array.Empty<Route>() };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy }, capacityBonusByCity: null);

        Assert.Equal(3, result["bz"]);
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_CapacityBonus_AddsOnTopOfAutoCapacity()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>> { ["A"] = Array.Empty<Route>() };
        var bonus = new System.Collections.Generic.Dictionary<string, int> { ["bz"] = 2 };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy }, capacityBonusByCity: bonus);

        Assert.Equal(5, result["bz"]);
    }

    // --- HasRoomAtEveryCity ---

    [Fact]
    public void HasRoomAtEveryCity_NullBalance_AlwaysTrue()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50, autoCapacity: 1);

        Assert.True(AiToolkit.HasRoomAtEveryCity(a, b, null, new System.Collections.Generic.Dictionary<string, int>()));
    }

    [Fact]
    public void HasRoomAtEveryCity_PositiveRemainingBalance_True()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 1 };

        Assert.True(AiToolkit.HasRoomAtEveryCity(a, b, remaining, new System.Collections.Generic.Dictionary<string, int>()));
    }

    [Fact]
    public void HasRoomAtEveryCity_ZeroRemainingBalance_False()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 0 };

        Assert.False(AiToolkit.HasRoomAtEveryCity(a, b, remaining, new System.Collections.Generic.Dictionary<string, int>()));
    }

    [Fact]
    public void HasRoomAtEveryCity_AlreadyReservedThisPass_ConsumesTheBalance()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 1 };
        var reserved = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 1 };

        Assert.False(AiToolkit.HasRoomAtEveryCity(a, b, remaining, reserved));
    }

    [Fact]
    public void HasRoomAtEveryCity_CityWithNoAutoCapacity_NeverGated()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50); // AutoCapacity: null
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int>(); // "a" absent - would read as 0 if checked

        Assert.True(AiToolkit.HasRoomAtEveryCity(a, b, remaining, new System.Collections.Generic.Dictionary<string, int>()));
    }

    // --- ReserveCityWideSlots ---

    [Fact]
    public void ReserveCityWideSlots_IncrementsBothEndpoints_WhenBothAreAutoCapacityCities()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50, autoCapacity: 3);
        var reserved = new System.Collections.Generic.Dictionary<string, int>();

        AiToolkit.ReserveCityWideSlots(a, b, reserved);

        Assert.Equal(1, reserved["a"]);
        Assert.Equal(1, reserved["b"]);
    }

    [Fact]
    public void ReserveCityWideSlots_SkipsEndpointWithNoAutoCapacity()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50); // AutoCapacity: null
        var reserved = new System.Collections.Generic.Dictionary<string, int>();

        AiToolkit.ReserveCityWideSlots(a, b, reserved);

        Assert.Equal(1, reserved["a"]);
        Assert.False(reserved.ContainsKey("b"));
    }

    // --- FindBestIdleContestableGateway ---

    [Fact]
    public void FindBestIdleContestableGateway_NullField_ReturnsNull()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, idleContestableSlotsByCity: null);

        Assert.Null(AiToolkit.FindBestIdleContestableGateway(context));
    }

    [Fact]
    public void FindBestIdleContestableGateway_PicksHighestDemandWeight()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var small = MakeCity("small", Region.NorthAmerica, 1, 1, 40, isGateway: true);
        var big = MakeCity("big", Region.NorthAmerica, 2, 2, 90, isGateway: true);
        var idle = new System.Collections.Generic.Dictionary<string, int> { ["small"] = 1, ["big"] = 1 };
        var context = MakeContext(new[] { home, small, big }, home, idleContestableSlotsByCity: idle);

        var result = AiToolkit.FindBestIdleContestableGateway(context);

        Assert.Equal("big", result!.Id);
    }

    [Fact]
    public void FindBestIdleContestableGateway_ZeroCountCity_NeverPicked()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var zero = MakeCity("zero", Region.NorthAmerica, 1, 1, 90, isGateway: true);
        var idle = new System.Collections.Generic.Dictionary<string, int> { ["zero"] = 0 };
        var context = MakeContext(new[] { home, zero }, home, idleContestableSlotsByCity: idle);

        Assert.Null(AiToolkit.FindBestIdleContestableGateway(context));
    }

    [Fact]
    public void FindBestIdleContestableGateway_AlreadyPursued_Excluded()
    {
        // "target" has idle-expired inventory but this airline already
        // holds an available (won-but-unused) slot there - same exclusion
        // every other negotiation-target search already applies via
        // AiToolkit.CitiesAlreadyPursuedForNegotiation.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var target = MakeCity("target", Region.NorthAmerica, 1, 1, 90, isGateway: true);
        var idle = new System.Collections.Generic.Dictionary<string, int> { ["target"] = 1 };
        var context = MakeContext(new[] { home, target }, home, idleContestableSlotsByCity: idle) with
        {
            AvailableSlotsByCity = new System.Collections.Generic.Dictionary<string, int> { ["target"] = 1 },
        };

        Assert.Null(AiToolkit.FindBestIdleContestableGateway(context));
    }

    // --- ApplyManagementToolkit: hiring (M50) ---

    [Fact]
    public void ApplyManagementToolkit_ActionCapWasBinding_AffordableAndUnderHeadcountCap_Hires()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, actionCap: 3, availableCash: Money.FromDollars(10_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 3);

        Assert.Single(orders.OfType<HireExecutiveOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_ActionCapWasNotBinding_DoesNotHire()
    {
        // 1 of 3 actions spent - the cap was never the bottleneck this
        // quarter, so an extra one wouldn't have helped.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, actionCap: 3, availableCash: Money.FromDollars(10_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 1);

        Assert.Empty(orders.OfType<HireExecutiveOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_Unaffordable_DoesNotHireEvenWhenBinding()
    {
        // Binding (3 of 3 spent), but cash falls short of ExecutiveHiringCost
        // ($600,000) x the 3x comfort multiplier ($1,800,000).
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, actionCap: 3, availableCash: Money.FromDollars(1_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 3);

        Assert.Empty(orders.OfType<HireExecutiveOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_AtHeadcountCap_DoesNotHireEvenWhenBindingAndAffordable()
    {
        // ActionCap already at QuarterEngine.MaxActionCap - 4 executives
        // already on staff (the derivation this method itself uses),
        // MaxExecutives reached.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, actionCap: QuarterEngine.MaxActionCap, availableCash: Money.FromDollars(10_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: QuarterEngine.MaxActionCap);

        Assert.Empty(orders.OfType<HireExecutiveOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_NullAvailableCash_TreatedAsAffordable_PermissiveDefault()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, actionCap: 3, availableCash: null);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 3);

        Assert.Single(orders.OfType<HireExecutiveOrder>());
    }

    // --- ApplyManagementToolkit: fare cut (M37, removed by M51, reinstated by M53) ---

    [Fact]
    public void ApplyManagementToolkit_StrugglingRoute_CutsFare()
    {
        // BigAircraft's huge capacity against real demand for these
        // close, weight-50 cities gives a low load factor - below
        // BaselineAi.CloseRouteLoadFactorFloor.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route });
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        var order = Assert.Single(orders.OfType<SetFareOrder>());
        Assert.Equal(new HashSet<string> { "a", "b" }, new HashSet<string> { order.Origin.Id, order.Destination.Id });
        Assert.True(order.FarePerPassenger < route.FarePerPassenger); // sanity: this is a cut, not a raise
    }

    [Fact]
    public void ApplyManagementToolkit_HealthyRoute_DoesNotCutFare()
    {
        // Aircraft's small capacity against the same demand gives a high
        // (likely oversold/saturated) load factor - not struggling.
        // Asserts specifically that no cut happened (rather than "no
        // SetFareOrder at all") since a saturated route legitimately
        // still gets a raise from the separate RaisedFare branch above.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route });
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        // No SetFareOrder below the route's own current (suggested) fare
        // - a raise (if the route also happens to be saturated) is fine,
        // a cut is not.
        Assert.DoesNotContain(orders.OfType<SetFareOrder>(), o => o.FarePerPassenger < route.FarePerPassenger);
    }

    // --- ApplyMarketingCampaign (M52, retargeted at struggling routes by M53) ---

    // M53: Aircraft (100 seats) against these tests' own close, weight-50
    // city pairs produces demand well above capacity - a "healthy"
    // (oversold, high load factor) route, exactly why the pre-M53 tests
    // below broke when the struggling-only filter landed. BigAircraft's
    // huge capacity against the same demand produces a "struggling" (near
    // -zero load factor) route instead - the two together let a test
    // build both kinds from the same cities/demand.
    private static readonly AircraftType BigAircraft = new("big", "Big", Seats: 100_000, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);

    private static StrategyContext MakeMarketingContext(
        IReadOnlyList<Route> routes,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), int>? rivalCountByServedRoute,
        Money? availableCash = null, int actionCap = 3)
    {
        var home = routes.Count > 0 ? routes[0].Origin : MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var cities = routes.SelectMany(r => new[] { r.Origin, r.Destination }).Distinct().DefaultIfEmpty(home).ToList();

        return new(routes, actionCap, cities, DemandModel, new[] { Aircraft }, AirlineFleet.Empty, home.Region, home,
            AvailableCash: availableCash, RivalCountByServedRoute: rivalCountByServedRoute);
    }

    [Fact]
    public void ApplyMarketingCampaign_NullRivalCount_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var context = MakeMarketingContext(new[] { route }, rivalCountByServedRoute: null, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_NoContestedRoutes_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 0 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_OneStrugglingContestedRoute_ProposesRouteMarketingOrder()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1); // huge capacity against real demand - low load factor
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        var order = Assert.Single(orders.OfType<RouteMarketingOrder>());
        Assert.Equal(new HashSet<string> { "a", "b" }, new HashSet<string> { order.Origin.Id, order.Destination.Id });
        Assert.Empty(orders.OfType<NetworkMarketingOrder>());
        Assert.Equal(1, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_ContestedRouteNotStruggling_NoOp()
    {
        // M53: a contested route with a HIGH load factor (demand exceeds
        // this small aircraft's capacity) no longer draws a campaign at
        // all - M52's own original demand-ranking would have picked it
        // unconditionally; the retargeted version requires it be
        // genuinely struggling first.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1); // small capacity against real demand - high load factor
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_StrugglingContestedRoute_PickedOverHealthierHigherDemandOne()
    {
        // M53: two contested routes, only one struggling - the healthier
        // one (route to c, high raw demand, high load factor) must NOT
        // be picked even though M52's own original demand-ranking would
        // have preferred it.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 200); // much bigger demand weight
        var strugglingRoute = new Route(a, b, BigAircraft, AircraftCount: 1);
        var healthyRoute = new Route(a, c, Aircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = 1,
            [QuarterEngine.NormalizedPairKey(a, c)] = 1,
        };
        var context = MakeMarketingContext(new[] { strugglingRoute, healthyRoute }, rivalCount, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        var order = Assert.Single(orders.OfType<RouteMarketingOrder>());
        Assert.Equal(new HashSet<string> { "a", "b" }, new HashSet<string> { order.Origin.Id, order.Destination.Id });
    }

    [Fact]
    public void ApplyMarketingCampaign_TwoStrugglingContestedRoutes_ProposesNetworkMarketingOrder()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 50);
        var routeAb = new Route(a, b, BigAircraft, AircraftCount: 1);
        var routeAc = new Route(a, c, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = 1,
            [QuarterEngine.NormalizedPairKey(a, c)] = 2,
        };
        var context = MakeMarketingContext(new[] { routeAb, routeAc }, rivalCount, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Single(orders.OfType<NetworkMarketingOrder>());
        Assert.Empty(orders.OfType<RouteMarketingOrder>());
        Assert.Equal(1, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_TwoStrugglingContestedRoutes_Unaffordable_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 50);
        var routeAb = new Route(a, b, BigAircraft, AircraftCount: 1);
        var routeAc = new Route(a, c, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = 1,
            [QuarterEngine.NormalizedPairKey(a, c)] = 2,
        };
        var context = MakeMarketingContext(new[] { routeAb, routeAc }, rivalCount, availableCash: Money.FromDollars(1));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_Unaffordable_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(1));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_BudgetExhausted_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(50_000_000), actionCap: 3);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 3; // already at cap

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(3, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_NullAvailableCash_TreatedAsAffordable_PermissiveDefault()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: null);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Single(orders.OfType<RouteMarketingOrder>());
        Assert.Equal(1, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_TwoStrugglingContestedRoutes_NullAvailableCash_TreatedAsAffordable()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 50);
        var routeAb = new Route(a, b, BigAircraft, AircraftCount: 1);
        var routeAc = new Route(a, c, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = 1,
            [QuarterEngine.NormalizedPairKey(a, c)] = 2,
        };
        var context = MakeMarketingContext(new[] { routeAb, routeAc }, rivalCount, availableCash: null);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Single(orders.OfType<NetworkMarketingOrder>());
        Assert.Equal(1, spent);
    }
}
