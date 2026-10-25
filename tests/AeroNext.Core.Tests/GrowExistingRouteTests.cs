using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>AiToolkit.GrowExistingRoute: idle aircraft first onto a route where one more pays; buy or lease only when none has a profitable home.</summary>
public class GrowExistingRouteTests
{
    private static readonly GravityDemandModel DemandModel = new();

    // Same-region pairs. Small aircraft against big demand: every route is
    // full, so another aircraft has passengers to carry.
    private static readonly City Home = new("home", "HOME", Region.NorthAmerica, 0, 0, 100, false, null);
    private static readonly City Near = new("near", "NEAR", Region.NorthAmerica, 3, 3, 200, false, null);
    private static readonly City Far = new("far", "FAR", Region.NorthAmerica, 8, 8, 200, false, null);
    private static readonly City Gate = new("gate", "GATE", Region.NorthAmerica, 5, 0, 200, true, 2);
    private static readonly City Thin = new("thin", "THIN", Region.NorthAmerica, 20, 20, 1, false, null);
    private static readonly IReadOnlyList<City> Cities = new[] { Home, Near, Far, Gate, Thin };

    private static AircraftType Type(string id, int seats = 20) =>
        new(id, id, Seats: seats, RangeKm: 10_000, Cost: Money.FromDollars(1_000_000), CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.05);

    private static readonly AircraftType X = Type("x");
    private static readonly AircraftType Y = Type("y");

    private static AirlineFleet Fleet(IReadOnlyList<Route> routes, params AircraftType[] idle)
    {
        var units = routes.Select((r, i) => new FleetUnit(i + 1, r.Aircraft, FleetOwnershipMode.Owned, null))
            .Concat(idle.Select((t, i) => new FleetUnit(routes.Count + i + 1, t, FleetOwnershipMode.Owned, null)))
            .ToList();
        var fleet = new AirlineFleet(units, units.Count + 1);

        for (var i = 0; i < routes.Count; i++)
        {
            fleet = FleetLedger.Assign(fleet, i + 1, $"{routes[i].Origin.Id}-{routes[i].Destination.Id}");
        }

        return fleet;
    }

    private static StrategyContext Context(IReadOnlyList<Route> routes, AirlineFleet fleet, long cashDollars = 1_000_000_000,
        IReadOnlyDictionary<string, int>? gatewaySlots = null, int actionCap = 3,
        IReadOnlyDictionary<(string OriginId, string DestinationId), AiRouteHistory>? history = null) =>
        new(routes, actionCap, Cities, DemandModel, new[] { X, Y }, fleet, Region.NorthAmerica, Home,
            AvailableSlotsByCity: gatewaySlots ?? new Dictionary<string, int> { ["gate"] = 1 },
            AvailableCash: Money.FromDollars(cashDollars), RouteHistoryByPair: history, QuarterNumber: 40);

    private static (List<QuarterOrder> Orders, int Spent) Grow(StrategyContext context)
    {
        var orders = new List<QuarterOrder>();
        var spent = 0;
        AiToolkit.GrowExistingRoute(context, orders, ref spent, new HashSet<int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), cashComfortMultiplier: 3);
        return (orders, spent);
    }

    [Fact]
    public void AnIdleAircraftGoesToTheRouteWhereOneMoreEarnsMost()
    {
        var routes = new[] { new Route(Home, Near, X, 1), new Route(Home, Far, X, 1) };
        var context = Context(routes, Fleet(routes, X));
        var nearGain = AiToolkit.MarginalProfitOfAnotherAircraft(context, routes[0], requiresLease: false);
        var farGain = AiToolkit.MarginalProfitOfAnotherAircraft(context, routes[1], requiresLease: false);

        var (orders, spent) = Grow(context);

        var best = farGain.Cents > nearGain.Cents ? Far : Near;
        Assert.True(Math.Min(nearGain.Cents, farGain.Cents) > 0);
        var order = Assert.IsType<RouteOrder>(Assert.Single(orders));
        Assert.Equal(best.Id, order.Destination.Id);
        Assert.Equal(1, spent);
    }

    [Fact]
    public void IdleAircraftComeFirst_EvenWhenANewAircraftElsewhereWouldEarnMore()
    {
        // home-far (type Y) would gain more from another aircraft, but only
        // an X is idle and home-near (type X) can use it profitably.
        var routes = new[] { new Route(Home, Near, X, 1), new Route(Home, Far, Y, 1) };
        var context = Context(routes, Fleet(routes, X));
        Assert.True(AiToolkit.MarginalProfitOfAnotherAircraft(context, routes[1], requiresLease: false).Cents
            > AiToolkit.MarginalProfitOfAnotherAircraft(context, routes[0], requiresLease: false).Cents);

        var (orders, _) = Grow(context);

        var order = Assert.IsType<RouteOrder>(Assert.Single(orders));
        Assert.Equal("near", order.Destination.Id);
        Assert.Equal("x", order.Aircraft.Id);
    }

    [Fact]
    public void BuysOnlyWhenNoIdleAircraftHasAProfitableRoute()
    {
        // The idle X's only route (home-thin) has empty seats, so it stays
        // idle and a Y is bought for full home-far instead.
        var routes = new[] { new Route(Home, Thin, X, 1), new Route(Home, Far, Y, 1) };
        var context = Context(routes, Fleet(routes, X));

        var (orders, _) = Grow(context);

        var order = Assert.IsType<AircraftOrder>(Assert.Single(orders));
        Assert.Equal("y", order.Type.Id);
        Assert.True(AiToolkit.MarginalProfitOfAnotherAircraft(context, routes[0], requiresLease: false).Cents < 0);
    }

    [Fact]
    public void SkipsARouteWithNoFreeSlot_AndGrowsTheNextBest()
    {
        var routes = new[] { new Route(Home, Gate, X, 1), new Route(Home, Near, X, 1) };
        var context = Context(routes, Fleet(routes, X), gatewaySlots: new Dictionary<string, int> { ["gate"] = 0 });

        var (orders, _) = Grow(context);

        var order = Assert.IsType<RouteOrder>(Assert.Single(orders));
        Assert.Equal("near", order.Destination.Id);
    }

    [Fact]
    public void SkipsARouteShrunkWithinTheCooldown()
    {
        var routes = new[] { new Route(Home, Near, X, 1) };
        var history = new Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(Home, Near)] = new(LastShrunkQuarter: 39, LastClosedQuarter: null, 0),
        };

        var (orders, spent) = Grow(Context(routes, Fleet(routes, X), history: history));

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void LeasesWhenBuyingIsNotComfortablyAffordable()
    {
        var routes = new[] { new Route(Home, Near, X, 1) };

        var (orders, _) = Grow(Context(routes, Fleet(routes), cashDollars: 2_000_000)); // under 3x the $1M price

        var order = Assert.IsType<LeaseAircraftOrder>(Assert.Single(orders));
        Assert.Equal("x", order.Type.Id);
    }

    [Fact]
    public void DoesNothingWithoutUnmetDemand_OrWithoutABudget()
    {
        var thinOnly = new[] { new Route(Home, Thin, X, 1) };
        var full = new[] { new Route(Home, Near, X, 1) };

        Assert.Empty(Grow(Context(thinOnly, Fleet(thinOnly, X))).Orders);
        Assert.Empty(Grow(Context(full, Fleet(full, X), actionCap: 0)).Orders);
    }

    [Fact]
    public void MarginalProfit_IsWorthlessOnARouteThatIsntFull()
    {
        // Real carried well under seats (rivals take the rest): no amount of
        // demand on the pair makes another aircraft pay.
        var route = new Route(Home, Near, X, 1);
        var context = Context(new[] { route }, Fleet(new[] { route })) with
        {
            LastQuarterSeatsCarriedByRoute = new Dictionary<(string, string), long> { [QuarterEngine.NormalizedPairKey(Home, Near)] = route.SeatsPerQuarter / 2 },
        };

        Assert.True(AiToolkit.MarginalProfitOfAnotherAircraft(context, route, requiresLease: false).Cents < 0);
        Assert.True(AiToolkit.MarginalProfitOfAnotherAircraft(context with { LastQuarterSeatsCarriedByRoute = null }, route, requiresLease: false).Cents > 0);
    }
}
