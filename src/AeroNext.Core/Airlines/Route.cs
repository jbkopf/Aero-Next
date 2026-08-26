using AeroNext.Core.World;

namespace AeroNext.Core.Airlines;

/// <summary>
/// A nonstop route: one aircraft type flying a fixed number of round trips
/// per week between two cities.
/// </summary>
public sealed record Route(City Origin, City Destination, AircraftType Aircraft, int WeeklyRoundTrips)
{
    private const int WeeksPerQuarter = 13;

    /// <summary>Total seats available across both directions this quarter.</summary>
    public long SeatsPerQuarter => (long)Aircraft.Seats * WeeklyRoundTrips * 2 * WeeksPerQuarter;

    public double DistanceKm => GreatCircle.DistanceKm(Origin, Destination);

    /// <summary>Whether Aircraft can physically fly this distance nonstop — M11's range constraint.</summary>
    public bool IsWithinRange => DistanceKm <= Aircraft.RangeKm;
}
