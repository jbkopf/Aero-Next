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
        var spent = 0;

        var domesticRouteCount = context.CurrentRoutes
            .Count(r => r.Origin.Region == context.HomeRegion && r.Destination.Region == context.HomeRegion);

        if (domesticRouteCount < MaxDomesticRoutes && spent + 1 <= context.ActionCap)
        {
            var idleBest = FindMostProfitableDomesticPairUsingIdleFleet(context, reservedUnitIds);

            if (idleBest is not null && BaselineAi.HasSlotAtEveryGateway(idleBest.Value.Pair.Origin, idleBest.Value.Pair.Destination, context.AvailableSlotsByCity, slotsReservedByCity))
            {
                var (pair, aircraft) = idleBest.Value;
                orders.Add(new RouteOrder(pair.Origin, pair.Destination, aircraft));
                BaselineAi.ReserveSlots(pair.Origin, pair.Destination, slotsReservedByCity);
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
                var bestOrdered = FindMostProfitableDomesticPairAcrossTypes(context, context.EraAvailableTypes);

                if (bestOrdered is not null && context.AvailableCash is not null
                    && FinancialState.CanAfford(context.AvailableCash.Value, BaselineAi.ComfortableOrderCost(bestOrdered.Value.Aircraft)))
                {
                    orders.Add(new AircraftOrder(bestOrdered.Value.Aircraft, Quantity: 1));
                    spent += 1;
                }
                else if (bestOrdered is not null && spent + 2 <= context.ActionCap
                    && BaselineAi.HasSlotAtEveryGateway(bestOrdered.Value.Pair.Origin, bestOrdered.Value.Pair.Destination, context.AvailableSlotsByCity, slotsReservedByCity))
                {
                    orders.Add(new LeaseAircraftOrder(bestOrdered.Value.Aircraft, Quantity: 1));
                    orders.Add(new RouteOrder(bestOrdered.Value.Pair.Origin, bestOrdered.Value.Pair.Destination, bestOrdered.Value.Aircraft));
                    BaselineAi.ReserveSlots(bestOrdered.Value.Pair.Origin, bestOrdered.Value.Pair.Destination, slotsReservedByCity);
                    spent += 2;
                }
            }
        }

        // Negotiates for a gateway strictly within its own home region -
        // "never cross-region" (the class doc comment) covers routes and
        // negotiation alike, but a domestic hub is still real and worth
        // securing.
        var bestDomesticGateway = FindBestUnservedDomesticGateway(context);
        AiToolkit.NegotiateOrBorrow(context, bestDomesticGateway, orders, ref spent);

        // Apply the shared toolkit to whatever's already held -
        // close/raise/cut, retire, pay down debt, hire. Never
        // region-filtered - managing what's already there is sound
        // regardless of how it got there.
        AiToolkit.ApplyManagementToolkit(context, orders, reservedUnitIds);

        return orders;
    }

    private static City? FindBestUnservedDomesticGateway(StrategyContext context)
    {
        var touchedCityIds = context.CurrentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet();

        return context.AllCities
            .Where(c => c.Region == context.HomeRegion && c.IsGateway && !touchedCityIds.Contains(c.Id))
            .OrderByDescending(c => c.DemandWeight)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>Mirrors BaselineAi.FindBestUnservedPairUsingIdleFleet's shape, but region-filtered and profitability-ranked instead of demand-ranked.</summary>
    private static (PairDemand Pair, AircraftType Aircraft)? FindMostProfitableDomesticPairUsingIdleFleet(StrategyContext context, IReadOnlySet<int> reservedUnitIds)
    {
        var candidateTypes = context.OwnFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Select(u => u.Type)
            .Distinct()
            .ToList();

        return FindMostProfitableDomesticPairAcrossTypes(context, candidateTypes);
    }

    private static (PairDemand Pair, AircraftType Aircraft)? FindMostProfitableDomesticPairAcrossTypes(StrategyContext context, IReadOnlyList<AircraftType> candidateTypes)
    {
        var served = context.CurrentRoutes
            .SelectMany(r => new[] { (r.Origin.Id, r.Destination.Id), (r.Destination.Id, r.Origin.Id) })
            .ToHashSet();

        var domesticPairs = context.DemandModel.AllPairs(context.AllCities)
            .Where(p => p.Origin.Region == context.HomeRegion && p.Destination.Region == context.HomeRegion)
            .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
            .ToList();

        // Built once per DecideOrders call, not once per candidate - hub
        // bonus depends only on the existing network, not on which
        // candidate/aircraft is being scored.
        var neighborsByCity = BuildNeighborsByCity(context.CurrentRoutes);
        var pairsWithHubBonus = domesticPairs
            .Select(p => (Pair: p, HubBonus: EstimatedHubBonusDemand(context.DemandModel, neighborsByCity, p)))
            .ToList();

        var best = candidateTypes
            .SelectMany(type => pairsWithHubBonus
                .Where(pb => pb.Pair.DistanceKm <= type.RangeKm)
                .Select(pb => (Pair: pb.Pair, Aircraft: type, Profit: EstimatedProfit(pb.Pair, type, pb.HubBonus))))
            .OrderByDescending(c => c.Profit.Cents)
            .ThenBy(c => c.Pair.Origin.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Pair.Destination.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Aircraft.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        return best.Pair is null ? null : (best.Pair, best.Aircraft);
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
    /// fallback needed, EstimatedProfit already reduces to today's
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

    /// <summary>fare-per-passenger x carried (nonstop demand plus M40's real hub bonus) - the actual dollar figure ranked by; see the class doc comment for why this is a real profit proxy, not a loose one, under today's uniform-margin fare model.</summary>
    private static Money EstimatedProfit(PairDemand pair, AircraftType aircraft, long hubBonusDemand)
    {
        var hypotheticalRoute = new Route(pair.Origin, pair.Destination, aircraft, AircraftCount: 1);
        var cappedCarried = Math.Min(pair.PassengersPerQuarter + hubBonusDemand, hypotheticalRoute.SeatsPerQuarter);

        return FareCalculator.SuggestedFarePerPassenger(hypotheticalRoute) * cappedCarried;
    }
}
