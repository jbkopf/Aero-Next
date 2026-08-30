using AeroNext.Core.Airlines;
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
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 3);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.Routes);
        Assert.Equal(("a", "b"), (result.Routes[0].Origin.Id, result.Routes[0].Destination.Id));
    }

    [Fact]
    public void RunQuarter_OrderForExistingPair_ReplacesRatherThanDuplicates()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, WeeklyRoundTrips: 1) };
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 7);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel);

        Assert.Single(result.Routes);
        Assert.Equal(7, result.Routes[0].WeeklyRoundTrips);
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
            new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 3),
            new RouteOrder(a, c, Aircraft, WeeklyRoundTrips: 2),
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
        var routes = new[] { new Route(a, b, Aircraft, WeeklyRoundTrips: 3) };

        var first = QuarterEngine.RunQuarter(1, routes, QuarterEngine.BaseActionCap, Array.Empty<QuarterOrder>(), new[] { a, b }, demandModel);
        var second = QuarterEngine.RunQuarter(1, routes, QuarterEngine.BaseActionCap, Array.Empty<QuarterOrder>(), new[] { a, b }, demandModel);

        Assert.Equal(first.Plan.TotalCarried, second.Plan.TotalCarried);
    }

    [Fact]
    public void Cost_NewPair_CostsOneAction()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 3);

        Assert.Equal(1, QuarterEngine.Cost(order, Array.Empty<Route>()));
    }

    [Fact]
    public void Cost_ExistingPair_IsFree()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, WeeklyRoundTrips: 1) };
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 7);

        Assert.Equal(0, QuarterEngine.Cost(order, existing));
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
        var existing = new[] { new Route(a, c, Aircraft, WeeklyRoundTrips: 1) }; // a-c exists, not a-b
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 3);

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
        var orders = cities.Skip(1).Select(spoke => (QuarterOrder)new RouteOrder(hub, spoke, Aircraft, 3)).ToArray();

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, demandModel);

        Assert.Equal(3, result.AppliedOrders.Count);
        Assert.Single(result.RejectedOrders);
        Assert.Equal(3, result.Routes.Count);
    }

    [Fact]
    public void RunQuarter_FreeOrderAfterRejection_StillApplies()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var d = MakeCity("d", 3, 3, 50);
        var existing = new[] { new Route(a, b, Aircraft, WeeklyRoundTrips: 1) };

        // Cap is 1: first two cost-1 orders eat the whole budget between them
        // (the second is rejected), but the free frequency change on a-b
        // should still go through even though it's listed after a rejection.
        var orders = new QuarterOrder[]
        {
            new RouteOrder(a, c, Aircraft, WeeklyRoundTrips: 3), // costs 1, fits
            new RouteOrder(a, d, Aircraft, WeeklyRoundTrips: 3), // costs 1, rejected (cap exhausted)
            new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 9), // existing pair, costs 0, should still apply
        };

        var result = QuarterEngine.RunQuarter(1, existing, currentActionCap: 1, orders, new[] { a, b, c, d }, demandModel);

        Assert.Contains(result.Routes, r => r.Origin.Id == "a" && r.Destination.Id == "b" && r.WeeklyRoundTrips == 9);
        Assert.DoesNotContain(result.Routes, r => r.Origin.Id == "a" && r.Destination.Id == "d");
    }

    [Fact]
    public void RunQuarter_ExecutiveHire_RaisesActionCap_ForResultOnly()
    {
        var demandModel = new GravityDemandModel();
        var cities = new[] { MakeCity("a", 0, 0, 50), MakeCity("b", 1, 1, 50) };
        var orders = new QuarterOrder[] { new HireExecutiveOrder(CapIncrease: 2) };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, demandModel);

        Assert.Equal(QuarterEngine.BaseActionCap + 2, result.ActionCap);
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
        orders.AddRange(cities.Skip(1).Select(spoke => (QuarterOrder)new RouteOrder(hub, spoke, Aircraft, 3)));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, demandModel);

        Assert.Equal(3, result.Routes.Count); // still only 3 new routes, not 4
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
            new RouteOrder(a, b, Aircraft, 3),
            new RouteOrder(a, c, Aircraft, 2),
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
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 3);

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
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 3);

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
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 3);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        Assert.Single(result.AppliedOrders);
        Assert.NotNull(result.UpdatedFleet);
        Assert.Empty(result.UpdatedFleet!.Idle); // the one unit is now assigned
        Assert.Equal("a-b", result.UpdatedFleet.Units.Single().AssignedRouteKey);
    }

    [Fact]
    public void RunQuarter_ExistingRoute_SameAircraftType_FrequencyBump_NoFleetCheckNeeded()
    {
        // A frequency-only change on an already-served pair with the same
        // aircraft type doesn't need a new unit at all - must apply even
        // with an empty fleet, since nothing new is being assigned.
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var existing = new[] { new Route(a, b, Aircraft, WeeklyRoundTrips: 1) };
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 7);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, AirlineFleet.Empty);

        Assert.Single(result.AppliedOrders);
        Assert.Equal(7, result.Routes.Single().WeeklyRoundTrips);
    }

    [Fact]
    public void RunQuarter_ExistingRoute_AircraftTypeSwap_FreesOldUnit_AssignsNew()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var otherAircraft = new AircraftType("t2", "Test2", Seats: 100);
        var existing = new[] { new Route(a, b, Aircraft, WeeklyRoundTrips: 1) };
        var order = new RouteOrder(a, b, otherAircraft, WeeklyRoundTrips: 1);

        // Seed a fleet with the OLD unit already assigned to a-b, plus one
        // idle unit of the new type.
        var withOld = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var oldAssigned = FleetLedger.Assign(withOld, withOld.Units.Single().Id, "a-b");
        var fleet = FleetLedger.ApplyDelivery(oldAssigned, otherAircraft, quantity: 1, FleetOwnershipMode.Owned);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, demandModel, fleet);

        Assert.Single(result.AppliedOrders);
        var oldUnit = result.UpdatedFleet!.Units.Single(u => u.Type == Aircraft);
        var newUnit = result.UpdatedFleet.Units.Single(u => u.Type == otherAircraft);
        Assert.Null(oldUnit.AssignedRouteKey); // freed
        Assert.Equal("a-b", newUnit.AssignedRouteKey); // now backs the route
    }

    [Fact]
    public void RunQuarter_LeaseOrderEarlierInBatch_MakesUnitAvailableToLaterRouteOrder()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var orders = new QuarterOrder[]
        {
            new LeaseAircraftOrder(Aircraft, Quantity: 1),
            new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 3),
        };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), currentActionCap: 3, orders, new[] { a, b }, demandModel, AirlineFleet.Empty);

        Assert.Equal(2, result.AppliedOrders.Count);
        Assert.Empty(result.RejectedOrders);
        Assert.Single(result.Routes);
        Assert.Equal(FleetOwnershipMode.Leased, result.UpdatedFleet!.Units.Single().Mode);
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
        var existing = new[] { new Route(a, c, Aircraft, WeeklyRoundTrips: 1) }; // a-c exists, not a-b
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 3);

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
        var existing = new[] { new Route(x, y, Aircraft, WeeklyRoundTrips: 1) };
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 3);

        var result = QuarterEngine.RunQuarter(2, existing, QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b, x, y }, demandModel, AirlineFleet.Empty);

        Assert.Single(result.RejectedOrders); // no idle unit -> rejected, treated as a genuinely new pair
    }

    [Fact]
    public void RunQuarter_MultipleIdleUnitsOfSameType_AssignsLowestIdDeterministically()
    {
        var demandModel = new GravityDemandModel();
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var order = new RouteOrder(a, b, Aircraft, WeeklyRoundTrips: 3);
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
}
