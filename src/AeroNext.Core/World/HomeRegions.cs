namespace AeroNext.Core.World;

/// <summary>
/// CLAUDE.md specifies exactly 4 player-assignable home regions, matching
/// the always-4-airlines rule. Africa is a chokepoint/bridge region (Cairo,
/// Dakar) — reachable and strategically relevant, but never a player's home
/// base, per M1's original note. This is the first place that distinction
/// becomes a real, queryable thing rather than just a comment.
/// </summary>
public static class HomeRegions
{
    /// <summary>
    /// Fixed, deterministic order — never derived from enum declaration
    /// order or dictionary iteration (determinism rule #3).
    /// </summary>
    public static readonly IReadOnlyList<Region> All = new[]
    {
        Region.NorthAmerica,
        Region.Europe,
        Region.Asia,
        Region.SouthAmerica,
    };

    public static bool IsHomeRegion(Region region) => All.Contains(region);

    /// <summary>
    /// Non-gateway cities in the given home region, sorted by id. Gateway
    /// cities stay inside the ordinary M9 contested mechanic for everyone,
    /// including the local airline — a region's flagship hub isn't a free
    /// starting asset, per "a home region cannot fund a win."
    /// </summary>
    public static IReadOnlyList<City> EligibleStartingCities(Region region, IReadOnlyList<City> allCities)
    {
        if (!IsHomeRegion(region))
        {
            throw new ArgumentException($"'{region}' is not a home region (eligible: {string.Join(", ", All)}).", nameof(region));
        }

        return allCities
            .Where(c => c.Region == region && !c.IsGateway)
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();
    }
}
