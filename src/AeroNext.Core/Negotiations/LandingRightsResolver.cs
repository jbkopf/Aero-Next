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

    /// <summary>
    /// M59 (proposed, tunable) — the largest fraction of a city's own
    /// current total slots (starting capacity + any M46 growth bonus) a
    /// single bid may request, minimum 1 regardless of how small that
    /// fraction rounds down to. Enforced by clamping in Submit, never by
    /// rejecting the bid outright.
    /// </summary>
    public const double BidSlotRequestCapFraction = 0.3;

    /// <param name="capacityBonusByCity">
    /// M59: extra slots each city has earned via M46's own periodic
    /// expansion (see CityCapacityBoard) — the same value ResolveQuarter
    /// already accepts, threaded here too since the slot-request cap is
    /// against the city's *current* total, not just its starting figure.
    /// Null (the default) preserves every pre-M59 call site's exact
    /// behavior (the cap applies against starting capacity alone).
    /// </param>
    public static LandingRightsBoard Submit(
        LandingRightsBoard board, Airline airline, NegotiationOrder order, int currentQuarter, SimRng rng,
        IReadOnlyDictionary<string, int>? capacityBonusByCity = null)
    {
        // M59: no duplicate pending bid at the same city - a real,
        // engine-enforced rule now, not just the AI's own
        // CitiesAlreadyPursuedForNegotiation convention. The real,
        // cash-safe enforcement is QuarterEngine.RunQuarter's own
        // pendingNegotiationCityIds rejection, which runs *before* any
        // cash is spent; this is a defense-in-depth backstop for any
        // caller that doesn't thread that parameter through - a silent
        // no-op, matching this codebase's own "never attempt what will
        // predictably fail" convention, rather than throwing.
        if (board.Pending.Any(p => p.Airline.Name == airline.Name && p.TargetCity.Id == order.TargetCity.Id))
        {
            return board;
        }

        // M59: clamp, never reject outright - "minimum 1" only means
        // something if a request that's too large still becomes a real,
        // smaller bid rather than nothing at all. Bid itself never
        // scales with quantity (no per-slot cost model exists), so
        // clamping down costs the bidder nothing extra beyond the
        // smaller grant.
        var startingCapacity = order.TargetCity.GateCapacity ?? order.TargetCity.AutoCapacity ?? 0;
        var totalSlots = startingCapacity + (capacityBonusByCity?.GetValueOrDefault(order.TargetCity.Id) ?? 0);
        var maxRequestable = Math.Max(1, (int)Math.Floor(BidSlotRequestCapFraction * totalSlots));
        var slotsRequested = Math.Clamp(order.SlotsRequested, 1, maxRequestable);

        var delay = rng.Next(MinDelayQuarters, MaxDelayQuartersExclusive);
        var pending = new PendingNegotiation(airline, order.TargetCity, order.Bid, currentQuarter, currentQuarter + delay, slotsRequested);

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
            var freshRemaining = board.SlotsRemaining(city, capacityBonusByCity?.GetValueOrDefault(city.Id) ?? 0);
            var idleExpiredHolders = routesByAirline is null
                ? Array.Empty<string>()
                : board.IdleExpiredHolders(city, routesByAirline);
            var transferIndex = 0;

            var ranked = group
                .Select(p => (Pending: p, Score: p.Bid.Cents * board.GetStanding(p.Airline, city)))
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Pending.Airline.Name, StringComparer.Ordinal)
                .ToList();

            // M59: running-counter allocation, not index-vs-remainingSlots
            // - a single ranked bidder can now consume more than one
            // unit (real multi-slot bidding), walked in the same
            // Score-descending order as before. Fresh grants are drawn
            // first, any shortfall from idle-expired transfers next,
            // exactly the same fresh-then-transfer priority the old
            // index-based check encoded. A bid that can't get its full
            // request still gets whatever's left (partial fulfillment) -
            // when every bid requests exactly 1 (every pre-M59 caller),
            // this reproduces the old win/lose behavior exactly.
            foreach (var (pending, _) in ranked)
            {
                var key = (pending.Airline.Name, city.Id);
                var requested = pending.SlotsRequested;

                var freshGranted = Math.Min(requested, freshRemaining);
                freshRemaining -= freshGranted;

                var stillNeeded = requested - freshGranted;
                var transferAvailable = idleExpiredHolders.Count - transferIndex;
                var transferGranted = Math.Min(stillNeeded, transferAvailable);

                for (var t = 0; t < transferGranted; t++)
                {
                    var fromHolder = idleExpiredHolders[transferIndex];
                    var fromKey = (fromHolder, city.Id);
                    slotsHeld[fromKey] = slotsHeld.GetValueOrDefault(fromKey) - 1;
                    transferIndex++;
                }

                var totalGranted = freshGranted + transferGranted;

                if (freshGranted > 0)
                {
                    slots[city.Id] = slots.GetValueOrDefault(city.Id) + freshGranted;
                }

                if (totalGranted > 0)
                {
                    slotsHeld[key] = slotsHeld.GetValueOrDefault(key) + totalGranted;
                }

                // M59: any unfulfilled remainder - a full loss included -
                // raises Standing by the same flat StandingGainPerLoss
                // once, not scaled by how many slots were missed. Same
                // flat generosity the old binary win/lose model already
                // had, extended rather than re-tuned.
                if (totalGranted < requested)
                {
                    standing[key] = board.GetStanding(pending.Airline, city) + StandingGainPerLoss;
                }

                outcomes.Add(new NegotiationOutcome(
                    pending.Airline, city, pending.Bid, totalGranted > 0,
                    standing.GetValueOrDefault(key, board.GetStanding(pending.Airline, city)), totalGranted));
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
