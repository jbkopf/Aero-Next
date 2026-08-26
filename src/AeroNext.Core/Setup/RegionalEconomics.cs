using AeroNext.Core.World;

namespace AeroNext.Core.Setup;

/// <summary>
/// A region's starting-cash strength, derived from data already in
/// cities.json rather than a second, independently-tuned economic table
/// (M16). Deliberately scoped to the same non-gateway cities
/// <see cref="HomeRegions.EligibleStartingCities"/> already treats as
/// "the region" for setup purposes — using every city tagged with that
/// Region (including gateways and remote chokepoints like Anchorage/Wake,
/// which carry almost no demand by design per CLAUDE.md) would drag a
/// region's average down for reasons that have nothing to do with its
/// economic strength.
/// </summary>
public static class RegionalEconomics
{
    /// <summary>
    /// The region's average eligible-city demand weight, normalized against
    /// the average across all four home regions' eligible cities. 1.0 is
    /// exactly average; above/below 1.0 scales starting cash up/down.
    /// </summary>
    public static double Factor(Region region, IReadOnlyList<City> allCities)
    {
        var regionAverage = HomeRegions.EligibleStartingCities(region, allCities).Average(c => (double)c.DemandWeight);

        var globalAverage = HomeRegions.All
            .SelectMany(r => HomeRegions.EligibleStartingCities(r, allCities))
            .Average(c => (double)c.DemandWeight);

        return regionAverage / globalAverage;
    }
}
