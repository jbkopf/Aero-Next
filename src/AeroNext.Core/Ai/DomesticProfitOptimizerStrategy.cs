using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// M39 (docs/BASELINE_AI_ROADMAP.md): consolidate a small, profitable
/// home-region network rather than chase every market in the world.
/// Below MaxDomesticRoutes intra-region routes, opens the single best
/// remaining candidate ranked by raw demand (M56 follow-up below revised
/// this from the original revenue-based ranking - see that paragraph for
/// why). At the cap, stops opening entirely and only optimizes what it
/// already holds (reusing
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
/// (which pair wins) was, at the time, left completely unchanged - still
/// EstimatedRevenue, still the same ranking as M40 (see the M56 follow-up
/// paragraph below for why that stopped being true). Phase 2, added on
/// top, re-selects only
/// the *aircraft type* for that one already-decided pair, maximizing
/// real, cost-subtracted profit (FareCalculator.EstimatedProfit) among
/// the same candidate pool the pair search already had access to. That
/// comparison only ever happens within one fixed pair, never across
/// pairs, so the catalog-accident risk above can't reach pair selection
/// at all — and unlike the floor design, it substitutes a better fit
/// rather than rejecting, so there's no "opens nothing" failure mode.
/// See FindMostProfitableDomesticPairAcrossTypes for the exact mechanism.
///
/// M58 resume (round 3): ApplicableEras narrowed from { Starting,
/// MidGame } to { Starting } only. Real 60-game diagnostic data found
/// this strategy's own Suitability (1 - CashRatio, calibrated in M39
/// against a $20M quarter-1 reference) pinned at exactly 0.0 for 90.3% of
/// all MidGame/LateGame SelectStrategy comparisons - any airline that
/// outgrows the quarter-1 reference (nearly all of them, by year 11)
/// permanently loses the comparison to NetworkOptimizerStrategy
/// regardless of real need, measured as NetworkOptimizerStrategy being
/// chosen at an average $596.1M cash versus $286.5M when this strategy
/// was chosen - backwards from either strategy's intent. Rather than
/// rescale a cash reference that was only ever meant to differentiate at
/// quarter 1, the eras are now exclusive: this strategy owns the
/// early-game job it actually describes above (build cash and a
/// home-region network), and NetworkOptimizerStrategy owns everything
/// from year 11 on (see that class's own doc comment). See
/// docs/BASELINE_AI_ROADMAP.md's M58 section for the full evidence chain.
///
/// M56 follow-up (docs/BASELINE_AI_ROADMAP.md): the deferred half of
/// M56/M57's own history, finally closed. Real per-quarter instrumentation
/// (digging into why NorthAmerica specifically fails to recover) found
/// NorthAmerica's own network exploding in size once it ran out of
/// genuinely good candidate pairs (its 22-city pool leaves a large tail
/// of merely-mediocre ones after the best ~5 are gone) - operating cost
/// growing faster than revenue as progressively worse pairs kept getting
/// opened, margin ratio collapsing from a healthy ~1.4 to 0.587, net
/// income reaching -$33.07M/quarter right before a bankruptcy wave.
/// EastAsia, with only 10 cities, simply runs out of candidates early and
/// stops growing - the real difference was never city *quality* (pre-collapse
/// revenue-per-route was similar, $3.72M NA vs $3.91M EA), it was having
/// enough mediocre candidates left to keep reaching for. Phase 1's own
/// EstimatedRevenue ranking (fare x capped-carried) had no way to tell a
/// genuinely good pair from a merely-adequate one once "good enough to
/// carry its own capacity" was cleared - exactly the mechanism M56's own
/// trace already named responsible for 51.7% of this strategy's own bad
/// opens.
///
/// Fixed by switching Phase 1 to raw demand (PassengersPerQuarter + the
/// existing M40 hub bonus) - mechanistically different from all four of
/// M56's own reverted attempts (real profit, real captured demand, a
/// profitability floor, load-factor-derated ranking), every one of which
/// still depended on a specific candidate aircraft's own capacity once
/// carried gets capped at demand, which M56 proved favors ever-smaller
/// aircraft without bound. Raw demand never references capacity at all -
/// the exact same shape as RapidExpansionStrategy's own already-proven-safe
/// AiToolkit.EffectiveDemandScore (13.1% bad-open rate, never the dominant
/// driver), just without that strategy's own M64 ConsolidationMultiplier,
/// which this class's own M64 paragraph above already found regresses
/// when stacked on this class's own M40 hub bonus. EstimatedRevenue
/// itself is removed (confirmed dead - no other caller anywhere). Phase 2
/// (M57's own aircraft-type refinement) is untouched - it only ever
/// compares types within one already-chosen pair, never across pairs, so
/// it can't leak a capacity bias back into pair selection regardless of
/// what Phase 1 ranks by.
///
/// Capacity-mismatch follow-up (docs/OPEN_WORK.md): a real traced
/// bankruptcy (Saipan) found Phase 1+2 can still land on a genuinely
/// catastrophic combination (~23% load factor) even under raw-demand
/// ranking - "good enough to carry its own capacity" isn't actually
/// enforced anywhere, it just usually happens to hold. Rather than a
/// fifth attempt at changing the ranking (the exact shape all four M56
/// attempts shared, and the reason each one leaked a catalog accident
/// into pair selection), <see cref="MeetsCatastropheFloor"/> is a
/// post-hoc gate: Phase 1 and Phase 2 run completely unmodified and
/// still pick exactly what they always would; only whether to *act* on
/// that already-chosen pick is now conditional. See that method's own
/// doc comment for the full reasoning and why this is structurally
/// different from the four reverted attempts.
/// </summary>
public sealed class DomesticProfitOptimizerStrategy : IAiStrategy
{
    /// <summary>(Proposed, tunable) — see the class doc comment.</summary>
    public const int MaxDomesticRoutes = 5;

    public string Name => "Domestic Profit Optimizer";

    public IReadOnlySet<GameEra> ApplicableEras { get; } = new HashSet<GameEra> { GameEra.Starting };

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
            // Both searches below only return routes that can be opened right
            // now (AiToolkit.CanOpenNow), so no separate slot/capacity check.
            var idleBest = FindMostProfitableDomesticPairUsingIdleFleet(context, reservedUnitIds, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity);
            if (idleBest is not null && !MeetsCatastropheFloor(idleBest.Value.Pair, idleBest.Value.Aircraft, idleBest.Value.Stops))
            {
                idleBest = null; // catastrophic mismatch - skip this resource rather than commit; falls through to the buy-new branch below, a genuinely different resource, not a downgrade to a worse pair
            }

            if (idleBest is not null && AiToolkit.TryOrientFromValidOrigin(idleBest.Value.Pair, validOriginCityIds, out var origin, out var destination))
            {
                var stops = AiToolkit.OrientStops(idleBest.Value.Pair, origin, idleBest.Value.Stops);
                orders.Add(new RouteOrder(origin, destination, idleBest.Value.Aircraft, stops));
                BaselineAi.ReserveSlots(origin, destination, slotsReservedByCity, stops);
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
                // Geopolitical-aircraft-restrictions follow-up: nothing
                // idle, so the winning candidate will end up bought or
                // leased - the union of both stays legal for either
                // branch below.
                var acquirableTypes = context.PurchasableOrEraTypes.Union(context.LeasableOrEraTypes).ToList();
                var bestOrdered = FindMostProfitableDomesticPairAcrossTypes(context, acquirableTypes, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity);
                if (bestOrdered is not null && !MeetsCatastropheFloor(bestOrdered.Value.Pair, bestOrdered.Value.Aircraft, bestOrdered.Value.Stops))
                {
                    bestOrdered = null; // catastrophic mismatch - skip opening anything this quarter rather than commit
                }

                var oriented = bestOrdered is not null && AiToolkit.TryOrientFromValidOrigin(bestOrdered.Value.Pair, validOriginCityIds, out var newOrigin, out var newDestination)
                    ? (Origin: newOrigin, Destination: newDestination)
                    : ((City Origin, City Destination)?)null;

                if (bestOrdered is not null && oriented is not null && context.AvailableCash is not null
                    && FinancialState.CanAfford(context.AvailableCash.Value, BaselineAi.ComfortableOrderCost(bestOrdered.Value.Aircraft)))
                {
                    orders.Add(new AircraftOrder(bestOrdered.Value.Aircraft, Quantity: 1));
                    spent += 1;
                }
                else if (bestOrdered is not null && oriented is not null && spent + 1 <= context.ActionCap)
                {
                    // Leasing resolves next quarter now (two-phase,
                    // docs/AIRCRAFT_ACQUISITION_ROADMAP.md) - submit it
                    // alone, 1 action, no paired RouteOrder yet. Once
                    // delivered it shows up idle and a later quarter's own
                    // re-evaluation opens the pair.
                    orders.Add(new LeaseAircraftOrder(bestOrdered.Value.Aircraft, Quantity: 1));
                    spent += 1;
                }
            }
        }

        // Negotiates for a gateway strictly within its own home region -
        // "never cross-region" (the class doc comment) covers routes and
        // negotiation alike, but a domestic hub is still real and worth
        // securing. M45: falls back to a domestic gateway with
        // idle-expired contestable inventory when no fully-unserved one
        // remains in the home region.
        // Multi-stop routes (docs/MULTI_STOP_ROUTES_DESIGN.md): a home-region
        // refuelling stop its best profitable domestic multi-stop route
        // still needs comes first.
        var bestDomesticGateway = AiToolkit.FindStopNeededByBestMultiStopPlan(context, reservedUnitIds, validOriginCityIds,
                p => p.Origin.Region == context.HomeRegion && p.Destination.Region == context.HomeRegion,
                slotsReservedByCity, citySpendReservedByCity, stopFilter: c => c.Region == context.HomeRegion)
            ?? FindBestUnservedDomesticGateway(context)
            ?? FindBestIdleContestableDomesticGateway(context);
        AiToolkit.NegotiateOrBorrow(context, bestDomesticGateway, orders, ref spent);

        // A marketing campaign, if one is estimated to pay for itself (see
        // AiToolkit.ApplyMarketingCampaign) - never region-filtered, same
        // reasoning as the shared toolkit below.
        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        // Apply the shared toolkit to whatever's already held -
        // close/raise/cut, retire, pay down debt, hire. Never
        // region-filtered - managing what's already there is sound
        // regardless of how it got there.
        AiToolkit.ApplyManagementToolkit(context, orders, reservedUnitIds, spent);

        return orders;
    }

    /// <summary>M58: internal, not private - NetworkOptimizerStrategy reuses this directly rather than re-deriving the same domestic-gateway search.</summary>
    internal static City? FindBestUnservedDomesticGateway(StrategyContext context)
    {
        var touchedCityIds = context.CurrentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet();
        var alreadyPursued = AiToolkit.CitiesAlreadyPursuedForNegotiation(context);

        return context.AllCities
            .Where(c => c.Region == context.HomeRegion && c.IsGateway && !touchedCityIds.Contains(c.Id) && !alreadyPursued.Contains(c.Id))
            .Where(c => AiToolkit.HasProfitableNonstopRouteTo(context, c))
            .OrderByDescending(c => c.DemandWeight)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// M45: the region-restricted sibling of AiToolkit.FindBestIdleContestableGateway - "never cross-region" (the class doc comment) covers this too.
    /// M58: internal, not private - NetworkOptimizerStrategy reuses this directly.
    /// </summary>
    internal static City? FindBestIdleContestableDomesticGateway(StrategyContext context)
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

    /// <summary>
    /// Mirrors BaselineAi.FindBestUnservedPairUsingIdleFleet's shape, but region-filtered and profitability-ranked instead of demand-ranked.
    /// M58: internal, not private - NetworkOptimizerStrategy reuses this directly (same domestic-only, profit-aware search it wants too, unmodified).
    /// </summary>
    internal static (PairDemand Pair, AircraftType Aircraft, StopList Stops)? FindMostProfitableDomesticPairUsingIdleFleet(
        StrategyContext context, IReadOnlySet<int> reservedUnitIds, IReadOnlySet<string> validOriginCityIds,
        Dictionary<string, int> slotsReservedByCity, Dictionary<string, int> citySpendReservedByCity)
    {
        var candidateTypes = context.OwnFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Select(u => u.Type)
            .Distinct()
            .ToList();

        return FindMostProfitableDomesticPairAcrossTypes(context, candidateTypes, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity);
    }

    /// <summary>
    /// M44: validOriginCityIds filters candidates to those touching a
    /// city this airline can legally originate a new route from - see
    /// AiToolkit.ValidOriginCityIds/TryOrientFromValidOrigin. M55: also
    /// excludes any pair this airline closed within ReopenCooldownQuarters
    /// - this strategy's own profit-based ranking already makes this a
    /// smaller real risk than RapidExpansionStrategy's own demand-only
    /// search (M54's named risk), but the same rule applies regardless of
    /// which strategy is looking, for one consistent contract. M58:
    /// internal, not private - NetworkOptimizerStrategy calls this
    /// directly for its own (much lower-priority) new-pair search rather
    /// than re-deriving the same already-proven, already-measured logic.
    /// </summary>
    internal static (PairDemand Pair, AircraftType Aircraft, StopList Stops)? FindMostProfitableDomesticPairAcrossTypes(
        StrategyContext context, IReadOnlyList<AircraftType> candidateTypes, IReadOnlySet<string> validOriginCityIds,
        Dictionary<string, int> slotsReservedByCity, Dictionary<string, int> citySpendReservedByCity)
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
            .Where(p => AiToolkit.CanOpenNow(context, p, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity))
            .ToList();

        // Built once per DecideOrders call, not once per candidate - hub
        // bonus depends only on the existing network, not on which
        // candidate/aircraft is being scored.
        var neighborsByCity = BuildNeighborsByCity(context.CurrentRoutes);
        var pairsWithHubBonus = domesticPairs
            .Select(p => (Pair: p, HubBonus: EstimatedHubBonusDemand(context.DemandModel, neighborsByCity, p)))
            .ToList();

        // M64: deliberately NOT wired in here, unlike RapidExpansionStrategy.
        // A real 300-game sweep found stacking AiToolkit's own
        // ConsolidationMultiplier on top of this strategy's already-real
        // M40 EstimatedHubBonusDemand caused a severe regression
        // (isolated by direct ablation: this strategy's wiring alone
        // reproduced the collapse; RapidExpansionStrategy's wiring alone
        // did not) - the two mechanisms answer overlapping questions
        // ("does this candidate connect to my existing network") without
        // being designed to compose, and this strategy's Phase 1 has no
        // cross-region tier the way RapidExpansionStrategy's ranking does
        // to bound the bonus's effect. See docs/HUB_DEMAND_ROADMAP.md's
        // M64 section for the real measured numbers.

        // Phase 1: which pair wins - M56 follow-up: ranked by raw demand
        // (PassengersPerQuarter + HubBonus, both aircraft-independent),
        // not EstimatedRevenue - see the class doc comment for the real
        // evidence and why this is safe where M56's own four attempts
        // weren't. DemandScore never depends on `type`, so every
        // candidate type reachable for a given pair scores identically;
        // Phase 2 below re-derives the real best type for whichever pair
        // wins regardless of which one is carried through here.
        // Multi-stop routes (docs/MULTI_STOP_ROUTES_DESIGN.md): each type
        // flies its own path - nonstop when it can, else through refuelling
        // stops it holds free slots at. A multi-stop candidate must also be
        // profitable: it ties up a scarce stop slot.
        var stopPaths = new StopPathFinder(context.AllCities);

        (PairDemand Pair, AircraftType Aircraft, long HubBonus, StopList Stops)? Fly(PairDemand pair, AircraftType type, long hubBonus)
        {
            var stops = stopPaths.ShortestStops(pair.Origin, pair.Destination, type.RangeKm);

            if (stops is null)
            {
                return null;
            }

            if (!stops.Value.IsEmpty
                && (!AiToolkit.CanOpenNow(context, pair, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity, stops.Value)
                    || EstimatedProfitForAircraft(pair, type, hubBonus, stops.Value) < Money.Zero))
            {
                return null;
            }

            return (pair, type, hubBonus, stops.Value);
        }

        var best = candidateTypes
            .SelectMany(type => pairsWithHubBonus
                .Select(pb => Fly(pb.Pair, type, pb.HubBonus))
                .Where(c => c is not null)
                .Select(c => (Pair: c!.Value.Pair, Aircraft: type, HubBonus: c.Value.HubBonus, DemandScore: c.Value.Pair.PassengersPerQuarter + c.Value.HubBonus)))
            .OrderByDescending(c => c.DemandScore)
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
            .Select(type => Fly(best.Pair, type, best.HubBonus))
            .Where(c => c is not null)
            .Select(c => (Aircraft: c!.Value.Aircraft, c.Value.Stops, Profit: EstimatedProfitForAircraft(best.Pair, c.Value.Aircraft, best.HubBonus, c.Value.Stops)))
            .OrderByDescending(c => c.Profit.Cents)
            .ThenBy(c => c.Aircraft.Id, StringComparer.Ordinal)
            .First();

        return (best.Pair, bestType.Aircraft, bestType.Stops);
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
    private static Money EstimatedProfitForAircraft(PairDemand pair, AircraftType aircraft, long hubBonusDemand, StopList stops = default)
    {
        var hypotheticalRoute = new Route(pair.Origin, pair.Destination, aircraft, AircraftCount: 1, Stops: stops);
        var cappedCarried = Math.Min(pair.PassengersPerQuarter + hubBonusDemand, hypotheticalRoute.SeatsPerQuarter);

        return FareCalculator.EstimatedProfit(hypotheticalRoute, cappedCarried);
    }

    /// <summary>
    /// Capacity-mismatch follow-up (docs/OPEN_WORK.md): a genuinely
    /// different angle from the four already-tried-and-reverted fixes the
    /// class doc comment above documents (real profit, real captured
    /// demand, a minimum profitability floor, a load-factor-derated
    /// ranking) — every one of those changed the *ranking itself*, which
    /// is exactly what let a catalog accident (which pair happens to have
    /// an available type near its own best fit) leak into *which pair
    /// wins* across a cross-pair comparison. This gate never touches
    /// ranking at all: Phase 1 and Phase 2 run completely unmodified and
    /// pick exactly what they always would; this only decides, after the
    /// fact, whether to act on their own already-chosen (pair, aircraft)
    /// at all. A real traced case (Saipan, docs/OPEN_WORK.md): this
    /// strategy's own Phase 1+2 chose a pair/aircraft combination whose
    /// real load factor was ~23% — a structural, compounding loss from
    /// the quarter it opened, not a slow-growing one, since operating
    /// cost is charged on capacity offered, not seats filled.
    /// <see cref="MinLoadFactorBeforeSkipping"/> is deliberately far below
    /// FareCalculator's own break-even load factor (~77%, 1/1.3) — this
    /// is a catastrophe backstop, not a profitability gate; a genuinely
    /// mediocre-but-viable pick (the ordinary outcome this strategy's own
    /// M56/M57 history already tuned for) still goes through untouched.
    /// </summary>
    private const double MinLoadFactorBeforeSkipping = 0.25;

    private static bool MeetsCatastropheFloor(PairDemand pair, AircraftType aircraft, StopList stops)
    {
        var route = new Route(pair.Origin, pair.Destination, aircraft, AircraftCount: 1, Stops: stops);
        return pair.PassengersPerQuarter >= MinLoadFactorBeforeSkipping * route.SeatsPerQuarter;
    }
}
