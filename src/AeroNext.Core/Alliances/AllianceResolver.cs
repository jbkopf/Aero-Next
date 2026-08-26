using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;

namespace AeroNext.Core.Alliances;

/// <summary>
/// A simultaneous, same-quarter contest — no bid, no queue, no secrecy.
/// Score is Airline.Quality: whichever suitor is the more attractive
/// partner wins the regional carrier outright, permanently. CLAUDE.md
/// doesn't describe alliance courting as a cash commitment the way landing
/// rights are, so unlike LandingRightsResolver there's no money factor and
/// no partial payoff for losers — you either land the carrier or you
/// don't, and there's nothing left to try again for once it's joined
/// someone else.
/// </summary>
public static class AllianceResolver
{
    public static (AllianceBoard Board, IReadOnlyList<AllianceOutcome> Outcomes) ResolveQuarter(
        AllianceBoard board, IReadOnlyList<(Airline Airline, AllianceCourtOrder Order)> courtOrders)
    {
        var joined = new Dictionary<string, string>(board.JoinedCarrierToAirlineName);
        var outcomes = new List<AllianceOutcome>();

        foreach (var group in courtOrders.GroupBy(x => x.Order.Target.Id).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var carrier = group.First().Order.Target;

            if (!board.IsAvailable(carrier))
            {
                outcomes.AddRange(group.Select(x => new AllianceOutcome(x.Airline, carrier, false)));
                continue;
            }

            var winner = group
                .OrderByDescending(x => x.Airline.Quality)
                .ThenBy(x => x.Airline.Name, StringComparer.Ordinal)
                .First()
                .Airline;

            joined[carrier.Id] = winner.Name;
            outcomes.AddRange(group.Select(x => new AllianceOutcome(x.Airline, carrier, x.Airline.Name == winner.Name)));
        }

        var sortedOutcomes = outcomes
            .OrderBy(o => o.Target.Id, StringComparer.Ordinal)
            .ThenBy(o => o.Airline.Name, StringComparer.Ordinal)
            .ToList();

        return (board with { JoinedCarrierToAirlineName = joined }, sortedOutcomes);
    }
}
