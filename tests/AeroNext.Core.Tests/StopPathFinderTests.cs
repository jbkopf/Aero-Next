using AeroNext.Core.Airlines;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// docs/MULTI_STOP_ROUTES_DESIGN.md, Phase 3. Cities on the equator, so
/// distance is ~111.2 km per degree of longitude.
/// </summary>
public class StopPathFinderTests
{
    private static City MakeCity(string id, double lon) =>
        new(id, id.ToUpperInvariant(), Region.Europe, 0, lon, 50, false, null);

    private static City MakeGateway(string id, double lon) =>
        new(id, id.ToUpperInvariant(), Region.NorthAmerica, 0, lon, 1, true, 2);

    [Fact]
    public void InRange_IsNonstop()
    {
        var a = MakeCity("a", 0);
        var b = MakeCity("b", 10);
        var finder = new StopPathFinder(new[] { a, b, MakeGateway("g", 5) });

        Assert.Equal(StopList.Empty, finder.ShortestStops(a, b, rangeKm: 2_000));
    }

    [Fact]
    public void OneStop_PicksTheShortestTotalDistance_AndIgnoresNonGateways()
    {
        // ~4,448 km end to end, 3,000 km range. Both gateways work as a
        // single stop; the one on the great circle is shorter overall. The
        // plain city sits exactly halfway but isn't a gateway.
        var a = MakeCity("a", 0);
        var b = MakeCity("b", 40);
        var onTheWay = MakeGateway("zon", 20);
        var offTheWay = new City("aoff", "AOFF", Region.NorthAmerica, 5, 20, 1, true, 2);
        var plainHalfway = MakeCity("plain", 20);
        var finder = new StopPathFinder(new[] { a, b, onTheWay, offTheWay, plainHalfway });

        var stops = finder.ShortestStops(a, b, rangeKm: 3_000);

        Assert.Equal(StopList.Of(onTheWay), stops);
    }

    [Fact]
    public void TwoStops_WhenNoSingleStopReaches()
    {
        // ~6,672 km, 2,300 km range: no single gateway puts both legs in
        // range, two evenly spaced ones do.
        var a = MakeCity("a", 0);
        var b = MakeCity("b", 60);
        var first = MakeGateway("g1", 20);
        var second = MakeGateway("g2", 40);
        var finder = new StopPathFinder(new[] { a, b, first, second });

        Assert.Equal(StopList.Of(first, second), finder.ShortestStops(a, b, rangeKm: 2_300));
        Assert.Equal(StopList.Of(second, first), finder.ShortestStops(b, a, rangeKm: 2_300));
    }

    [Fact]
    public void Unreachable_WithinTwoStops_IsNull()
    {
        var a = MakeCity("a", 0);
        var b = MakeCity("b", 80);
        var finder = new StopPathFinder(new[] { a, b, MakeGateway("g1", 20), MakeGateway("g2", 40), MakeGateway("g3", 60) });

        Assert.Null(finder.ShortestStops(a, b, rangeKm: 2_300));
    }

    [Fact]
    public void AnEndpointIsNeverItsOwnStop()
    {
        // The destination is the only gateway, and out of nonstop range -
        // it can't double as a stop on its own route, so there's no path.
        var a = MakeCity("a", 0);
        var gatewayEnd = MakeGateway("gend", 40);
        var finder = new StopPathFinder(new[] { a, gatewayEnd });

        Assert.Null(finder.ShortestStops(a, gatewayEnd, rangeKm: 3_000));
        Assert.Null(finder.ShortestStops(gatewayEnd, a, rangeKm: 3_000));
    }

    [Fact]
    public void Ties_BreakOnTheLowerCityId()
    {
        // Two gateways at the same spot - the same total distance either way.
        var a = MakeCity("a", 0);
        var b = MakeCity("b", 40);
        var lower = MakeGateway("g-a", 20);
        var higher = MakeGateway("g-b", 20);
        var finder = new StopPathFinder(new[] { a, b, higher, lower });

        Assert.Equal(StopList.Of(lower), finder.ShortestStops(a, b, rangeKm: 3_000));
    }
}
