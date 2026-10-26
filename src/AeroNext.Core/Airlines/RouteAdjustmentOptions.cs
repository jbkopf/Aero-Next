using AeroNext.Core.Fleet;

namespace AeroNext.Core.Airlines;

/// <summary>
/// Read-only query surface for a future UI's route-management menu — not
/// itself a QuarterEngine input, just answers "what can this menu offer
/// the player right now for this route." See
/// docs/ROUTE_AIRCRAFT_ADJUSTMENT_DESIGN.md for the full design.
/// </summary>
public static class RouteAdjustmentOptions
{
    /// <summary>
    /// Every type eligible for an AdjustRouteOrder on this route: the
    /// airline holds at least 1 idle unit of it, it can physically reach
    /// this route's distance, and it isn't the route's own current type
    /// (AdjustRouteOrder itself would reject that — see its own doc
    /// comment). Since a swap always lands on exactly 1 aircraft
    /// regardless of how many were there before, the menu only needs to
    /// know *which* types are legal, not how many idle units of each
    /// exist — unlike RouteOrder's own idle-unit-count question, which a
    /// separate, already-existing query answers for ordinary growth.
    ///
    /// Range is checked against the route's current stops, leg by leg
    /// (docs/MULTI_STOP_ROUTES_DESIGN.md) - a type that could only fly the
    /// route with a different stop list isn't offered here.
    /// </summary>
    public static IReadOnlyList<AircraftType> EligibleTypesForSwap(Route route, AirlineFleet fleet) =>
        fleet.Idle
            .Select(u => u.Type)
            .Distinct()
            .Where(type => type.Id != route.Aircraft.Id && RoutePath.IsWithinRange(route.Origin, route.Stops, route.Destination, type))
            .OrderBy(type => type.Id, StringComparer.Ordinal)
            .ToList();
}
