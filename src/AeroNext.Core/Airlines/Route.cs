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
}
