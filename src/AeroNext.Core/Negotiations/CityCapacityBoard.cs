using AeroNext.Core.Airlines;
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
///
/// Hub growth profiles (2026-10-23, docs/HUB_CAPACITY_GROWTH_DESIGN.md):
/// the rule above caps every city at double its starting capacity, which
/// the biggest hubs reach by ~1975 while their demand keeps growing to
/// 5-190x. A major non-gateway hub with a profile (HubCapacityProfile, from
/// its real traffic history) gets whichever is higher - the rule above or
/// its profile - re-evaluated every year, from quarter 1 (a hub already
/// big in 1955 starts above its authored capacity). Gateways stay on the
/// rule alone: their gates are meant to be scarce. With no profiles, every
/// city behaves exactly as before.
///
/// Hub demand coverage (2026-10-24, docs/HUB_CAPACITY_GROWTH_DESIGN.md):
/// even with profiles, hub demand ran 4-35x the seats flown there, so most
/// of it could never be competed for. A profiled hub's capacity also never
/// falls below the aircraft needed to fly <see cref="HubDemandCoverage"/>
/// of its current demand, at the seats its own flights average
/// (<see cref="SeatsPerAircraftByCity"/>) - re-evaluated every quarter
/// when the caller supplies those averages, a ratchet like the rest.
/// </summary>
public sealed record CityCapacityBoard(
    IReadOnlyDictionary<string, long> BaselineDemandByCity,
    IReadOnlyDictionary<string, int> CapacityBonusByCity,
    IReadOnlyDictionary<string, HubCapacityProfile>? HubProfiles = null)
{
    /// <summary>(Proposed, tunable) — how often, in quarters, capacity is re-evaluated. 20 = every 5 years, matching the real pace of a terminal/runway expansion project, not a continuous dial.</summary>
    public const int ExpansionPeriodQuarters = 20;

    /// <summary>(Proposed, tunable) — the growth-per-slot pace a city at ReferenceBaseCapacity keeps; every additional 50% of demand growth over baseline earns it one more slot. Smaller cities need proportionally more growth per slot (see GrowthBreakpoints); larger cities need proportionally less.</summary>
    public const double BaseGrowthIncrementFraction = 0.5;

    /// <summary>(Proposed, tunable) — the starting capacity at which BaseGrowthIncrementFraction applies unchanged. A city smaller than this needs more relative growth per slot; a city larger than this needs less.</summary>
    public const int ReferenceBaseCapacity = 2;

    /// <summary>(Proposed, tunable) - the share of a profiled hub's demand its capacity must always be able to fly.</summary>
    public const double HubDemandCoverage = 0.8;

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
    /// <param name="hubProfiles">Growth profiles for major hubs; a profiled non-gateway city starts at its profile's 1955 capacity if that's above its authored one.</param>
    public static CityCapacityBoard Initialize(IReadOnlyList<City> cities, GravityDemandModel campaignStartDemandModel,
        IReadOnlyDictionary<string, HubCapacityProfile>? hubProfiles = null) =>
        new(
            TotalDemandByCity(cities, campaignStartDemandModel),
            cities.ToDictionary(c => c.Id, c => ProfileBonus(c, hubProfiles, Timeline.StartYear), StringComparer.Ordinal),
            hubProfiles);

    /// <summary>
    /// The bonus a city's growth profile gives it in a year: its profile's
    /// capacity above its starting capacity, or 0 - for a gateway, a city
    /// with no profile, or a year the profile hasn't outgrown the start yet.
    /// </summary>
    public static int ProfileBonus(City city, IReadOnlyDictionary<string, HubCapacityProfile>? hubProfiles, int year)
    {
        if (city.IsGateway || hubProfiles is null || !hubProfiles.TryGetValue(city.Id, out var profile))
        {
            return 0;
        }

        var startingCapacity = city.GateCapacity ?? city.AutoCapacity ?? 0;

        return Math.Max(0, profile.CapacityIn(year) - startingCapacity);
    }

    /// <summary>
    /// The bonus a profiled hub needs so its capacity can fly
    /// <see cref="HubDemandCoverage"/> of its demand at seatsPerAircraft
    /// seats per aircraft per quarter, or 0 - for a gateway, a city with no
    /// profile, or no seat average to go on.
    /// </summary>
    public static int DemandCoverageBonus(City city, IReadOnlyDictionary<string, HubCapacityProfile>? hubProfiles, long cityDemand, double seatsPerAircraft)
    {
        if (city.IsGateway || hubProfiles is null || !hubProfiles.ContainsKey(city.Id) || seatsPerAircraft <= 0)
        {
            return 0;
        }

        var startingCapacity = city.GateCapacity ?? city.AutoCapacity ?? 0;
        var requiredCapacity = (int)Math.Ceiling(HubDemandCoverage * cityDemand / seatsPerAircraft);

        return Math.Max(0, requiredCapacity - startingCapacity);
    }

    /// <summary>
    /// Average seats per aircraft per quarter on the routes that start or end
    /// at each city (an aircraft only passing through as a refuelling stop
    /// carries none of that city's demand), falling back to the average
    /// across every route for a city nobody serves yet. Empty when there
    /// are no routes at all.
    /// </summary>
    public static IReadOnlyDictionary<string, double> SeatsPerAircraftByCity(IEnumerable<Route> routes, IReadOnlyList<City> cities)
    {
        var allRoutes = routes.ToList();
        var totalAircraft = allRoutes.Sum(r => (long)r.AircraftCount);

        if (totalAircraft == 0)
        {
            return new Dictionary<string, double>(StringComparer.Ordinal);
        }

        var networkAverage = (double)allRoutes.Sum(r => r.SeatsPerQuarter) / totalAircraft;

        return cities.ToDictionary(
            c => c.Id,
            c =>
            {
                var serving = allRoutes.Where(r => r.Origin.Id == c.Id || r.Destination.Id == c.Id).ToList();
                var aircraft = serving.Sum(r => (long)r.AircraftCount);
                return aircraft == 0 ? networkAverage : (double)serving.Sum(r => r.SeatsPerQuarter) / aircraft;
            },
            StringComparer.Ordinal);
    }

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
    /// <param name="seatsPerAircraftByCity">Each city's <see cref="SeatsPerAircraftByCity"/> entering this quarter; when given, profiled hubs also get <see cref="DemandCoverageBonus"/>.</param>
    public CityCapacityBoard AdvanceQuarter(int quarterNumber, IReadOnlyList<City> cities, GravityDemandModel demandModel,
        IReadOnlyDictionary<string, double>? seatsPerAircraftByCity = null)
    {
        var onCadence = quarterNumber % ExpansionPeriodQuarters == 0;
        var hasProfiles = HubProfiles is { Count: > 0 };

        if (!onCadence && !hasProfiles)
        {
            return this;
        }

        var bonus = new Dictionary<string, int>(CapacityBonusByCity);

        if (onCadence)
        {
            var currentDemandByCity = TotalDemandByCity(cities, demandModel);

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
        }

        // Hub growth profiles: re-evaluated every quarter against the
        // current year, so a hub grows the year its real airport did.
        if (hasProfiles)
        {
            var year = Timeline.YearForQuarter(quarterNumber);

            foreach (var city in cities)
            {
                bonus[city.Id] = Math.Max(bonus.GetValueOrDefault(city.Id), ProfileBonus(city, HubProfiles, year));
            }

            if (seatsPerAircraftByCity is { Count: > 0 })
            {
                var currentDemandByCity = TotalDemandByCity(cities, demandModel);

                foreach (var city in cities)
                {
                    var coverage = DemandCoverageBonus(city, HubProfiles, currentDemandByCity[city.Id], seatsPerAircraftByCity.GetValueOrDefault(city.Id));
                    bonus[city.Id] = Math.Max(bonus[city.Id], coverage);
                }
            }
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
