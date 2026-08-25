using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// Resolves one quarter: all orders are collected before any are applied,
/// then the resulting network is planned all at once. A single airline
/// can't conflict with its own orders, so this doesn't matter yet — but
/// it's the shape M6+'s multi-airline simultaneous resolution builds on,
/// and CLAUDE.md is explicit that retrofitting this later means rewriting
/// the core.
/// </summary>
public static class QuarterEngine
{
    public static QuarterResult RunQuarter(
        int quarterNumber,
        IReadOnlyList<Route> currentRoutes,
        IReadOnlyList<Order> orders,
        IReadOnlyList<City> cities,
        GravityDemandModel demandModel)
    {
        var routes = ApplyOrders(currentRoutes, orders);
        var plan = NetworkPlanner.Plan(routes, cities, demandModel);

        return new QuarterResult(quarterNumber, routes, plan);
    }

    private static IReadOnlyList<Route> ApplyOrders(IReadOnlyList<Route> currentRoutes, IReadOnlyList<Order> orders)
    {
        var byKey = currentRoutes.ToDictionary(RouteKey);

        foreach (var order in orders)
        {
            byKey[(order.Origin.Id, order.Destination.Id)] =
                new Route(order.Origin, order.Destination, order.Aircraft, order.WeeklyRoundTrips);
        }

        return byKey.Values
            .OrderBy(r => r.Origin.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Destination.Id, StringComparer.Ordinal)
            .ToList();
    }

    internal static (string Origin, string Destination) RouteKey(Route route) => (route.Origin.Id, route.Destination.Id);
}
