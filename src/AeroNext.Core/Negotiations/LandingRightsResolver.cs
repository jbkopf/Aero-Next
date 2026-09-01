using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

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

    /// <param name="routesByAirline">
    /// M45: null (the default) preserves fully permissive behavior for
    /// every pre-existing call site — a fully-granted city's idle-expired
    /// inventory (see LandingRightsBoard.IdleExpiredHolders) is never
    /// treated as contestable, exactly as before this parameter existed.
    /// Passing every airline's current routes (keyed by name) turns on
    /// idle-slot recontestability (M45): once a fresh city's remaining
    /// capacity runs out, a ranked bidder can still win by taking a
    /// transfer from an idle-expired holding instead of a brand-new grant
    /// — SlotsGranted's city-wide total is unchanged (ownership moved, it
    /// never vacated), only SlotsHeld moves from the idle owner to the
    /// winner. The idle owner is never entered as a bidder for their own
    /// slot — passive retention only; they keep using it until someone
    /// else actually wins it away, per CLAUDE.md's own contested-action
    /// rules applied to this new inventory too.
    /// </param>
    /// <param name="capacityBonusByCity">
    /// M46: extra slots each city has earned via periodic, demand-driven
    /// expansion (see <see cref="CityCapacityBoard"/>) — null (the
    /// default) preserves every pre-existing call site's exact behavior.
    /// Threaded straight into <see cref="LandingRightsBoard.SlotsRemaining"/>.
    /// </param>
    public static (LandingRightsBoard Board, IReadOnlyList<NegotiationOutcome> Outcomes) ResolveQuarter(
        LandingRightsBoard board, int currentQuarter, IReadOnlyDictionary<string, IReadOnlyList<Route>>? routesByAirline = null,
        IReadOnlyDictionary<string, int>? capacityBonusByCity = null)
    {
        var due = board.Pending.Where(p => p.ResolvesQuarter == currentQuarter).ToList();
        var stillPending = board.Pending.Where(p => p.ResolvesQuarter != currentQuarter).ToList();

        var standing = new Dictionary<(string, string), double>(board.Standing);
        var slots = new Dictionary<string, int>(board.SlotsGranted);
        var slotsHeld = new Dictionary<(string, string), int>(board.SlotsHeld);
        var outcomes = new List<NegotiationOutcome>();

        foreach (var group in due.GroupBy(p => p.TargetCity.Id).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var city = group.First().TargetCity;
            var remainingSlots = board.SlotsRemaining(city, capacityBonusByCity?.GetValueOrDefault(city.Id) ?? 0);
            var idleExpiredHolders = routesByAirline is null
                ? Array.Empty<string>()
                : board.IdleExpiredHolders(city, routesByAirline);

            var ranked = group
                .Select(p => (Pending: p, Score: p.Bid.Cents * board.GetStanding(p.Airline, city)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Pending.Airline.Name, StringComparer.Ordinal)
                .ToList();

            for (var i = 0; i < ranked.Count; i++)
            {
                var pending = ranked[i].Pending;
                var wonFreshGrant = i < remainingSlots;
                var wonTransfer = !wonFreshGrant && i < remainingSlots + idleExpiredHolders.Count;
                var won = wonFreshGrant || wonTransfer;
                var key = (pending.Airline.Name, city.Id);

                if (wonFreshGrant)
                {
                    slots[city.Id] = slots.GetValueOrDefault(city.Id) + 1;
                    slotsHeld[key] = slotsHeld.GetValueOrDefault(key) + 1;
                }
                else if (wonTransfer)
                {
                    var fromHolder = idleExpiredHolders[i - remainingSlots];
                    var fromKey = (fromHolder, city.Id);
                    slotsHeld[fromKey] = slotsHeld.GetValueOrDefault(fromKey) - 1;
                    slotsHeld[key] = slotsHeld.GetValueOrDefault(key) + 1;
                }
                else
                {
                    standing[key] = board.GetStanding(pending.Airline, city) + StandingGainPerLoss;
                }

                outcomes.Add(new NegotiationOutcome(pending.Airline, city, pending.Bid, won, standing.GetValueOrDefault(key, board.GetStanding(pending.Airline, city))));
            }
        }

        var resolvedBoard = board with { Pending = stillPending, Standing = standing, SlotsGranted = slots, SlotsHeld = slotsHeld };
        var sortedOutcomes = outcomes
            .OrderBy(o => o.TargetCity.Id, StringComparer.Ordinal)
            .ThenBy(o => o.Airline.Name, StringComparer.Ordinal)
            .ToList();

        return (resolvedBoard, sortedOutcomes);
    }

    /// <summary>
    /// M28: returns one held slot to the contested pool — decrements both
    /// this airline's SlotsHeld at city and the city-wide SlotsGranted
    /// total, so SlotsRemaining sees it as contestable again immediately.
    /// A safe no-op (returns board unchanged) if the airline holds zero
    /// there — reachable when QuarterEngine never gated the order at all
    /// (permissive mode, availableSlotsByCity: null), since eligibility
    /// there is checked against *available* balance, not this board
    /// directly.
    /// </summary>
    public static LandingRightsBoard Relinquish(LandingRightsBoard board, Airline airline, City city)
    {
        var key = (airline.Name, city.Id);
        var currentlyHeld = board.SlotsHeldBy(airline, city);

        if (currentlyHeld <= 0)
        {
            return board;
        }

        var slotsHeld = new Dictionary<(string, string), int>(board.SlotsHeld) { [key] = currentlyHeld - 1 };
        var slotsGranted = new Dictionary<string, int>(board.SlotsGranted) { [city.Id] = Math.Max(0, board.SlotsGranted.GetValueOrDefault(city.Id) - 1) };

        return board with { SlotsHeld = slotsHeld, SlotsGranted = slotsGranted };
    }
}
