namespace AeroNext.Core.Fleet;

/// <summary>
/// How many consecutive quarter-ends each fleet unit has sat idle (no
/// AssignedRouteKey), keyed by FleetUnit.Id. Built fresh each quarter from
/// the previous map plus the fleet's post-orders state, so a unit that's
/// assigned, retired, or newly delivered never carries a stale count.
/// Lookups only - nothing iterates this map in a way that affects outcomes.
/// </summary>
public static class IdleStreaks
{
    public static IReadOnlyDictionary<int, int> Empty { get; } = new Dictionary<int, int>();

    public static IReadOnlyDictionary<int, int> Advance(IReadOnlyDictionary<int, int> previous, AirlineFleet fleet) =>
        fleet.Idle.ToDictionary(u => u.Id, u => previous.GetValueOrDefault(u.Id) + 1);
}
