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

    /// <summary>
    /// globalMultiplier is M13's historical growth knob — the campaign
    /// constructs a new model each year with the era's multiplier from
    /// Timeline.DemandMultiplierForYear, reusing the same tuned scale,
    /// exponent, and cross-region multiplier throughout.
    /// </summary>
    public GravityDemandModel(
        double scale = DefaultScale,
        double exponent = DefaultExponent,
        double crossRegionMultiplier = DefaultCrossRegionMultiplier,
        double globalMultiplier = DefaultGlobalMultiplier)
    {
        _scale = scale;
        _exponent = exponent;
        _crossRegionMultiplier = crossRegionMultiplier;
        _globalMultiplier = globalMultiplier;
    }

    public PairDemand ForPair(City a, City b)
    {
        var distanceKm = Math.Max(MinDistanceKm, GreatCircle.DistanceKm(a, b));
        var raw = _scale * (a.DemandWeight * (double)b.DemandWeight) / Math.Pow(distanceKm, _exponent);

        if (a.Region != b.Region)
        {
            raw *= _crossRegionMultiplier;
        }

        raw *= _globalMultiplier;

        var passengers = (long)Math.Round(raw, MidpointRounding.AwayFromZero);

        return new PairDemand(a, b, distanceKm, passengers);
    }

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
