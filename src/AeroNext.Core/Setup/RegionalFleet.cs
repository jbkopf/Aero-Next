using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Setup;

/// <summary>
/// Which aircraft types are "appropriate" for a home region to start with
/// (M16): prefer types actually manufactured in that region, era-gated the
/// same way QuarterEngine already gates mid-campaign orders. If a region
/// has no home-manufactured type available yet — true for Asia and South
/// America at the 1955 campaign start, since neither had an airliner
/// manufacturer that early — it falls back to every era-available type
/// regardless of manufacturer ("best available import"), rather than
/// hardcoding a specific source region. That fallback happens to resolve
/// to American types today only because that's all the catalog has for
/// 1955; it isn't wired to NorthAmerica specifically, so it stays correct
/// as the catalog grows (see GAME_SETUP_ROADMAP.md's "broaden the aircraft
/// catalog" note).
/// </summary>
public static class RegionalFleet
{
    public static IReadOnlyList<AircraftType> AppropriateAircraft(Region homeRegion, IReadOnlyList<AircraftType> fleet, int year)
    {
        if (!HomeRegions.IsHomeRegion(homeRegion))
        {
            throw new ArgumentException($"'{homeRegion}' is not a home region.", nameof(homeRegion));
        }

        var available = Timeline.AvailableAircraft(fleet, year)
            .OrderBy(a => a.Id, StringComparer.Ordinal)
            .ToList();

        var ownManufacturer = available.Where(a => a.ManufacturerRegion == homeRegion).ToList();

        return ownManufacturer.Count > 0 ? ownManufacturer : available;
    }
}
