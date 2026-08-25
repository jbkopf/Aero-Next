namespace AeroNext.Core.World;

/// <summary>
/// Base O&amp;D demand between city pairs: how many people want to travel A-B
/// this quarter, before any airline, route, or itinerary exists. Standard
/// gravity formula: demand scales with the product of city weights and falls
/// off with the square of distance.
///
/// Scale and exponent are placeholders tuned by inspection against known
/// market sizes (e.g. JFK-LHR should dwarf a chokepoint pair like YQX-TER).
/// Expect to retune once M4 evaluates whether hub-and-spoke emerges.
///
/// Uses double internally rather than integer math — this is population
/// modeling, not money, so the determinism rule against float/double
/// (CLAUDE.md rule #1) doesn't apply; deterministic floating-point ops given
/// identical inputs still reproduce identical quarters.
/// </summary>
public sealed class GravityDemandModel
{
    private const double DefaultScale = 5.0e8;
    private const double DefaultExponent = 2.0;
    private const double MinDistanceKm = 100.0;

    private readonly double _scale;
    private readonly double _exponent;

    public GravityDemandModel(double scale = DefaultScale, double exponent = DefaultExponent)
    {
        _scale = scale;
        _exponent = exponent;
    }

    public PairDemand ForPair(City a, City b)
    {
        var distanceKm = Math.Max(MinDistanceKm, GreatCircle.DistanceKm(a, b));
        var raw = _scale * (a.DemandWeight * (double)b.DemandWeight) / Math.Pow(distanceKm, _exponent);
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
