using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;

namespace AeroNext.Core.Negotiations;

/// <summary>
/// Silent-commitment bidding at gateway cities, per CLAUDE.md: resolves on
/// money x relations x reputation x political alignment, 2-4 quarters
/// after submission, with multiple suitors driving the effective bar
/// higher for everyone and losers always getting a partial payoff so the
/// mechanism doesn't die.
///
/// Simplification: relations, reputation, and political alignment collapse
/// into one number, Standing (per airline, per city) — CLAUDE.md doesn't
/// specify these as separately tunable, and modeling three independent
/// systems with no design behind any of them would be invented complexity.
/// Mirrors how Airline.Quality already stands in for fare/service/marketing
/// posture. Score = Bid.Cents x Standing; highest score wins each of a
/// city's remaining gate slots. Losers' Standing rises, so the same bid
/// buys more Score next time — "you lost the gate, not the investment."
/// </summary>
public static class LandingRightsResolver
{
    /// <summary>Delay before a submitted negotiation resolves, per CLAUDE.md ("takes 2-4 quarters to land").</summary>
    private const int MinDelayQuarters = 2;
    private const int MaxDelayQuartersExclusive = 5;

    /// <summary>Placeholder tunable: how much a loss raises standing (and therefore future Score) at that city.</summary>
    private const double StandingGainPerLoss = 0.2;

    public static LandingRightsBoard Submit(LandingRightsBoard board, Airline airline, NegotiationOrder order, int currentQuarter, SimRng rng)
    {
        var delay = rng.Next(MinDelayQuarters, MaxDelayQuartersExclusive);
        var pending = new PendingNegotiation(airline, order.TargetCity, order.Bid, currentQuarter, currentQuarter + delay);

        return board with { Pending = board.Pending.Append(pending).ToList() };
    }

    public static (LandingRightsBoard Board, IReadOnlyList<NegotiationOutcome> Outcomes) ResolveQuarter(LandingRightsBoard board, int currentQuarter)
    {
        var due = board.Pending.Where(p => p.ResolvesQuarter == currentQuarter).ToList();
        var stillPending = board.Pending.Where(p => p.ResolvesQuarter != currentQuarter).ToList();

        var standing = new Dictionary<(string, string), double>(board.Standing);
        var slots = new Dictionary<string, int>(board.SlotsGranted);
        var outcomes = new List<NegotiationOutcome>();

        foreach (var group in due.GroupBy(p => p.TargetCity.Id).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var city = group.First().TargetCity;
            var remainingSlots = board.SlotsRemaining(city);

            var ranked = group
                .Select(p => (Pending: p, Score: p.Bid.Cents * board.GetStanding(p.Airline, city)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Pending.Airline.Name, StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < ranked.Count; i++)
            {
                var pending = ranked[i].Pending;
                var won = i < remainingSlots;
                var key = (pending.Airline.Name, city.Id);

                if (won)
                {
                    slots[city.Id] = slots.GetValueOrDefault(city.Id) + 1;
                }
                else
                {
                    standing[key] = board.GetStanding(pending.Airline, city) + StandingGainPerLoss;
                }

                outcomes.Add(new NegotiationOutcome(pending.Airline, city, pending.Bid, won, standing.GetValueOrDefault(key, board.GetStanding(pending.Airline, city))));
            }
        }

        var resolvedBoard = board with { Pending = stillPending, Standing = standing, SlotsGranted = slots };
        var sortedOutcomes = outcomes
            .OrderBy(o => o.TargetCity.Id, StringComparer.Ordinal)
            .ThenBy(o => o.Airline.Name, StringComparer.Ordinal)
            .ToList();

        return (resolvedBoard, sortedOutcomes);
    }
}
