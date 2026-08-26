using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// Sets a route's aircraft and frequency for the coming quarter — covers
/// both "enter a new city-pair" and "add frequency to a pair already
/// served" (CLAUDE.md's market actions) with one shape, since applying an
/// order both creates a route that doesn't exist yet and updates one that
/// does. Costs 1 action if the pair isn't already served, 0 if it is — see
/// <see cref="QuarterEngine"/>.
/// </summary>
public sealed record RouteOrder(City Origin, City Destination, AircraftType Aircraft, int WeeklyRoundTrips) : QuarterOrder
{
    /// <summary>Whether Aircraft can physically fly Origin-Destination nonstop — M11's range constraint, mirroring Route.IsWithinRange.</summary>
    public bool IsWithinRange => GreatCircle.DistanceKm(Origin, Destination) <= Aircraft.RangeKm;
}
