using AeroNext.Core.World;

namespace AeroNext.Core.Competition;

/// <summary>
/// How many consecutive quarters each (airline, home region) pair has held
/// sole leadership — see RegionalDominanceTracker for how "leadership" is
/// computed. Keyed by airline name, not the Airline record itself: Airline
/// has structural equality over its Routes list, and a fresh Airline with a
/// new route list gets built every quarter at both call sites, so using the
/// record as a cross-quarter key would never match itself from one quarter
/// to the next.
/// </summary>
public sealed record RegionalDominanceState(IReadOnlyDictionary<(string AirlineName, Region Region), int> StreakByLeader)
{
    public static readonly RegionalDominanceState Empty = new(new Dictionary<(string, Region), int>());

    public int StreakFor(string airlineName, Region region) => StreakByLeader.GetValueOrDefault((airlineName, region), 0);
}
