using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// M39: the close/raise/cut/retire/pay-down-debt/hire logic shared by
/// every IAiStrategy — extracted from BaselineAi.DecideOrders's own tail
/// section (M31/M35-M37), which itself stays byte-for-byte unchanged
/// (see BaselineAi's own class doc comment) rather than being rewritten
/// to call this. A deliberate, small duplication in exchange for keeping
/// the ~490 tests and both real loops that already call
/// BaselineAi.DecideOrders directly completely unaffected by this
/// milestone.
///
/// M45: bumped from internal to public — ComputeCityWideSlotsRemaining is
/// needed directly by the orchestration layer (AeroNext.QaHarness/
/// FullGameRunner.cs, AeroNext.Sim's own M13 loop), not just by
/// IAiStrategy implementations within this assembly, the same reason
/// LandingRightsBoard's own analogous methods are public.
/// </summary>
public static class AiToolkit
{
    /// <summary>
    /// M64: the route-*opening* counterpart to
    /// <see cref="Airlines.NetworkPlanner"/>'s M62 hub-*selection* loyalty
    /// bonus. M63's real-dataset-verified, individually-safe
    /// GravityDemandModel.ForPair hub-demand bonus caused a real 300-game
    /// win-rate collapse (39.0%→7.0%) when its 5 real call sites were
    /// enabled - a topology diagnostic found hub-and-spoke structure never
    /// actually emerged, because every real route-opening ranking function
    /// (BaselineAi.FindBestUnservedPair and its two live-strategy
    /// equivalents) ranks candidate pairs by raw PassengersPerQuarter with
    /// zero concept of "prefer a pair that reinforces a city I already
    /// operate from." This constant is that missing signal's strength -
    /// multiplicative, not additive like NetworkPlanner's own km-based
    /// terms, because PassengersPerQuarter varies over orders of magnitude
    /// across different candidate pairs (unlike detour, already a bounded,
    /// comparable unit for one specific pair's own candidates). Bounded to
    /// a modest fraction so it can tilt a genuinely close choice toward
    /// consolidation, never make a small pair look bigger than a
    /// dramatically larger, unrelated one - see EffectiveDemandScore's own
    /// doc comment. Set to 0.0 to reproduce pre-M64 pure-demand ranking -
    /// the sanctioned ablation lever, matching this project's own
    /// established ablation-instrument discipline
    /// (docs/ROUTE_CAPACITY_ROADMAP.md's M45, NetworkPlanner's own M62
    /// constants).
    /// </summary>
    public const double ConsolidationBonusFraction = 0.5;

    /// <summary>
    /// M64: a candidate pair's demand, boosted by up to
    /// <see cref="ConsolidationBonusFraction"/> (at shareByCityId = 1.0)
    /// when either side of the pair is a city this airline's own existing
    /// network already meaningfully touches - <see cref="Airlines.NetworkPlanner.ComputeRouteShareByCityId"/>'s
    /// own route-count-share proxy, the exact "already a hub for this
    /// airline" signal M62 already built and verified, reused rather than
    /// re-derived. Math.Max, not the sum, of the two sides' shares - a
    /// pair touching an already-used city on either end benefits once, not
    /// twice. Callers should compute shareByCityId once per DecideOrders
    /// call (via ComputeRouteShareByCityId(context.CurrentRoutes)), not
    /// once per candidate pair being scored.
    /// </summary>
    public static double EffectiveDemandScore(PairDemand pair, IReadOnlyDictionary<string, double> shareByCityId) =>
        pair.PassengersPerQuarter * ConsolidationMultiplier(pair.Origin, pair.Destination, shareByCityId);

    /// <summary>
    /// M64: the same bounded `1 + ConsolidationBonusFraction * share`
    /// multiplier <see cref="EffectiveDemandScore"/> applies to raw
    /// demand, factored out so a caller ranking by something other than
    /// PassengersPerQuarter (e.g. DomesticProfitOptimizerStrategy's own
    /// estimated-revenue figure) can apply the identical bonus to its own
    /// metric instead, without going through PassengersPerQuarter at all.
    /// </summary>
    public static double ConsolidationMultiplier(City origin, City destination, IReadOnlyDictionary<string, double> shareByCityId)
    {
        var share = Math.Max(
            shareByCityId.GetValueOrDefault(origin.Id, 0.0),
            shareByCityId.GetValueOrDefault(destination.Id, 0.0));

        return 1.0 + ConsolidationBonusFraction * share;
    }

    /// <summary>
    /// M36's negotiate-or-borrow logic, extracted so every strategy can
    /// reuse it with its own gateway-selection rule (DomesticProfitOptimizerStrategy
    /// restricts to intra-region gateways; RapidExpansionStrategy weights
    /// cross-region ones). Mirrors BaselineAi.DecideOrders's own inline
    /// version exactly - never attempts the negotiation the same quarter
    /// as a same-quarter RefinanceOrder, since QuarterEngine's cash gate
    /// doesn't see it (M29's own established point).
    /// </summary>
    /// <summary>
    /// M41: every gateway city this airline has already spent real
    /// money pursuing and shouldn't bid on again — a NegotiationOrder's
    /// full bid is deducted the instant it's submitted (M28,
    /// FinancialResolver.ComputeRevenue's own negotiationBidCost), win or
    /// lose, so re-bidding a city with a still-pending negotiation is a
    /// straight, avoidable loss, not a second chance. Unions
    /// PendingNegotiationCityIds (bids already in flight, not yet
    /// resolved) with AvailableSlotsByCity's own keys (a slot already won
    /// there but not yet turned into a route - also no reason to bid
    /// again). Both source fields are nullable/permissive - a caller that
    /// doesn't populate either gets an empty set here, reproducing the
    /// pre-M41 unfixed behavior exactly.
    /// </summary>
    public static IReadOnlySet<string> CitiesAlreadyPursuedForNegotiation(StrategyContext context)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        if (context.PendingNegotiationCityIds is not null)
        {
            ids.UnionWith(context.PendingNegotiationCityIds);
        }

        if (context.AvailableSlotsByCity is not null)
        {
            ids.UnionWith(context.AvailableSlotsByCity.Keys);
        }

        return ids;
    }

    /// <summary>
    /// M44: the network-contiguity valid-origin set - a new route may
    /// only originate from the home city or a city the airline already
    /// serves (touched by CurrentRoutes), the same rule
    /// QuarterEngine.RunQuarter's own homeCity parameter enforces
    /// authoritatively. Every new-pair search below uses this to avoid
    /// proposing an order that would predictably be rejected, mirroring
    /// range/era/cash/slot's own existing "never attempt what will fail"
    /// checks. HomeCity is always included, unconditionally - a real,
    /// persistent right to re-enter from home, not just an exemption for
    /// the very first route.
    /// </summary>
    public static IReadOnlySet<string> ValidOriginCityIds(StrategyContext context)
    {
        var ids = context.CurrentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet(StringComparer.Ordinal);
        ids.Add(context.HomeCity.Id);
        return ids;
    }

    /// <summary>M55 (proposed, tunable) — a route shrunk within this many quarters of context.QuarterNumber is excluded from being picked as "best performing" to grow. See RecentlyShrunkPairKeys.</summary>
    public const int GrowthCooldownQuarters = 2;

    /// <summary>M55 (proposed, tunable) — a pair closed within this many quarters of context.QuarterNumber is excluded from every new-pair search. See RecentlyClosedPairKeys.</summary>
    public const int ReopenCooldownQuarters = 2;

    /// <summary>
    /// M55: every pair this airline shrunk (CloseRouteOrder with
    /// AircraftCount left &gt; 0) within GrowthCooldownQuarters of the
    /// current quarter - the real fix for the oscillation M43 traced
    /// (shrinking a route mechanically raises its own load factor, so a
    /// route shrunk one quarter could rank as "best performing" again the
    /// very next and get grown right back). A no-op (empty set, same
    /// permissive-default convention as every other nullable
    /// StrategyContext field) when RouteHistoryByPair is null.
    /// </summary>
    public static IReadOnlySet<(string, string)> RecentlyShrunkPairKeys(StrategyContext context, int cooldownQuarters)
    {
        if (context.RouteHistoryByPair is null)
        {
            return new HashSet<(string, string)>();
        }

        return context.RouteHistoryByPair
            .Where(kv => kv.Value.LastShrunkQuarter is not null && context.QuarterNumber - kv.Value.LastShrunkQuarter.Value < cooldownQuarters)
            .Select(kv => kv.Key)
            .ToHashSet();
    }

    /// <summary>
    /// M55: every pair this airline closed entirely (CloseRouteOrder down
    /// to AircraftCount 0) within ReopenCooldownQuarters of the current
    /// quarter - stops a demand-only new-pair search (RapidExpansionStrategy's
    /// own, the "known, accepted risk" M54 named but didn't fix) from
    /// immediately re-proposing a pair the shared toolkit just closed for
    /// real, structural reasons. A no-op (empty set) when
    /// RouteHistoryByPair is null.
    /// </summary>
    public static IReadOnlySet<(string, string)> RecentlyClosedPairKeys(StrategyContext context, int cooldownQuarters)
    {
        if (context.RouteHistoryByPair is null)
        {
            return new HashSet<(string, string)>();
        }

        return context.RouteHistoryByPair
            .Where(kv => kv.Value.LastClosedQuarter is not null && context.QuarterNumber - kv.Value.LastClosedQuarter.Value < cooldownQuarters)
            .Select(kv => kv.Key)
            .ToHashSet();
    }

    /// <summary>
    /// M44: true, and correctly oriented, only if at least one side of
    /// candidate is a valid origin. GravityDemandModel.AllPairs's own
    /// canonical Origin/Destination assignment is alphabetical by city
    /// id, entirely unrelated to which side the airline can legally
    /// originate a new route from - every new-pair search needs to
    /// re-orient the winning candidate before building a RouteOrder from
    /// it, not just filter candidates out. When both sides already
    /// qualify (e.g. a direct pair between the home city and an
    /// already-served city with no route between them yet), keeps
    /// AllPairs's own canonical Origin as Origin - an arbitrary but
    /// deterministic tie-break.
    ///
    /// The false path (neither side qualifies) is a real safety net, not
    /// dead code - every real search site today already filters its own
    /// candidates by this exact same "either side" check before ever
    /// calling this method, so it's not exercised by any current test
    /// (confirmed via the coverage report, not assumed), but a public
    /// helper validating its own invariant rather than trusting every
    /// future caller to have pre-filtered is the correct, legible
    /// failure mode - silently returning a wrongly-oriented pair would
    /// be worse.
    /// </summary>
    public static bool TryOrientFromValidOrigin(PairDemand pair, IReadOnlySet<string> validOriginCityIds, out City origin, out City destination)
    {
        if (validOriginCityIds.Contains(pair.Origin.Id))
        {
            origin = pair.Origin;
            destination = pair.Destination;
            return true;
        }

        if (validOriginCityIds.Contains(pair.Destination.Id))
        {
            origin = pair.Destination;
            destination = pair.Origin;
            return true;
        }

        origin = pair.Origin;
        destination = pair.Destination;
        return false;
    }

    /// <summary>
    /// The best unserved pair touching a gateway city this airline already
    /// holds an unused slot at - checked ahead of a general new-pair
    /// search so a contested negotiation win actually gets exploited
    /// instead of stranding cash spent to win it while more bids keep
    /// going out for other gateways (the regional-balance investigation's
    /// own diagnosed root cause: one traced game had an airline win 6
    /// gateway negotiations across 11 quarters and open a route at none
    /// of them). "Unused" means AvailableSlotsByCity shows a positive
    /// balance there (M33: this airline's own remaining slot count) *and*
    /// no current route already touches that city - a gateway this
    /// airline is already flying through doesn't need this priority
    /// boost, the existing "grow best-performing route" step already
    /// covers that. Searches only reachable-by-idle-fleet pairs, matching
    /// every other new-pair search's own idle-first convention; callers
    /// fall back to their own general search when this returns null,
    /// exactly as before this method existed. Returns null (not "no
    /// priority") when AvailableSlotsByCity itself is null - a caller
    /// that doesn't populate real slot data has nothing to prioritize,
    /// same permissive-default convention as every other nullable
    /// StrategyContext field.
    ///
    /// M44: a won slot doesn't grant "start a route from nowhere" rights
    /// either - the returned pair still needs its *other* endpoint (or
    /// the won city itself) to be a valid origin. Callers use
    /// TryOrientFromValidOrigin on the returned Pair before building a
    /// RouteOrder from it - this method doesn't orient itself, since some
    /// callers (the M43 priority-boost's own two proof tests) inspect
    /// Pair.Origin/Destination directly against AllPairs's own canonical
    /// ordering.
    /// </summary>
    public static (PairDemand Pair, AircraftType Aircraft)? FindBestUnservedPairTouchingWonSlot(StrategyContext context, IReadOnlySet<int> reservedUnitIds)
    {
        if (context.AvailableSlotsByCity is null)
        {
            return null;
        }

        var touchedCityIds = context.CurrentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet(StringComparer.Ordinal);

        var wonUnusedCityIds = context.AvailableSlotsByCity
            .Where(kv => kv.Value > 0 && !touchedCityIds.Contains(kv.Key))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (wonUnusedCityIds.Count == 0)
        {
            return null;
        }

        var validOriginCityIds = ValidOriginCityIds(context);

        var served = context.CurrentRoutes
            .SelectMany(r => new[] { (r.Origin.Id, r.Destination.Id), (r.Destination.Id, r.Origin.Id) })
            .ToHashSet();

        var candidateTypes = context.OwnFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Select(u => u.Type)
            .Distinct()
            .ToList();

        var best = candidateTypes
            .SelectMany(type => context.DemandModel.AllPairs(context.AllCities)
                .Where(p => wonUnusedCityIds.Contains(p.Origin.Id) || wonUnusedCityIds.Contains(p.Destination.Id))
                .Where(p => validOriginCityIds.Contains(p.Origin.Id) || validOriginCityIds.Contains(p.Destination.Id))
                .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
                .Where(p => p.DistanceKm <= type.RangeKm)
                .Select(p => (Pair: p, Aircraft: type)))
            .OrderByDescending(c => c.Pair.PassengersPerQuarter)
            .ThenBy(c => c.Pair.Origin.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Pair.Destination.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Aircraft.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        return best.Pair is null ? null : (best.Pair, best.Aircraft);
    }

    /// <summary>
    /// M45: the shared, ownerless capacity balance for every non-gateway
    /// city with a modeled AutoCapacity - each such city's AutoCapacity
    /// minus the AircraftCount every airline's current routes commit
    /// there, combined. Unlike LandingRightsBoard.AvailableSlotsByCity,
    /// this has no per-airline owner; it's the same number for whichever
    /// airline asks. The caller must recompute this fresh immediately
    /// before each airline's own QuarterEngine.RunQuarter call within a
    /// quarter (from routesByAirline as it stands at that point), or
    /// multiple airlines could each independently see spare room and
    /// collectively overshoot a hard cap - see QuarterEngine's own
    /// cityWideSlotsRemaining doc comment.
    /// </summary>
    /// <param name="capacityBonusByCity">
    /// M46: extra slots each city has earned via periodic, demand-driven
    /// expansion (see CityCapacityBoard) — null (the default) preserves
    /// every pre-existing caller's exact behavior.
    /// </param>
    public static IReadOnlyDictionary<string, int> ComputeCityWideSlotsRemaining(
        IReadOnlyDictionary<string, IReadOnlyList<Route>> routesByAirline, IReadOnlyList<City> cities,
        IReadOnlyDictionary<string, int>? capacityBonusByCity = null)
    {
        var used = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var routes in routesByAirline.Values)
        {
            foreach (var route in routes)
            {
                used[route.Origin.Id] = used.GetValueOrDefault(route.Origin.Id) + route.AircraftCount;
                used[route.Destination.Id] = used.GetValueOrDefault(route.Destination.Id) + route.AircraftCount;
            }
        }

        return cities
            .Where(c => c.AutoCapacity is not null)
            .ToDictionary(
                c => c.Id,
                c => Math.Max(0, c.AutoCapacity!.Value + (capacityBonusByCity?.GetValueOrDefault(c.Id) ?? 0) - used.GetValueOrDefault(c.Id)),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// M45: true when cityWideSlotsRemaining is null (permissive), or
    /// there's still room left at every AutoCapacity-modeled endpoint of
    /// origin/destination, net of what this same planning pass has
    /// already reserved. Mirrors BaselineAi.HasSlotAtEveryGateway's own
    /// shape exactly, for the ownerless non-gateway mechanic instead of
    /// the negotiated gateway one.
    /// </summary>
    public static bool HasRoomAtEveryCity(City origin, City destination, IReadOnlyDictionary<string, int>? cityWideSlotsRemaining, Dictionary<string, int> citySpendReservedThisPass)
    {
        if (cityWideSlotsRemaining is null)
        {
            return true;
        }

        return new[] { origin, destination }
            .Where(c => c.AutoCapacity is not null)
            .All(c => cityWideSlotsRemaining.GetValueOrDefault(c.Id) - citySpendReservedThisPass.GetValueOrDefault(c.Id) > 0);
    }

    /// <summary>M45: the AutoCapacity-modeled sibling of BaselineAi.ReserveSlots.</summary>
    public static void ReserveCityWideSlots(City origin, City destination, Dictionary<string, int> citySpendReservedThisPass)
    {
        foreach (var city in new[] { origin, destination }.Where(c => c.AutoCapacity is not null))
        {
            citySpendReservedThisPass[city.Id] = citySpendReservedThisPass.GetValueOrDefault(city.Id) + 1;
        }
    }

    /// <summary>
    /// M45: the best gateway with idle-expired, contestable inventory this
    /// airline hasn't already spent real money pursuing (same
    /// CitiesAlreadyPursuedForNegotiation exclusion every other
    /// negotiation-target search already applies) - lets AI-vs-AI play
    /// actually reach the idle-then-recontestable mechanic instead of it
    /// only ever being reachable by a human player submitting a
    /// NegotiationOrder by hand (CLAUDE.md: the sim never runs with fewer
    /// than 4 competing networks, so a mechanic invisible to AI is a real
    /// gap). Deliberately the same "single best candidate, no fallback
    /// search" shape as FindBestUnservedGateway - deeper tuning (weighing
    /// an idle-contest bid against a fresh-expansion bid) is a follow-on.
    /// Returns null when IdleContestableSlotsByCity itself is null - a
    /// caller that doesn't populate it has nothing to target, same
    /// permissive-default convention as every other nullable
    /// StrategyContext field.
    /// </summary>
    public static City? FindBestIdleContestableGateway(StrategyContext context)
    {
        if (context.IdleContestableSlotsByCity is null)
        {
            return null;
        }

        var alreadyPursued = CitiesAlreadyPursuedForNegotiation(context);
        var cityById = context.AllCities.ToDictionary(c => c.Id, StringComparer.Ordinal);

        return context.IdleContestableSlotsByCity
            .Where(kv => kv.Value > 0 && !alreadyPursued.Contains(kv.Key))
            .Select(kv => cityById[kv.Key])
            .OrderByDescending(c => c.DemandWeight)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static void NegotiateOrBorrow(StrategyContext context, City? bestGateway, List<QuarterOrder> orders, ref int spent)
    {
        if (bestGateway is null || spent + 1 > context.ActionCap)
        {
            return;
        }

        var canAffordNow = context.AvailableCash is null || FinancialState.CanAfford(context.AvailableCash.Value, BaselineAi.NegotiationBidAmount);

        if (canAffordNow)
        {
            orders.Add(new NegotiationOrder(bestGateway, BaselineAi.NegotiationBidAmount));
            spent += 1;
        }
        else if (context.AvailableCash is not null && context.CurrentDebtBalance is not null)
        {
            var shortfall = BaselineAi.NegotiationBidAmount - context.AvailableCash.Value;
            var ceilingHeadroom = Money.FromDollars(FinancialResolver.DebtCeilingDollars) - context.CurrentDebtBalance.Value;

            if (FinancialState.CanAfford(ceilingHeadroom, shortfall))
            {
                orders.Add(new RefinanceOrder(shortfall));
            }
        }
    }

    /// <summary>
    /// M54: the best-available real-world carried figure for a route —
    /// LastQuarterSeatsCarriedByRoute's own real, competitively-resolved
    /// number when one exists (a contested route, where the solo
    /// estimate below is wrong), otherwise soloCarried (RouteEvaluator's
    /// own solo-carrier estimate, already accurate for an uncontested
    /// route since there's no rival to split demand with). Shared by
    /// every profitability check in this file so they never disagree on
    /// which number is "real."
    ///
    /// M58: internal, not private - NetworkOptimizerStrategy's own
    /// Suitability needs the exact same "real profit" figure everything
    /// else in this file already agrees on.
    /// </summary>
    internal static long BestAvailableCarried(Route route, long soloCarried, StrategyContext context)
    {
        if (context.LastQuarterSeatsCarriedByRoute is null)
        {
            return soloCarried;
        }

        var key = QuarterEngine.NormalizedPairKey(route.Origin, route.Destination);
        return context.LastQuarterSeatsCarriedByRoute.TryGetValue(key, out var real) ? real : soloCarried;
    }

    /// <summary>
    /// M58 resume (proposed, tunable): a route this far into a sustained
    /// unprofitable streak (AiRouteHistory.ConsecutiveUnprofitableQuarters)
    /// is excluded from marketing entirely - real 300-game evidence found
    /// NetworkOptimizerStrategy (which runs this method first, every
    /// quarter it's selected) repeatedly re-spending real cash on a route
    /// whose real problem was a permanent demand shock (the 1973 oil
    /// crisis), not low visibility - marketing genuinely can't fix that,
    /// and since the triggering condition never clears, the campaign just
    /// re-fires forever. Measured: NorthAmerica's own bankruptcy rate
    /// under NetworkOptimizerStrategy rose from 65.0% to 85.0% once its
    /// own Suitability signal was fixed to trigger correctly on real,
    /// sustained trouble - it was the marketing spend itself doing the
    /// damage once selection worked as intended, confirmed by cash
    /// trajectory (airlines switching to it collapsed toward zero cash by
    /// year 30, while ones that stayed on DomesticProfitOptimizerStrategy
    /// kept growing steadily despite the exact same underlying route
    /// trouble). A route that's merely newly-struggling (few consecutive
    /// bad quarters) is still a real, worthwhile marketing target - this
    /// only gives up once the pattern is genuinely persistent.
    /// </summary>
    private const int MaximumConsecutiveUnprofitableQuartersForMarketing = 4;

    /// <summary>
    /// M52, retargeted by M53 (load factor) then M54 (real profit):
    /// proposes a marketing campaign to help a genuinely unprofitable
    /// contested route recover, not to squeeze more out of one already
    /// doing fine. Restricted to contested routes (CompetitiveMarket.Resolve's
    /// own boost never applies to an uncontested pair, M47's own design,
    /// so spending real cash on one there would buy nothing visible)
    /// with FareCalculator.EstimatedProfit below zero — a real profit
    /// estimate, using LastQuarterSeatsCarriedByRoute's own real carried
    /// figure when available (see BestAvailableCarried) rather than the
    /// load-factor proxy M52/M53 used, since a contested route's real
    /// carried volume can genuinely differ from the solo-carrier
    /// estimate. Ranked worst-profit-first among the survivors. A no-op
    /// (same permissive-default convention as every other nullable
    /// StrategyContext field) when RivalCountByServedRoute is null, when
    /// the action budget is already spent, or when no served route is
    /// both contested and unprofitable. Exactly one such route picks the
    /// bigger, targeted RouteMarketingOrder there; two or more pick the
    /// smaller but broader NetworkMarketingOrder instead, since
    /// QuarterEngine's own mutual-exclusivity rule means a route campaign
    /// can only ever help one contested pair the same quarter. A
    /// legible, cheap heuristic - not a modeled cost/benefit optimization
    /// weighing route-cost-per-pair against network-cost-per-route - kept
    /// deliberately simple like every other "single best candidate, no
    /// deeper search" method in this file.
    ///
    /// M58 resume: also excludes a route whose own sustained-unprofitable
    /// streak has already passed MaximumConsecutiveUnprofitableQuartersForMarketing
    /// - see that constant's own doc comment. A route with no history yet
    /// (RouteHistoryByPair null, or no entry for this pair) is treated as
    /// a fresh 0-quarter streak, same permissive-default convention as
    /// everything else here - never excluded on that basis alone.
    /// </summary>
    public static void ApplyMarketingCampaign(StrategyContext context, List<QuarterOrder> orders, ref int spent)
    {
        if (context.RivalCountByServedRoute is null || spent + 1 > context.ActionCap)
        {
            return;
        }

        var routeLoads = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel);

        var unprofitableContestedRoutes = routeLoads
            .Select(load => (load.Route, Profit: FareCalculator.EstimatedProfit(load.Route, BestAvailableCarried(load.Route, load.PassengersCarried, context))))
            .Where(x => x.Profit < Money.Zero)
            .Where(x => context.RivalCountByServedRoute.GetValueOrDefault(QuarterEngine.NormalizedPairKey(x.Route.Origin, x.Route.Destination)) > 0)
            .Where(x => (context.RouteHistoryByPair?.GetValueOrDefault(QuarterEngine.NormalizedPairKey(x.Route.Origin, x.Route.Destination))?.ConsecutiveUnprofitableQuarters ?? 0)
                < MaximumConsecutiveUnprofitableQuartersForMarketing)
            .OrderBy(x => x.Profit.Cents)
            .ThenBy(x => x.Route.Origin.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Route.Destination.Id, StringComparer.Ordinal)
            .Select(x => x.Route)
            .ToList();

        if (unprofitableContestedRoutes.Count == 1)
        {
            var target = unprofitableContestedRoutes[0];
            var demand = context.DemandModel.ForPair(target.Origin, target.Destination).PassengersPerQuarter;
            var cost = MarketingCostCalculator.RouteCampaignCost(demand);

            if (context.AvailableCash is null || FinancialState.CanAfford(context.AvailableCash.Value, cost))
            {
                orders.Add(new RouteMarketingOrder(target.Origin, target.Destination));
                spent += 1;
            }
        }
        else if (unprofitableContestedRoutes.Count >= 2)
        {
            var cost = MarketingCostCalculator.NetworkCampaignCost(context.CurrentRoutes.Count);

            if (context.AvailableCash is null || FinancialState.CanAfford(context.AvailableCash.Value, cost))
            {
                orders.Add(new NetworkMarketingOrder());
                spent += 1;
            }
        }
    }

    /// <summary>
    /// M44: mirrors BaselineAi.FindBestUnservedPair's own shape (plain
    /// demand-ranked, no cross-region/domestic preference) but also
    /// respects network contiguity - used by ApplyManagementToolkit's own
    /// stranded-unit check rather than BaselineAi.FindBestUnservedPair
    /// itself, which stays untouched (BaselineAi.DecideOrders is legacy,
    /// no longer called by either real loop - see its own class doc
    /// comment). A unit that could only reach a non-contiguous pair now
    /// correctly counts as stranded: winning a gateway slot doesn't grant
    /// route rights from a city the airline has no presence near.
    /// </summary>
    private static PairDemand? FindBestUnservedPairFromValidOrigin(StrategyContext context, IReadOnlySet<string> validOriginCityIds, AircraftType aircraft)
    {
        var served = context.CurrentRoutes
            .SelectMany(r => new[] { (r.Origin.Id, r.Destination.Id), (r.Destination.Id, r.Origin.Id) })
            .ToHashSet();

        return context.DemandModel.AllPairs(context.AllCities)
            .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
            .Where(p => p.DistanceKm <= aircraft.RangeKm)
            .Where(p => validOriginCityIds.Contains(p.Origin.Id) || validOriginCityIds.Contains(p.Destination.Id))
            .OrderByDescending(p => p.PassengersPerQuarter)
            .ThenBy(p => p.Origin.Id, StringComparer.Ordinal)
            .ThenBy(p => p.Destination.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// M50 (proposed, tunable) — hire only when cash comfortably clears
    /// this multiple of FinancialResolver.ExecutiveHiringCost, mirroring
    /// BaselineAi.AircraftOrderCashComfortMultiplier's own precedent
    /// exactly: never gets its own HireExecutiveOrder rejected, and keeps
    /// margin for the new recurring ExecutiveSalaryPerQuarter this hire
    /// commits to, not just the one-time cost.
    /// </summary>
    internal const int ExecutiveHireCashComfortMultiplier = 3;

    /// <summary>
    /// NetworkOptimizerStrategy follow-up (proposed, tunable) - a real
    /// per-quarter/per-line-item diagnostic found a plateaued, no-longer-
    /// growing network running a small but *structurally flat* net loss
    /// (-$3.0M to -$3.7M/quarter, barely moving) for 30+ consecutive
    /// quarters before finally exhausting cash - Revenue and OperatingCost
    /// nearly perfectly balanced, the entire deficit just SalaryCost with
    /// nothing ever removing the dead weight. A persistently-unprofitable
    /// multi-aircraft route already shrinks to exactly 1 aircraft via the
    /// existing logic below, then becomes permanently un-fixable under
    /// the pre-this-fix toolkit (only ever a fare cut, forever). 8
    /// quarters (2 years) is deliberately more patient than M55's own
    /// 6-quarter attempt, which "improved but didn't fix" a *different*
    /// failure mode (a fast-growing, still-thrashing network where
    /// closures chased opens) - this strategy's own plateaued state has
    /// no more opens happening to thrash against by the time this would
    /// ever fire, and DomesticProfitOptimizerStrategy.FindMostProfitableDomesticPairAcrossTypes's
    /// own RecentlyClosedPairKeys cooldown (already wired in, reused
    /// unmodified by this same strategy's own new-pair search) guards
    /// against an immediate re-open regardless.
    /// </summary>
    private const int SustainedSingleAircraftClosureThreshold = 8;

    /// <summary>
    /// (Proposed, tunable) — bankruptcy-conditions investigation: a real
    /// 300-game diagnostic found 100% of 545 bankruptcy events were
    /// triggered by cash going negative, never the $80M debt ceiling
    /// (FinancialResolver.DebtCeilingDollars) - 82% of bankruptcies carried
    /// zero debt at all. Almost always a slow, multi-quarter bleed
    /// crossing zero by a modest margin (avg -$5.12M, 84.8% within -$10M),
    /// not a cliff - plenty of quarters of visible warning before the
    /// actual crossing, and a large, essentially unused gap between where
    /// airlines actually die and the real borrowing capacity available to
    /// them. The only existing RefinanceOrder use anywhere (NegotiateOrBorrow,
    /// below) is narrowly scoped to a negotiation-bid shortfall - no
    /// strategy ever borrowed as a general "running low, buy runway" move
    /// before this. Below this many quarters of runway (see
    /// MaintainCashRunway's own quarterlyBurn - the fleet's own
    /// SalaryCostPerAircraftPerQuarter, the dominant fixed cost per the
    /// M58-follow-up investigation's own finding that revenue and
    /// operating cost nearly cancel out), a rescue borrow fires.
    /// </summary>
    private const int MinimumCashRunwayQuarters = 2;

    /// <summary>
    /// (Proposed, tunable) — the runway level a rescue borrow restores to,
    /// deliberately higher than MinimumCashRunwayQuarters (hysteresis) -
    /// stops the AI borrowing a token amount every single quarter once
    /// hovering right at the warning line.
    /// </summary>
    private const int TargetCashRunwayQuarters = 4;

    /// <summary>
    /// Borrows real cash (RefinanceOrder - free, Private, 0 action-cost
    /// per QuarterEngine's own RefinanceOrder =&gt; 0 mapping, per
    /// CLAUDE.md's "these cost no action" rule) whenever this airline's
    /// own current cash falls below MinimumCashRunwayQuarters worth of its
    /// current fleet's own quarterly burn - enough to restore
    /// TargetCashRunwayQuarters, capped by real headroom under
    /// FinancialResolver.DebtCeilingDollars (never exceeds it - the exact
    /// same never-exceed-the-ceiling idiom NegotiateOrBorrow already
    /// established, reused here rather than re-derived). A no-op (same
    /// permissive-default convention as every other nullable StrategyContext
    /// field) when AvailableCash or CurrentDebtBalance is null, or when the
    /// fleet is empty (zero burn rate - nothing to maintain runway
    /// against). Deliberately not a cure for bad route economics - a
    /// genuinely structurally-unprofitable airline (the chronic-thin-
    /// margin pattern the same diagnostic also found, e.g. in
    /// SouthAmerica) may still exhaust its full borrowing capacity and go
    /// bankrupt via the debt ceiling instead of cash&lt;0 - a real,
    /// honest possibility this safety net doesn't paper over, only the
    /// fare-cut/shrink/closure levers already shipped this session
    /// actually fix that.
    /// </summary>
    public static void MaintainCashRunway(StrategyContext context, List<QuarterOrder> orders)
    {
        if (context.AvailableCash is null || context.CurrentDebtBalance is null)
        {
            return;
        }

        var quarterlyBurn = FinancialResolver.SalaryCostPerAircraftPerQuarter * context.OwnFleet.Units.Count;
        if (quarterlyBurn <= Money.Zero)
        {
            return;
        }

        var warningLine = quarterlyBurn * MinimumCashRunwayQuarters;
        if (FinancialState.CanAfford(context.AvailableCash.Value, warningLine))
        {
            return;
        }

        var targetCash = quarterlyBurn * TargetCashRunwayQuarters;
        var shortfall = targetCash - context.AvailableCash.Value;
        var ceilingHeadroom = Money.FromDollars(FinancialResolver.DebtCeilingDollars) - context.CurrentDebtBalance.Value;
        var borrowAmount = shortfall < ceilingHeadroom ? shortfall : ceilingHeadroom;

        if (borrowAmount > Money.Zero)
        {
            orders.Add(new RefinanceOrder(borrowAmount));
        }
    }

    /// <summary>
    /// Applies BaselineAi's M31/M35/M36/M37 passive toolkit against
    /// whatever this strategy already holds, unconditionally and
    /// unfiltered by region — a route/unit is a route/unit regardless of
    /// how it got there. Mutates `orders` in place, matching
    /// BaselineAi.DecideOrders's own style.
    /// </summary>
    /// <param name="spentBeforeToolkit">
    /// M50: how many of context.ActionCap this strategy's own
    /// growth/negotiation steps already committed this quarter, before
    /// this toolkit's own free (Private, per CLAUDE.md) actions run. Used
    /// only to gate hiring — see the hire check below.
    /// </param>
    /// <param name="enableSustainedSingleAircraftClosure">
    /// NetworkOptimizerStrategy follow-up (default false, same
    /// "ablation lever, off is exactly old behavior" guarantee
    /// GravityDemandModel's own enableHubDemandBonus already established)
    /// - see SustainedSingleAircraftClosureThreshold's own doc comment
    /// for the real evidence this exists for. Only NetworkOptimizerStrategy
    /// passes true; RapidExpansionStrategy and DomesticProfitOptimizerStrategy
    /// are unaffected, unchanged, byte-identical to before this parameter
    /// existed.
    /// </param>
    public static void ApplyManagementToolkit(StrategyContext context, List<QuarterOrder> orders, HashSet<int> reservedUnitIds, int spentBeforeToolkit, bool enableSustainedSingleAircraftClosure = false)
    {
        // Solvency first, before any other spending decision - see
        // MaintainCashRunway's own doc comment for the real evidence.
        MaintainCashRunway(context, orders);

        var routeLoads = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel);

        foreach (var saturated in routeLoads
            .Where(load => load.LoadFactor >= BaselineAi.SaturatedLoadFactorCeiling)
            .Select(load => load.Route))
        {
            var target = BaselineAi.RaisedFare(saturated);

            if (saturated.FarePerPassenger < target)
            {
                orders.Add(new SetFareOrder(saturated.Origin, saturated.Destination, target));
            }
        }

        // M54: real profitability drives this now, not load factor -
        // replaces M35's own shrink-only loop and M51/M53's own cut loop
        // with one consolidated per-route decision. See
        // docs/BASELINE_AI_ROADMAP.md's M54 for the full reasoning.
        // FareCalculator.EstimatedProfit uses the best available carried
        // figure (real, from LastQuarterSeatsCarriedByRoute, when a
        // contested route has one - see BestAvailableCarried - otherwise
        // RouteEvaluator's own solo estimate, already accurate for an
        // uncontested route). Free action (Private, per CLAUDE.md), so
        // every qualifying route gets a response each quarter.
        foreach (var load in routeLoads)
        {
            var carried = BestAvailableCarried(load.Route, load.PassengersCarried, context);
            var profit = FareCalculator.EstimatedProfit(load.Route, carried);

            if (profit >= Money.Zero)
            {
                continue;
            }

            if (enableSustainedSingleAircraftClosure && load.Route.AircraftCount == 1)
            {
                var pairKey = QuarterEngine.NormalizedPairKey(load.Route.Origin, load.Route.Destination);
                var rawSustainedQuarters = context.RouteHistoryByPair?.GetValueOrDefault(pairKey)?.ConsecutiveUnprofitableQuarters ?? 0;

                // Zero credit - not capped/partial credit - whenever the
                // raw streak is older than MidGame itself. A real 100-game
                // measurement found the raw, uncapped streak (which keeps
                // accumulating under Starting-era strategies that never
                // once check it) averaging 34 quarters the very first time
                // this branch ever runs, at quarter GameEras.MidGameStartQuarter
                // exactly, for every airline simultaneously - a
                // synchronized "backlog dump" collapsing the real win rate
                // 7.3%→4.0%. A first fix attempt (Math.Min-style capping to
                // quarters-since-MidGame) measured *no better* - every
                // route with a long enough backlog still hits the exact
                // same capped value on the exact same calendar quarter
                // (quartersSinceMidGameStart, identical for every route in
                // the world that quarter), just delaying the identical
                // synchronized dump from quarter 41 to quarter 48
                // (MidGameStartQuarter + SustainedSingleAircraftClosureThreshold
                // - 1) instead of removing it. The real fix: a route whose
                // raw streak *exceeds* how long MidGame has existed
                // necessarily includes at least one pre-MidGame quarter -
                // it gets zero credit, full stop, until it actually
                // improves (streak resets to 0 on a real profitable
                // quarter) and goes bad again for real, genuinely fresh,
                // entirely within the MidGame/LateGame window - which
                // happens at a different real quarter for each
                // independent route, the real desynchronization a shared
                // global cap can never provide.
                var quartersSinceMidGameStart = Math.Max(0, context.QuarterNumber - GameEras.MidGameStartQuarter + 1);
                var sustainedQuarters = rawSustainedQuarters > quartersSinceMidGameStart ? 0 : rawSustainedQuarters;
                var isUncontested = (context.RivalCountByServedRoute?.GetValueOrDefault(pairKey) ?? 0) == 0;

                if (sustainedQuarters >= SustainedSingleAircraftClosureThreshold && isUncontested)
                {
                    // Real, permanent closure - see SustainedSingleAircraftClosureThreshold's
                    // own doc comment for why this route, specifically, is
                    // never coming back regardless of how many more fare
                    // cuts it gets. A close instead of (not alongside) a
                    // cut - no point discounting a fare on a route about
                    // to disappear this same quarter.
                    orders.Add(new CloseRouteOrder(load.Route.Origin, load.Route.Destination));
                    continue;
                }
            }

            // Cut the fare - universal, works whether or not the route
            // is contested, same target BaselineAi.CutFare has always
            // used. Can coexist with a same-quarter shrink below, or with
            // ApplyMarketingCampaign's own RouteMarketingOrder on the same
            // route (complementary levers, not exclusive).
            var cutTarget = BaselineAi.CutFare(load.Route);

            if (load.Route.FarePerPassenger > cutTarget)
            {
                orders.Add(new SetFareOrder(load.Route.Origin, load.Route.Destination, cutTarget));
            }

            if (load.Route.AircraftCount > 1)
            {
                // Right-size capacity toward real demand - same shrink
                // mechanism as M35, now triggered by real unprofitability.
                orders.Add(new CloseRouteOrder(load.Route.Origin, load.Route.Destination));
            }

            // A single-aircraft route just gets the cut above, unless the
            // closure branch near the top of this loop already handled it
            // and continue'd past here. M54 tried full closure gated on a
            // same-quarter profit read alone and measured it firing on
            // nearly as many routes as were ever opened (19,679 closures
            // against 25,722 opens) - pulled entirely. M55 tried again
            // with the real fix that finding called for - requiring
            // ConsecutiveUnprofitableQuarters sustained for real, not just
            // a same-quarter snapshot - plus the same uncontested-only and
            // contiguity-bridge safety checks M54 already reasoned
            // through. It didn't help the way hypothesized: at a
            // 2-quarter confirmation window, closures still ran 12,997
            // against 19,044 opens (68%) and win rate fell to 12.0% (from
            // 41.3% with closure disabled); a much more patient 6-quarter
            // window improved but didn't fix it (58% closure ratio, 24.7%
            // win rate, still well below 41.3%). The real signal at the
            // time: this AI's own route-opening heuristics sent a large,
            // persistent fraction of new routes into genuine, sustained
            // unprofitability in a still-growing, still-thrashing network
            // - punishing that with closure cost more than it saved.
            // NetworkOptimizerStrategy's own follow-up round found real,
            // different territory once a network stops growing at all -
            // see enableSustainedSingleAircraftClosure/
            // SustainedSingleAircraftClosureThreshold above for the
            // evidence and the narrow, patient closure path this class
            // now offers for exactly that plateaued case. See
            // docs/BASELINE_AI_ROADMAP.md's M55 and NetworkOptimizerStrategy
            // follow-up sections for the full numbers both ways.
        }

        var validOriginCityIds = ValidOriginCityIds(context);

        foreach (var stranded in context.OwnFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Where(u => context.CurrentRoutes.All(r => r.Aircraft.Id != u.Type.Id))
            .Where(u => (FindBestUnservedPairFromValidOrigin(context, validOriginCityIds, u.Type)?.PassengersPerQuarter ?? 0) <= 0)
            .OrderBy(u => u.Id))
        {
            orders.Add(new RetireAircraftOrder(stranded.Id));
        }

        if (context.AvailableCash is not null && context.CurrentDebtBalance is not null
            && context.CurrentDebtBalance.Value > Money.Zero && context.AvailableCash.Value > BaselineAi.PayDownCashBuffer)
        {
            var affordablePaydown = context.AvailableCash.Value - BaselineAi.PayDownCashBuffer;
            var payDownAmount = affordablePaydown < context.CurrentDebtBalance.Value ? affordablePaydown : context.CurrentDebtBalance.Value;

            if (payDownAmount > Money.Zero)
            {
                orders.Add(new PayDownDebtOrder(payDownAmount));
            }
        }

        // M50: hiring used to be unconditional (M31), correct while it
        // was free and uncapped. M49 made it cost real money — $600,000
        // up front plus $300,000/quarter forever — so hiring regardless
        // of need is now a straight, compounding drain (see
        // docs/OPEN_WORK.md's M49 finding: win rate 40.7%→9.7%). Three
        // checks, all must hold, each independently legible:
        var executiveCount = Math.Max(0, (context.ActionCap - QuarterEngine.BaseActionCap) / QuarterEngine.ActionCapPerExecutive);
        var underHeadcountCap = executiveCount < QuarterEngine.MaxExecutives;

        // The cap was actually the bottleneck this quarter - a real,
        // reactive-to-current-state proxy for "an extra action would
        // have helped," not a forecast of future quarters (matching this
        // toolkit's own close/raise/cut style everywhere else).
        var actionCapWasBinding = spentBeforeToolkit >= context.ActionCap;

        var canAffordComfortably = context.AvailableCash is null
            || FinancialState.CanAfford(context.AvailableCash.Value, FinancialResolver.ExecutiveHiringCost * ExecutiveHireCashComfortMultiplier);

        if (underHeadcountCap && actionCapWasBinding && canAffordComfortably)
        {
            orders.Add(new HireExecutiveOrder());
        }
    }
}
