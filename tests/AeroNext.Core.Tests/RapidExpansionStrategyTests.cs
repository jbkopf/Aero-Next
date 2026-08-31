using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class RapidExpansionStrategyTests
{
    private static City MakeCity(string id, Region region, double lat, double lon, int weight, bool isGateway = false) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, isGateway, isGateway ? 3 : null);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly RapidExpansionStrategy Strategy = new();

    private static StrategyContext MakeContext(
        IReadOnlyList<Route> routes, IReadOnlyList<City> cities, IReadOnlyList<AircraftType> eraTypes, Region homeRegion,
        AirlineFleet? fleet = null, int actionCap = 3, Money? cash = null) =>
        new(routes, actionCap, cities, DemandModel, eraTypes, fleet ?? AirlineFleet.Empty, homeRegion, AvailableCash: cash);

    [Fact]
    public void DecideOrders_TightBudget_PrefersOpeningANewPair_OverGrowingAnExistingRoute()
    {
        // Cap 1: only one of the two candidate actions fits. If new-pair
        // genuinely runs before grow-existing (the inversion from
        // BaselineAi's own order), the one RouteOrder targets the new
        // pair, not the already-served one.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 5); // already served - low weight so it doesn't make an attractive *second* leg for other candidates
        var far = MakeCity("far", Region.NorthAmerica, 5, 5, 20); // new-pair candidate
        var cities = new[] { home, near, far };
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned); // one idle unit - enough for exactly one growth action

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        // The key claim: it opened something new, not grown the existing
        // (home, near) route - proving new-pair ran first and claimed
        // the cap-1 budget, exactly the inversion from BaselineAi's own
        // order.
        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.NotEqual(new HashSet<string> { "home", "near" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
        Assert.Contains("far", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_PrefersACrossRegionCandidate_OverAHigherDemandIntraRegionOne()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var domestic = MakeCity("dom", Region.NorthAmerica, 1, 1, 500); // higher raw demand
        var foreign = MakeCity("frn", Region.Europe, 2, 2, 20); // lower raw demand, but cross-region
        var cities = new[] { home, domestic, foreign };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        // "dom" and "frn" also form a valid (still cross-region) pair, so
        // the exact winner isn't pinned down to (home, frn) specifically
        // - the real claim is that whichever pair wins, it touches the
        // foreign city, never the higher-weight purely-domestic option.
        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Contains("frn", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_NegotiatesForACrossRegionGateway_OverAHigherDemandDomesticOne()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var domesticGateway = MakeCity("dgw", Region.NorthAmerica, 1, 1, 500, isGateway: true);
        var foreignGateway = MakeCity("fgw", Region.Europe, 2, 2, 20, isGateway: true);
        var cities = new[] { home, domesticGateway, foreignGateway };

        var context = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica, cash: Money.FromDollars(10_000_000));

        var orders = Strategy.DecideOrders(context);

        var negotiation = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("fgw", negotiation.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_NoRouteCap_KeepsGrowingWellBeyondFiveRoutes()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var partners = Enumerable.Range(0, 6)
            .Select(i => MakeCity($"p{i}", Region.NorthAmerica, i + 1, i + 1, 50))
            .ToList();
        var cities = new[] { home }.Concat(partners).ToList();
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var existing = partners.Take(5).Select(p => new Route(home, p, aircraft, AircraftCount: 1)).ToList(); // already at DomesticProfitOptimizer's own cap
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        // The 6th partner (p5) is still a legal target - no cap here.
        // (The grow-existing step also fires, via a fresh lease, once
        // new-pair has claimed the one idle unit - correct "no
        // double-booking" behavior, not asserted on here directly; see
        // the dedicated QuarterEngine integration test below for that.)
        Assert.Contains(orders.OfType<RouteOrder>(), o => new[] { o.Origin.Id, o.Destination.Id }.Contains("p5"));
    }

    [Fact]
    public void Suitability_HigherWhenCashIsHigher()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var cities = new[] { home };
        var richContext = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica, cash: Money.FromDollars(20_000_000));
        var poorContext = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica, cash: Money.FromDollars(2_000_000));

        Assert.True(Strategy.Suitability(richContext) > Strategy.Suitability(poorContext));
    }

    [Fact]
    public void ApplicableEras_CoversOnlyStarting()
    {
        Assert.Equal(new[] { GameEra.Starting }, Strategy.ApplicableEras);
    }

    [Fact]
    public void DecideOrders_NewPairAndGrowExisting_BothFireTheSameQuarter_NeitherDoubleBooksTheIdleUnit()
    {
        // The real risk the inverted order creates (see the class doc
        // comment): with exactly one idle unit, both steps could pick
        // the same one if reservedUnitIds isn't threaded through
        // correctly. Proven end to end through QuarterEngine, not just
        // by inspecting the order list - zero rejections is the real
        // guarantee.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var far = MakeCity("far", Region.NorthAmerica, 5, 5, 20);
        var cities = new[] { home, near, far };
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };
        var withUnits = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 2, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.Assign(withUnits, withUnits.Units[0].Id, "home-near"); // 1 assigned to the existing route, 1 genuinely idle

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet, actionCap: QuarterEngine.BaseActionCap);

        var orders = Strategy.DecideOrders(context);
        var result = QuarterEngine.RunQuarter(1, existing, context.ActionCap, orders, cities, DemandModel, fleet);

        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void DecideOrders_ProducesLegalOrders_WhenRunThroughQuarterEngine()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, fleet, cash: Money.FromDollars(10_000_000));

        var orders = Strategy.DecideOrders(context);
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), context.ActionCap, orders, cities, DemandModel, fleet, availableCash: Money.FromDollars(10_000_000));

        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void DecideOrders_NewPair_NothingIdle_CashComfortable_OrdersInsteadOfLeasing()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 10, Cost: Money.FromDollars(1_000_000));

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, cash: Money.FromDollars(2_000_000)); // 2x RapidExpansion's own comfort multiplier

        var orders = Strategy.DecideOrders(context);

        var order = Assert.Single(orders.OfType<AircraftOrder>());
        Assert.Equal("t", order.Type.Id);
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_NewPair_NothingIdle_CashNotComfortable_LeasesInstead()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 10, Cost: Money.FromDollars(1_000_000));

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica); // no idle fleet, no cash passed

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<AircraftOrder>());
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>());
        Assert.Equal("t", lease.Type.Id);
        Assert.Single(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_GrowExisting_NothingIdleAndNoNewPairAvailable_CashComfortable_OrdersInsteadOfLeasing()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near }; // no third city - new-pair step finds nothing, falls through to grow-existing
        var aircraft = new AircraftType("t", "Test", Seats: 10, Cost: Money.FromDollars(1_000_000));
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, cash: Money.FromDollars(2_000_000));

        var orders = Strategy.DecideOrders(context);

        var order = Assert.Single(orders.OfType<AircraftOrder>());
        Assert.Equal("t", order.Type.Id);
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_GrowExisting_NothingIdleAndNoNewPairAvailable_CashNotComfortable_LeasesInstead()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 10, Cost: Money.FromDollars(1_000_000));
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<AircraftOrder>());
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>());
        Assert.Equal("t", lease.Type.Id);
        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "near" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_GrowExisting_IdleUnitAvailable_UsesItDirectly()
    {
        // Step 2's own success path (idleUnit is not null) was never
        // exercised - every prior grow-existing test had nothing idle.
        // Only two cities, so step 1 finds no unserved pair and leaves
        // reservedUnitIds empty, letting step 2's idle unit through.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned); // idle, not assigned

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "near" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
        Assert.Empty(orders.OfType<AircraftOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_MultipleIdleAircraftTypes_PicksTheOneThatCanReachTheBestPair()
    {
        // Step 1's idle-unit lookup filters `u.Type.Id == aircraft.Id`
        // across every idle unit, not just ones of the winning type -
        // needs a genuinely mixed idle fleet to exercise both outcomes of
        // that equality check.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var far = MakeCity("far", Region.NorthAmerica, 40, 40, 50); // long distance
        var cities = new[] { home, far };
        var shortRange = new AircraftType("short", "Short", Seats: 140, RangeKm: 100); // can't reach "far"
        var longRange = new AircraftType("long", "Long", Seats: 140, RangeKm: 10_000);
        var withShort = FleetLedger.ApplyDelivery(AirlineFleet.Empty, shortRange, quantity: 1, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.ApplyDelivery(withShort, longRange, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { shortRange, longRange }, Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal("long", routeOrder.Aircraft.Id);
    }

    [Fact]
    public void DecideOrders_GrowExisting_MixedIdleFleet_SkipsUnitsOfTheWrongType()
    {
        // Step 2's own idle-unit lookup filters `u.Type.Id ==
        // bestExisting.Aircraft.Id` the same way step 1's does - needs a
        // genuinely mixed idle fleet (not just the single matching-type
        // unit every other grow-existing test uses) to exercise the
        // "wrong type, skip it" outcome, not just "matching type, take it".
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near }; // no unserved pair - step 1 reserves nothing
        var routeType = new AircraftType("t", "Test", Seats: 140);
        var otherType = new AircraftType("other", "Other", Seats: 140);
        var existing = new[] { new Route(home, near, routeType, AircraftCount: 1) };
        var withOther = FleetLedger.ApplyDelivery(AirlineFleet.Empty, otherType, quantity: 1, FleetOwnershipMode.Owned); // wrong type - must be skipped
        var fleet = FleetLedger.ApplyDelivery(withOther, routeType, quantity: 1, FleetOwnershipMode.Owned); // matching type - must be taken

        var context = MakeContext(existing, cities, new[] { routeType, otherType }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal("t", routeOrder.Aircraft.Id);
        Assert.Empty(orders.OfType<AircraftOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_CashShortOfBid_CrossRegionTarget_CeilingHasRoom_Borrows()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var foreignGateway = MakeCity("fgw", Region.Europe, 30, 30, 90, isGateway: true);
        var cities = new[] { home, foreignGateway };

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, cities, DemandModel, Array.Empty<AircraftType>(), AirlineFleet.Empty, Region.NorthAmerica,
            AvailableCash: Money.FromDollars(500_000), CurrentDebtBalance: Money.Zero);

        var orders = Strategy.DecideOrders(context);

        var refinance = Assert.Single(orders.OfType<RefinanceOrder>());
        Assert.Equal(Money.FromDollars(1_500_000), refinance.AdditionalBorrowing);
        Assert.Empty(orders.OfType<NegotiationOrder>());
    }
}
