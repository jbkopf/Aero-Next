using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// Assigns one more aircraft of Aircraft's type to the Origin-Destination
/// pair — covers both "enter a new city-pair" (no route there yet) and
/// "grow a route already served" (add another unit to it) with one shape.
/// No frequency field anymore (M32): WeeklyRoundTrips is derived from how
/// many aircraft end up assigned, not chosen here. Always costs 1 action —
/// see <see cref="QuarterEngine"/>. A pair already served by a *different*
/// aircraft type rejects this order outright; swapping a route's type
/// isn't supported yet (no detach action exists to free the old units).
/// </summary>
public sealed record RouteOrder(City Origin, City Destination, AircraftType Aircraft) : QuarterOrder
{
    /// <summary>Whether Aircraft can physically fly Origin-Destination nonstop — M11's range constraint, mirroring Route.IsWithinRange.</summary>
    public bool IsWithinRange => GreatCircle.DistanceKm(Origin, Destination) <= Aircraft.RangeKm;
}
