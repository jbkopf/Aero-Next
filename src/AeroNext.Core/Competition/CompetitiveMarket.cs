using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Competition;

/// <summary>
/// Splits shared city-pair demand across airlines whose networks both reach
/// it. Each airline's own <see cref="NetworkPlanner"/> plan — computed as if
/// it had no rivals — gives its "monopoly ceiling" per pair: the most it
/// could ever carry there, given its own routes and hub connections.
///
/// On a pair reached by more than one airline, demand splits by
/// (bottleneck-leg capacity × Quality) — raw seats-per-quarter on the
/// itinerary's tightest leg, not the demand-capped monopoly ceiling. Using
/// the ceiling as the weight would make frequency stop mattering the moment
/// one airline's capacity alone already exceeds demand (the ceiling
/// saturates at demand and stops growing even as capacity keeps growing),
/// which would silently kill the "more frequency wins more share" dynamic
/// this milestone exists to demonstrate. Each airline's raw share is then
/// capped at its own monopoly ceiling, so competition can only redistribute
/// demand, never grant an airline more than its network could physically
/// carry.
///
/// This only produces a visible effect when combined industry capacity on a
/// pair exceeds that pair's demand. If every competitor is individually
/// capacity-constrained (demand alone would fill their planes), each just
/// carries its own ceiling regardless of rivals — competition has nothing
/// to redistribute. That's not a bug: it's the same "capacity- vs demand-
/// constrained" distinction M3 established, now applied per competitor.
///
/// Phase 2 of docs/MULTI_STOP_ROUTES_DESIGN.md: the weight also carries a
/// journey-cost multiplier - each offer's fare plus journey time
/// (<see cref="JourneyCost"/>) compared with the cheapest offer on the pair,
/// raised to FareCalculator.FareDemandElasticity. Passengers compare real
/// price and time across rivals: a dearer fare, a slower aircraft, a
/// refuelling stop or a connection all cost share, with no flat penalty for
/// any of them. It replaced an earlier multiplier (M48) that compared each
/// airline's fare only with its own default fare, so a cheap nonstop and a
/// dear multi-stop route looked equally attractive at their defaults.
///
/// Simplification: capacity freed up by capping one airline's share at its
/// ceiling is not redistributed to less-constrained rivals (no iterative
/// water-filling). Leftover demand in that case goes uncarried by anyone
/// this quarter, rather than exhaustively reallocated.
/// </summary>
public static class CompetitiveMarket
{
    /// <summary>(Proposed, tunable) — the "good effect" a route-targeted campaign gives that specific pair: a Quality multiplier on a contested pair, a genuine added-demand fraction on an uncontested one.</summary>
    public const double RouteCampaignBoost = 0.5;

    /// <summary>(Proposed, tunable) — the smaller effect a network-wide campaign gives every pair, same two forms as RouteCampaignBoost.</summary>
    public const double NetworkCampaignBoost = 0.15;

    /// <param name="marketingByAirlineName">
    /// M47: null (the default) preserves fully permissive behavior — no
    /// airline's Quality is boosted and no extra demand is ever generated,
    /// exactly as before this parameter existed. Passing each airline's
    /// own active campaign for the quarter (QuarterEngine.RunQuarter's
    /// own accepted NetworkMarketingOrder/RouteMarketingOrder for that
    /// airline) has two different effects depending on whether the pair
    /// is contested: on a *contested* pair, it multiplies Quality up in
    /// the weight formula below, same as always. On an *uncontested*
    /// (solo-carrier) pair — round 2, per the user's own direct
    /// follow-up — it generates real added demand instead, since there's
    /// no rival share to win there; see ApplyMarketingDemandBoost's own
    /// doc comment for the "little gain near capacity" mechanics.
    /// </param>
    public static CompetitiveMarketResult Resolve(
        IReadOnlyList<Airline> airlines, IReadOnlyList<City> cities, GravityDemandModel demandModel,
        IReadOnlyDictionary<string, AirlineMarketingCampaign>? marketingByAirlineName = null)
    {
        var monopolyPlans = airlines.ToDictionary(a => a, a => NetworkPlanner.Plan(a.Routes, cities, demandModel));

        // M47 round 2: a running per-airline, per-leg spare-capacity
        // ledger for the uncontested-pair demand boost below - seeded
        // from each airline's own already-finalized NetworkPlan.RouteUsage
        // (SeatsPerQuarter minus SeatsUsed: what's left after this
        // airline's own real network was fully allocated), then
        // decremented as boosted pairs actually consume it, in the same
        // deterministic pair order the main loop below already iterates
        // in - so two of one airline's own itineraries sharing a hub leg
        // (e.g. both covered by one network-wide campaign) can never
        // combine to claim more than that leg's own real spare capacity.
        // Deliberately does not interact with the contested branch below
        // (a contested pair's own ceiling stays the original, unboosted
        // monopoly figure) - a known, small simplification: a leg shared
        // between an uncontested boosted itinerary and a separately
        // contested one isn't re-checked against the contested side after
        // the boost consumes some of it. Accepted rather than rebuilding
        // NetworkPlanner's own full allocation with marketing-aware
        // demand, which would be a much larger change for a soft,
        // tunable revenue lever, not a hard scarcity mechanic.
        var spareCapacityByAirline = airlines.ToDictionary(
            a => a,
            a => monopolyPlans[a].RouteUsage.ToDictionary(u => u.Route, u => Math.Max(0L, u.SeatsPerQuarter - u.SeatsUsed)));

        var pairKeys = monopolyPlans.Values
            .SelectMany(p => p.Itineraries.Where(i => i.Itinerary.Kind != ItineraryKind.Unserved))
            .Select(i => (i.Itinerary.Origin, i.Itinerary.Destination))
            .GroupBy(t => (t.Origin.Id, t.Destination.Id))
            .Select(g => g.First())
            .OrderBy(t => t.Origin.Id, StringComparer.Ordinal)
            .ThenBy(t => t.Destination.Id, StringComparer.Ordinal);

        var pairResults = new List<CompetitivePairResult>();
        var routeSeatsUsed = new Dictionary<(Airline, Route), long>();
        // Seat return (below): each pair's demand as passengers see it -
        // an uncontested pair's after its fare/marketing adjustment.
        var effectiveDemandByPair = new List<long>();

        foreach (var (origin, destination) in pairKeys)
        {
            var demand = demandModel.ForPair(origin, destination).PassengersPerQuarter;

            var competitors = airlines
                .Select(a => (Airline: a, Load: FindPair(monopolyPlans[a], origin.Id, destination.Id)))
                .Where(x => x.Load is { PassengersCarried: > 0 })
                .ToList();

            var carriedByAirline = new Dictionary<Airline, long>();

            var effectiveDemand = demand;

            if (competitors.Count == 1)
            {
                var (airline, load) = competitors[0];
                var (adjustment, adjustedDemand) = ApplyDemandAdjustment(airline, load!, origin, destination, spareCapacityByAirline[airline], marketingByAirlineName);
                carriedByAirline[airline] = load!.PassengersCarried + adjustment;
                effectiveDemand = adjustedDemand;
            }
            else if (competitors.Count > 1)
            {
                var journeyCosts = competitors.Select(c => JourneyCost.PerPassenger(c.Load!.Itinerary)).ToList();
                var cheapestJourney = new Money(journeyCosts.Min(c => c.Cents));
                var weights = competitors
                    .Select((c, i) => (
                        c.Airline,
                        Weight: c.Load!.Itinerary.Legs.Min(l => l.SeatsPerQuarter) * EffectiveContestedQuality(c.Airline, origin, destination, marketingByAirlineName) * JourneyMultiplier(journeyCosts[i], cheapestJourney),
                        Ceiling: c.Load!.PassengersCarried))
                    .ToList();
                var totalWeight = weights.Sum(w => w.Weight);

                foreach (var (airline, weight, ceiling) in weights)
                {
                    var rawShare = totalWeight <= 0 ? 0 : demand * weight / totalWeight;
                    carriedByAirline[airline] = Math.Min(ceiling, (long)Math.Round(rawShare, MidpointRounding.AwayFromZero));
                }
            }

            var offers = airlines
                .Select(a => (Airline: a, Load: FindPair(monopolyPlans[a], origin.Id, destination.Id)))
                .Where(x => x.Load is not null && x.Load.Itinerary.Kind != ItineraryKind.Unserved && x.Load.Itinerary.Legs.Min(l => l.SeatsPerQuarter) > 0)
                .Select(x => new PairOffer(x.Airline.Name, x.Load!.Itinerary.Legs.Min(l => l.SeatsPerQuarter), x.Airline.Quality, JourneyCost.PerPassenger(x.Load.Itinerary)))
                .ToList();

            pairResults.Add(new CompetitivePairResult(origin, destination, demand, carriedByAirline, offers));
            effectiveDemandByPair.Add(effectiveDemand);

            foreach (var (airline, carried) in carriedByAirline)
            {
                var itinerary = FindPair(monopolyPlans[airline], origin.Id, destination.Id)!.Itinerary;
                foreach (var leg in itinerary.Legs)
                {
                    var key = (airline, leg);
                    routeSeatsUsed[key] = routeSeatsUsed.GetValueOrDefault(key) + carried;
                }
            }
        }

        ReturnUnusedSeats(airlines, monopolyPlans, pairResults, effectiveDemandByPair, routeSeatsUsed);

        var turnedAwayByAirline = monopolyPlans.ToDictionary(kv => kv.Key, kv => kv.Value.RouteUsage.ToDictionary(u => u.Route, u => u.TurnedAway));
        var routeUsage = airlines
            .SelectMany(a => a.Routes.Select(r => new CompetitiveRouteUsage(a, r, r.SeatsPerQuarter, routeSeatsUsed.GetValueOrDefault((a, r)), turnedAwayByAirline[a].GetValueOrDefault(r))))
            .OrderBy(u => u.Airline.Name, StringComparer.Ordinal)
            .ThenBy(u => u.Route.Origin.Id, StringComparer.Ordinal)
            .ThenBy(u => u.Route.Destination.Id, StringComparer.Ordinal)
            .ToList();

        return new CompetitiveMarketResult(pairResults, routeUsage);
    }

    /// <summary>
    /// The share of a pair's passengers an offer would win against rivals'
    /// offers - the contest's own weighting (seats on the tightest leg x
    /// Quality x JourneyMultiplier against the cheapest journey on offer),
    /// shared so the AI's route estimates price competition the way the
    /// market does (docs/CONNECTING_PASSENGERS_DESIGN.md). 1.0 with no
    /// rivals. Ignores marketing boosts and rivals' capacity ceilings.
    /// </summary>
    public static double ExpectedShare(long ownSeats, double ownQuality, Money ownJourneyCost, IReadOnlyList<PairOffer> rivals)
    {
        if (rivals.Count == 0)
        {
            return 1.0;
        }

        var cheapest = new Money(Math.Min(ownJourneyCost.Cents, rivals.Min(r => r.JourneyCost.Cents)));
        var own = ownSeats * ownQuality * JourneyMultiplier(ownJourneyCost, cheapest);
        var total = own + rivals.Sum(r => r.Seats * r.Quality * JourneyMultiplier(r.JourneyCost, cheapest));

        return total <= 0 ? 0.0 : own / total;
    }

    /// <summary>M47: Quality, boosted by this airline's own active marketing campaign on this specific pair, if any — a route campaign's boost, else a network campaign's smaller one, else unboosted. Contested pairs only — see ApplyDemandAdjustment for the uncontested-pair mechanism.</summary>
    private static double EffectiveContestedQuality(Airline airline, City origin, City destination, IReadOnlyDictionary<string, AirlineMarketingCampaign>? marketingByAirlineName) =>
        airline.Quality * (1 + MarketingDeltaFraction(airline, origin, destination, marketingByAirlineName));

    /// <summary>
    /// Phase 2 of docs/MULTI_STOP_ROUTES_DESIGN.md: how this offer's journey
    /// cost compares with the cheapest on the pair - 1.0 for the cheapest,
    /// (cheapest / own) ^ FareCalculator.FareDemandElasticity for the rest,
    /// so a 10% dearer journey keeps about 87% of the weight. A fare cut
    /// still moves share, since it lowers this offer's own journey cost.
    /// Both costs are floored at one cent, so a zero or negative player fare
    /// can never produce a zero divisor or a negative base.
    /// </summary>
    private static double JourneyMultiplier(Money ownJourneyCost, Money cheapestJourneyCost) =>
        Math.Pow((double)Math.Max(1, cheapestJourneyCost.Cents) / Math.Max(1, ownJourneyCost.Cents), FareCalculator.FareDemandElasticity);

    /// <summary>M47: this airline's active campaign's own boost fraction for this specific pair — a route campaign's, else a network campaign's smaller one, else zero. Shared by both the contested weight multiplier and the uncontested demand adjustment below.</summary>
    private static double MarketingDeltaFraction(Airline airline, City origin, City destination, IReadOnlyDictionary<string, AirlineMarketingCampaign>? marketingByAirlineName)
    {
        if (marketingByAirlineName is null || !marketingByAirlineName.TryGetValue(airline.Name, out var campaign))
        {
            return 0.0;
        }

        return campaign.RouteKeys.Contains(QuarterEngine.NormalizedPairKey(origin, destination))
            ? RouteCampaignBoost
            : campaign.NetworkWide
                ? NetworkCampaignBoost
                : 0.0;
    }

    /// <summary>
    /// M47 round 2 / M48: the demand-adjustment mechanism for an
    /// uncontested pair — there's no rival share to win via Quality
    /// here, so marketing and fare instead adjust real demand directly.
    /// Combines marketing's own boost fraction (MarketingDeltaFraction,
    /// always ≥ 0) with fare's own signed fraction (FareCalculator.
    /// DemandDeltaFraction — positive for a discount, negative for a
    /// premium) into one total delta against this pair's own
    /// DemandPassengers.
    ///
    /// A non-negative delta (net discount/marketing) grows carried
    /// passengers, capped by Math.Min against however much spare
    /// capacity the itinerary's own tightest leg actually has left this
    /// quarter — the whole mechanism behind "real tangible effects would
    /// only be felt on routes that have lower load factors": a route
    /// already near its own ceiling has near-zero spare capacity to add
    /// to, regardless of the delta's own size.
    ///
    /// A negative delta (net fare premium) shrinks demand — but per the
    /// user's own direct instruction, must never force carried
    /// passengers down while the now-lower demand is still above
    /// capacity ("do not force decreased passenger numbers on increase
    /// of fares... the new lowered demand could still be greater than
    /// capacity"). unmetDemand is exactly that cushion — how much demand
    /// was already going uncarried before this adjustment, zero unless
    /// the route was already sold out — and the cut is absorbed by it
    /// first; carried only actually drops once the cut exceeds the
    /// cushion.
    ///
    /// Mutates remainingSpareCapacityByRoute either way (decrementing on
    /// growth, incrementing on an genuine carried-passenger reduction —
    /// a route that just got less popular really does free up shared hub
    /// capacity for a later pair) so a later pair sharing the same leg
    /// sees accurate remaining room, not the same starting snapshot every
    /// pair would otherwise see.
    /// </summary>
    private static (long Adjustment, long AdjustedDemand) ApplyDemandAdjustment(
        Airline airline, ItineraryLoad load, City origin, City destination,
        Dictionary<Route, long> remainingSpareCapacityByRoute,
        IReadOnlyDictionary<string, AirlineMarketingCampaign>? marketingByAirlineName)
    {
        var marketingDeltaFraction = MarketingDeltaFraction(airline, origin, destination, marketingByAirlineName);
        var fareDeltaFraction = load.Itinerary.Legs.Min(leg => FareCalculator.DemandDeltaFraction(leg));
        var totalDeltaFraction = marketingDeltaFraction + fareDeltaFraction;

        if (totalDeltaFraction == 0.0)
        {
            return (0, load.DemandPassengers);
        }

        var deltaDemand = (long)Math.Round(load.DemandPassengers * totalDeltaFraction, MidpointRounding.AwayFromZero);

        if (deltaDemand >= 0)
        {
            var spareCapacity = load.Itinerary.Legs.Min(leg => remainingSpareCapacityByRoute.GetValueOrDefault(leg));
            var additionalCarried = Math.Min(deltaDemand, spareCapacity);

            foreach (var leg in load.Itinerary.Legs)
            {
                remainingSpareCapacityByRoute[leg] = remainingSpareCapacityByRoute.GetValueOrDefault(leg) - additionalCarried;
            }

            return (additionalCarried, load.DemandPassengers + deltaDemand);
        }

        var unmetDemand = Math.Max(0L, load.DemandPassengers - load.PassengersCarried);
        // Clamped at PassengersCarried itself - an extreme premium (a large
        // enough fare hike, or a low elasticity constant applied to a huge
        // demand figure) must never drive carried below zero; it can only
        // ever claw back what this itinerary was actually carrying.
        var excessReduction = Math.Min(load.PassengersCarried, Math.Max(0L, -deltaDemand - unmetDemand));

        foreach (var leg in load.Itinerary.Legs)
        {
            remainingSpareCapacityByRoute[leg] = remainingSpareCapacityByRoute.GetValueOrDefault(leg) + excessReduction;
        }

        return (-excessReduction, Math.Max(0, load.DemandPassengers + deltaDemand));
    }

    /// <summary>
    /// Connecting passengers (docs/CONNECTING_PASSENGERS_DESIGN.md): each
    /// airline's own plan shares a full route's seats between local and
    /// connecting itineraries, but a connection can then lose its pair's
    /// contest to a rival's better journey - and the seats it held stay
    /// empty while the local passengers it displaced go unserved. This
    /// returns those seats: every airline's spare seats (capacity left
    /// after the contest) go to passengers still unserved on pairs its
    /// network reaches, nonstop pairs first, then connections by unserved
    /// demand, largest first. When several airlines have room for the same
    /// pair, the unserved passengers split by the room each has on its
    /// tightest leg times its Quality, the contest's own capacity x Quality
    /// weighting. Never more than the pair's demand (an uncontested
    /// pair's after its fare/marketing adjustment) - rivals' results are
    /// untouched; only passengers nobody carried are reassigned.
    ///
    /// Traced on a bankrupt Rio airline (game 144, seed 1): its new
    /// Atlanta-Rio route carried 361 of 5,780 local passengers with half
    /// its seats empty - the seats had been held for connections that lost
    /// to rivals.
    /// </summary>
    private static void ReturnUnusedSeats(
        IReadOnlyList<Airline> airlines, IReadOnlyDictionary<Airline, NetworkPlan> monopolyPlans,
        List<CompetitivePairResult> pairResults, List<long> effectiveDemandByPair, Dictionary<(Airline, Route), long> routeSeatsUsed)
    {
        long Spare(Airline airline, Route leg) => leg.SeatsPerQuarter - routeSeatsUsed.GetValueOrDefault((airline, leg));

        var order = Enumerable.Range(0, pairResults.Count).ToList();

        foreach (var kind in new[] { ItineraryKind.Nonstop, ItineraryKind.OneStop })
        {
            var pass = kind == ItineraryKind.Nonstop
                ? order
                : order.OrderByDescending(k => effectiveDemandByPair[k] - pairResults[k].CarriedByAirline.Values.Sum()).ThenBy(k => k).ToList();

            foreach (var k in pass)
            {
                var pair = pairResults[k];
                var carried = (Dictionary<Airline, long>)pair.CarriedByAirline;
                var unserved = effectiveDemandByPair[k] - carried.Values.Sum();

                if (unserved <= 0)
                {
                    continue;
                }

                var offers = airlines
                    .Select(a => (Airline: a, Load: FindPair(monopolyPlans[a], pair.Origin.Id, pair.Destination.Id)))
                    .Where(x => x.Load is not null && x.Load.Itinerary.Kind == kind)
                    .Select(x => (x.Airline, x.Load!.Itinerary.Legs, Room: x.Load!.Itinerary.Legs.Min(leg => Spare(x.Airline, leg))))
                    .Where(x => x.Room > 0 && x.Airline.Quality > 0)
                    .ToList();
                var totalWeight = offers.Sum(x => x.Room * x.Airline.Quality);

                foreach (var (airline, legs, room) in offers)
                {
                    var extra = Math.Min(room, (long)Math.Floor(unserved * room * airline.Quality / totalWeight));

                    if (extra <= 0)
                    {
                        continue;
                    }

                    carried[airline] = carried.GetValueOrDefault(airline) + extra;
                    foreach (var leg in legs)
                    {
                        routeSeatsUsed[(airline, leg)] = routeSeatsUsed.GetValueOrDefault((airline, leg)) + extra;
                    }
                }
            }
        }
    }

    private static ItineraryLoad? FindPair(NetworkPlan plan, string originId, string destinationId)
    {
        foreach (var load in plan.Itineraries)
        {
            if (load.Itinerary.Origin.Id == originId && load.Itinerary.Destination.Id == destinationId)
            {
                return load;
            }
        }

        return null;
    }
}
