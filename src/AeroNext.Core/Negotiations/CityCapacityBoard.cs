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
/// </summary>
public sealed record CityCapacityBoard(
    IReadOnlyDictionary<string, long> BaselineDemandByCity,
    IReadOnlyDictionary<string, int> CapacityBonusByCity)
{
    /// <summary>(Proposed, tunable) — how often, in quarters, capacity is re-evaluated. 20 = every 5 years, matching the real pace of a terminal/runway expansion project, not a continuous dial.</summary>
    public const int ExpansionPeriodQuarters = 20;

    /// <summary>(Proposed, tunable) — every additional 50% of demand growth over a city's own 1955 baseline earns it one more slot.</summary>
    public const double GrowthIncrementFraction = 0.5;

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
    /// each city's bonus is recomputed as a pure function of (its fixed
    /// 1955 baseline, its current total demand, its own previous bonus)
    /// — never incremented from a "demand at last check" value, so a
    /// missed or delayed check can never drift the result. `Max` with
    /// the existing bonus makes this a ratchet: a later demand dip (a
    /// shock event reverting) never un-builds a terminal already earned.
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
            var current = currentDemandByCity.GetValueOrDefault(city.Id);
            var grown = Math.Max(0L, current - baseline);
            var earnedBonus = baseline > 0 ? (int)(grown / (baseline * GrowthIncrementFraction)) : 0;

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
