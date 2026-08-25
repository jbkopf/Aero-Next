using AeroNext.Core.Airlines;

namespace AeroNext.Core.Competition;

public sealed record CompetitiveRouteUsage(Airline Airline, Route Route, long SeatsPerQuarter, long SeatsUsed)
{
    public double LoadFactor => SeatsPerQuarter == 0 ? 0.0 : (double)SeatsUsed / SeatsPerQuarter;
}
