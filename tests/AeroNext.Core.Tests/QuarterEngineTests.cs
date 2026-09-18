using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class QuarterEngineTests
{
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, false, null);

    private static readonly AircraftType Aircraft = new("t", "Test", Seats: 100);

    [Fact]
    public void RunQuarter_WithNoOrdersAndNoRoutes_ProducesEmptyNetwork()
    {
        var demandModel = new GravityDemandModel();
        var cities = new[] { MakeCity("a", 0, 0, 50), MakeCity("b", 1, 1, 50) };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, Array.Empty<QuarterOrder>(), cities, demandModel);

        Assert.Empty(result.Routes);
        Assert.Equal(0, result.Plan.TotalCarried);
    }

    [Fact]
    public void RunQuarter_AppliesNewOrder_AddsRoute()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new RouteOrder(a, b, Aircraft);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.Routes);
        Assert.Equal(("a", "b"), (result.Routes[0].Origin.Id, result.Routes[0].Destination.Id));
    }

    [Fact]
    public void RunQuarter_OrderForExistingPair_AddsAnotherAircraft_NotReplaces()
    {
        // M32: a RouteOrder on an already-served pair (same type) means
        // "assign one more aircraft" - it grows AircraftCount, it doesn't
        // overwrite whatever was there. Still one Route per pair, not a
        // duplicate entry.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new RouteOrder(a, b, Aircraft);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.Routes);
        Assert.Equal(2, result.Routes[0].AircraftCount);
    }

    [Fact]
    public void RunQuarter_MultipleOrdersInOneBatch_AllApply_WhenWithinBudget()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var orders = new[]
        {
            new RouteOrder(a, b, Aircraft),
            new RouteOrder(a, c, Aircraft),
        };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { a, b, c }, demandModel);

        Assert.Equal(2, result.Routes.Count);
        Assert.Equal(2, result.AppliedOrders.Count);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_UnchangedInputs_ProducesIdenticalResult()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var routes = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };

        var first = QuarterEngine.RunQuarter(1, routes, QuarterEngine.BaseActionCap, Array.Empty<QuarterOrder>(), new[] { a, b }, demandModel);
        var second = QuarterEngine.RunQuarter(1, routes, QuarterEngine.BaseActionCap, Array.Empty<QuarterOrder>(), new[] { a, b }, demandModel);

        Assert.Equal(first.Plan.TotalCarried, second.Plan.TotalCarried);
    }

    [Fact]
    public void Cost_NewPair_CostsOneAction()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new RouteOrder(a, b, Aircraft);

        Assert.Equal(1, QuarterEngine.Cost(order, Array.Empty<Route>()));
    }

    [Fact]
    public void Cost_ExistingPair_AlsoCostsOneAction()
    {
        // M32: growing an already-served pair with another aircraft costs
        // the same as opening a brand-new one - no more free variant.
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new RouteOrder(a, b, Aircraft);

        Assert.Equal(1, QuarterEngine.Cost(order, existing));
    }

    [Fact]
    public void Cost_PairWithMatchingOriginButDifferentDestination_CostsOneAction()
    {
        // Guards the && in the existing-pair check: a route sharing only
        // the origin must not be mistaken for the pair actually being
        // ordered.
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var existing = new[] { new Route(a, c, Aircraft, AircraftCount: 1) }; // a-c exists, not a-b
        var order = new RouteOrder(a, b, Aircraft);

        Assert.Equal(1, QuarterEngine.Cost(order, existing));
    }

    [Fact]
    public void Cost_HireExecutive_IsAlwaysFree()
    {
        Assert.Equal(0, QuarterEngine.Cost(new HireExecutiveOrder(), Array.Empty<Route>()));
    }

    [Fact]
    public void RunQuarter_OverBudgetOrder_IsRejected_NotApplied()
    {
        var demandModel = new GravityDemandModel();
        var cities = Enumerable.Range(0, 5).Select(i => MakeCity($"c{i}", i, i, 50)).ToList();
        var hub = cities[0];
        // 4 new-pair orders against a cap of 3 — the 4th shouldn't fit.
        var orders = cities.Skip(1).Select(spoke => (QuarterOrder)new RouteOrder(hub, spoke, Aircraft)).ToArray();

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, demandModel);

        Assert.Equal(3, result.AppliedOrders.Count);
        Assert.Single(result.RejectedOrders);
        Assert.Equal(3, result.Routes.Count);
    }

    [Fact]
    public void RunQuarter_FreeOrderAfterRejection_StillApplies()
    {
        // Cap is 1: the first cost-1 order eats the whole budget, the
        // second is rejected - but a free private action listed after the
        // rejection should still go through. M32: RouteOrder no longer has
        // a free variant at all (that was the old "existing pair" case),
        // so this now exercises "scanning continues past a rejection" with
        // a genuinely-free order type instead.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var d = MakeCity("d", 3, 3, 50);

        var orders = new QuarterOrder[]
        {
            new RouteOrder(a, c, Aircraft), // costs 1, fits
            new RouteOrder(a, d, Aircraft), // costs 1, rejected (cap exhausted)
            new HireExecutiveOrder(), // free, should still apply
        };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), currentActionCap: 1, orders, new[] { a, b, c, d }, demandModel);

        Assert.Contains(result.AppliedOrders, o => o is HireExecutiveOrder);
        Assert.DoesNotContain(result.Routes, r => r.Origin.Id == "a" && r.Destination.Id == "d");
    }

    [Fact]
    public void RunQuarter_ExecutiveHire_RaisesActionCap_ForResultOnly()
    {
        var demandModel = new GravityDemandModel();
        var cities = new[] { MakeCity("a", 0, 0, 50), MakeCity("b", 1, 1, 50) };
        var orders = new QuarterOrder[] { new HireExecutiveOrder() };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, demandModel);

        Assert.Equal(QuarterEngine.BaseActionCap + QuarterEngine.ActionCapPerExecutive, result.ActionCap);
    }

    [Fact]
    public void RunQuarter_ExecutiveHire_DoesNotIncreaseSameQuartersBudget()
    {
        var demandModel = new GravityDemandModel();
        var cities = Enumerable.Range(0, 5).Select(i => MakeCity($"c{i}", i, i, 50)).ToList();
        var hub = cities[0];

        // Hire (free) plus 4 new-pair orders in the same quarter, cap 3.
        // The hire shouldn't unlock a 4th route order in this same quarter —
        // the cap only rises starting next quarter.
        var orders = new List<QuarterOrder> { new HireExecutiveOrder() };
        orders.AddRange(cities.Skip(1).Select(spoke => (QuarterOrder)new RouteOrder(hub, spoke, Aircraft)));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, demandModel);

        Assert.Equal(3, result.Routes.Count); // still only 3 new routes, not 4
    }

    // --- M49: executives cost real money and are capped ---

    [Fact]
    public void RunQuarter_HireExecutive_AtHeadcountCap_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var cities = new[] { MakeCity("a", 0, 0, 50) };
        var order = new HireExecutiveOrder();

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.MaxActionCap, new[] { order }, cities, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
        Assert.Equal(QuarterEngine.MaxActionCap, result.ActionCap); // never grows past the cap
    }

    [Fact]
    public void RunQuarter_HireExecutive_BelowHeadcountCap_Accepted()
    {
        var demandModel = new GravityDemandModel();
        var cities = new[] { MakeCity("a", 0, 0, 50) };
        var order = new HireExecutiveOrder();

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.MaxActionCap - 1, new[] { order }, cities, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_MultipleHiresInOneBatch_CappedAtHeadcountRegardlessOfOrder()
    {
        // Starting one executive below the cap - only one of two
        // HireExecutiveOrders in the same batch can fit.
        var demandModel = new GravityDemandModel();
        var cities = new[] { MakeCity("a", 0, 0, 50) };
        var orders = new QuarterOrder[] { new HireExecutiveOrder(), new HireExecutiveOrder() };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.MaxActionCap - 1, orders, cities, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
        Assert.Equal(QuarterEngine.MaxActionCap, result.ActionCap);
    }

    [Fact]
    public void RunQuarter_HireExecutive_Unaffordable_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var cities = new[] { MakeCity("a", 0, 0, 50) };
        var order = new HireExecutiveOrder();

        var result = QuarterEngine.RunQuarter(
            1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, cities, demandModel,
            availableCash: Money.Zero);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_HireExecutive_Affordable_Accepted()
    {
        var demandModel = new GravityDemandModel();
        var cities = new[] { MakeCity("a", 0, 0, 50) };
        var order = new HireExecutiveOrder();

        var result = QuarterEngine.RunQuarter(
            1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, cities, demandModel,
            availableCash: Money.FromDollars(10_000_000));

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_HireExecutive_CostFreeWhenAvailableCashIsNull()
    {
        var demandModel = new GravityDemandModel();
        var cities = new[] { MakeCity("a", 0, 0, 50) };
        var order = new HireExecutiveOrder();

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, cities, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_ReportsQuarterNumber()
    {
        var demandModel = new GravityDemandModel();
        var cities = new[] { MakeCity("a", 0, 0, 50), MakeCity("b", 1, 1, 50) };

        var result = QuarterEngine.RunQuarter(7, Array.Empty<Route>(), QuarterEngine.BaseActionCap, Array.Empty<QuarterOrder>(), cities, demandModel);

        Assert.Equal(7, result.QuarterNumber);
    }

    private sealed record UnknownOrder : QuarterOrder;

    [Fact]
    public void Cost_UnknownOrderType_ThrowsNotSupportedException()
    {
        // A future order type that forgets to update Cost() should fail
        // loudly rather than silently be treated as free or crash the
        // budget check some other way.
        Assert.Throws<NotSupportedException>(() => QuarterEngine.Cost(new UnknownOrder(), Array.Empty<Route>()));
    }

    [Fact]
    public void RunQuarter_IsDeterministic_RegardlessOfOrderOrCityInputOrder()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var orders = new QuarterOrder[]
        {
            new RouteOrder(a, b, Aircraft),
            new RouteOrder(a, c, Aircraft),
        };

        var original = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { a, b, c }, demandModel);
        var shuffled = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders.Reverse().ToArray(), new[] { c, b, a }, demandModel);

        Assert.Equal(
            original.Routes.Select(r => (r.Origin.Id, r.Destination.Id, r.WeeklyRoundTrips)),
            shuffled.Routes.Select(r => (r.Origin.Id, r.Destination.Id, r.WeeklyRoundTrips)));
        Assert.Equal(original.Plan.TotalCarried, shuffled.Plan.TotalCarried);
    }

    // --- Fleet-aware RouteOrder ownership checks ---

    [Fact]
    public void RunQuarter_NoFleetPassed_NewRoute_StillAppliesWithNoOwnershipCheck()
    {
        // The default (fleet: null) preserves today's fully permissive
        // behavior — the 44 pre-existing call sites across this codebase
        // never set up a fleet and must keep working unmodified.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new RouteOrder(a, b, Aircraft);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Null(result.UpdatedFleet);
    }

    [Fact]
    public void RunQuarter_FleetPassed_NewRoute_NoIdleUnitOfType_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new RouteOrder(a, b, Aircraft);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, AirlineFleet.Empty);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
        Assert.Empty(result.Routes);
    }

    [Fact]
    public void RunQuarter_FleetPassed_NewRoute_IdleUnitAvailable_AppliesAndConsumesUnit()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new RouteOrder(a, b, Aircraft);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        Assert.Single(result.AppliedOrders);
        Assert.NotNull(result.UpdatedFleet);
        Assert.Empty(result.UpdatedFleet!.Idle); // the one unit is now assigned
        Assert.Equal("a-b", result.UpdatedFleet.Units.Single().AssignedRouteKey);
    }

    [Fact]
    public void RunQuarter_ExistingRoute_SameAircraftType_GrowingItStillNeedsAFreshIdleUnit()
    {
        // M32: unlike the old free frequency-bump, every accepted
        // RouteOrder — including one growing an already-served pair with
        // the SAME type — needs one more idle unit. An empty fleet means
        // it's rejected, not silently exempted.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new RouteOrder(a, b, Aircraft);

        var rejected = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, AirlineFleet.Empty);
        Assert.Single(rejected.RejectedOrders);

        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var accepted = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);
        Assert.Single(accepted.AppliedOrders);
        Assert.Equal(2, accepted.Routes.Single().AircraftCount);
    }

    [Fact]
    public void RunQuarter_ExistingRoute_AircraftTypeMismatch_RejectedOutright_UseAdjustRouteOrderInstead()
    {
        // M32: RouteOrder itself still means "assign one more of this
        // exact type" - a different type than what's already flying the
        // pair is rejected outright, even with an idle unit of the new
        // type sitting there ready to go. Swapping type is a real,
        // supported operation now, just via AdjustRouteOrder instead (see
        // the AdjustRouteOrder tests below) - this guard on RouteOrder
        // itself is unchanged and still correct.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var otherAircraft = new AircraftType("t2", "Test2", Seats: 100);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new RouteOrder(a, b, otherAircraft);

        // Seed a fleet with the OLD unit already assigned to a-b, plus one
        // idle unit of the new type.
        var withOld = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var oldAssigned = FleetLedger.Assign(withOld, withOld.Units.Single().Id, "a-b");
        var fleet = FleetLedger.ApplyDelivery(oldAssigned, otherAircraft, quantity: 1, FleetOwnershipMode.Owned);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        Assert.Single(result.RejectedOrders);
        Assert.Empty(result.AppliedOrders);
        var oldUnit = result.UpdatedFleet!.Units.Single(u => u.Type == Aircraft);
        var newUnit = result.UpdatedFleet.Units.Single(u => u.Type == otherAircraft);
        Assert.Equal("a-b", oldUnit.AssignedRouteKey); // untouched — still backing the route
        Assert.Null(newUnit.AssignedRouteKey); // never consumed
    }

    [Fact]
    public void RunQuarter_LeaseOrderEarlierInBatch_NoLongerMakesUnitAvailableToLaterRouteOrder()
    {
        // Aircraft-acquisition-limits follow-up (docs/AIRCRAFT_ACQUISITION_ROADMAP.md):
        // leasing is two-phase now, like purchasing - LeaseAircraftOrder
        // itself is accepted (era + Quantity checks only) but no longer
        // mutates the fleet inline, so a RouteOrder later in the *same*
        // batch still has no idle unit to use and is rejected. This is
        // the deliberate, confirmed behavior change from the prior
        // version of this test (which asserted the old inline-delivery
        // shape) - see LeaseAllocationResolver for where the unit
        // actually gets delivered, one quarter later.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var orders = new QuarterOrder[]
        {
            new LeaseAircraftOrder(Aircraft, Quantity: 1),
            new RouteOrder(a, b, Aircraft),
        };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), currentActionCap: 3, orders, new[] { a, b }, demandModel, AirlineFleet.Empty);

        Assert.Single(result.AppliedOrders);
        Assert.IsType<LeaseAircraftOrder>(result.AppliedOrders.Single());
        Assert.Single(result.RejectedOrders);
        Assert.IsType<RouteOrder>(result.RejectedOrders.Single());
        Assert.Empty(result.Routes);
        Assert.Empty(result.UpdatedFleet!.Units); // the lease order itself no longer creates a fleet unit
    }

    [Fact]
    public void RunQuarter_FleetAware_RouteSharingOnlyOrigin_NotMistakenForExistingPair_NeedsIdleUnit()
    {
        // Mirrors Cost_PairWithMatchingOriginButDifferentDestination, but
        // through the fleet-aware path: a route sharing only the origin
        // (a-c exists, not a-b) must still be treated as a brand-new pair
        // needing its own idle unit, not silently matched to a-c's.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var existing = new[] { new Route(a, c, Aircraft, AircraftCount: 1) }; // a-c exists, not a-b
        var order = new RouteOrder(a, b, Aircraft);

        var rejected = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b, c }, demandModel, AirlineFleet.Empty);
        Assert.Single(rejected.RejectedOrders); // no idle unit -> rejected, not silently treated as free

        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var accepted = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b, c }, demandModel, fleet);
        Assert.Single(accepted.AppliedOrders);
    }

    [Fact]
    public void RunQuarter_FleetAware_ExistingRouteWithWhollyDifferentOrigin_ShortCircuitsOriginCheck_StillNeedsIdleUnit()
    {
        // A route sharing neither origin nor destination (x-y exists, order
        // is for a-b) must short-circuit on the origin mismatch alone -
        // distinct from the same-origin-different-destination case above,
        // which only exercises the && after origin already matched.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var x = MakeCity("x", 5, 5, 50);
        var y = MakeCity("y", 6, 6, 50);
        var existing = new[] { new Route(x, y, Aircraft, AircraftCount: 1) };
        var order = new RouteOrder(a, b, Aircraft);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b, x, y }, demandModel, AirlineFleet.Empty);

        Assert.Single(result.RejectedOrders); // no idle unit -> rejected, treated as a genuinely new pair
    }

    [Fact]
    public void RunQuarter_TwoRouteOrders_SamePair_SameQuarterBatch_BothApply_AircraftCountAddsUp()
    {
        // M32: nothing stops submitting two RouteOrders for the same pair
        // in one quarter (e.g. two idle units free at once) - each still
        // costs its own action, and both add to AircraftCount.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var orders = new[] { new RouteOrder(a, b, Aircraft), new RouteOrder(a, b, Aircraft) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 2, FleetOwnershipMode.Owned);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { a, b }, demandModel, fleet);

        Assert.Equal(2, result.AppliedOrders.Count);
        Assert.Empty(result.RejectedOrders);
        Assert.Equal(2, result.Routes.Single().AircraftCount);
        Assert.Empty(result.UpdatedFleet!.Idle);
    }

    [Fact]
    public void RunQuarter_TwoRouteOrders_SamePair_DifferentTypes_SameQuarterBatch_SecondRejected()
    {
        // M32: type-consistency has to be checked against what THIS batch
        // has already committed, not just quarter-start state - otherwise
        // a second order for the same pair with a different type would
        // consume a fleet unit that ApplyRouteOrders then silently drops
        // (only the first type would end up backing the final Route).
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var otherAircraft = new AircraftType("t2", "Test2", Seats: 100);
        var orders = new QuarterOrder[] { new RouteOrder(a, b, Aircraft), new RouteOrder(a, b, otherAircraft) };
        var fleet = FleetLedger.ApplyDelivery(
            FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned),
            otherAircraft, quantity: 1, FleetOwnershipMode.Owned);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { a, b }, demandModel, fleet);

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
        var route = Assert.Single(result.Routes);
        Assert.Equal(Aircraft.Id, route.Aircraft.Id);
        Assert.Equal(1, route.AircraftCount);
        // The otherAircraft unit was never consumed - still idle.
        Assert.Contains(result.UpdatedFleet!.Idle, u => u.Type == otherAircraft);
    }

    [Fact]
    public void RunQuarter_MultipleIdleUnitsOfSameType_AssignsLowestIdDeterministically()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new RouteOrder(a, b, Aircraft);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 3, FleetOwnershipMode.Owned);
        var lowestId = fleet.Units.Min(u => u.Id);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        var assignedUnit = result.UpdatedFleet!.Units.Single(u => u.AssignedRouteKey is not null);
        Assert.Equal(lowestId, assignedUnit.Id);
        Assert.Equal(2, result.UpdatedFleet.Idle.Count());
    }

    // --- M26: aircraft purchase affordability and RetireAircraftOrder ---

    private static readonly AircraftType CostlyAircraft = new("costly", "Costly", Seats: 100, Cost: Money.FromDollars(1_000_000));

    [Fact]
    public void RunQuarter_NoCashPassed_AircraftOrderNeverRejectedForCost()
    {
        var demandModel = new GravityDemandModel();
        var order = new AircraftOrder(CostlyAircraft, Quantity: 1);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_AircraftOrderExceedingAvailableCash_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var order = new AircraftOrder(CostlyAircraft, Quantity: 1);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel, availableCash: Money.FromDollars(999_999));

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_AircraftOrderExactlyMatchingAvailableCash_Applied()
    {
        var demandModel = new GravityDemandModel();
        var order = new AircraftOrder(CostlyAircraft, Quantity: 1);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel, availableCash: Money.FromDollars(1_000_000));

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_TwoAircraftOrders_SecondExceedsWhatFirstLeftOfAvailableCash_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var orders = new QuarterOrder[]
        {
            new AircraftOrder(CostlyAircraft, Quantity: 1),
            new AircraftOrder(CostlyAircraft, Quantity: 1),
        };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, Array.Empty<City>(), demandModel, availableCash: Money.FromDollars(1_500_000));

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    // --- Aircraft-acquisition-limits follow-up: purchase/lease windows and Quantity validation ---

    [Fact]
    public void RunQuarter_AircraftOrder_ZeroQuantity_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var order = new AircraftOrder(Aircraft, Quantity: 0);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_AircraftOrder_NegativeQuantity_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var order = new AircraftOrder(Aircraft, Quantity: -1);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_LeaseAircraftOrder_ZeroQuantity_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var order = new LeaseAircraftOrder(Aircraft, Quantity: 0);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_LeaseAircraftOrder_NegativeQuantity_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var order = new LeaseAircraftOrder(Aircraft, Quantity: -5);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_AircraftOrder_PastEndOfManufactureYear_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var retiredType = new AircraftType("retired", "Retired", Seats: 100, AvailableFromYear: 1970, EndOfManufactureYear: 1985);
        var order = new AircraftOrder(retiredType, Quantity: 1);

        // Quarter 125 -> year 1955 + (125-1)/4 = 1986, one year past EndOfManufactureYear.
        var result = QuarterEngine.RunQuarter(125, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_AircraftOrder_AtEndOfManufactureYear_StillAccepted()
    {
        var demandModel = new GravityDemandModel();
        var retiredType = new AircraftType("retired", "Retired", Seats: 100, AvailableFromYear: 1970, EndOfManufactureYear: 1985);
        var order = new AircraftOrder(retiredType, Quantity: 1);

        // Quarter 121 -> year 1985, the last purchasable year.
        var result = QuarterEngine.RunQuarter(121, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_LeaseAircraftOrder_WithinTwoYearDelayAfterRelease_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var newType = new AircraftType("new", "New", Seats: 100, AvailableFromYear: 1970);
        var order = new LeaseAircraftOrder(newType, Quantity: 1);

        // Quarter 61 -> year 1970, purchasable but not yet leasable.
        var result = QuarterEngine.RunQuarter(61, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_LeaseAircraftOrder_AfterTwoYearDelay_Accepted()
    {
        var demandModel = new GravityDemandModel();
        var newType = new AircraftType("new", "New", Seats: 100, AvailableFromYear: 1970);
        var order = new LeaseAircraftOrder(newType, Quantity: 1);

        // Quarter 69 -> year 1972 (1970 + 2), the first leasable year.
        var result = QuarterEngine.RunQuarter(69, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_LeaseAircraftOrder_PastFiveYearExtensionAfterManufactureEnds_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var retiredType = new AircraftType("retired", "Retired", Seats: 100, AvailableFromYear: 1970, EndOfManufactureYear: 1985);
        var order = new LeaseAircraftOrder(retiredType, Quantity: 1);

        // Quarter 145 -> year 1991, one year past the 1985 + 5 lease window.
        var result = QuarterEngine.RunQuarter(145, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_LeaseAircraftOrder_AtFiveYearExtensionAfterManufactureEnds_StillAccepted()
    {
        var demandModel = new GravityDemandModel();
        var retiredType = new AircraftType("retired", "Retired", Seats: 100, AvailableFromYear: 1970, EndOfManufactureYear: 1985);
        var order = new LeaseAircraftOrder(retiredType, Quantity: 1);

        // Quarter 141 -> year 1990 (1985 + 5), the last leasable year.
        var result = QuarterEngine.RunQuarter(141, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RouteOrder_UnaffectedByEndOfManufactureYear_StillUsesPlainIsAvailable()
    {
        // RouteOrder assigns an aircraft to fly, it doesn't acquire one -
        // deliberately still gated by Timeline.IsAvailable's own floor-only
        // check, never the new acquisition-specific windows.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var retiredType = new AircraftType("retired", "Retired", Seats: 100, AvailableFromYear: 1970, EndOfManufactureYear: 1985);
        var order = new RouteOrder(a, b, retiredType);

        // Quarter 145 -> year 1991, well past both new acquisition windows,
        // but RouteOrder only ever cared about AvailableFromYear.
        var result = QuarterEngine.RunQuarter(145, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_AdjustRouteOrder_UnaffectedByEndOfManufactureYear_StillUsesPlainIsAvailable()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var retiredType = new AircraftType("retired", "Retired", Seats: 100, AvailableFromYear: 1970, EndOfManufactureYear: 1985);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new AdjustRouteOrder(a, b, retiredType);

        var result = QuarterEngine.RunQuarter(145, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RetireAircraftOrder_NoFleetTracked_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var order = new RetireAircraftOrder(UnitId: 1);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
        Assert.Empty(result.RetiredUnits!);
    }

    [Fact]
    public void RunQuarter_RetireAircraftOrder_UnknownUnitId_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var order = new RetireAircraftOrder(UnitId: 999);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel, fleet);

        Assert.Single(result.RejectedOrders);
        Assert.Single(result.UpdatedFleet!.Units); // untouched
    }

    [Fact]
    public void RunQuarter_RetireAircraftOrder_UnitAssignedToRoute_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var withUnit = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.Assign(withUnit, withUnit.Units.Single().Id, "a-b");
        var order = new RetireAircraftOrder(fleet.Units.Single().Id);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel, fleet);

        Assert.Single(result.RejectedOrders);
        Assert.Single(result.UpdatedFleet!.Units); // still there, still assigned
        Assert.Empty(result.RetiredUnits!);
    }

    [Fact]
    public void RunQuarter_RetireAircraftOrder_IdleUnit_Succeeds_AndAppearsInRetiredUnits()
    {
        var demandModel = new GravityDemandModel();
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var unit = fleet.Units.Single();
        var order = new RetireAircraftOrder(unit.Id);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel, fleet);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
        Assert.Empty(result.UpdatedFleet!.Units);
        var retired = Assert.Single(result.RetiredUnits!);
        Assert.Equal(unit.Id, retired.Id);
        Assert.Equal(unit.Type, retired.Type);
        Assert.Equal(unit.Mode, retired.Mode);
    }

    [Fact]
    public void RunQuarter_RetireAircraftOrder_IsFreeAction()
    {
        Assert.Equal(0, QuarterEngine.Cost(new RetireAircraftOrder(UnitId: 1), Array.Empty<Route>()));
    }

    // --- M33: gateway slot gating ---

    private static City MakeGateway(string id, double lat, double lon, int weight, int gateCapacity) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, true, gateCapacity);

    [Fact]
    public void RunQuarter_NoSlotsPassed_NeverGatesOnGateScarcity()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var gateway = MakeGateway("gw", 1, 1, 50, gateCapacity: 1);
        var order = new RouteOrder(a, gateway, Aircraft);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, gateway }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_GatewayWithZeroAvailableSlots_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var gateway = MakeGateway("gw", 1, 1, 50, gateCapacity: 1);
        var order = new RouteOrder(a, gateway, Aircraft);
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 0 };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, gateway }, demandModel, availableSlotsByCity: availableSlotsByCity);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_GatewayWithOneAvailableSlot_FirstOrderSucceeds_SecondInSameBatchRejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 2, 2, 50);
        var gateway = MakeGateway("gw", 1, 1, 50, gateCapacity: 5);
        var orders = new[] { new RouteOrder(a, gateway, Aircraft), new RouteOrder(b, gateway, Aircraft) };
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 1 };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { a, b, gateway }, demandModel, availableSlotsByCity: availableSlotsByCity);

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_NonGatewayCity_NeverGated_RegardlessOfAvailableSlotsByCityContents()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50); // GateCapacity: null - not a gateway
        var order = new RouteOrder(a, b, Aircraft);
        var availableSlotsByCity = new Dictionary<string, int>(); // empty - "b" never appears, would read as 0 if it were checked

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, availableSlotsByCity: availableSlotsByCity);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_BothEndpointsAreGateways_EachIndependentlyGates()
    {
        var demandModel = new GravityDemandModel();
        var gateA = MakeGateway("ga", 0, 0, 50, gateCapacity: 5);
        var gateB = MakeGateway("gb", 1, 1, 50, gateCapacity: 5);
        var order = new RouteOrder(gateA, gateB, Aircraft);
        // Slot available at ga but not at gb - order must still be rejected.
        var availableSlotsByCity = new Dictionary<string, int> { ["ga"] = 1, ["gb"] = 0 };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateA, gateB }, demandModel, availableSlotsByCity: availableSlotsByCity);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    // --- M45: non-gateway city-wide automatic capacity gating ---

    private static City MakeAutoCapacityCity(string id, double lat, double lon, int weight, int autoCapacity) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, false, null, autoCapacity);

    [Fact]
    public void RunQuarter_NoCityWideSlotsPassed_NeverGatesOnAutoCapacity()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var busy = MakeAutoCapacityCity("bz", 1, 1, 50, autoCapacity: 1);
        var order = new RouteOrder(a, busy, Aircraft);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, busy }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_AutoCapacityCityWithZeroRemaining_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var busy = MakeAutoCapacityCity("bz", 1, 1, 50, autoCapacity: 1);
        var order = new RouteOrder(a, busy, Aircraft);
        var cityWideSlotsRemaining = new Dictionary<string, int> { ["bz"] = 0 };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, busy }, demandModel, cityWideSlotsRemaining: cityWideSlotsRemaining);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_AutoCapacityCityWithOneRemaining_FirstOrderSucceeds_SecondInSameBatchRejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 2, 2, 50);
        var busy = MakeAutoCapacityCity("bz", 1, 1, 50, autoCapacity: 5);
        var orders = new[] { new RouteOrder(a, busy, Aircraft), new RouteOrder(b, busy, Aircraft) };
        var cityWideSlotsRemaining = new Dictionary<string, int> { ["bz"] = 1 };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { a, b, busy }, demandModel, cityWideSlotsRemaining: cityWideSlotsRemaining);

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_PlainCity_NeverGatedByAutoCapacity_RegardlessOfCityWideSlotsRemainingContents()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50); // AutoCapacity: null - unmodeled
        var order = new RouteOrder(a, b, Aircraft);
        var cityWideSlotsRemaining = new Dictionary<string, int>(); // empty - "b" never appears, would read as 0 if it were checked

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, cityWideSlotsRemaining: cityWideSlotsRemaining);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_BothEndpointsAreAutoCapacityCities_EachIndependentlyGates()
    {
        var demandModel = new GravityDemandModel();
        var busyA = MakeAutoCapacityCity("ba", 0, 0, 50, autoCapacity: 5);
        var busyB = MakeAutoCapacityCity("bb", 1, 1, 50, autoCapacity: 5);
        var order = new RouteOrder(busyA, busyB, Aircraft);
        // Slot available at ba but not at bb - order must still be rejected.
        var cityWideSlotsRemaining = new Dictionary<string, int> { ["ba"] = 1, ["bb"] = 0 };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { busyA, busyB }, demandModel, cityWideSlotsRemaining: cityWideSlotsRemaining);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_GatewaySlotsAndAutoCapacitySlots_BothEnforcedIndependently()
    {
        // A route between a gateway and an AutoCapacity city is gated by
        // both mechanisms at once - each endpoint's own rule applies,
        // never one overriding the other.
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var busy = MakeAutoCapacityCity("bz", 1, 1, 50, autoCapacity: 5);
        var order = new RouteOrder(gateway, busy, Aircraft);
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 1 };
        var cityWideSlotsRemaining = new Dictionary<string, int> { ["bz"] = 0 };

        var result = QuarterEngine.RunQuarter(
            1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateway, busy }, demandModel,
            availableSlotsByCity: availableSlotsByCity, cityWideSlotsRemaining: cityWideSlotsRemaining);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    // --- M47: marketing campaigns ---

    [Fact]
    public void RunQuarter_RouteMarketingOrder_UnservedPair_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new RouteMarketingOrder(a, b); // no route between a and b

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RouteMarketingOrder_AirlineServesADifferentPair_StillRejected()
    {
        // A real route exists, but to a different destination - the
        // Origin-matches/Destination-doesn't case, distinct from "no
        // routes at all".
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var existing = new[] { new Route(a, c, Aircraft, AircraftCount: 1) }; // a-c, not a-b
        var order = new RouteMarketingOrder(a, b);

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b, c }, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RouteMarketingOrder_ExistingRouteWithWhollyDifferentOrigin_ShortCircuitsOriginCheck_Rejected()
    {
        // A route sharing neither origin nor destination (x-y exists,
        // targeting a-b) must short-circuit on the origin mismatch alone
        // - distinct from the same-origin-different-destination case
        // above (SetFareOrder's own equivalent test guards the same &&).
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var x = MakeCity("x", 5, 5, 50);
        var y = MakeCity("y", 6, 6, 50);
        var existing = new[] { new Route(x, y, Aircraft, AircraftCount: 1) };
        var order = new RouteMarketingOrder(a, b);

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b, x, y }, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RouteMarketingOrder_ServedPair_Accepted()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new RouteMarketingOrder(a, b);

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_NetworkMarketingOrder_NoExistingRoutesNeeded_Accepted()
    {
        var demandModel = new GravityDemandModel();
        var order = new NetworkMarketingOrder();

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_NetworkAndRouteMarketing_SameBatch_RouteFirst_NetworkRejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var orders = new QuarterOrder[] { new RouteMarketingOrder(a, b), new NetworkMarketingOrder() };

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, orders, new[] { a, b }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.IsType<RouteMarketingOrder>(result.AppliedOrders.Single());
        Assert.Single(result.RejectedOrders);
        Assert.IsType<NetworkMarketingOrder>(result.RejectedOrders.Single());
    }

    [Fact]
    public void RunQuarter_NetworkAndRouteMarketing_SameBatch_NetworkFirst_RouteRejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var orders = new QuarterOrder[] { new NetworkMarketingOrder(), new RouteMarketingOrder(a, b) };

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, orders, new[] { a, b }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.IsType<NetworkMarketingOrder>(result.AppliedOrders.Single());
        Assert.Single(result.RejectedOrders);
        Assert.IsType<RouteMarketingOrder>(result.RejectedOrders.Single());
    }

    [Fact]
    public void RunQuarter_ThreeDistinctRouteMarketingOrders_AllAccepted()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var d = MakeCity("d", 3, 3, 50);
        var existing = new[]
        {
            new Route(a, b, Aircraft, AircraftCount: 1),
            new Route(a, c, Aircraft, AircraftCount: 1),
            new Route(a, d, Aircraft, AircraftCount: 1),
        };
        var orders = new QuarterOrder[] { new RouteMarketingOrder(a, b), new RouteMarketingOrder(a, c), new RouteMarketingOrder(a, d) };

        var result = QuarterEngine.RunQuarter(1, existing, currentActionCap: 3, orders, new[] { a, b, c, d }, demandModel);

        Assert.Equal(3, result.AppliedOrders.Count);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_FourthRouteMarketingOrder_Rejected_CapIsThreeRegardlessOfActionCap()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var d = MakeCity("d", 3, 3, 50);
        var e = MakeCity("e", 4, 4, 50);
        var existing = new[]
        {
            new Route(a, b, Aircraft, AircraftCount: 1),
            new Route(a, c, Aircraft, AircraftCount: 1),
            new Route(a, d, Aircraft, AircraftCount: 1),
            new Route(a, e, Aircraft, AircraftCount: 1),
        };
        var orders = new QuarterOrder[]
        {
            new RouteMarketingOrder(a, b), new RouteMarketingOrder(a, c), new RouteMarketingOrder(a, d), new RouteMarketingOrder(a, e),
        };

        // Action cap raised past 3 (e.g. via HireExecutiveOrder growth over
        // the campaign) - the concurrent-campaign cap is still exactly 3,
        // independent of ActionCap.
        var result = QuarterEngine.RunQuarter(1, existing, currentActionCap: 10, orders, new[] { a, b, c, d, e }, demandModel);

        Assert.Equal(3, result.AppliedOrders.Count);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_DuplicateRouteMarketingOrder_SamePairTwice_SecondRejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var orders = new QuarterOrder[] { new RouteMarketingOrder(a, b), new RouteMarketingOrder(a, b) };

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, orders, new[] { a, b }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_NetworkMarketingOrder_Unaffordable_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) }; // 1 route -> costs $100,000
        var order = new NetworkMarketingOrder();

        var result = QuarterEngine.RunQuarter(
            1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel,
            availableCash: Money.FromDollars(50_000)); // less than the $100,000 cost

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_NetworkMarketingOrder_Affordable_Accepted()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new NetworkMarketingOrder();

        var result = QuarterEngine.RunQuarter(
            1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel,
            availableCash: Money.FromDollars(10_000_000));

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RouteMarketingOrder_Affordable_Accepted()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 5);
        var b = MakeCity("b", 1, 1, 5); // light weights -> modest demand -> modest, affordable cost
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new RouteMarketingOrder(a, b);

        var result = QuarterEngine.RunQuarter(
            1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel,
            availableCash: Money.FromDollars(10_000_000));

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RouteMarketingOrder_Unaffordable_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 500);
        var b = MakeCity("b", 1, 1, 500); // heavy weights -> real, substantial demand -> real, substantial cost
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new RouteMarketingOrder(a, b);

        var result = QuarterEngine.RunQuarter(
            1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel,
            availableCash: Money.Zero);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_MarketingOrders_CostFreeWhenAvailableCashIsNull()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 500);
        var b = MakeCity("b", 1, 1, 500);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new RouteMarketingOrder(a, b);

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    // --- M27: closing a route (detaching one aircraft at a time) ---

    [Fact]
    public void RunQuarter_CloseRouteOrder_NoFleetTracked_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new CloseRouteOrder(a, b);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_CloseRouteOrder_AirlineDoesNotOperateThePair_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var order = new CloseRouteOrder(a, b);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_CloseRouteOrder_RouteSharingOnlyOrigin_NotMistakenForThePairBeingClosed_Rejected()
    {
        // Guards the && inside the operatesRoute lookup: a route sharing
        // only the origin (a-c exists, closing a-b) must not be treated
        // as though the airline operates a-b.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var withUnit = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.Assign(withUnit, withUnit.Units.Single().Id, "a-c");
        var existing = new[] { new Route(a, c, Aircraft, AircraftCount: 1) }; // a-c exists, not a-b
        var order = new CloseRouteOrder(a, b);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b, c }, demandModel, fleet);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_CloseRouteOrder_ExistingRouteWithWhollyDifferentOrigin_ShortCircuitsOriginCheck_Rejected()
    {
        // A route sharing neither origin nor destination (x-y exists,
        // closing a-b) must short-circuit on the origin mismatch alone -
        // distinct from the same-origin-different-destination case above.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var x = MakeCity("x", 5, 5, 50);
        var y = MakeCity("y", 6, 6, 50);
        var withUnit = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.Assign(withUnit, withUnit.Units.Single().Id, "x-y");
        var existing = new[] { new Route(x, y, Aircraft, AircraftCount: 1) };
        var order = new CloseRouteOrder(a, b);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b, x, y }, demandModel, fleet);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_CloseRouteOrder_RouteExistsButNoUnitActuallyAssignedToIt_Rejected()
    {
        // Defensive guard for an inconsistent fleet/route state that
        // shouldn't arise in practice (currentRoutes says this pair is
        // operated, but no fleet unit is actually assigned to it) - must
        // reject rather than throw or silently no-op.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var fleet = AirlineFleet.Empty; // no units at all, despite currentRoutes below claiming a-b is flown
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new CloseRouteOrder(a, b);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_CloseRouteOrder_SingleAircraftRoute_RemovesRouteEntirely_FreesTheUnit()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var withUnit = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.Assign(withUnit, withUnit.Units.Single().Id, "a-b");
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new CloseRouteOrder(a, b);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
        Assert.Empty(result.Routes); // gone entirely, not left as a zero-capacity entry
        var freedUnit = result.UpdatedFleet!.Units.Single();
        Assert.Null(freedUnit.AssignedRouteKey);
        Assert.Contains(freedUnit, result.UpdatedFleet.Idle);
    }

    [Fact]
    public void RunQuarter_CloseRouteOrder_MultiAircraftRoute_ReducesCountByOne_RouteStays()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var withUnits = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 3, FleetOwnershipMode.Owned);
        var fleet = withUnits.Units.Aggregate(withUnits, (f, u) => FleetLedger.Assign(f, u.Id, "a-b"));
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 3) };
        var order = new CloseRouteOrder(a, b);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        Assert.Single(result.AppliedOrders);
        var route = Assert.Single(result.Routes);
        Assert.Equal(2, route.AircraftCount);
        Assert.Single(result.UpdatedFleet!.Idle);
    }

    [Fact]
    public void RunQuarter_CloseRouteOrder_PreservesTheFreedUnitsOwnershipMode()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var withUnit = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Leased);
        var fleet = FleetLedger.Assign(withUnit, withUnit.Units.Single().Id, "a-b");
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new CloseRouteOrder(a, b);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        Assert.Equal(FleetOwnershipMode.Leased, result.UpdatedFleet!.Units.Single().Mode);
    }

    [Fact]
    public void RunQuarter_TwoCloseRouteOrders_SamePair_SameBatch_EachFreesADifferentUnit()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var withUnits = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 2, FleetOwnershipMode.Owned);
        var fleet = withUnits.Units.Aggregate(withUnits, (f, u) => FleetLedger.Assign(f, u.Id, "a-b"));
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 2) };
        var orders = new[] { new CloseRouteOrder(a, b), new CloseRouteOrder(a, b) };

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, orders, new[] { a, b }, demandModel, fleet);

        Assert.Equal(2, result.AppliedOrders.Count);
        Assert.Empty(result.RejectedOrders);
        Assert.Empty(result.Routes);
        Assert.Equal(2, result.UpdatedFleet!.Idle.Count()); // both units freed, not the same one twice
    }

    [Fact]
    public void RunQuarter_CloseRouteOrder_IsFreeAction()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);

        Assert.Equal(0, QuarterEngine.Cost(new CloseRouteOrder(a, b), Array.Empty<Route>()));
    }

    [Fact]
    public void RunQuarter_CloseThenReopenSamePairSameBatch_UsesTheJustFreedUnit()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var withUnit = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.Assign(withUnit, withUnit.Units.Single().Id, "a-b");
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var orders = new QuarterOrder[] { new CloseRouteOrder(a, b), new RouteOrder(a, c, Aircraft) };

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, orders, new[] { a, b, c }, demandModel, fleet);

        Assert.Equal(2, result.AppliedOrders.Count);
        Assert.Empty(result.RejectedOrders);
        Assert.DoesNotContain(result.Routes, r => r.Destination.Id == "b");
        Assert.Contains(result.Routes, r => r.Destination.Id == "c");
        Assert.Equal("a-c", result.UpdatedFleet!.Units.Single().AssignedRouteKey);
    }

    // --- M28: NegotiationOrder costs real cash, RelinquishSlotOrder ---

    [Fact]
    public void RunQuarter_NoCashPassed_NegotiationOrderNeverRejectedForCost()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var order = new NegotiationOrder(gateway, Money.FromDollars(2_000_000));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateway }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_NegotiationOrderExceedingAvailableCash_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var order = new NegotiationOrder(gateway, Money.FromDollars(2_000_000));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateway }, demandModel, availableCash: Money.FromDollars(1_999_999));

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_NegotiationOrderExactlyMatchingAvailableCash_Applied()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var order = new NegotiationOrder(gateway, Money.FromDollars(2_000_000));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateway }, demandModel, availableCash: Money.FromDollars(2_000_000));

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_TwoNegotiationOrders_SecondExceedsWhatFirstLeftOfAvailableCash_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var otherGateway = MakeGateway("gw2", 1, 1, 50, gateCapacity: 5);
        var orders = new QuarterOrder[]
        {
            new NegotiationOrder(gateway, Money.FromDollars(2_000_000)),
            new NegotiationOrder(otherGateway, Money.FromDollars(2_000_000)),
        };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { gateway, otherGateway }, demandModel, availableCash: Money.FromDollars(3_000_000));

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    // --- M59: no duplicate pending bid at the same city ---

    [Fact]
    public void RunQuarter_NoPendingNegotiationCityIdsPassed_NeverRejectedForDuplicate()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var order = new NegotiationOrder(gateway, Money.FromDollars(2_000_000));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateway }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_NegotiationOrderTargetsCityAlreadyPending_Rejected_CashLeftUntouched()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var order = new NegotiationOrder(gateway, Money.FromDollars(2_000_000));
        var pendingCityIds = new HashSet<string> { "gw" };

        var result = QuarterEngine.RunQuarter(
            1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateway }, demandModel,
            availableCash: Money.FromDollars(2_000_000), pendingNegotiationCityIds: pendingCityIds);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_NegotiationOrderTargetsCityAlreadyPending_RejectedBeforeCashCheck_LeavesFullBudgetForOtherOrders()
    {
        // The real reason this has to be rejected before the cash gate:
        // if it were rejected after, the cash would already be spent for
        // nothing. Confirmed here indirectly - a second, real
        // NegotiationOrder at a DIFFERENT city still gets the full
        // availableCash, proving the duplicate never touched cashSpent.
        var demandModel = new GravityDemandModel();
        var duplicateTarget = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var freshTarget = MakeGateway("gw2", 1, 1, 50, gateCapacity: 5);
        var orders = new QuarterOrder[]
        {
            new NegotiationOrder(duplicateTarget, Money.FromDollars(2_000_000)),
            new NegotiationOrder(freshTarget, Money.FromDollars(2_000_000)),
        };
        var pendingCityIds = new HashSet<string> { "gw" };

        var result = QuarterEngine.RunQuarter(
            1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { duplicateTarget, freshTarget }, demandModel,
            availableCash: Money.FromDollars(2_000_000), pendingNegotiationCityIds: pendingCityIds);

        Assert.Single(result.AppliedOrders);
        Assert.Equal("gw2", Assert.IsType<NegotiationOrder>(result.AppliedOrders.Single()).TargetCity.Id);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_NegotiationOrderTargetsDifferentCity_NotBlockedByPendingBidElsewhere()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var order = new NegotiationOrder(gateway, Money.FromDollars(2_000_000));
        var pendingCityIds = new HashSet<string> { "some-other-city" };

        var result = QuarterEngine.RunQuarter(
            1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateway }, demandModel,
            availableCash: Money.FromDollars(2_000_000), pendingNegotiationCityIds: pendingCityIds);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RelinquishSlotOrder_NoSlotsPassed_NeverRejectedForScarcity()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var order = new RelinquishSlotOrder(gateway);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateway }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RelinquishSlotOrder_ZeroAvailableBalance_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var order = new RelinquishSlotOrder(gateway);
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 0 };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateway }, demandModel, availableSlotsByCity: availableSlotsByCity);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RelinquishSlotOrder_AbsentFromBalance_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var order = new RelinquishSlotOrder(gateway);
        var availableSlotsByCity = new Dictionary<string, int>(); // "gw" never appears - never held

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateway }, demandModel, availableSlotsByCity: availableSlotsByCity);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RelinquishSlotOrder_PositiveAvailableBalance_Applied()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var order = new RelinquishSlotOrder(gateway);
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 1 };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { gateway }, demandModel, availableSlotsByCity: availableSlotsByCity);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_TwoRelinquishSlotOrders_SameCity_SecondRejectedOnceBalanceExhausted()
    {
        var demandModel = new GravityDemandModel();
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var orders = new[] { new RelinquishSlotOrder(gateway), new RelinquishSlotOrder(gateway) };
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 1 };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { gateway }, demandModel, availableSlotsByCity: availableSlotsByCity);

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RelinquishAndRouteOrder_SameGateway_SameBatch_CompeteForTheLastSlot()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 2, 2, 50);
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);
        var orders = new QuarterOrder[] { new RelinquishSlotOrder(gateway), new RouteOrder(a, gateway, Aircraft) };
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 1 };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { a, gateway }, demandModel, availableSlotsByCity: availableSlotsByCity);

        Assert.Single(result.AppliedOrders); // the relinquish
        Assert.Single(result.RejectedOrders); // no slot left for the RouteOrder
    }

    [Fact]
    public void RunQuarter_RelinquishSlotOrder_IsFreeAction()
    {
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 5);

        Assert.Equal(0, QuarterEngine.Cost(new RelinquishSlotOrder(gateway), Array.Empty<Route>()));
    }

    // --- M29: manual debt paydown ---

    [Fact]
    public void RunQuarter_PayDownDebtOrder_NoLimitsPassed_NeverRejected()
    {
        var demandModel = new GravityDemandModel();
        var order = new PayDownDebtOrder(Money.FromDollars(10_000));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_PayDownDebtOrder_ExceedingAvailableCash_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var order = new PayDownDebtOrder(Money.FromDollars(10_000));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel, availableCash: Money.FromDollars(9_999));

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_PayDownDebtOrder_ExceedingCurrentDebtBalance_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var order = new PayDownDebtOrder(Money.FromDollars(10_000));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel, currentDebtBalance: Money.FromDollars(9_999));

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_PayDownDebtOrder_ExactlyMatchingCashAndDebtBalance_Applied()
    {
        var demandModel = new GravityDemandModel();
        var order = new PayDownDebtOrder(Money.FromDollars(10_000));

        var result = QuarterEngine.RunQuarter(
            1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel,
            availableCash: Money.FromDollars(10_000), currentDebtBalance: Money.FromDollars(10_000));

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_TwoPayDownDebtOrders_SecondExceedsWhatFirstLeftOfDebtBalance_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var orders = new QuarterOrder[]
        {
            new PayDownDebtOrder(Money.FromDollars(6_000)),
            new PayDownDebtOrder(Money.FromDollars(6_000)),
        };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, Array.Empty<City>(), demandModel, currentDebtBalance: Money.FromDollars(10_000));

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    // --- Bankruptcy-conditions follow-up: debt ceiling as a borrowing cap, not a bankruptcy trigger ---

    [Fact]
    public void RunQuarter_RefinanceOrder_NoLimitsPassed_NeverRejected()
    {
        var demandModel = new GravityDemandModel();
        var order = new RefinanceOrder(Money.FromDollars(FinancialResolver.DebtCeilingDollars * 10));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RefinanceOrder_ExceedingDebtCeiling_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var order = new RefinanceOrder(Money.FromDollars(10_000));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel, currentDebtBalance: Money.FromDollars(FinancialResolver.DebtCeilingDollars - 9_999));

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_RefinanceOrder_ExactlyMatchingRemainingCeilingHeadroom_Applied()
    {
        var demandModel = new GravityDemandModel();
        var order = new RefinanceOrder(Money.FromDollars(10_000));

        var result = QuarterEngine.RunQuarter(
            1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, Array.Empty<City>(), demandModel,
            currentDebtBalance: Money.FromDollars(FinancialResolver.DebtCeilingDollars - 10_000));

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_TwoRefinanceOrders_SecondExceedsWhatFirstLeftOfCeilingHeadroom_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var orders = new QuarterOrder[]
        {
            new RefinanceOrder(Money.FromDollars(6_000)),
            new RefinanceOrder(Money.FromDollars(6_000)),
        };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, Array.Empty<City>(), demandModel, currentDebtBalance: Money.FromDollars(FinancialResolver.DebtCeilingDollars - 10_000));

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_PayDownDebtOrder_IsFreeAction()
    {
        Assert.Equal(0, QuarterEngine.Cost(new PayDownDebtOrder(Money.FromDollars(1_000)), Array.Empty<Route>()));
    }

    // --- M34: fares (SetFareOrder, and preserving a customized fare when growing a route) ---

    [Fact]
    public void RunQuarter_SetFareOrder_AirlineOperatesThePair_Applied_OverridesTheFare()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new SetFareOrder(a, b, Money.FromDollars(500));

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
        Assert.Equal(Money.FromDollars(500), result.Routes.Single().FareOverride);
    }

    [Fact]
    public void RunQuarter_SetFareOrder_NoRoutesAtAll_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new SetFareOrder(a, b, Money.FromDollars(500));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_SetFareOrder_RouteSharingOnlyOrigin_NotMistakenForThePairBeingOperated_Rejected()
    {
        // Guards the && inside the operatesPair lookup: a route sharing
        // only the origin (a-c exists, setting a fare on a-b) must not be
        // treated as though the airline operates a-b.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var existing = new[] { new Route(a, c, Aircraft, AircraftCount: 1) }; // a-c exists, not a-b
        var order = new SetFareOrder(a, b, Money.FromDollars(500));

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b, c }, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_SetFareOrder_ExistingRouteWithWhollyDifferentOrigin_ShortCircuitsOriginCheck_Rejected()
    {
        // A route sharing neither origin nor destination (x-y exists,
        // setting a fare on a-b) must short-circuit on the origin
        // mismatch alone - distinct from the same-origin-different-
        // destination case above.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var x = MakeCity("x", 5, 5, 50);
        var y = MakeCity("y", 6, 6, 50);
        var existing = new[] { new Route(x, y, Aircraft, AircraftCount: 1) };
        var order = new SetFareOrder(a, b, Money.FromDollars(500));

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b, x, y }, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_SetFareOrder_NegativeFare_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new SetFareOrder(a, b, Money.FromDollars(-1));

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_SetFareOrder_IsFreeAction()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);

        Assert.Equal(0, QuarterEngine.Cost(new SetFareOrder(a, b, Money.FromDollars(500)), Array.Empty<Route>()));
    }

    [Fact]
    public void RunQuarter_RouteOrder_AssignsAnotherAircraftToAnAlreadyFareCustomizedRoute_PreservesTheCustomFare()
    {
        // A necessary fix, not incidental (see M34's roadmap writeup):
        // ApplyRouteOrders used to always construct a brand-new Route,
        // which would silently reset a customized fare back to the
        // computed default every time another aircraft joined the pair.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1, FareOverride: Money.FromDollars(500)) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var order = new RouteOrder(a, b, Aircraft);

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        var route = result.Routes.Single();
        Assert.Equal(2, route.AircraftCount);
        Assert.Equal(Money.FromDollars(500), route.FareOverride);
    }

    // M44: network contiguity - a new route may only originate from the
    // home city or a city the airline already serves.

    [Fact]
    public void RunQuarter_HomeCityNull_RouteFromAnyCity_NeverRejectedForOrigin()
    {
        // The permissive default: homeCity null preserves today's
        // behavior exactly, matching every other optional enforcement
        // parameter's own convention.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new RouteOrder(a, b, Aircraft);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Empty(result.RejectedOrders);
        Assert.Single(result.Routes);
    }

    [Fact]
    public void RunQuarter_HomeCityProvided_RouteFromANonServedNonHomeCity_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var home = MakeCity("home", 0, 0, 50);
        var elsewhere = MakeCity("elsewhere", 5, 5, 50);
        var far = MakeCity("far", 6, 6, 50);
        var order = new RouteOrder(elsewhere, far, Aircraft); // touches neither home nor any served city

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { home, elsewhere, far }, demandModel, homeCity: home);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
        Assert.Empty(result.Routes);
    }

    [Fact]
    public void RunQuarter_HomeCityProvided_RouteFromHome_AlwaysAccepted_EvenWithZeroExistingRoutes()
    {
        var demandModel = new GravityDemandModel();
        var home = MakeCity("home", 0, 0, 50);
        var elsewhere = MakeCity("elsewhere", 5, 5, 50);
        var order = new RouteOrder(home, elsewhere, Aircraft);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { home, elsewhere }, demandModel, homeCity: home);

        Assert.Empty(result.RejectedOrders);
        Assert.Single(result.Routes);
    }

    [Fact]
    public void RunQuarter_HomeCityProvided_RouteFromAnAlreadyServedCity_Accepted()
    {
        // Contiguity is evaluated against quarter-start routes - the
        // same "not other orders in the same batch" rule Cost() already
        // applies (see the class doc comment). A pair already served
        // trivially satisfies this: its own origin was already touched
        // before this quarter began.
        var demandModel = new GravityDemandModel();
        var home = MakeCity("home", 0, 0, 50);
        var served = MakeCity("served", 5, 5, 50);
        var newDestination = MakeCity("newdest", 6, 6, 50);
        var existing = new[] { new Route(home, served, Aircraft, AircraftCount: 1) };
        var order = new RouteOrder(served, newDestination, Aircraft); // origin is "served", not home

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { home, served, newDestination }, demandModel, homeCity: home);

        Assert.Empty(result.RejectedOrders);
        Assert.Equal(2, result.Routes.Count);
    }

    [Fact]
    public void RunQuarter_HomeCityProvided_NegotiationOrderNeverRejectedForOrigin()
    {
        // Scoped to RouteOrder only - a NegotiationOrder can still
        // target any gateway in the world, a real forward-looking
        // investment rather than a route.
        var demandModel = new GravityDemandModel();
        var home = MakeCity("home", 0, 0, 50);
        var farGateway = new City("gw", "GW", Region.EastAsia, 40, 40, 90, IsGateway: true, GateCapacity: 3);
        var order = new NegotiationOrder(farGateway, Money.FromDollars(2_000_000));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { home, farGateway }, demandModel, homeCity: home);

        Assert.Empty(result.RejectedOrders);
        Assert.Single(result.AppliedOrders);
    }

    // --- AdjustRouteOrder: changing an existing route's aircraft type ---

    private static readonly AircraftType OtherAircraft = new("t2", "Test2", Seats: 100);

    [Fact]
    public void RunQuarter_AdjustRouteOrder_UnservedPair_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new AdjustRouteOrder(a, b, OtherAircraft);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_AdjustRouteOrder_SameTypeAsCurrent_Rejected()
    {
        // Not a real type change — allowing it would be a strictly-worse
        // paid alternative to CloseRouteOrder's already-free shrink.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 3) };
        var order = new AdjustRouteOrder(a, b, Aircraft);

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
        Assert.Equal(3, result.Routes.Single().AircraftCount); // untouched
    }

    [Fact]
    public void RunQuarter_AdjustRouteOrder_TargetTypeOutOfRange_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 50, 50, 50); // ~7,800km apart
        var shortRange = new AircraftType("sr", "ShortRange", Seats: 100, RangeKm: 100);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new AdjustRouteOrder(a, b, shortRange);

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
        Assert.Equal(Aircraft, result.Routes.Single().Aircraft); // untouched
    }

    [Fact]
    public void RunQuarter_AdjustRouteOrder_TargetTypeNotYetAvailable_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var futureAircraft = new AircraftType("fa", "Future", Seats: 100, AvailableFromYear: 2050);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var order = new AdjustRouteOrder(a, b, futureAircraft);

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel); // quarter 1 -> year 1955

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_AdjustRouteOrder_NoIdleUnitOfTargetType_Rejected()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var withOld = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.Assign(withOld, withOld.Units.Single().Id, "a-b"); // no OtherAircraft units at all
        var order = new AdjustRouteOrder(a, b, OtherAircraft);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        Assert.Empty(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
        Assert.Equal("a-b", result.UpdatedFleet!.Units.Single().AssignedRouteKey); // untouched
    }

    [Fact]
    public void RunQuarter_AdjustRouteOrder_Applied_ResetsToOneOfTheNewType_FreesEveryOldUnit_ResetsFare()
    {
        // The reset-to-1 behavior is only really exercised by starting
        // from MORE than one aircraft - proves it's a real reset, not a
        // no-op that happens to look right at count 1.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 3, FareOverride: Money.FromDollars(999)) };
        var withThreeOld = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 3, FleetOwnershipMode.Owned);
        var oldIds = withThreeOld.Units.Select(u => u.Id).ToList();
        var assigned = oldIds.Aggregate(withThreeOld, (f, id) => FleetLedger.Assign(f, id, "a-b"));
        var fleet = FleetLedger.ApplyDelivery(assigned, OtherAircraft, quantity: 1, FleetOwnershipMode.Owned);
        var order = new AdjustRouteOrder(a, b, OtherAircraft);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
        Assert.Equal(1, QuarterEngine.Cost(order, existing));

        var route = result.Routes.Single();
        Assert.Equal(OtherAircraft, route.Aircraft);
        Assert.Equal(1, route.AircraftCount);
        Assert.Null(route.FareOverride); // reset, not carried over

        var oldUnits = result.UpdatedFleet!.Units.Where(u => u.Type == Aircraft).ToList();
        Assert.Equal(3, oldUnits.Count);
        Assert.All(oldUnits, u => Assert.Null(u.AssignedRouteKey)); // every old unit freed, none retired

        var newUnit = result.UpdatedFleet.Units.Single(u => u.Type == OtherAircraft);
        Assert.Equal("a-b", newUnit.AssignedRouteKey);
    }

    [Fact]
    public void RunQuarter_AdjustRouteOrder_PermissiveMode_NoFleetTracked_StillApplies()
    {
        // AdjustRouteOrder's own "unserved pair"/"same type" checks are
        // unconditional (unlike RouteOrder's own committedType check,
        // which is fleet-gated for historical reasons) - so it applies in
        // permissive mode too, resetting to 1 of the new type exactly as
        // in fleet-aware mode.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 4) };
        var order = new AdjustRouteOrder(a, b, OtherAircraft);

        var result = QuarterEngine.RunQuarter(1, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel); // no fleet passed

        Assert.Single(result.AppliedOrders);
        var route = result.Routes.Single();
        Assert.Equal(OtherAircraft, route.Aircraft);
        Assert.Equal(1, route.AircraftCount);
    }

    [Fact]
    public void RunQuarter_AdjustRouteOrder_AtAGatewayWithZeroSpareSlots_NeverRejected_NoSlotCheckApplies()
    {
        // The key regression guard: the route already holds 3 slots'
        // worth of usage at this gateway (currentRoutes' own AircraftCount
        // is already baked into availableSlotsByCity), and the swap can
        // only ever hold flat or free that usage (reset to 1, never more
        // than the old count) - so it must never be rejected for gate
        // scarcity, even with zero spare balance.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var gateway = MakeGateway("gw", 1, 1, 50, gateCapacity: 3);
        var existing = new[] { new Route(a, gateway, Aircraft, AircraftCount: 3) };
        var withThreeOld = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 3, FleetOwnershipMode.Owned);
        var oldIds = withThreeOld.Units.Select(u => u.Id).ToList();
        var assigned = oldIds.Aggregate(withThreeOld, (f, id) => FleetLedger.Assign(f, id, RouteKeyStringForTests(a, gateway)));
        var fleet = FleetLedger.ApplyDelivery(assigned, OtherAircraft, quantity: 1, FleetOwnershipMode.Owned);
        var order = new AdjustRouteOrder(a, gateway, OtherAircraft);
        var availableSlotsByCity = new Dictionary<string, int> { ["gw"] = 0 }; // fully spent

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, gateway }, demandModel, fleet, availableSlotsByCity: availableSlotsByCity);

        Assert.Single(result.AppliedOrders);
        Assert.Empty(result.RejectedOrders);
        Assert.Equal(1, result.Routes.Single().AircraftCount);
    }

    [Fact]
    public void RunQuarter_AdjustRouteOrder_FollowedByGrowthRouteOrders_SameQuarterSwapThenGrow_CostsExactlyFinalCount()
    {
        // "The player can then increase the number, even in the same
        // turn, using the existing cost structure" - one AdjustRouteOrder
        // (cost 1, -> 1 of the new type) followed by 2 ordinary
        // RouteOrders (cost 1 each) for the same pair/new type reaches a
        // final count of 3, total cost 3 - exactly BaseActionCap.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var withOld = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var oldAssigned = FleetLedger.Assign(withOld, withOld.Units.Single().Id, "a-b");
        var fleet = FleetLedger.ApplyDelivery(oldAssigned, OtherAircraft, quantity: 3, FleetOwnershipMode.Owned);
        var orders = new QuarterOrder[]
        {
            new AdjustRouteOrder(a, b, OtherAircraft),
            new RouteOrder(a, b, OtherAircraft),
            new RouteOrder(a, b, OtherAircraft),
        };

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, orders, new[] { a, b }, demandModel, fleet);

        Assert.Equal(3, result.AppliedOrders.Count);
        Assert.Empty(result.RejectedOrders);
        var route = result.Routes.Single();
        Assert.Equal(OtherAircraft, route.Aircraft);
        Assert.Equal(3, route.AircraftCount);
    }

    [Fact]
    public void RunQuarter_GrowthRouteOrderBeforeAdjustRouteOrder_WrongOrder_RouteOrderRejected_AdjustRouteOrderStillApplies()
    {
        // Submission order matters: a RouteOrder for the new type placed
        // BEFORE the AdjustRouteOrder in the same batch sees the pair's
        // still-old committed type (from currentRoutes) and is rejected
        // as a mismatch - the AdjustRouteOrder later in the same list is
        // unaffected by that rejection and still applies on its own.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, AircraftCount: 1) };
        var withOld = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var oldAssigned = FleetLedger.Assign(withOld, withOld.Units.Single().Id, "a-b");
        var fleet = FleetLedger.ApplyDelivery(oldAssigned, OtherAircraft, quantity: 1, FleetOwnershipMode.Owned);
        var orders = new QuarterOrder[]
        {
            new RouteOrder(a, b, OtherAircraft), // wrong order - type still committed to Aircraft here
            new AdjustRouteOrder(a, b, OtherAircraft),
        };

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, orders, new[] { a, b }, demandModel, fleet);

        Assert.Single(result.RejectedOrders);
        Assert.IsType<RouteOrder>(result.RejectedOrders.Single());
        Assert.Single(result.AppliedOrders);
        Assert.IsType<AdjustRouteOrder>(result.AppliedOrders.Single());
        var route = result.Routes.Single();
        Assert.Equal(OtherAircraft, route.Aircraft);
        Assert.Equal(1, route.AircraftCount); // the growth order never landed
    }

    private static string RouteKeyStringForTests(City origin, City destination) => $"{origin.Id}-{destination.Id}";
}
