namespace AeroNext.Core.Airlines;

/// <param name="TurnedAway">
/// Connecting passengers (docs/CONNECTING_PASSENGERS_DESIGN.md): passengers
/// who wanted a seat on this route and didn't get one because this route
/// was the full one - local passengers, and connecting passengers for whom
/// this leg (not their other leg) was the tighter. What another aircraft
/// here could carry.
/// </param>
public sealed record RouteCapacityUsage(Route Route, long SeatsPerQuarter, long SeatsUsed, long TurnedAway = 0)
{
    public double LoadFactor => SeatsPerQuarter == 0 ? 0.0 : (double)SeatsUsed / SeatsPerQuarter;
}
