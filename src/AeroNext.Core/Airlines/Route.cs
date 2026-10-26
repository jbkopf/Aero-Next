using AeroNext.Core.Finance;
using AeroNext.Core.World;

namespace AeroNext.Core.Airlines;

/// <summary>
/// A nonstop route: one aircraft type, flown by AircraftCount individual
/// units (M32) between two cities. WeeklyRoundTrips is no longer a
/// player-chosen dial — it's derived from how many round trips a single
/// aircraft of this type can realistically fly this distance
/// (<see cref="FlightPerformance"/>) times how many units are assigned.
/// Growing a route's capacity means assigning another aircraft to it, not
/// picking a bigger number.
///
/// FareOverride is null until a player sets one via SetFareOrder (M34) —
/// FarePerPassenger falls back to FareCalculator's cost-plus-margin
/// default in that case, so every route (new, test, or demo scenario)
/// gets a real, non-zero fare with no migration burden on any existing
/// call site, the same reasoning CruiseSpeedKmh's own default followed.
///
/// Stops (docs/MULTI_STOP_ROUTES_DESIGN.md) are intermediate landings for
/// fuel - empty unless the route says otherwise. Passengers and demand stay
/// the Origin-Destination pair's; everything about flying the route
/// (distance, ground time, seats, range, operating cost through
/// DistanceKm) covers every leg. A route is still identified by its
/// endpoint pair, and every aircraft on it flies the same stops, just as
/// every aircraft on it is the same type.
/// </summary>
public sealed record Route(City Origin, City Destination, AircraftType Aircraft, int AircraftCount, Money? FareOverride = null, StopList Stops = default)
{
    private const int WeeksPerQuarter = 13;

    /// <summary>One-way distance flown: the single leg for a nonstop route, every leg added together for a multi-stop one.</summary>
    public double DistanceKm => RoutePath.TotalDistanceKm(Origin, Stops, Destination);

    /// <summary>Max round trips a single assigned aircraft can fly this route per week - each stop adds a landing's ground time in both directions.</summary>
    public int MaxWeeklyRoundTripsPerAircraft => FlightPerformance.MaxWeeklyRoundTrips(Aircraft, DistanceKm, landingsPerDirection: Stops.Count + 1);

    public int WeeklyRoundTrips => MaxWeeklyRoundTripsPerAircraft * AircraftCount;

    /// <summary>Total seats available across both directions this quarter.</summary>
    public long SeatsPerQuarter => (long)Aircraft.Seats * WeeklyRoundTrips * 2 * WeeksPerQuarter;

    /// <summary>Whether Aircraft can physically fly every leg — M11's range constraint, applied per leg for a multi-stop route.</summary>
    public bool IsWithinRange => RoutePath.IsWithinRange(Origin, Stops, Destination, Aircraft);

    /// <summary>Whether this route lands at the city at all - an endpoint or a stop. A gateway slot is used at every such city, one per aircraft.</summary>
    public bool Touches(string cityId) =>
        Origin.Id == cityId || Destination.Id == cityId || Stops.Any(stop => stop.Id == cityId);

    /// <summary>Player-set fare if one exists, otherwise FareCalculator's cost-plus-margin default.</summary>
    public Money FarePerPassenger => FareOverride ?? FareCalculator.SuggestedFarePerPassenger(this);
}
