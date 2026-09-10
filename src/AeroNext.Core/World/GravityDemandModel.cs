namespace AeroNext.Core.World;

/// <summary>
/// Base O&amp;D demand between city pairs: how many people want to travel A-B
/// this quarter, before any airline, route, or itinerary exists. Standard
/// gravity formula: demand scales with the product of city weights and falls
/// off with the square of distance, then a flat multiplier applies to any
/// pair that crosses a region boundary.
///
/// The cross-region multiplier is an authored game-balance rule, not a
/// hidden hub bonus — it applies identically to every intercontinental
/// pair whether or not any airline actually connects it. It exists because
/// M4 measured, with a same-size route budget, that no distance-decay
/// exponent in a physically sensible range (tested 1.0-2.0) lets an
/// intercontinental network out-earn a regional-only one; the gap narrowed
/// but never flipped. That's the map design pillar "a home region cannot
/// fund a win" failing to emerge from pure population/distance gravity, so
/// it's authored explicitly instead. See docs/SIM_CORE_ROADMAP.md M4.
///
/// Scale, exponent, and the multiplier are placeholders tuned by inspection
/// against known market sizes (e.g. JFK-LHR should dwarf a chokepoint pair
/// like YQX-TER) and, as of M3, against plausible seats-per-quarter for a
/// single airline's route so load factor isn't trivially pinned at 100%
/// everywhere.
///
/// Uses double internally rather than integer math — this is population
/// modeling, not money, so the determinism rule against float/double
/// (CLAUDE.md rule #1) doesn't apply; deterministic floating-point ops given
/// identical inputs still reproduce identical quarters.
///
/// M20: growth over time was previously only ever a single flat
/// <see cref="_globalMultiplier"/> baked in at construction — the same
/// number applied identically to every pair on earth. `schedule`
/// (paired with `year`) generalizes this without changing that
/// existing behavior's shape: when provided, the *global* portion is
/// resolved from the schedule's own global-scoped breakpoints (via
/// <see cref="Timeline.DemandMultiplierForYear"/> — the exact computation
/// every real call site already did upstream, now done once, inside the
/// model, instead of twice), applied once per pair exactly as
/// <see cref="_globalMultiplier"/> always has been. Region- and
/// city-scoped breakpoints are a genuinely new, *separate* per-city factor
/// (1.0 when a city carries no override) multiplied in on top, once per
/// city in the pair — deliberately not folded into the global figure
/// itself, since doing so would make an unscoped pair's growth apply
/// *twice* (once per city) instead of the flat once-per-pair effect this
/// class has always had, breaking byte-identical output for every existing
/// caller that doesn't use scoped breakpoints yet. When `schedule`
/// is null (every existing call site, and any new one that doesn't need
/// this), behavior is unchanged: <see cref="_globalMultiplier"/> applies
/// exactly as it always has, and the region/city factor is always 1.0.
///
/// M21 (trend/event split): each of the global, region, and city factors
/// above is itself now a composition of two independently-resolved
/// layers — <see cref="DemandGrowthPointKind.Trend"/> (the sustained
/// secular curve, unchanged resolution/fallback from M20) times
/// <see cref="DemandGrowthPointKind.Event"/> (a 747/oil-crisis/9-11
/// style discrete shock, always defaulting to neutral 1.0 when none is
/// active, at every scope including global) — rather than one flat pool
/// per scope where a trend breakpoint and an event breakpoint at the
/// same scope would otherwise silently clobber each other instead of
/// composing.
///
/// M21 (performance fix, found stress-testing a large batch of real
/// events — every Olympic Games 1956-2015): every one of the factors
/// above depends only on `schedule`/`year`, both fixed for the lifetime
/// of one model instance (a fresh instance is constructed once per
/// quarter at every real call site) — never on which two cities
/// `ForPair` is asked about. Re-deriving them from a `Where`/`GroupBy`
/// scan of the whole schedule on *every* `ForPair` call was correct but
/// wastefully repeated: `AllPairs` alone calls `ForPair` O(cities^2)
/// times a quarter, and callers like `BaselineAi.FindBestUnservedPair`
/// call `AllPairs` itself multiple times a quarter — with a schedule
/// large enough (the Olympics round roughly quadrupled
/// `historical_events.json`'s size), a 150-game/244-quarter sweep that
/// used to finish in minutes stretched out far enough to look hung.
/// The constructor now resolves the global factor and every distinct
/// region/city's own factor exactly **once**, into a couple of small
/// dictionaries; `ForPair` become simple lookups. Deliberately not a
/// resolution-logic change — every intermediate value and fallback rule
/// above is identical, only *when* it's computed differs, confirmed by
/// the full existing test suite passing unchanged.
///
/// M63 (hub-and-spoke demand, step 2 of docs/HUB_DEMAND_DESIGN.md's
/// recommended Path B): a real, if modest, per-city innate demand bonus
/// for cities with a nonzero <see cref="City.HubQuality"/> — representing
/// the outward demand pressure of unmodeled small cities that really
/// travel via a hub (the Buffalo-flies-via-NYC-to-Miami example the
/// feature was built around), a fact about the city itself, not about any
/// one airline's built network (unlike M62's <see cref="Airlines.NetworkPlanner"/>
/// changes, which are airline-specific and never touch this class).
/// `enableHubDemandBonus` follows the exact `schedule`/`year` convention
/// already established above: optional, defaults `false`, resolved once
/// at construction into <see cref="_hubDemandBonusEnabled"/>; `ForPair`
/// stays a pure lookup, swapping in <see cref="EffectiveWeight"/> for the
/// raw `DemandWeight` on each side of the product. When disabled (every
/// pre-M63 call site, and any new one that doesn't opt in) `HubQuality` is
/// never read and output is byte-identical to pre-M63 behavior — the same
/// "off is exactly old behavior" guarantee this class already gives
/// `schedule`/`year`.
///
/// Deliberately a flat additive term (`DemandWeight + HubQuality`), not
/// proportional to a neighboring city's weight and not a change to
/// `exponent` — the two specific mechanisms an earlier M60 attempt at
/// this same idea tried and measured wrong (see docs/HUB_DEMAND_ROADMAP.md
/// and the now-reverted M60 history in docs/OPEN_WORK.md): an additive
/// *catchment* bonus scaled to nearby cities' weight structurally favored
/// small cities more (a bigger percentage of their own small base); an
/// exponent discount compounds multiplicatively over real-world distances
/// and measured a 31x world-demand inflation at the strength needed to
/// flip any real ranking. `HubQuality` is a flat, authored-per-city,
/// <see cref="CityCatalog.MaxHubQualityToDemandWeightRatio"/>-capped
/// number with no dependency on which partner city is on the other side
/// of a pair or how far away it is — bounded in advance, verified against
/// the real dataset before any real call site enables it (see the
/// `RealDataset_*` tests in this class's own test file).
/// </summary>
public sealed class GravityDemandModel
{
    private const double DefaultScale = 7.5e6;
    private const double DefaultExponent = 2.0;
    private const double DefaultCrossRegionMultiplier = 6.0;
    private const double DefaultGlobalMultiplier = 1.0;
    private const double MinDistanceKm = 100.0;

    private readonly double _scale;
    private readonly double _exponent;
    private readonly double _crossRegionMultiplier;
    private readonly double _globalMultiplier;

    /// <summary>Trend x Event, global scope - identical for every pair, so resolved once here rather than per ForPair call.</summary>
    private readonly double _resolvedGlobalMultiplier;

    /// <summary>Trend x Event per region, populated only for regions that actually have a schedule entry - a region with none defaults to 1.0 (not present as a key).</summary>
    private readonly IReadOnlyDictionary<Region, double> _regionMultiplierByRegion;

    /// <summary>Trend x Event per city, populated only for cities that actually have a schedule entry - same "absent = 1.0" convention as the region dictionary.</summary>
    private readonly IReadOnlyDictionary<string, double> _cityMultiplierByCityId;

    /// <summary>M63: resolved once at construction from the constructor's own `enableHubDemandBonus` parameter - see this class's own M63 doc comment above.</summary>
    private readonly bool _hubDemandBonusEnabled;

    /// <summary>
    /// Factor D (docs/AUSTRALASIA_TUNING_EXPERIMENTS.md): a flat multiplier
    /// applied to a pair whose endpoints are *both* in the keyed region -
    /// the intra-region sibling of <see cref="_crossRegionMultiplier"/>.
    /// Absent region = 1.0 (no change); an empty map is exactly today's
    /// behavior. Distinct from <see cref="_regionMultiplierByRegion"/>,
    /// which is the M20 growth schedule's per-endpoint factor.
    /// </summary>
    private readonly IReadOnlyDictionary<Region, double> _intraRegionMultipliers;

    /// <summary>
    /// globalMultiplier is M13's historical growth knob — the campaign
    /// constructs a new model each year with the era's multiplier from
    /// Timeline.DemandMultiplierForYear, reusing the same tuned scale,
    /// exponent, and cross-region multiplier throughout. M20:
    /// schedule/year supersede globalMultiplier entirely when both are
    /// provided (the richer, scope-aware path); leave them null to keep
    /// today's exact flat-multiplier behavior. M63: enableHubDemandBonus
    /// defaults false, same "off is exactly old behavior" guarantee.
    /// intraRegionMultipliers (factor D) defaults null/empty, same
    /// guarantee.
    /// </summary>
    public GravityDemandModel(
        double scale = DefaultScale,
        double exponent = DefaultExponent,
        double crossRegionMultiplier = DefaultCrossRegionMultiplier,
        double globalMultiplier = DefaultGlobalMultiplier,
        IReadOnlyList<DemandGrowthPoint>? schedule = null,
        int? year = null,
        bool enableHubDemandBonus = false,
        IReadOnlyDictionary<Region, double>? intraRegionMultipliers = null)
    {
        _scale = scale;
        _exponent = exponent;
        _crossRegionMultiplier = crossRegionMultiplier;
        _globalMultiplier = globalMultiplier;
        _hubDemandBonusEnabled = enableHubDemandBonus;
        _intraRegionMultipliers = intraRegionMultipliers ?? new Dictionary<Region, double>();

        if (schedule is null || year is null)
        {
            _resolvedGlobalMultiplier = globalMultiplier;
            _regionMultiplierByRegion = new Dictionary<Region, double>();
            _cityMultiplierByCityId = new Dictionary<string, double>(StringComparer.Ordinal);
            return;
        }

        var y = year.Value;
        var trend = Timeline.DemandMultiplierForYear(schedule, y);
        var globalEvents = schedule.Where(p => p.Region is null && p.CityId is null);
        _resolvedGlobalMultiplier = trend * ResolveEvents(globalEvents, y);

        var regionMultipliers = new Dictionary<Region, double>();
        foreach (var region in schedule.Where(p => p.Region is not null).Select(p => p.Region!.Value).Distinct())
        {
            var matches = schedule.Where(p => p.Region == region && p.CityId is null).ToList();
            regionMultipliers[region] = ResolveTrend(matches, y) * ResolveEvents(matches, y);
        }

        _regionMultiplierByRegion = regionMultipliers;

        var cityMultipliers = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var cityId in schedule.Where(p => p.CityId is not null).Select(p => p.CityId!).Distinct(StringComparer.Ordinal))
        {
            var matches = schedule.Where(p => p.CityId == cityId).ToList();
            cityMultipliers[cityId] = ResolveTrend(matches, y) * ResolveEvents(matches, y);
        }

        _cityMultiplierByCityId = cityMultipliers;
    }

    public PairDemand ForPair(City a, City b)
    {
        var distanceKm = Math.Max(MinDistanceKm, GreatCircle.DistanceKm(a, b));
        var raw = _scale * (EffectiveWeight(a) * EffectiveWeight(b)) / Math.Pow(distanceKm, _exponent);

        if (a.Region != b.Region)
        {
            raw *= _crossRegionMultiplier;
        }
        else if (_intraRegionMultipliers.TryGetValue(a.Region, out var intraRegionMultiplier))
        {
            raw *= intraRegionMultiplier;
        }

        raw *= _resolvedGlobalMultiplier;
        raw *= EffectiveMultiplier(a) * EffectiveMultiplier(b);

        var passengers = (long)Math.Round(raw, MidpointRounding.AwayFromZero);

        return new PairDemand(a, b, distanceKm, passengers);
    }

    /// <summary>M20/M21: the region/city-scoped portion, resolved per city from the precomputed dictionaries - a plain dictionary lookup, 1.0 when the city (or its region) has no schedule entry at all.</summary>
    private double EffectiveMultiplier(City city) =>
        _regionMultiplierByRegion.GetValueOrDefault(city.Region, 1.0) * _cityMultiplierByCityId.GetValueOrDefault(city.Id, 1.0);

    /// <summary>
    /// M63: DemandWeight plus a bounded, flat HubQuality addition when the
    /// hub-demand bonus is enabled - see this class's own M63 doc comment
    /// above for why this shape (not proportional, not exponent-based).
    /// When disabled, reduces to exactly city.DemandWeight, so HubQuality
    /// is never read at all.
    /// </summary>
    private double EffectiveWeight(City city) =>
        _hubDemandBonusEnabled ? city.DemandWeight + city.HubQuality : city.DemandWeight;

    /// <summary>The Trend portion only, for a given already-scope-filtered candidate set — falls back to neutral 1.0 when that scope has no trend entry at all (matches EffectiveMultiplier's own pre-existing region/city fallback).</summary>
    private static double ResolveTrend(IEnumerable<DemandGrowthPoint> candidates, int year) =>
        Timeline.LatestAtOrBefore(candidates.Where(p => p.Kind == DemandGrowthPointKind.Trend), year)?.GlobalMultiplier ?? 1.0;

    /// <summary>
    /// M21 (correctness fix found during round 3's own verification,
    /// not incidental): every *independent* event active for this scope
    /// composes, rather than only the single most recent event
    /// breakpoint winning — grouped by `EventGroup`
    /// (`DemandGrowthCatalog` requires one on every Event breakpoint),
    /// each group resolved via the same "latest at or before this year"
    /// rule (1.0 if that specific event hasn't started, or has already
    /// reverted), then every group's own result multiplied together.
    /// Confirmed the bug directly before writing this: without grouping,
    /// a later, entirely unrelated event's own breakpoint (e.g. the 1973
    /// oil crisis) silently overwrote an earlier event's value (the
    /// 747's permanent +15%) rather than composing with it — and that
    /// earlier event never came back, even past its own reversion year,
    /// since the "latest breakpoint" was now the later event's.
    /// </summary>
    private static double ResolveEvents(IEnumerable<DemandGrowthPoint> candidates, int year) =>
        candidates
            .Where(p => p.Kind == DemandGrowthPointKind.Event)
            .GroupBy(p => p.EventGroup)
            .Select(group => Timeline.LatestAtOrBefore(group, year)?.GlobalMultiplier ?? 1.0)
            .Aggregate(1.0, (product, multiplier) => product * multiplier);

    /// <summary>
    /// Every unordered city pair, sorted by origin id then destination id for
    /// deterministic iteration (determinism rule #3).
    /// </summary>
    public IReadOnlyList<PairDemand> AllPairs(IReadOnlyList<City> cities)
    {
        var ordered = cities.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
        var pairs = new List<PairDemand>(ordered.Count * (ordered.Count - 1) / 2);

        for (var i = 0; i < ordered.Count; i++)
        {
            for (var j = i + 1; j < ordered.Count; j++)
            {
                pairs.Add(ForPair(ordered[i], ordered[j]));
            }
        }

        return pairs;
    }
}
