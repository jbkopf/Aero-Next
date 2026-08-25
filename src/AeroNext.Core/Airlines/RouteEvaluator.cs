using AeroNext.Core.World;

namespace AeroNext.Core.Airlines;

/// <summary>
/// Assigns a route's nonstop city-pair demand to its seat capacity. No
/// competition yet (M6) — the route is assumed to be the only carrier on
/// that pair, so it captures demand up to capacity and no further.
/// </summary>
public static class RouteEvaluator
{
    public static RouteLoad Evaluate(Route route, GravityDemandModel demandModel)
    {
        var demand = demandModel.ForPair(route.Origin, route.Destination).PassengersPerQuarter;
        var carried = Math.Min(demand, route.SeatsPerQuarter);

        return new RouteLoad(route, demand, route.SeatsPerQuarter, carried);
    }

    /// <summary>Evaluates every route in a network, sorted by origin id then destination id.</summary>
    public static IReadOnlyList<RouteLoad> EvaluateNetwork(IReadOnlyList<Route> routes, GravityDemandModel demandModel) =>
        routes
            .OrderBy(r => r.Origin.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Destination.Id, StringComparer.Ordinal)
            .Select(r => Evaluate(r, demandModel))
            .ToList();
}
