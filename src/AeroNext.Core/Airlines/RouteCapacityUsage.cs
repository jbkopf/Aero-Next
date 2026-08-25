namespace AeroNext.Core.Airlines;

public sealed record RouteCapacityUsage(Route Route, long SeatsPerQuarter, long SeatsUsed)
{
    public double LoadFactor => SeatsPerQuarter == 0 ? 0.0 : (double)SeatsUsed / SeatsPerQuarter;
}
