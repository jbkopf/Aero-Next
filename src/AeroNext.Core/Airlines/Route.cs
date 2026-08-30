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
/// </summary>
public sealed record Route(City Origin, City Destination, AircraftType Aircraft, int AircraftCount, Money? FareOverride = null)
{
    private const int WeeksPerQuarter = 13;

    public double DistanceKm => GreatCircle.DistanceKm(Origin, Destination);

    /// <summary>Max round trips a single assigned aircraft can fly this route per week.</summary>
    public int MaxWeeklyRoundTripsPerAircraft => FlightPerformance.MaxWeeklyRoundTrips(Aircraft, DistanceKm);

    public int WeeklyRoundTrips => MaxWeeklyRoundTripsPerAircraft * AircraftCount;

    /// <summary>Total seats available across both directions this quarter.</summary>
    public long SeatsPerQuarter => (long)Aircraft.Seats * WeeklyRoundTrips * 2 * WeeksPerQuarter;

    /// <summary>Whether Aircraft can physically fly this distance nonstop — M11's range constraint.</summary>
    public bool IsWithinRange => DistanceKm <= Aircraft.RangeKm;

    /// <summary>Player-set fare if one exists, otherwise FareCalculator's cost-plus-margin default.</summary>
    public Money FarePerPassenger => FareOverride ?? FareCalculator.SuggestedFarePerPassenger(this);
}
