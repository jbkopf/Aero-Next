using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// M39 (docs/BASELINE_AI_ROADMAP.md): consolidate a small, profitable
/// home-region network rather than chase every market in the world.
/// Below MaxDomesticRoutes intra-region routes, opens the single best
/// remaining candidate ranked by estimated *profitability*, not raw
/// demand — a real equivalence under today's fare model, not a loose
/// proxy: FareCalculator bakes in a flat, uniform margin fraction over
/// cost everywhere, so profit is directly proportional to
/// fare-per-passenger x carried, meaning ranking by that revenue
/// estimate genuinely *is* ranking by profit. At the cap, stops opening
/// entirely and only optimizes what it already holds (reusing
/// BaselineAi's own close/raise/cut/retire/debt/hire toolkit, which
/// applies to every held route regardless of region — an inherited
/// cross-region route from a prior strategy still gets managed, just
/// never opened fresh). Never proposes a cross-region RouteOrder or
/// NegotiationOrder.
///
/// M40 (hub-awareness): that same profitability ranking is now
/// hub-aware, not just nonstop-demand-aware — a real equivalence again,
/// not an invented bonus. FinancialResolver pays each *leg* its own fare
/// for every passenger CompetitiveMarket routes over it, one-stop
/// itineraries included (NetworkPlanner fills a leg's leftover capacity
/// with connecting traffic after nonstop demand), so a candidate that
/// connects to an already-served city really would carry extra,
/// currently-uncounted passengers at this leg's own fare — see
/// EstimatedHubBonusDemand for exactly which demand that is and why.
///
/// M41: domestic gateway negotiation now skips any city already pursued
/// (a pending bid in flight, or a won-but-unused slot) instead of
/// re-bidding the same top-ranked gateway every quarter — the same
/// latent bug RapidExpansionStrategy was measurably losing real cash to;
/// see AiToolkit.CitiesAlreadyPursuedForNegotiation.
///
/// M57 (docs/BASELINE_AI_ROADMAP.md): a real trace (M55) found this
/// strategy alone sent 51.7% of its own opens into sustained real
/// unprofitability — by far the dominant driver, well ahead of
/// RapidExpansionStrategy's 13.1%. The mechanism: EstimatedRevenue
/// (renamed this round from its old, misleading name EstimatedProfit —
/// it never subtracted cost) has no capacity term, so a bigger aircraft
/// than a pair's own demand needs never lowers the score, since carried
/// is capped at demand either way. Four fixes were tried and reverted
/// (ranking by real profit directly, by real captured demand, a minimal
/// profitability floor, a load-factor-derated ranking — see M56) —
/// every one let aircraft-fit quality influence *which pair* wins, and
/// every one collapsed a real 300-game sweep, because this game's
/// era-available fleet is coarse and discrete, so which pair happens to
/// have an available type near its own best fit is partly an accident
/// of the catalog, not a signal of real market size — comparing a
/// fit-sensitive number *across pairs* leaks that accident into pair
/// selection.
///
/// What ships now instead is two strictly separated phases: Phase 1
/// (which pair wins) is completely unchanged — still EstimatedRevenue,
/// still the same ranking as M40. Phase 2, added on top, re-selects only
/// the *aircraft type* for that one already-decided pair, maximizing
/// real, cost-subtracted profit (FareCalculator.EstimatedProfit) among
/// the same candidate pool the pair search already had access to. That
/// comparison only ever happens within one fixed pair, never across
/// pairs, so the catalog-accident risk above can't reach pair selection
/// at all — and unlike the floor design, it substitutes a better fit
/// rather than rejecting, so there's no "opens nothing" failure mode.
/// See FindMostProfitableDomesticPairAcrossTypes for the exact mechanism.
/// </summary>
public sealed class DomesticProfitOptimizerStrategy : IAiStrategy
{
    /// <summary>(Proposed, tunable) — see the class doc comment.</summary>
    public const int MaxDomesticRoutes = 5;

    public string Name => "Domestic Profit Optimizer";

    public IReadOnlySet<GameEra> ApplicableEras { get; } = new HashSet<GameEra> { GameEra.Starting, GameEra.MidGame };

    /// <summary>
    /// (Proposed, tunable) — tighter cash favors the cautious
    /// consolidation play; see docs/BASELINE_AI_ROADMAP.md's M39 section
    /// for why cash (not route count) is the signal used to
    /// differentiate airlines at quarter 1, when every airline's route
    /// count is identically zero.
    /// </summary>
    public double Suitability(StrategyContext context) => 1 - AiSuitability.CashRatio(context);

    public IReadOnlyList<QuarterOrder> DecideOrders(StrategyContext context)
    {
        var orders = new List<QuarterOrder>();
        var reservedUnitIds = new HashSet<int>();
        var slotsReservedByCity = new Dictionary<string, int>();
        var citySpendReservedByCity = new Dictionary<string, int>();
        var spent = 0;
        var validOriginCityIds = AiToolkit.ValidOriginCityIds(context);

        var domesticRouteCount = context.CurrentRoutes
            .Count(r => r.Origin.Region == context.HomeRegion && r.Destination.Region == context.HomeRegion);

        if (domesticRouteCount < MaxDomesticRoutes && spent + 1 <= context.ActionCap)
        {
            var idleBest = FindMostProfitableDomesticPairUsingIdleFleet(context, reservedUnitIds, validOriginCityIds);

            if (idleBest is not null && AiToolkit.TryOrientFromValidOrigin(idleBest.Value.Pair, validOriginCityIds, out var origin, out var destination)
                && BaselineAi.HasSlotAtEveryGateway(origin, destination, context.AvailableSlotsByCity, slotsReservedByCity)
                && AiToolkit.HasRoomAtEveryCity(origin, destination, context.CityWideSlotsRemaining, citySpendReservedByCity))
            {
                orders.Add(new RouteOrder(origin, destination, idleBest.Value.Aircraft));
                BaselineAi.ReserveSlots(origin, destination, slotsReservedByCity);
                AiToolkit.ReserveCityWideSlots(origin, destination, citySpendReservedByCity);
                spent += 1;
            }
            else if (idleBest is null)
            {
                // Nothing idle can reach a profitable intra-region pair -
                // either lease the best-range era-available type and open
                // the most profitable candidate it can reach, or (M38-style)
                // order it instead when cash comfortably covers buying
                // outright. No "best available type by range" search here
                // the way BaselineAi's own new-pair fallback does - the
                // profitability ranking itself picks the type, since a
                // bigger/faster aircraft isn't automatically the most
                // profitable one on a short intra-region hop.
                var bestOrdered = FindMostProfitableDomesticPairAcrossTypes(context, context.EraAvailableTypes, validOriginCityIds);
                var oriented = bestOrdered is not null && AiToolkit.TryOrientFromValidOrigin(bestOrdered.Value.Pair, validOriginCityIds, out var newOrigin, out var newDestination)
                    ? (Origin: newOrigin, Destination: newDestination)
                    : ((City Origin, City Destination)?)null;

                if (bestOrdered is not null && oriented is not null && context.AvailableCash is not null
                    && FinancialState.CanAfford(context.AvailableCash.Value, BaselineAi.ComfortableOrderCost(bestOrdered.Value.Aircraft)))
                {
                    orders.Add(new AircraftOrder(bestOrdered.Value.Aircraft, Quantity: 1));
                    spent += 1;
                }
                else if (bestOrdered is not null && oriented is not null && spent + 2 <= context.ActionCap
                    && BaselineAi.HasSlotAtEveryGateway(oriented.Value.Origin, oriented.Value.Destination, context.AvailableSlotsByCity, slotsReservedByCity)
                    && AiToolkit.HasRoomAtEveryCity(oriented.Value.Origin, oriented.Value.Destination, context.CityWideSlotsRemaining, citySpendReservedByCity))
                {
                    orders.Add(new LeaseAircraftOrder(bestOrdered.Value.Aircraft, Quantity: 1));
                    orders.Add(new RouteOrder(oriented.Value.Origin, oriented.Value.Destination, bestOrdered.Value.Aircraft));
                    BaselineAi.ReserveSlots(oriented.Value.Origin, oriented.Value.Destination, slotsReservedByCity);
                    AiToolkit.ReserveCityWideSlots(oriented.Value.Origin, oriented.Value.Destination, citySpendReservedByCity);
                    spent += 2;
                }
            }
        }

        // Negotiates for a gateway strictly within its own home region -
        // "never cross-region" (the class doc comment) covers routes and
        // negotiation alike, but a domestic hub is still real and worth
        // securing. M45: falls back to a domestic gateway with
        // idle-expired contestable inventory when no fully-unserved one
        // remains in the home region.
        var bestDomesticGateway = FindBestUnservedDomesticGateway(context) ?? FindBestIdleContestableDomesticGateway(context);
        AiToolkit.NegotiateOrBorrow(context, bestDomesticGateway, orders, ref spent);

        // M52: a marketing campaign on whichever already-served route(s)
        // are genuinely contested - never region-filtered, same reasoning
        // as the shared toolkit below.
        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        // Apply the shared toolkit to whatever's already held -
        // close/raise/cut, retire, pay down debt, hire. Never
        // region-filtered - managing what's already there is sound
        // regardless of how it got there.
        AiToolkit.ApplyManagementToolkit(context, orders, reservedUnitIds, spent);

        return orders;
    }

    private static City? FindBestUnservedDomesticGateway(StrategyContext context)
    {
        var touchedCityIds = context.CurrentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet();
        var alreadyPursued = AiToolkit.CitiesAlreadyPursuedForNegotiation(context);

        return context.AllCities
            .Where(c => c.Region == context.HomeRegion && c.IsGateway && !touchedCityIds.Contains(c.Id) && !alreadyPursued.Contains(c.Id))
            .OrderByDescending(c => c.DemandWeight)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>M45: the region-restricted sibling of AiToolkit.FindBestIdleContestableGateway - "never cross-region" (the class doc comment) covers this too.</summary>
    private static City? FindBestIdleContestableDomesticGateway(StrategyContext context)
    {
        if (context.IdleContestableSlotsByCity is null)
        {
            return null;
        }

        var alreadyPursued = AiToolkit.CitiesAlreadyPursuedForNegotiation(context);
        var cityById = context.AllCities.ToDictionary(c => c.Id, StringComparer.Ordinal);

        return context.IdleContestableSlotsByCity
            .Where(kv => kv.Value > 0 && !alreadyPursued.Contains(kv.Key))
            .Select(kv => cityById[kv.Key])
            .Where(c => c.Region == context.HomeRegion)
            .OrderByDescending(c => c.DemandWeight)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>Mirrors BaselineAi.FindBestUnservedPairUsingIdleFleet's shape, but region-filtered and profitability-ranked instead of demand-ranked.</summary>
    private static (PairDemand Pair, AircraftType Aircraft)? FindMostProfitableDomesticPairUsingIdleFleet(StrategyContext context, IReadOnlySet<int> reservedUnitIds, IReadOnlySet<string> validOriginCityIds)
    {
        var candidateTypes = context.OwnFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Select(u => u.Type)
            .Distinct()
            .ToList();

        return FindMostProfitableDomesticPairAcrossTypes(context, candidateTypes, validOriginCityIds);
    }

    /// <summary>
    /// M44: validOriginCityIds filters candidates to those touching a
    /// city this airline can legally originate a new route from - see
    /// AiToolkit.ValidOriginCityIds/TryOrientFromValidOrigin. M55: also
    /// excludes any pair this airline closed within ReopenCooldownQuarters
    /// - this strategy's own profit-based ranking already makes this a
    /// smaller real risk than RapidExpansionStrategy's own demand-only
    /// search (M54's named risk), but the same rule applies regardless of
    /// which strategy is looking, for one consistent contract.
    /// </summary>
    private static (PairDemand Pair, AircraftType Aircraft)? FindMostProfitableDomesticPairAcrossTypes(StrategyContext context, IReadOnlyList<AircraftType> candidateTypes, IReadOnlySet<string> validOriginCityIds)
    {
        var served = context.CurrentRoutes
            .SelectMany(r => new[] { (r.Origin.Id, r.Destination.Id), (r.Destination.Id, r.Origin.Id) })
            .ToHashSet();
        var recentlyClosed = AiToolkit.RecentlyClosedPairKeys(context, AiToolkit.ReopenCooldownQuarters);

        var domesticPairs = context.DemandModel.AllPairs(context.AllCities)
            .Where(p => p.Origin.Region == context.HomeRegion && p.Destination.Region == context.HomeRegion)
            .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
            .Where(p => !recentlyClosed.Contains(QuarterEngine.NormalizedPairKey(p.Origin, p.Destination)))
            .Where(p => validOriginCityIds.Contains(p.Origin.Id) || validOriginCityIds.Contains(p.Destination.Id))
            .ToList();

        // Built once per DecideOrders call, not once per candidate - hub
        // bonus depends only on the existing network, not on which
        // candidate/aircraft is being scored.
        var neighborsByCity = BuildNeighborsByCity(context.CurrentRoutes);
        var pairsWithHubBonus = domesticPairs
            .Select(p => (Pair: p, HubBonus: EstimatedHubBonusDemand(context.DemandModel, neighborsByCity, p)))
            .ToList();

        // Phase 1: which pair wins - unchanged since M40. Still ranked by
        // EstimatedRevenue, not real profit; see the class doc comment for
        // why this stays untouched while Phase 2 below is new.
        var best = candidateTypes
            .SelectMany(type => pairsWithHubBonus
                .Where(pb => pb.Pair.DistanceKm <= type.RangeKm)
                .Select(pb => (Pair: pb.Pair, Aircraft: type, HubBonus: pb.HubBonus, Revenue: EstimatedRevenue(pb.Pair, type, pb.HubBonus))))
            .OrderByDescending(c => c.Revenue.Cents)
            .ThenBy(c => c.Pair.Origin.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Pair.Destination.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Aircraft.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (best.Pair is null)
        {
            return null;
        }

        // Phase 2 (M57): the pair is already decided - now re-select only
        // the aircraft *type* for it, maximizing real, cost-subtracted
        // profit among the same candidateTypes pool (never a type that
        // wasn't already a real option), filtered to whatever can reach
        // this one pair. This comparison never crosses pairs, so it can't
        // leak the catalog-accident risk the class doc comment describes
        // into pair selection - it only ever refines which aircraft flies
        // the pair Phase 1 already chose. best.Aircraft itself always
        // satisfies the range filter below (it won Phase 1's own filtered
        // ranking), so this is guaranteed non-empty.
        var bestType = candidateTypes
            .Where(type => best.Pair.DistanceKm <= type.RangeKm)
            .Select(type => (Aircraft: type, Profit: EstimatedProfitForAircraft(best.Pair, type, best.HubBonus)))
            .OrderByDescending(c => c.Profit.Cents)
            .ThenBy(c => c.Aircraft.Id, StringComparer.Ordinal)
            .First();

        return (best.Pair, bestType.Aircraft);
    }

    /// <summary>Every city directly reachable from a given city via an existing route, keyed by city id - the "who already connects to whom" map EstimatedHubBonusDemand needs.</summary>
    private static Dictionary<string, List<City>> BuildNeighborsByCity(IReadOnlyList<Route> currentRoutes)
    {
        var neighborsByCity = new Dictionary<string, List<City>>(StringComparer.Ordinal);

        void AddNeighbor(City from, City to)
        {
            if (!neighborsByCity.TryGetValue(from.Id, out var list))
            {
                list = new List<City>();
                neighborsByCity[from.Id] = list;
            }

            list.Add(to);
        }

        foreach (var route in currentRoutes)
        {
            AddNeighbor(route.Origin, route.Destination);
            AddNeighbor(route.Destination, route.Origin);
        }

        return neighborsByCity;
    }

    /// <summary>
    /// M40: the real, currently-uncounted one-stop demand a candidate
    /// pair would carry on its own new leg, if opened. For whichever
    /// endpoint is already served (the "connector"), every other city S
    /// already reachable from it becomes a brand-new S ↔ connector ↔
    /// otherEndpoint itinerary once this leg exists - carried on THIS
    /// leg, earning THIS leg's own fare, exactly the mechanic
    /// FinancialResolver/CompetitiveMarket/NetworkPlanner already apply
    /// for real (see the class doc comment). Skips any S already
    /// directly connected to otherEndpoint elsewhere - that demand is
    /// already nonstop-served, matching NetworkPlanner.Plan's own
    /// nonstop-first allocation, and skips a city already counted from
    /// the other connector to avoid double-counting when both endpoints
    /// happen to already be served. Legitimately returns 0 when neither
    /// endpoint is served yet (e.g. quarter 1) - no special-cased
    /// fallback needed, EstimatedRevenue already reduces to today's
    /// pure-nonstop ranking in that case.
    /// </summary>
    private static long EstimatedHubBonusDemand(GravityDemandModel demandModel, IReadOnlyDictionary<string, List<City>> neighborsByCity, PairDemand candidate)
    {
        var connectorPairs = new[]
        {
            (Connector: candidate.Origin, OtherEndpoint: candidate.Destination),
            (Connector: candidate.Destination, OtherEndpoint: candidate.Origin),
        };

        var bonus = 0L;
        var countedNeighborIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (connector, otherEndpoint) in connectorPairs)
        {
            if (!neighborsByCity.TryGetValue(connector.Id, out var neighbors))
            {
                continue; // connector isn't served at all - nothing to connect through
            }

            var otherEndpointNeighborIds = neighborsByCity.TryGetValue(otherEndpoint.Id, out var otherNeighbors)
                ? otherNeighbors.Select(c => c.Id).ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            foreach (var s in neighbors)
            {
                if (s.Id == otherEndpoint.Id || !countedNeighborIds.Add(s.Id) || otherEndpointNeighborIds.Contains(s.Id))
                {
                    continue;
                }

                bonus += demandModel.ForPair(s, otherEndpoint).PassengersPerQuarter;
            }
        }

        return bonus;
    }

    /// <summary>
    /// M57: renamed from its old, misleading name EstimatedProfit - it
    /// never subtracted cost. fare-per-passenger x carried (nonstop
    /// demand plus M40's real hub bonus) - Phase 1's own ranking figure;
    /// see the class doc comment for why this stays the pair-selection
    /// signal while Phase 2 (EstimatedProfitForAircraft, below) handles
    /// aircraft-type fit separately.
    /// </summary>
    private static Money EstimatedRevenue(PairDemand pair, AircraftType aircraft, long hubBonusDemand)
    {
        var hypotheticalRoute = new Route(pair.Origin, pair.Destination, aircraft, AircraftCount: 1);
        var cappedCarried = Math.Min(pair.PassengersPerQuarter + hubBonusDemand, hypotheticalRoute.SeatsPerQuarter);

        return FareCalculator.SuggestedFarePerPassenger(hypotheticalRoute) * cappedCarried;
    }

    /// <summary>
    /// M57: Phase 2's own real, cost-subtracted profit for one specific
    /// (pair, aircraft) combination - FareCalculator.EstimatedProfit,
    /// the same formula every other real profitability decision in this
    /// codebase already uses (route management, marketing targeting).
    /// Provably single-peaked in aircraft capacity for a *fixed* pair
    /// (strictly increasing while capacity ≤ demand, since a bigger
    /// cabin dilutes the aircraft's own flat quarterly salary over more
    /// seats; strictly decreasing once capacity exceeds demand, since
    /// unused capacity still costs full price) - see the class doc
    /// comment for why that shape is only safe to rely on *within* one
    /// already-chosen pair, never across different pairs.
    /// </summary>
    private static Money EstimatedProfitForAircraft(PairDemand pair, AircraftType aircraft, long hubBonusDemand)
    {
        var hypotheticalRoute = new Route(pair.Origin, pair.Destination, aircraft, AircraftCount: 1);
        var cappedCarried = Math.Min(pair.PassengersPerQuarter + hubBonusDemand, hypotheticalRoute.SeatsPerQuarter);

        return FareCalculator.EstimatedProfit(hypotheticalRoute, cappedCarried);
    }
}
