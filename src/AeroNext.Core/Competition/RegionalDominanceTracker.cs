using AeroNext.Core.World;

namespace AeroNext.Core.Competition;

/// <summary>
/// M23: tracks which airline leads each region's passenger traffic, and
/// for how many consecutive quarters — the basis for the regional-
/// dominance win condition (see WinConditionChecker). "Leads" means carries
/// the most passengers this quarter on routes it itself operates that
/// touch that region (either endpoint) — CompetitiveRouteUsage.SeatsUsed,
/// not CompetitivePairResult (which can include connecting itineraries
/// through a hub the airline doesn't actually fly to). Scoped to
/// DominanceTrackedRegions.All (the 6 home regions plus Africa) — see
/// that class's own doc comment for why Africa counts here despite still
/// never being home-eligible.
/// </summary>
public static class RegionalDominanceTracker
{
    /// <param name="eligibleAirlineNames">
    /// Airlines allowed to hold or extend a leadership streak this quarter
    /// — non-bankrupt only. A frozen, bankrupt airline's route list can
    /// still technically carry the most passengers in a region nobody else
    /// contests, but it shouldn't be able to "win" that way.
    /// </param>
    public static RegionalDominanceState Advance(
        RegionalDominanceState previous,
        CompetitiveMarketResult marketResult,
        IReadOnlySet<string> eligibleAirlineNames)
    {
        var next = new Dictionary<(string, Region), int>();

        foreach (var region in DominanceTrackedRegions.All)
        {
            var carriedByAirlineName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var usage in marketResult.RouteUsage)
            {
                var name = usage.Airline.Name;
                if (!eligibleAirlineNames.Contains(name))
                {
                    continue;
                }

                if (usage.Route.Origin.Region != region && usage.Route.Destination.Region != region)
                {
                    continue;
                }

                carriedByAirlineName[name] = carriedByAirlineName.GetValueOrDefault(name) + usage.SeatsUsed;
            }

            if (carriedByAirlineName.Count == 0)
            {
                continue;
            }

            var maxCarried = carriedByAirlineName.Values.Max();
            if (maxCarried == 0)
            {
                continue;
            }

            var leaders = carriedByAirlineName
                .Where(kv => kv.Value == maxCarried)
                .Select(kv => kv.Key)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

            if (leaders.Count != 1)
            {
                continue; // a tie extends nobody's streak
            }

            var leaderName = leaders[0];
            next[(leaderName, region)] = previous.StreakFor(leaderName, region) + 1;
        }

        return new RegionalDominanceState(next);
    }
}
