using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class EraAvailabilityTests
{
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, false, null);

    private static readonly GravityDemandModel DemandModel = new();

    [Fact]
    public void QuarterEngine_RouteOrder_FutureAircraft_IsRejected()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var futureAircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1995);
        var order = new RouteOrder(a, b, futureAircraft, 3);

        // Quarter 1 = 1955 (Timeline.StartYear) - decades before this aircraft exists.
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, DemandModel);

        Assert.Single(result.RejectedOrders);
        Assert.Empty(result.Routes);
    }

    [Fact]
    public void QuarterEngine_RouteOrder_AircraftAvailableThatYear_IsApplied()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var futureAircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1995);
        var order = new RouteOrder(a, b, futureAircraft, 3);

        // Quarter 161 = 1955 + 160/4 = 1995: the aircraft's debut year.
        var result = QuarterEngine.RunQuarter(161, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, DemandModel);

        Assert.Empty(result.RejectedOrders);
        Assert.Single(result.Routes);
    }

    [Fact]
    public void QuarterEngine_AircraftOrder_FutureType_IsRejected()
    {
        var futureAircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 2000);
        var order = new AircraftOrder(futureAircraft, Quantity: 1);
        var a = MakeCity("a", 0, 0, 50);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { (QuarterOrder)order }, new[] { a }, DemandModel);

        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void QuarterEngine_LeaseAircraftOrder_FutureType_IsRejected()
    {
        var futureAircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 2000);
        var order = new LeaseAircraftOrder(futureAircraft, Quantity: 1);
        var a = MakeCity("a", 0, 0, 50);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { (QuarterOrder)order }, new[] { a }, DemandModel);

        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void QuarterEngine_NonAircraftOrders_AreNeverEraGated()
    {
        var gateway = new City("gw", "Gateway", Region.EastAsia, 0, 0, 50, true, 1);
        var order = new NegotiationOrder(gateway, AeroNext.Core.Money.FromDollars(1));

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { (QuarterOrder)order }, new[] { gateway }, DemandModel);

        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void BriefingGenerator_EraRejection_ReportedDistinctlyFromRangeAndBudget()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var futureAircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1995);
        var order = new RouteOrder(a, b, futureAircraft, 3);

        var quarter = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, DemandModel);
        var briefing = BriefingGenerator.Generate(null, quarter);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.OrderRejectedAircraftNotYetAvailable);
        Assert.DoesNotContain(briefing, i => i.Kind == ExceptionKind.OrderRejectedOutOfRange);
    }

    [Fact]
    public void BriefingGenerator_AircraftOrder_EraRejection_IsReported()
    {
        var a = MakeCity("a", 0, 0, 50);
        var futureAircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 2000);
        var order = new AircraftOrder(futureAircraft, Quantity: 1);

        var quarter = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { (QuarterOrder)order }, new[] { a }, DemandModel);
        var briefing = BriefingGenerator.Generate(null, quarter);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.OrderRejectedAircraftNotYetAvailable);
    }

    [Fact]
    public void BriefingGenerator_LeaseAircraftOrder_EraRejection_IsReported()
    {
        var a = MakeCity("a", 0, 0, 50);
        var futureAircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 2000);
        var order = new LeaseAircraftOrder(futureAircraft, Quantity: 1);

        var quarter = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { (QuarterOrder)order }, new[] { a }, DemandModel);
        var briefing = BriefingGenerator.Generate(null, quarter);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.OrderRejectedAircraftNotYetAvailable);
        Assert.DoesNotContain(briefing, i => i.Kind == ExceptionKind.OrderRejectedOverBudget);
    }

    [Fact]
    public void QuarterEngine_RangeCheckedBeforeEraCheck_BothFail_StillJustOneRejection()
    {
        // An aircraft that is both out of range AND not yet available -
        // must not be counted twice or crash on double-rejection logic.
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 40, 40, 50);
        var futureShortRange = new AircraftType("t", "Test", Seats: 100, RangeKm: 100, AvailableFromYear: 2000);
        var order = new RouteOrder(a, b, futureShortRange, 3);

        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a, b }, DemandModel);

        Assert.Single(result.RejectedOrders);
    }
}
