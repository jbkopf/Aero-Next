using AeroNext.Core.Airlines;

namespace AeroNext.Core.World;

/// <summary>
/// Quarter-to-year conversion and the two things the campaign checks
/// against it: which aircraft a manufacturer has actually built yet, and
/// how much bigger the world's total demand has grown. Quarter 1 is 1955
/// Q1; four quarters per year thereafter.
/// </summary>
public static class Timeline
{
    public const int StartYear = 1955;

    public static int YearForQuarter(int quarterNumber) => StartYear + (quarterNumber - 1) / 4;

    public static bool IsAvailable(AircraftType aircraft, int year) => aircraft.AvailableFromYear <= year;

    public static IReadOnlyList<AircraftType> AvailableAircraft(IReadOnlyList<AircraftType> fleet, int year) =>
        fleet.Where(a => IsAvailable(a, year)).ToList();

    /// <summary>Whichever breakpoint's year is the latest one at or before the given year (piecewise-constant, not interpolated).</summary>
    public static double DemandMultiplierForYear(IReadOnlyList<DemandGrowthPoint> schedule, int year)
    {
        var applicable = schedule
            .Where(p => p.Year <= year)
            .OrderByDescending(p => p.Year)
            .FirstOrDefault();

        return applicable?.GlobalMultiplier ?? schedule.OrderBy(p => p.Year).First().GlobalMultiplier;
    }
}
