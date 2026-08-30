using AeroNext.Core.Airlines;

namespace AeroNext.Core.Fleet;

/// <summary>
/// One specific aircraft, not just a type + quantity — needed because two
/// otherwise-identical idle units of the same type must still be
/// individually addressable (assigned to a route, retired, etc). Id is a
/// plain per-airline sequential integer assigned by FleetLedger, not
/// caller-supplied or randomly generated — a GUID or similar would violate
/// CLAUDE.md's determinism rules.
///
/// AssignedRouteKey is null for an idle/spare unit. A non-null value is a
/// free-form label — deliberately not given a fixed shape here, since
/// nothing consumes it yet; a future milestone wiring this into
/// QuarterEngine will define it (probably matching QuarterEngine.RouteKey's
/// (Origin, Destination) shape as a string).
/// </summary>
public sealed record FleetUnit(int Id, AircraftType Type, FleetOwnershipMode Mode, string? AssignedRouteKey);
