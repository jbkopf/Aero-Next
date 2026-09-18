using AeroNext.Core.Airlines;

namespace AeroNext.Core.Fleet;

/// <summary>
/// M25: the fleet inventory foundation — a real, queryable record of what
/// aircraft an airline owns, since nothing tracked this before (Route.
/// Aircraft references a type, not a specific unit, and AircraftOrderResolver's
/// deliveries previously vanished into nothing once produced). Now wired
/// into QuarterEngine (fleet-aware RouteOrder ownership checks) and both
/// real quarter loops.
/// </summary>
public static class FleetLedger
{
    /// <param name="mode">
    /// AircraftDelivery itself doesn't distinguish purchased from leased —
    /// that's determined by which resolver produced the delivery being
    /// applied (AircraftOrderResolver.ResolveQuarter for purchases,
    /// LeaseAllocationResolver.ResolveQuarter for leases — both two-phase
    /// now, see docs/AIRCRAFT_ACQUISITION_ROADMAP.md), so the caller
    /// supplies it here rather than this ledger trying to infer it.
    /// </param>
    public static AirlineFleet ApplyDelivery(AirlineFleet fleet, AircraftDelivery delivery, FleetOwnershipMode mode) =>
        ApplyDelivery(fleet, delivery.Type, delivery.Quantity, mode);

    /// <summary>
    /// Same as the AircraftDelivery overload, without needing a full
    /// delivery record — used where there's no natural Airline reference
    /// to build one from (QuarterEngine's inline lease resolution, which
    /// only knows the fleet it's updating, not the Airline record).
    /// </summary>
    public static AirlineFleet ApplyDelivery(AirlineFleet fleet, AircraftType type, int quantity, FleetOwnershipMode mode)
    {
        var newUnits = Enumerable.Range(0, quantity)
            .Select(i => new FleetUnit(fleet.NextUnitId + i, type, mode, AssignedRouteKey: null))
            .ToList();

        return fleet with
        {
            Units = fleet.Units.Concat(newUnits).ToList(),
            NextUnitId = fleet.NextUnitId + quantity,
        };
    }

    public static AirlineFleet Assign(AirlineFleet fleet, int unitId, string routeKey)
    {
        var unit = FindUnit(fleet, unitId);

        if (unit.AssignedRouteKey is not null)
        {
            throw new InvalidOperationException($"Unit {unitId} is already assigned to route '{unit.AssignedRouteKey}'.");
        }

        return Replace(fleet, unit with { AssignedRouteKey = routeKey });
    }

    public static AirlineFleet Unassign(AirlineFleet fleet, int unitId)
    {
        var unit = FindUnit(fleet, unitId);

        return Replace(fleet, unit with { AssignedRouteKey = null });
    }

    public static AirlineFleet Retire(AirlineFleet fleet, int unitId)
    {
        _ = FindUnit(fleet, unitId); // throws if unknown, same validation every other operation gets

        return fleet with { Units = fleet.Units.Where(u => u.Id != unitId).ToList() };
    }

    private static FleetUnit FindUnit(AirlineFleet fleet, int unitId) =>
        fleet.Units.SingleOrDefault(u => u.Id == unitId)
            ?? throw new ArgumentException($"No unit with id {unitId} in this fleet.", nameof(unitId));

    private static AirlineFleet Replace(AirlineFleet fleet, FleetUnit updated) =>
        fleet with { Units = fleet.Units.Select(u => u.Id == updated.Id ? updated : u).ToList() };
}
