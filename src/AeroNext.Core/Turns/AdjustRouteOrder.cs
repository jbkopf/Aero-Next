using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// Changes an already-served pair's aircraft type — the atomic,
/// same-quarter swap RouteOrder's own doc comment names as unsupported
/// ("swapping a route's type isn't supported yet"). Resets the pair to
/// exactly 1 aircraft of AircraftType: not a player-chosen target count,
/// since a bigger final count is reached afterward with ordinary
/// RouteOrders for the same pair/type, submitted later in the same
/// order list at their own existing, unchanged price. See
/// docs/ROUTE_AIRCRAFT_ADJUSTMENT_DESIGN.md for the full design and the
/// reasoning behind every rule below.
///
/// Rejected outright (see QuarterEngine) when: the pair isn't currently
/// served (use RouteOrder to open a genuinely new pair instead);
/// AircraftType matches the pair's current type (not a real change —
/// allowing it would be a strictly-worse-than-free way to shrink a route
/// down to 1, since CloseRouteOrder already does that for nothing); the
/// aircraft can't reach this pair or isn't era-available yet; or fewer
/// than 1 idle unit of AircraftType exists. Always costs exactly 1
/// action, and always resets FareOverride to the computed default — a
/// real, deliberate consequence of aircraft type having its own cost
/// structure, not a bug to work around.
/// </summary>
public sealed record AdjustRouteOrder(City Origin, City Destination, AircraftType AircraftType) : QuarterOrder
{
    /// <summary>Whether AircraftType can physically fly Origin-Destination nonstop — mirrors RouteOrder.IsWithinRange.</summary>
    public bool IsWithinRange => GreatCircle.DistanceKm(Origin, Destination) <= AircraftType.RangeKm;
}
