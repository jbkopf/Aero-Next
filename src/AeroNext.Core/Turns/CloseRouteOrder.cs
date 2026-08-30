using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// A private action (CLAUDE.md: "resolves against nothing") — always free,
/// closing the gap RetireAircraftOrder's own doc comment named: retiring
/// only works on an idle unit today, so freeing an assigned one first
/// needs this. Detaches one aircraft from the Origin-Destination pair —
/// not "close the whole route" in one shot. Against a route with more
/// than one aircraft, this shrinks AircraftCount by one and the route
/// stays; against the last aircraft on it, the route disappears from the
/// list entirely rather than lingering as a zero-capacity entry. "Close"
/// and "shrink" are the same primitive, matching the one-aircraft-at-a-time
/// granularity RouteOrder itself already uses to grow a route (M32).
///
/// No UnitId, unlike RetireAircraftOrder: every unit assigned to a route
/// is the same aircraft type (Route.Aircraft is singular) and this game
/// tracks no per-unit condition, so which specific unit gets freed is
/// immaterial — QuarterEngine picks deterministically. Requires fleet
/// awareness to mean anything — rejected outright when fleet tracking is
/// off or the airline doesn't currently operate this pair, the same
/// asymmetry from RouteOrder's permissive-when-null check that
/// RetireAircraftOrder already established.
/// </summary>
public sealed record CloseRouteOrder(City Origin, City Destination) : QuarterOrder;
