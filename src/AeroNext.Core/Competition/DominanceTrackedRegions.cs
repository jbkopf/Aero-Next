using AeroNext.Core.World;

namespace AeroNext.Core.Competition;

/// <summary>
/// Africa-counts-toward-the-win-condition follow-up (docs/OPEN_WORK.md):
/// the set of regions RegionalDominanceTracker computes leadership over -
/// deliberately a distinct list from HomeRegions.All, not a superset
/// exposed there, since "can this region be led" and "can an airline be
/// based here" are now two different questions. Africa was originally
/// excluded from both by the same boundary (RegionalDominanceTracker's
/// own prior doc comment, and RegionalEconomics.Factor still today) - a
/// real comparison against AustralAsia (the current weakest home region)
/// found Africa's own eligible cities carry real, if modest, demand
/// (Cairo 50, Lagos 45, Nairobi 40 - not the near-zero chokepoint numbers
/// CLAUDE.md describes for Wake/Honolulu/Anchorage/Gander/Azores), and a
/// touch-matrix diagnostic found it's genuinely under-contested today
/// (Europe touches it 96.4% of the time, with only SouthWestAsia at
/// 59.0% as real competition - unlike Europe's actual foreign targets,
/// which are dominated by entrenched incumbents). Africa still never
/// becomes home-eligible (HomeRegions.All, HomeRegions.EligibleStartingCities,
/// RegionalEconomics.Factor are all untouched by this list) - this is
/// deliberately scoped to "can be led," not "can be someone's home."
/// Explicit fixed order, not Enum.GetValues&lt;Region&gt;() - matches
/// HomeRegions.All's own convention (determinism rule #3) even though no
/// current consumer's outcome depends on this list's iteration order.
/// </summary>
public static class DominanceTrackedRegions
{
    public static readonly IReadOnlyList<Region> All = HomeRegions.All.Append(Region.Africa).ToList();
}
