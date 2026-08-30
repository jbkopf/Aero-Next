namespace AeroNext.Core.Competition;

/// <summary>One airline's final standing when the campaign ends without a winner.</summary>
public sealed record RankedAirline(string AirlineName, int RegionsLed, long TotalPassengersCarried, Money NetWorth);

/// <summary>
/// M23: if the 1955-2015 timeline ends before anyone meets
/// WinConditionChecker's threshold, rank all 4 airlines by how close they
/// got — regions currently led (streak &gt;= 1 as of the final quarter),
/// then total passengers carried, then final net worth — kept
/// philosophically consistent with the chosen win metric (closest to
/// actually winning) rather than switching to a different scoring axis
/// only at the very end.
/// </summary>
public static class CampaignRanking
{
    public static IReadOnlyList<RankedAirline> Rank(
        RegionalDominanceState finalState,
        CompetitiveMarketResult finalMarketResult,
        IReadOnlyDictionary<string, Money> netWorthByName)
    {
        var regionsLedByName = finalState.StreakByLeader.Keys
            .GroupBy(k => k.AirlineName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var passengersByName = finalMarketResult.RouteUsage
            .GroupBy(u => u.Airline.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(u => u.SeatsUsed), StringComparer.OrdinalIgnoreCase);

        return netWorthByName.Keys
            .Select(name => new RankedAirline(
                name,
                regionsLedByName.GetValueOrDefault(name),
                passengersByName.GetValueOrDefault(name),
                netWorthByName[name]))
            .OrderByDescending(r => r.RegionsLed)
            .ThenByDescending(r => r.TotalPassengersCarried)
            .ThenByDescending(r => r.NetWorth.Cents)
            .ThenBy(r => r.AirlineName, StringComparer.Ordinal)
            .ToList();
    }
}
