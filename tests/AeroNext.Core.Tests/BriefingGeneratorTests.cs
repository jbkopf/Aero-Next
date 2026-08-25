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
        var current = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), new[] { new Order(a, b, Aircraft, 3) }, cities, DemandModel);

        var briefing = BriefingGenerator.Generate(null, current);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.RouteEntered);
    }

    [Fact]
    public void Generate_UnchangedNetwork_ProducesNoItems()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var routes = new[] { new Route(a, b, Aircraft, WeeklyRoundTrips: 3) };
        var cities = new[] { a, b };

        var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), new[] { new Order(a, b, Aircraft, 3) }, cities, DemandModel);
        var quarter2 = QuarterEngine.RunQuarter(2, quarter1.Routes, Array.Empty<Order>(), cities, DemandModel);

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

        var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), new[] { new Order(a, b, Aircraft, 3) }, cities, DemandModel);
        var quarter2 = QuarterEngine.RunQuarter(2, quarter1.Routes, new[] { new Order(a, c, Aircraft, 3) }, cities, DemandModel);

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

        var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), new[] { new Order(a, b, Aircraft, 1) }, cities, DemandModel);
        var quarter2 = QuarterEngine.RunQuarter(2, quarter1.Routes, new[] { new Order(a, b, Aircraft, 50) }, cities, DemandModel);

        var briefing = BriefingGenerator.Generate(quarter1, quarter2);

        Assert.Contains(briefing, i => i.Kind == ExceptionKind.LoadFactorDropped);
    }

    [Fact]
    public void Generate_CapsAtFiveItems()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var cities = Enumerable.Range(0, 14).Select(i => MakeCity($"c{i}", i, i, 50)).ToList();
        var hub = cities[0];

        // 13 spokes off one hub, all entering in the same quarter.
        var orders = cities.Skip(1).Select(spoke => new Order(hub, spoke, aircraft, 3)).ToArray();
        var quarter = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), orders, cities, DemandModel);

        var briefing = BriefingGenerator.Generate(null, quarter);

        Assert.Equal(5, briefing.Count);
    }
}
