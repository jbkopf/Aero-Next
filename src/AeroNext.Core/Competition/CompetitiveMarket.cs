using AeroNext.Core.Airlines;
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

        foreach (var (origin, destination) in pairKeys)
        {
            var demand = demandModel.ForPair(origin, destination).PassengersPerQuarter;

            var competitors = airlines
                .Select(a => (Airline: a, Load: FindPair(monopolyPlans[a], origin.Id, destination.Id)))
                .Where(x => x.Load is { PassengersCarried: > 0 })
                .ToList();

            var carriedByAirline = new Dictionary<Airline, long>();

            if (competitors.Count == 1)
            {
                var (airline, load) = competitors[0];
                var additional = ApplyMarketingDemandBoost(airline, load!, origin, destination, spareCapacityByAirline[airline], marketingByAirlineName);
                carriedByAirline[airline] = load!.PassengersCarried + additional;
            }
            else if (competitors.Count > 1)
            {
                var weights = competitors
                    .Select(c => (
                        c.Airline,
                        Weight: c.Load!.Itinerary.Legs.Min(l => l.SeatsPerQuarter) * EffectiveContestedQuality(c.Airline, origin, destination, marketingByAirlineName),
                        Ceiling: c.Load!.PassengersCarried))
                    .ToList();
                var totalWeight = weights.Sum(w => w.Weight);

                foreach (var (airline, weight, ceiling) in weights)
                {
                    var rawShare = totalWeight <= 0 ? 0 : demand * weight / totalWeight;
                    carriedByAirline[airline] = Math.Min(ceiling, (long)Math.Round(rawShare, MidpointRounding.AwayFromZero));
                }
            }

            pairResults.Add(new CompetitivePairResult(origin, destination, demand, carriedByAirline));

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

        var routeUsage = airlines
            .SelectMany(a => a.Routes.Select(r => new CompetitiveRouteUsage(a, r, r.SeatsPerQuarter, routeSeatsUsed.GetValueOrDefault((a, r)))))
            .OrderBy(u => u.Airline.Name, StringComparer.Ordinal)
            .ThenBy(u => u.Route.Origin.Id, StringComparer.Ordinal)
            .ThenBy(u => u.Route.Destination.Id, StringComparer.Ordinal)
            .ToList();

        return new CompetitiveMarketResult(pairResults, routeUsage);
    }

    /// <summary>M47: Quality, boosted by this airline's own active marketing campaign on this specific pair, if any — a route campaign's boost, else a network campaign's smaller one, else unboosted. Contested pairs only — see ApplyMarketingDemandBoost for the uncontested-pair mechanism.</summary>
    private static double EffectiveContestedQuality(Airline airline, City origin, City destination, IReadOnlyDictionary<string, AirlineMarketingCampaign>? marketingByAirlineName)
    {
        if (marketingByAirlineName is null || !marketingByAirlineName.TryGetValue(airline.Name, out var campaign))
        {
            return airline.Quality;
        }

        var multiplier = campaign.RouteKeys.Contains(QuarterEngine.NormalizedPairKey(origin, destination))
            ? 1 + RouteCampaignBoost
            : campaign.NetworkWide
                ? 1 + NetworkCampaignBoost
                : 1.0;

        return airline.Quality * multiplier;
    }

    /// <summary>
    /// M47 round 2: the added-demand mechanism for an uncontested pair —
    /// there's no rival share to win via Quality here, so instead the
    /// campaign generates real added demand (RouteCampaignBoost/
    /// NetworkCampaignBoost applied as a genuine fraction of this pair's
    /// own DemandPassengers, not a weight multiplier), capped at however
    /// much spare capacity the itinerary's own tightest leg actually has
    /// left this quarter. This single Math.Min against spare capacity is
    /// the whole mechanism behind "real tangible effects would only be
    /// felt on routes that have lower load factors" (the user's own
    /// words): a route already near its own ceiling has near-zero spare
    /// capacity, so the boost has near-nothing left to add regardless of
    /// its own size, while a route flying half-empty can realize close to
    /// the full added-demand figure. Mutates remainingSpareCapacityByRoute
    /// (decrementing whichever legs this itinerary actually uses) so a
    /// later pair sharing the same leg sees accurate remaining room, not
    /// the same starting snapshot every pair would otherwise see.
    /// </summary>
    private static long ApplyMarketingDemandBoost(
        Airline airline, ItineraryLoad load, City origin, City destination,
        Dictionary<Route, long> remainingSpareCapacityByRoute,
        IReadOnlyDictionary<string, AirlineMarketingCampaign>? marketingByAirlineName)
    {
        if (marketingByAirlineName is null || !marketingByAirlineName.TryGetValue(airline.Name, out var campaign))
        {
            return 0;
        }

        var boostFraction = campaign.RouteKeys.Contains(QuarterEngine.NormalizedPairKey(origin, destination))
            ? RouteCampaignBoost
            : campaign.NetworkWide
                ? NetworkCampaignBoost
                : 0.0;

        if (boostFraction <= 0.0)
        {
            return 0;
        }

        var additionalDemand = (long)Math.Round(load.DemandPassengers * boostFraction, MidpointRounding.AwayFromZero);
        var spareCapacity = load.Itinerary.Legs.Min(leg => remainingSpareCapacityByRoute.GetValueOrDefault(leg));
        var additionalCarried = Math.Min(additionalDemand, spareCapacity);

        foreach (var leg in load.Itinerary.Legs)
        {
            remainingSpareCapacityByRoute[leg] = remainingSpareCapacityByRoute.GetValueOrDefault(leg) - additionalCarried;
        }

        return additionalCarried;
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
