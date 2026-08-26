using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// Resolves one quarter: every order's cost is checked against the action
/// budget in submission order, then all orders that fit are applied
/// together and the resulting network is planned all at once. A single
/// airline can't conflict with its own orders, so batching doesn't matter
/// yet for that reason — but it's the shape M9+'s multi-airline
/// simultaneous resolution builds on, and CLAUDE.md is explicit that
/// retrofitting this later means rewriting the core.
///
/// Budget rule: orders are scanned in list order, tracking cumulative
/// spend. An order that fits within what's left of the cap is applied and
/// consumes its cost; an order that would exceed the remaining cap is
/// rejected — but scanning continues, so a free order (RouteOrder on an
/// already-served pair, or any HireExecutiveOrder) later in the list still
/// goes through even after an earlier, costlier order was rejected. Only
/// orders that don't fit get skipped, not everything after them — a hard
/// truncate-the-rest rule would needlessly block actions that cost nothing.
/// </summary>
public static class QuarterEngine
{
    public const int BaseActionCap = 3;

    public static QuarterResult RunQuarter(
        int quarterNumber,
        IReadOnlyList<Route> currentRoutes,
        int currentActionCap,
        IReadOnlyList<QuarterOrder> orders,
        IReadOnlyList<City> cities,
        GravityDemandModel demandModel)
    {
        var applied = new List<QuarterOrder>();
        var rejected = new List<QuarterOrder>();
        var spent = 0;

        foreach (var order in orders)
        {
            var cost = Cost(order, currentRoutes);

            if (spent + cost > currentActionCap)
            {
                rejected.Add(order);
                continue;
            }

            applied.Add(order);
            spent += cost;
        }

        var routes = ApplyRouteOrders(currentRoutes, applied.OfType<RouteOrder>());
        var nextActionCap = currentActionCap + applied.OfType<HireExecutiveOrder>().Sum(h => h.CapIncrease);
        var plan = NetworkPlanner.Plan(routes, cities, demandModel);

        return new QuarterResult(quarterNumber, routes, plan, nextActionCap, applied, rejected);
    }

    /// <summary>
    /// 1 action for a pair not already in currentRoutes (entering a new
    /// city-pair); 0 for a pair already served (adding frequency is free,
    /// per CLAUDE.md) and for private actions. A negotiation order always
    /// costs 1 — contested actions have no free variant, unlike market
    /// actions. Evaluated against the quarter's starting routes for every
    /// order in the batch, not against other orders being applied in the
    /// same batch — keeps cost order-independent within a quarter, matching
    /// the "collect, then resolve" simultaneity this engine is built
    /// around.
    /// </summary>
    public static int Cost(QuarterOrder order, IReadOnlyList<Route> currentRoutes) => order switch
    {
        RouteOrder ro => currentRoutes.Any(r => r.Origin.Id == ro.Origin.Id && r.Destination.Id == ro.Destination.Id) ? 0 : 1,
        HireExecutiveOrder => 0,
        NegotiationOrder => 1,
        _ => throw new NotSupportedException($"Unknown order type: {order.GetType().Name}"),
    };

    private static IReadOnlyList<Route> ApplyRouteOrders(IReadOnlyList<Route> currentRoutes, IEnumerable<RouteOrder> orders)
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
