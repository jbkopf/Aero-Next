using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class BriefingGeneratorTests
{
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.Asia, lat, lon, weight, false, null);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly AircraftType Aircraft = new("t", "Test", Seats: 100);

    [Fact]
    public void Generate_FirstQuarter_ReportsEveryRouteAsEntered()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var cities = new[] { a, b };
        var current = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { new RouteOrder(a, b, Aircraft, 3) }, cities, DemandModel);

        var briefing = BriefingGenerator.Generate(null, current);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.RouteEntered);
    }

    [Fact]
    public void Generate_UnchangedNetwork_ProducesNoItems()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var cities = new[] { a, b };

        var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { new RouteOrder(a, b, Aircraft, 3) }, cities, DemandModel);
        var quarter2 = QuarterEngine.RunQuarter(2, quarter1.Routes, quarter1.ActionCap, Array.Empty<QuarterOrder>(), cities, DemandModel);

        var briefing = BriefingGenerator.Generate(quarter1, quarter2);

        Assert.Empty(briefing);
    }

    [Fact]
    public void Generate_NewRouteAddedLaterQuarter_IsReportedAsEntered()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var cities = new[] { a, b, c };

        var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { new RouteOrder(a, b, Aircraft, 3) }, cities, DemandModel);
        var quarter2 = QuarterEngine.RunQuarter(2, quarter1.Routes, quarter1.ActionCap, new[] { new RouteOrder(a, c, Aircraft, 3) }, cities, DemandModel);

        var briefing = BriefingGenerator.Generate(quarter1, quarter2);

        var entered = Assert.Single(briefing);
        Assert.Equal(ExceptionKind.RouteEntered, entered.Kind);
        Assert.Contains("a-c", entered.Message);
    }

    [Fact]
    public void Generate_LoadFactorDrop_AboveThreshold_IsReported()
    {
        // Weighted so demand (~4,868/qtr) sits between quarter 1's capacity
        // (2,600 -> fully saturated) and quarter 2's (130,000 -> nowhere
        // near saturated), so the same fixed demand reads as a load factor
        // drop purely from adding capacity.
        var a = MakeCity("a", 0, 0, 4);
        var b = MakeCity("b", 1, 1, 4);
        var cities = new[] { a, b };

        var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { new RouteOrder(a, b, Aircraft, 1) }, cities, DemandModel);
        var quarter2 = QuarterEngine.RunQuarter(2, quarter1.Routes, quarter1.ActionCap, new[] { new RouteOrder(a, b, Aircraft, 50) }, cities, DemandModel);

        var briefing = BriefingGenerator.Generate(quarter1, quarter2);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.LoadFactorDropped);
    }

    [Fact]
    public void Generate_LoadFactorSurge_AboveThreshold_IsReported()
    {
        // Mirror image of the drop test: same fixed demand, capacity cut
        // instead of raised, so load factor rises sharply.
        var a = MakeCity("a", 0, 0, 4);
        var b = MakeCity("b", 1, 1, 4);
        var cities = new[] { a, b };

        var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { new RouteOrder(a, b, Aircraft, 50) }, cities, DemandModel);
        var quarter2 = QuarterEngine.RunQuarter(2, quarter1.Routes, quarter1.ActionCap, new[] { new RouteOrder(a, b, Aircraft, 1) }, cities, DemandModel);

        var briefing = BriefingGenerator.Generate(quarter1, quarter2);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.LoadFactorSurged);
    }

    [Fact]
    public void Generate_CapsAtFiveItems()
    {
        var cities = Enumerable.Range(0, 14).Select(i => MakeCity($"c{i}", i, i, 50)).ToList();
        var hub = cities[0];

        // 13 spokes off one hub, all entering in the same quarter — a high
        // action cap so every order is applied, isolating the briefing's
        // own 5-item cap from budget-driven rejections (covered separately
        // in QuarterEngineTests).
        var orders = cities.Skip(1).Select(spoke => (QuarterOrder)new RouteOrder(hub, spoke, Aircraft, 3)).ToArray();
        var quarter = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), currentActionCap: 20, orders, cities, DemandModel);

        var briefing = BriefingGenerator.Generate(null, quarter);

        Assert.Equal(5, briefing.Count);
    }

    [Fact]
    public void Generate_RejectedOrder_IsReported()
    {
        var cities = Enumerable.Range(0, 5).Select(i => MakeCity($"c{i}", i, i, 50)).ToList();
        var hub = cities[0];
        var orders = cities.Skip(1).Select(spoke => (QuarterOrder)new RouteOrder(hub, spoke, Aircraft, 3)).ToArray();

        var quarter = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, DemandModel);
        var briefing = BriefingGenerator.Generate(null, quarter);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.OrderRejectedOverBudget);
    }

    [Fact]
    public void Generate_RejectedNonRouteOrder_IsReportedAsOverBudget_NotOutOfRange()
    {
        // A contested order (no aircraft, no range concept) rejected purely
        // for budget must never be mistaken for an out-of-range rejection.
        var gateway = new City("gw", "Gateway", Region.Asia, 0, 0, 50, true, 1);
        var orders = new QuarterOrder[]
        {
            new NegotiationOrder(gateway, Money.FromDollars(1)),
            new NegotiationOrder(gateway, Money.FromDollars(1)),
            new NegotiationOrder(gateway, Money.FromDollars(1)),
            new NegotiationOrder(gateway, Money.FromDollars(1)), // 4th exceeds the base cap of 3
        };

        var quarter = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, new[] { gateway }, DemandModel);
        var briefing = BriefingGenerator.Generate(null, quarter);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.OrderRejectedOverBudget);
        Assert.DoesNotContain(briefing, i => i.Kind == ExceptionKind.OrderRejectedOutOfRange);
    }

    [Fact]
    public void Generate_ExecutiveHire_IsReported()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var cities = new[] { a, b };
        var orders = new QuarterOrder[] { new RouteOrder(a, b, Aircraft, 3), new HireExecutiveOrder() };

        var quarter = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, cities, DemandModel);
        var briefing = BriefingGenerator.Generate(null, quarter);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.ActionCapIncreased);
    }
}
