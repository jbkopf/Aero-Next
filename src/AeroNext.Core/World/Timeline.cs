using AeroNext.Core.Airlines;

namespace AeroNext.Core.World;

/// <summary>
/// Quarter-to-year conversion and the two things the campaign checks
/// against it: which aircraft a manufacturer has actually built yet, and
/// how much bigger the world's total demand has grown. Quarter 1 is 1955
/// Q1; four quarters per year thereafter.
///
/// <see cref="AvailableForPurchase"/>/<see cref="AvailableForLease"/> are
/// this class's own shared query surface for "what can this city
/// actually acquire right now" (era window + TradeRestriction combined) -
/// the same query a UI layer would call to show a human player only real
/// options, and what StrategyContext.PurchasableTypes/LeasableTypes feed
/// the AI from, so neither ever drifts from what QuarterEngine itself
/// would actually accept.
/// </summary>
public static class Timeline
{
    public const int StartYear = 1955;

    public static int YearForQuarter(int quarterNumber) => StartYear + (quarterNumber - 1) / 4;

    /// <summary>
    /// Whether an aircraft can be *flown* this year — a floor only
    /// (AvailableFromYear), never a ceiling. Used by RouteOrder/AdjustRouteOrder,
    /// which assign an aircraft to a route rather than acquire a new one:
    /// an airline that already owns or leases a type well past its own
    /// end-of-manufacture date can keep flying it, the same way a real
    /// airline isn't forced to ground an aircraft just because the
    /// manufacturer stopped building new ones. See
    /// <see cref="IsAvailableForPurchase"/>/<see cref="IsAvailableForLease"/>
    /// for the two acquisition-time windows this deliberately doesn't
    /// touch (docs/AIRCRAFT_ACQUISITION_ROADMAP.md).
    /// </summary>
    public static bool IsAvailable(AircraftType aircraft, int year) => aircraft.AvailableFromYear <= year;

    public static IReadOnlyList<AircraftType> AvailableAircraft(IReadOnlyList<AircraftType> fleet, int year) =>
        fleet.Where(a => IsAvailable(a, year)).ToList();

    /// <summary>
    /// Aircraft-acquisition-limits follow-up: how many years after an
    /// aircraft's own AvailableFromYear a lease first becomes possible -
    /// new aircraft aren't leasable right away, only purchasable.
    /// </summary>
    public const int LeaseEligibilityDelayYears = 2;

    /// <summary>
    /// Aircraft-acquisition-limits follow-up: how many years past an
    /// aircraft's own EndOfManufactureYear leasing remains possible after
    /// purchasing has already stopped - leased units already in service
    /// (and the secondhand/used-lease market they imply) don't vanish the
    /// instant the last one rolls off the line.
    /// </summary>
    public const int LeaseAvailabilityExtensionYears = 5;

    /// <summary>
    /// Whether an aircraft can be newly *purchased* this year - a real
    /// window, not just a floor: AvailableFromYear (unchanged) through
    /// EndOfManufactureYear (null = still in production, never stops).
    /// </summary>
    public static bool IsAvailableForPurchase(AircraftType aircraft, int year) =>
        aircraft.AvailableFromYear <= year
        && (aircraft.EndOfManufactureYear is null || year <= aircraft.EndOfManufactureYear);

    /// <summary>
    /// Whether an aircraft can be newly *leased* this year - a real
    /// window, shifted later on both ends relative to purchasing: starts
    /// LeaseEligibilityDelayYears after AvailableFromYear (a brand-new
    /// type isn't leasable yet, only purchasable), extends
    /// LeaseAvailabilityExtensionYears past EndOfManufactureYear (null =
    /// still in production, never stops).
    /// </summary>
    public static bool IsAvailableForLease(AircraftType aircraft, int year) =>
        aircraft.AvailableFromYear + LeaseEligibilityDelayYears <= year
        && (aircraft.EndOfManufactureYear is null || year <= aircraft.EndOfManufactureYear + LeaseAvailabilityExtensionYears);

    /// <summary>
    /// Geopolitical-aircraft-restrictions follow-up
    /// (docs/GEOPOLITICAL_AIRCRAFT_RESTRICTIONS_ROADMAP.md): the single
    /// query surface for "what can this city's airline actually buy right
    /// now" - combines the purchase era window with
    /// <see cref="TradeRestriction.IsRestricted"/>, so neither a UI layer
    /// nor an AI strategy has to know both rules exist and remember to
    /// apply them together. <paramref name="homeCity"/>: null (no
    /// visibility) and <paramref name="tradeRestrictions"/>: null (not
    /// modeled) both mean "nothing is restricted" - the same permissive
    /// default every optional identity field here already gets, so this
    /// degrades to a plain era-window filter for any caller that doesn't
    /// have a real home city or restriction list to pass.
    /// </summary>
    public static IReadOnlyList<AircraftType> AvailableForPurchase(
        IReadOnlyList<AircraftType> fleet, City? homeCity, int year, IReadOnlyList<TradeRestriction>? tradeRestrictions = null)
    {
        var restrictions = tradeRestrictions ?? Array.Empty<TradeRestriction>();
        return fleet.Where(a => IsAvailableForPurchase(a, year) && !TradeRestriction.IsRestricted(a, homeCity, year, restrictions)).ToList();
    }

    /// <summary>
    /// Same query surface as <see cref="AvailableForPurchase"/>, against
    /// the lease era window instead - the exact same
    /// <see cref="TradeRestriction.IsRestricted"/> check applies to both,
    /// since a real embargo blocks acquiring a bloc's aircraft either way;
    /// only the underlying era window differs (lease starts later, extends
    /// later - see <see cref="IsAvailableForLease"/>).
    /// </summary>
    public static IReadOnlyList<AircraftType> AvailableForLease(
        IReadOnlyList<AircraftType> fleet, City? homeCity, int year, IReadOnlyList<TradeRestriction>? tradeRestrictions = null)
    {
        var restrictions = tradeRestrictions ?? Array.Empty<TradeRestriction>();
        return fleet.Where(a => IsAvailableForLease(a, year) && !TradeRestriction.IsRestricted(a, homeCity, year, restrictions)).ToList();
    }

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
