using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class AircraftRangeTests
{
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.Asia, lat, lon, weight, false, null);

    private static readonly GravityDemandModel DemandModel = new();

    [Fact]
    public void Route_IsWithinRange_TrueWhenDistanceUnderAircraftRange()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50); // ~157 km apart
        var aircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 1000);

        Assert.True(new Route(a, b, aircraft, 3).IsWithinRange);
    }

    [Fact]
    public void Route_IsWithinRange_FalseWhenDistanceExceedsAircraftRange()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 40, 40, 50); // thousands of km apart
        var aircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 100);

        Assert.False(new Route(a, b, aircraft, 3).IsWithinRange);
    }

    [Fact]
    public void RouteOrder_IsWithinRange_MatchesRouteEquivalent()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 40, 40, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 100);

        var order = new RouteOrder(a, b, aircraft, 3);

        Assert.False(order.IsWithinRange);
        Assert.Equal(new Route(a, b, aircraft, 3).IsWithinRange, order.IsWithinRange);
    }

    [Fact]
    public void QuarterEngine_OutOfRangeRouteOrder_IsRejected_NotApplied()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 40, 40, 50);
        var shortRange = new AircraftType("t", "Test", Seats: 100, RangeKm: 100);
        var order = new RouteOrder(a, b, shortRange, 3);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, DemandModel);

        Assert.Single(result.RejectedOrders);
        Assert.Empty(result.Routes);
    }

    [Fact]
    public void QuarterEngine_OutOfRangeOrder_DoesNotConsumeBudget_LaterOrderStillApplies()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 40, 40, 50);
        var c = MakeCity("c", 0.1, 0.1, 50); // ~15 km from a — comfortably within a short range
        var shortRange = new AircraftType("t", "Test", Seats: 100, RangeKm: 100);
        var orders = new QuarterOrder[]
        {
            new RouteOrder(a, b, shortRange, 3), // out of range, rejected
            new RouteOrder(a, c, shortRange, 3), // in range, should still apply
        };

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { a, b, c }, DemandModel);

        Assert.Single(result.Routes);
        Assert.Equal(("a", "c"), (result.Routes[0].Origin.Id, result.Routes[0].Destination.Id));
    }

    [Fact]
    public void BriefingGenerator_OutOfRangeRejection_ReportedDistinctlyFromBudgetRejection()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 40, 40, 50);
        var shortRange = new AircraftType("t", "Test", Seats: 100, RangeKm: 100);
        var order = new RouteOrder(a, b, shortRange, 3);

        var quarter = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, DemandModel);
        var briefing = BriefingGenerator.Generate(null, quarter);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.OrderRejectedOutOfRange);
        Assert.DoesNotContain(briefing, i => i.Kind == ExceptionKind.OrderRejectedOverBudget);
    }

    [Fact]
    public void NetworkPlanner_OutOfRangeRoute_IsExcludedFromItineraries_ButStillReportedWithZeroUsage()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 40, 40, 50);
        var shortRange = new AircraftType("t", "Test", Seats: 100, RangeKm: 100);
        // Constructed directly, bypassing QuarterEngine's own rejection - NetworkPlanner
        // must independently refuse to treat this as a flyable leg.
        var route = new Route(a, b, shortRange, 3);

        var plan = NetworkPlanner.Plan(new[] { route }, new[] { a, b }, DemandModel);

        Assert.DoesNotContain(plan.Itineraries, i => i.Itinerary.Kind != ItineraryKind.Unserved);
        var usage = plan.RouteUsage.Single();
        Assert.Equal(0, usage.SeatsUsed);
    }

    [Fact]
    public void NetworkPlanner_ChokepointMandatory_WithShortRangeAircraft_ThenBypassed_WithLongRangeAircraft()
    {
        // The M11 acceptance scenario: origin and destination are too far
        // apart for a short-range aircraft to fly nonstop, but each leg via
        // an intermediate hub is short enough - so the short-range era
        // needs the hub stop, while a longer-range aircraft can fly direct.
        // Ids prefixed so "aaa_origin" sorts before "zzz_dest" alphabetically,
        // matching NetworkPlanner's own city ordering, so the itinerary
        // lookup below doesn't need to guess which side is Origin.
        var origin = MakeCity("aaa_origin", 0, 0, 50);
        var hub = MakeCity("mmm_hub", 0, 20, 50);          // ~2,225 km from origin
        var destination = MakeCity("zzz_dest", 0, 45, 50); // ~2,780 km from hub, ~5,004 km from origin direct
        var cities = new[] { origin, hub, destination };

        var shortRange = new AircraftType("early", "Early-Era Aircraft", Seats: 100, RangeKm: 3000);
        var longRange = new AircraftType("jet", "Jet-Era Aircraft", Seats: 140, RangeKm: 6000);

        // Early era: short-range aircraft can cover each leg but not direct.
        var earlyRoutes = new[]
        {
            new Route(origin, hub, shortRange, 3),
            new Route(hub, destination, shortRange, 3),
        };
        var earlyPlan = NetworkPlanner.Plan(earlyRoutes, cities, DemandModel);
        var earlyItinerary = earlyPlan.Itineraries.Single(i => i.Itinerary.Origin.Id == "aaa_origin" && i.Itinerary.Destination.Id == "zzz_dest");
        Assert.Equal(ItineraryKind.OneStop, earlyItinerary.Itinerary.Kind);

        // Jet era: same origin/destination, but now a long-range aircraft flies it direct.
        var jetEraRoutes = new[] { new Route(origin, destination, longRange, 3) };
        var jetEraPlan = NetworkPlanner.Plan(jetEraRoutes, cities, DemandModel);
        var jetEraItinerary = jetEraPlan.Itineraries.Single(i => i.Itinerary.Origin.Id == "aaa_origin" && i.Itinerary.Destination.Id == "zzz_dest");
        Assert.Equal(ItineraryKind.Nonstop, jetEraItinerary.Itinerary.Kind);
    }
}
