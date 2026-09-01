using AeroNext.Core.World;

namespace AeroNext.Core.Negotiations;

/// <summary>
/// M46: tracks each city's earned capacity bonus — how many additional
/// slots it's gained over its own 1955 starting capacity via periodic
/// demand-driven expansion, modeling real terminal/runway growth. A
/// separate board from <see cref="LandingRightsBoard"/> rather than a
/// field folded into it, since this applies uniformly to *every* city
/// (gateway or not), while `LandingRightsBoard` is specifically about the
/// negotiated-ownership mechanic that only ever applies to the 15
/// gateways. Immutable, same pattern as every other piece of persistent
/// turn state in the sim.
///
/// M46 round 2: growth is no longer one flat fraction for every city —
/// each city gets its own schedule of growth breakpoints
/// (<see cref="GrowthBreakpoints"/>), sized to its own starting capacity.
/// A small city that starts with 1 slot can earn at most 1 more, and only
/// after doubling its own demand; a large city with 4 starts closer
/// together and can earn up to 4 more. Without this, a small city could
/// rack up just as many bonus slots as a major hub purely from a large
/// *relative* demand swing, even though its *absolute* passenger volume
/// never remotely justified a real terminal expansion — the user's own
/// stated intent this round: real airports don't add gates in proportion
/// to a percentage change alone, and a small city shouldn't accumulate
/// capacity it has no real use for.
/// </summary>
public sealed record CityCapacityBoard(
    IReadOnlyDictionary<string, long> BaselineDemandByCity,
    IReadOnlyDictionary<string, int> CapacityBonusByCity)
{
    /// <summary>(Proposed, tunable) — how often, in quarters, capacity is re-evaluated. 20 = every 5 years, matching the real pace of a terminal/runway expansion project, not a continuous dial.</summary>
    public const int ExpansionPeriodQuarters = 20;

    /// <summary>(Proposed, tunable) — the growth-per-slot pace a city at ReferenceBaseCapacity keeps; every additional 50% of demand growth over baseline earns it one more slot. Smaller cities need proportionally more growth per slot (see GrowthBreakpoints); larger cities need proportionally less.</summary>
    public const double BaseGrowthIncrementFraction = 0.5;

    /// <summary>(Proposed, tunable) — the starting capacity at which BaseGrowthIncrementFraction applies unchanged. A city smaller than this needs more relative growth per slot; a city larger than this needs less.</summary>
    public const int ReferenceBaseCapacity = 2;

    /// <summary>
    /// This city's own growth schedule — the ascending list of
    /// demand-growth multiples (relative to its own baseline) at which it
    /// earns its 1st, 2nd, 3rd... bonus slot. Length always equals the
    /// city's own starting capacity (GateCapacity or AutoCapacity,
    /// whichever it carries — M45's own mutual-exclusivity rule) — a
    /// city that starts with N slots can never earn more than N more,
    /// full stop, regardless of how much demand grows; a city with
    /// neither field set (a data-completeness gap, not a real case in
    /// practice) gets an empty schedule and never expands. Breakpoints
    /// are spaced BaseGrowthIncrementFraction x ReferenceBaseCapacity /
    /// startingCapacity apart — a city at exactly ReferenceBaseCapacity
    /// reproduces today's original flat-50%-per-slot pace exactly; a
    /// smaller city's breakpoints are proportionally farther apart
    /// (harder to reach, and there are fewer of them to reach); a larger
    /// city's are proportionally closer together (and there are more).
    /// </summary>
    public static IReadOnlyList<double> GrowthBreakpoints(City city)
    {
        var startingCapacity = city.GateCapacity ?? city.AutoCapacity ?? 0;

        if (startingCapacity <= 0)
        {
            return Array.Empty<double>();
        }

        var incrementFraction = BaseGrowthIncrementFraction * ReferenceBaseCapacity / startingCapacity;

        return Enumerable.Range(1, startingCapacity)
            .Select(step => 1.0 + step * incrementFraction)
            .ToList();
    }

    /// <summary>
    /// Computed once, at campaign start (quarter 1, using a demand model
    /// for <see cref="Timeline.StartYear"/>) — every city's own total
    /// demand (summed across every pair touching it) becomes the fixed
    /// reference point every later check measures growth against. Zero
    /// bonus everywhere; nothing has expanded yet.
    /// </summary>
    public static CityCapacityBoard Initialize(IReadOnlyList<City> cities, GravityDemandModel campaignStartDemandModel) =>
        new(
            TotalDemandByCity(cities, campaignStartDemandModel),
            cities.ToDictionary(c => c.Id, _ => 0, StringComparer.Ordinal));

    /// <summary>
    /// A no-op (returns this board unchanged) unless quarterNumber lands
    /// on the fixed periodic cadence — capacity doesn't creep up
    /// continuously, it's re-evaluated in discrete, infrequent
    /// increments, same as a real expansion project. On a check quarter,
    /// each city's bonus becomes how many of its own GrowthBreakpoints
    /// its current-demand-over-baseline ratio has crossed — a pure
    /// function of (its fixed 1955 baseline, its current total demand,
    /// its own schedule), never incremented from a "demand at last
    /// check" value, so a missed or delayed check can never drift the
    /// result. `Max` with the existing bonus makes this a ratchet: a
    /// later demand dip (a shock event reverting) never un-builds a
    /// terminal already earned. A city with zero baseline demand never
    /// earns anything — a zero baseline would make every breakpoint
    /// read as trivially crossed, which is never the intent.
    /// </summary>
    public CityCapacityBoard AdvanceQuarter(int quarterNumber, IReadOnlyList<City> cities, GravityDemandModel demandModel)
    {
        if (quarterNumber % ExpansionPeriodQuarters != 0)
        {
            return this;
        }

        var currentDemandByCity = TotalDemandByCity(cities, demandModel);
        var bonus = new Dictionary<string, int>(CapacityBonusByCity);

        foreach (var city in cities)
        {
            var baseline = BaselineDemandByCity.GetValueOrDefault(city.Id);

            if (baseline <= 0)
            {
                continue;
            }

            var earnedBonus = GrowthBreakpoints(city).Count(breakpoint => currentDemandByCity.GetValueOrDefault(city.Id) >= baseline * breakpoint);

            bonus[city.Id] = Math.Max(bonus.GetValueOrDefault(city.Id), earnedBonus);
        }

        return this with { CapacityBonusByCity = bonus };
    }

    /// <summary>Every pair touching a city, summed — the same "total demand at this city" definition Initialize and AdvanceQuarter both use, so growth is measured on a like-for-like basis.</summary>
    private static IReadOnlyDictionary<string, long> TotalDemandByCity(IReadOnlyList<City> cities, GravityDemandModel demandModel)
    {
        var total = cities.ToDictionary(c => c.Id, _ => 0L, StringComparer.Ordinal);

        foreach (var pair in demandModel.AllPairs(cities))
        {
            total[pair.Origin.Id] += pair.PassengersPerQuarter;
            total[pair.Destination.Id] += pair.PassengersPerQuarter;
        }

        return total;
    }
}
