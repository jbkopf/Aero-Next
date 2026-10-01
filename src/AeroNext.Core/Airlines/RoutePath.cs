using AeroNext.Core.World;

namespace AeroNext.Core.Airlines;

/// <summary>
/// The geometry of a route flown origin -> stops -> destination
/// (docs/MULTI_STOP_ROUTES_DESIGN.md). Shared by <see cref="Route"/>, which
/// prices and schedules what's already flying, and the route orders, which
/// are validated before anything is flown, so both read range and distance
/// the same way. For a nonstop route every method reduces to the single
/// origin-destination leg.
/// </summary>
public static class RoutePath
{
    /// <summary>Every city the aircraft lands at, in order: origin, each stop, destination.</summary>
    public static IReadOnlyList<City> Cities(City origin, StopList stops, City destination) =>
        stops.Prepend(origin).Append(destination).ToList();

    /// <summary>Great-circle length of each leg flown, in order.</summary>
    public static IReadOnlyList<double> LegDistancesKm(City origin, StopList stops, City destination)
    {
        var cities = Cities(origin, stops, destination);

        return Enumerable.Range(0, cities.Count - 1)
            .Select(i => GreatCircle.DistanceKm(cities[i], cities[i + 1]))
            .ToList();
    }

    /// <summary>Total distance flown one way, every leg added together.</summary>
    /// <remarks>Nonstop routes skip the leg list entirely - this sits on hot paths (fares, operating cost, the planner).</remarks>
    public static double TotalDistanceKm(City origin, StopList stops, City destination) =>
        stops.IsEmpty ? GreatCircle.DistanceKm(origin, destination) : LegDistancesKm(origin, stops, destination).Sum();

    /// <summary>Whether the aircraft can fly every leg - a multi-stop route only needs each leg within range, not the whole journey.</summary>
    public static bool IsWithinRange(City origin, StopList stops, City destination, AircraftType aircraft) =>
        stops.IsEmpty
            ? GreatCircle.DistanceKm(origin, destination) <= aircraft.RangeKm
            : LegDistancesKm(origin, stops, destination).All(leg => leg <= aircraft.RangeKm);

    /// <summary>
    /// Whether a stop list is allowed at all, independent of any aircraft:
    /// every stop is a gateway city (the user's direct choice), no city
    /// appears twice, and no stop is one of the route's own endpoints.
    /// Range and slots are checked separately.
    /// </summary>
    public static bool AreValidStops(City origin, StopList stops, City destination)
    {
        if (stops.Any(stop => !stop.IsGateway))
        {
            return false;
        }

        var ids = Cities(origin, stops, destination).Select(c => c.Id).ToList();

        return ids.Distinct(StringComparer.Ordinal).Count() == ids.Count;
    }
}
