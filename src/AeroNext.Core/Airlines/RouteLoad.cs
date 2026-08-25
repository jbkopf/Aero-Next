namespace AeroNext.Core.Airlines;

/// <summary>
/// The result of assigning a city pair's nonstop demand to one route's
/// capacity. Demand-constrained when Demand > Seats (load factor pinned at
/// 1.0, unmet demand goes unserved); capacity-constrained otherwise.
/// </summary>
public sealed record RouteLoad(Route Route, long DemandPassengers, long SeatsPerQuarter, long PassengersCarried)
{
    public double LoadFactor => SeatsPerQuarter == 0 ? 0.0 : (double)PassengersCarried / SeatsPerQuarter;
}
