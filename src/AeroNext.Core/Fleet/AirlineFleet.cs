namespace AeroNext.Core.Fleet;

/// <summary>
/// One airline's aircraft, individually tracked (see FleetUnit). Immutable
/// — FleetLedger produces a new AirlineFleet rather than mutating this one,
/// matching every other resolver in this codebase.
/// </summary>
public sealed record AirlineFleet(IReadOnlyList<FleetUnit> Units, int NextUnitId)
{
    public static readonly AirlineFleet Empty = new(Array.Empty<FleetUnit>(), NextUnitId: 1);

    public IEnumerable<FleetUnit> Idle => Units.Where(u => u.AssignedRouteKey is null);
}
