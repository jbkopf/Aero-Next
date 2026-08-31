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

    /// <summary>
    /// The global-scoped *Trend* portion of the schedule only (M20: a
    /// schedule can now also carry region/city-scoped breakpoints;
    /// M21's trend/event split adds a separate global *Event* layer too
    /// - both resolved by <see cref="GravityDemandModel"/> itself, via
    /// <see cref="LatestAtOrBefore"/>, and composed on top of this
    /// method's own result rather than folded into it). Whichever global
    /// trend breakpoint's year is the latest one at or before the given
    /// year (piecewise-constant, not interpolated); falls back to the
    /// earliest global trend breakpoint if none is at or before it yet,
    /// matching this method's own pre-existing behavior for a schedule
    /// that starts after `year`.
    /// </summary>
    public static double DemandMultiplierForYear(IReadOnlyList<DemandGrowthPoint> schedule, int year)
    {
        var globalTrend = schedule.Where(p => p.Region is null && p.CityId is null && p.Kind == DemandGrowthPointKind.Trend).ToList();
        var applicable = LatestAtOrBefore(globalTrend, year);

        return applicable?.GlobalMultiplier ?? globalTrend.OrderBy(p => p.Year).First().GlobalMultiplier;
    }

    /// <summary>
    /// M20: the shared "piecewise-constant, latest breakpoint at or
    /// before `year`" resolution rule, factored out so
    /// <see cref="DemandMultiplierForYear"/> (global scope, falls back to
    /// the earliest entry) and <see cref="GravityDemandModel"/>'s
    /// region/city resolution (falls back to a neutral 1.0 when that
    /// scope has no entries at all - the common case today) can each
    /// apply their own, different "nothing found" behavior on top of the
    /// same lookup.
    /// </summary>
    internal static DemandGrowthPoint? LatestAtOrBefore(IEnumerable<DemandGrowthPoint> candidates, int year) =>
        candidates.Where(p => p.Year <= year).OrderByDescending(p => p.Year).FirstOrDefault();
}
