using AeroNext.Core.Airlines;

namespace AeroNext.Core.Competition;

/// <param name="TurnedAway">Passengers this route turned away in the airline's own network plan (RouteCapacityUsage.TurnedAway) - what another aircraft here could carry, before rivals take their share.</param>
public sealed record CompetitiveRouteUsage(Airline Airline, Route Route, long SeatsPerQuarter, long SeatsUsed, long TurnedAway = 0)
{
    public double LoadFactor => SeatsPerQuarter == 0 ? 0.0 : (double)SeatsUsed / SeatsPerQuarter;
}
