using AeroNext.Core.Airlines;

namespace AeroNext.Core.Fleet;

/// <summary>
/// M25: the fleet inventory foundation — a real, queryable record of what
/// aircraft an airline owns, since nothing tracked this before (Route.
/// Aircraft references a type, not a specific unit, and AircraftOrderResolver's
/// deliveries previously vanished into nothing once produced). Deliberately
/// not yet wired into QuarterEngine or either real quarter loop — ordering
/// or leasing an aircraft still has no effect on what a RouteOrder can do
/// until a later milestone makes that connection real. This milestone only
/// makes the ledger itself correct and testable in isolation.
/// </summary>
public static class FleetLedger
{
    /// <param name="mode">
    /// AircraftDelivery itself doesn't distinguish purchased from leased —
    /// that's determined by which resolver method produced it
    /// (AircraftOrderResolver.ResolveQuarter for purchases, ResolveLease
    /// for leases), so the caller supplies it here rather than this ledger
    /// trying to infer it.
    /// </param>
    public static AirlineFleet ApplyDelivery(AirlineFleet fleet, AircraftDelivery delivery, FleetOwnershipMode mode)
    {
        var newUnits = Enumerable.Range(0, delivery.Quantity)
            .Select(i => new FleetUnit(fleet.NextUnitId + i, delivery.Type, mode, AssignedRouteKey: null))
            .ToList();

        return fleet with
        {
            Units = fleet.Units.Concat(newUnits).ToList(),
            NextUnitId = fleet.NextUnitId + delivery.Quantity,
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
