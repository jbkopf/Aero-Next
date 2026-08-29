namespace AeroNext.Core.World;

/// <summary>
/// CLAUDE.md's always-4-airlines rule is about player *slots*, not region
/// count — since the original Asia region split into EastAsia/
/// SouthWestAsia/AustralAsia (an investigation into Asia's ~165° of
/// longitude sprawl, the largest of any region), there are 6 home regions
/// for exactly 4 slots. GameSetup.CreateSeats is a real draft: humans pick
/// any of these 6, and any slot they don't fill goes to the richest
/// unclaimed region by RegionalEconomics.Factor — so not every home region
/// appears in every game. Africa is a separate concept, a chokepoint/bridge
/// region (Cairo, Dakar) — reachable and strategically relevant, but never
/// player-assignable at all, per M1's original note.
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
        Region.EastAsia,
        Region.SouthWestAsia,
        Region.AustralAsia,
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
