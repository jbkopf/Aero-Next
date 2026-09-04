using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// M58: consolidate and optimize an already-mature network rather than
/// keep expanding — the counterpart DomesticProfitOptimizerStrategy was
/// never quite built to be (that one still opens a new pair *before*
/// touching what it already holds, every single quarter it's under its
/// own MaxDomesticRoutes cap). Never a Starting-era strategy (the user's
/// own explicit requirement — there's nothing to optimize yet at quarter
/// 1); only eligible once a network exists to have opinions about.
///
/// Priorities are deliberately **inverted** from both existing
/// strategies: ApplyMarketingCampaign (fixing contested, unprofitable
/// routes) gets first claim on the action budget, then growing the
/// best-performing existing route, and only *then* — if any budget
/// remains — a new pair. No artificial route cap enforces "sparingly";
/// the ordering alone does, through the same 3-action-budget competition
/// every other strategy already uses: by the time this strategy is even
/// selected (see Suitability below), there's usually real work for
/// steps 1-2 to do, which is exactly what makes step 3 rare in practice
/// without needing a separate throttle.
///
/// New-pair and gateway-negotiation logic is reused directly from
/// DomesticProfitOptimizerStrategy (FindMostProfitableDomesticPairUsingIdleFleet/
/// AcrossTypes, FindBestUnservedDomesticGateway/FindBestIdleContestableDomesticGateway
/// — all flipped internal for exactly this, zero behavior change to that
/// class) rather than re-derived: it's the same domestic-only,
/// profit-aware (M40/M57) search this strategy wants too, already
/// proven and already measured. Like that strategy, never proposes a
/// cross-region RouteOrder or NegotiationOrder — "optimize what's
/// already there," not expand into new territory.
///
/// M58 resume: the intent, restated directly by the user - this strategy
/// exists to *prevent bankruptcy*, stepping back from growth to shore up
/// or trim unprofitable routes. It originally also had a third trigger,
/// "fairly close to achieving the win condition" - measured, and removed
/// entirely: even after one real fix (requiring >= 2 regions deeply led,
/// not just one led at all), it still fired 9-30x more often than the
/// unprofitable-route signal (2,779 vs. 318 evaluations for NorthAmerica,
/// 9,037 vs. 87 for EastAsia), pulling healthy airlines out of active
/// growth as early as year 11 and driving NorthAmerica's bankruptcy rate
/// from 20.8% to 65.0% in a real 300-game sweep - the opposite of this
/// strategy's own purpose.
///
/// M58 resume (round 3): the deeper fix landed one level up, not in this
/// file. A real 60-game diagnostic found DomesticProfitOptimizerStrategy's
/// own Suitability pinned at exactly 0.0 for 90.3% of MidGame/LateGame
/// comparisons (its cash signal was calibrated for quarter-1
/// differentiation and never scales past it), so this strategy was
/// winning the comparison by default on almost any nonzero signal -
/// average cash at selection was $596.1M, richer than when
/// DomesticProfitOptimizerStrategy itself was chosen. The bankruptcy
/// number didn't move even after the win-proximity removal and the
/// ApplyMarketingCampaign exclusion below, both confirmed working
/// correctly, because neither touched the actual defect: the comparison
/// itself. Fixed by making the eras exclusive instead -
/// DomesticProfitOptimizerStrategy.ApplicableEras narrowed to { Starting
/// } only - so this strategy is now the sole eligible one for MidGame and
/// LateGame, chosen unconditionally at the year-11 boundary. See
/// docs/BASELINE_AI_ROADMAP.md's M58 section for the full evidence chain.
/// </summary>
public sealed class NetworkOptimizerStrategy : IAiStrategy
{
    public string Name => "Network Optimizer";

    public IReadOnlySet<GameEra> ApplicableEras { get; } = new HashSet<GameEra> { GameEra.MidGame, GameEra.LateGame };

    /// <summary>
    /// (Proposed, tunable) — a real 300-game sweep measured the first
    /// version of this Suitability (pure ratios, no floor) picking this
    /// strategy at an average of just 3.7 routes - a single unprofitable
    /// or contested route out of 2-3 total was enough to hit ratio 1.0,
    /// crowding out real early growth (win rate 41.3%→29.7%, NorthAmerica
    /// bankruptcy 24.1%→54.0%). The user's own wording is "multiple
    /// unprofitable routes" / "many contested routes" - plural, not a
    /// bare ratio - so an absolute-count floor is the direct fix, not a
    /// guess: below this many, the signal contributes nothing at all,
    /// however high the ratio.
    ///
    /// M58 resume: lowered 3 → 1, together with the new
    /// MinimumSustainedUnprofitableQuarters requirement below - the
    /// original floor of 3 was sized against a *same-quarter* snapshot
    /// (any one bad quarter counted), but the real damage this strategy
    /// exists to catch (NorthAmerica's own permanent ~12-14%
    /// unprofitable-route share after the 1973 oil-shock event) is
    /// roughly one persistently-bad route out of 5-8 - it never crossed a
    /// same-quarter floor of 3 at all, which is the literal mechanism by
    /// which this signal almost never fired for the real problem
    /// (measured: only 318 of ~59,000 NorthAmerica evaluations). Once
    /// "sustained 2+ quarters" is doing the real noise-filtering a
    /// same-quarter count floor was originally built for, a floor this
    /// strict is provably too strict for the real pattern.
    /// </summary>
    private const int MinimumTroubledRouteCountForSignal = 1;

    /// <summary>
    /// M58 resume (proposed, tunable): a route only counts toward
    /// unprofitableRatio once it's read unprofitable for this many
    /// quarters *in a row*, ending with the most recent one - reuses
    /// AiRouteHistory.ConsecutiveUnprofitableQuarters (real, already
    /// tracked every quarter by FullGameRunner.cs, previously unconsumed
    /// by anything). Same principle docs/BASELINE_AI_ROADMAP.md's M55
    /// already established for route *closure* specifically ("a route
    /// needs to remember it was just bad, not just prove itself bad in
    /// this one isolated quarter") - now applied to this strategy's own
    /// selection signal too, closing the gap that let a one-off bad
    /// quarter on a young network read the same as genuine, sustained
    /// trouble under the old same-quarter check.
    /// </summary>
    private const int MinimumSustainedUnprofitableQuarters = 2;

    /// <summary>
    /// Combined via Math.Max, not a weighted sum - either signal alone is
    /// enough to make this strategy the best choice. unprofitableRatio
    /// reuses AiRouteHistory.ConsecutiveUnprofitableQuarters (see
    /// MinimumSustainedUnprofitableQuarters); contestedRatio reuses the
    /// exact same "contested" definition ApplyMarketingCampaign already
    /// acts on, so this strategy gets picked by the same signal it will
    /// then go fix. Both gated by MinimumTroubledRouteCountForSignal so a
    /// tiny, young network with one struggling route doesn't register as
    /// "the network is in trouble" the same way a mature one genuinely
    /// does.
    ///
    /// M58 resume (round 3): with DomesticProfitOptimizerStrategy no
    /// longer eligible for MidGame or LateGame (see the class doc comment
    /// above), this strategy is currently the sole occupant of both
    /// eras - SelectStrategy's own eligible list is always length 1
    /// there, so this exact value is never actually compared against a
    /// rival's in practice today. Kept exactly as measured and tested
    /// anyway, not simplified to a constant: it's correct, it's already
    /// exercised directly by this class's own unit tests (not just
    /// through reachability), and it's the real signal this strategy
    /// should keep using if a future roster edit ever gives MidGame or
    /// LateGame a second eligible strategy again.
    /// </summary>
    public double Suitability(StrategyContext context)
    {
        var routes = context.CurrentRoutes;

        if (routes.Count == 0)
        {
            return 0.0;
        }

        var sustainedUnprofitableCount = routes.Count(r =>
            (context.RouteHistoryByPair?.GetValueOrDefault(QuarterEngine.NormalizedPairKey(r.Origin, r.Destination))?.ConsecutiveUnprofitableQuarters ?? 0)
                >= MinimumSustainedUnprofitableQuarters);
        var unprofitableRatio = sustainedUnprofitableCount < MinimumTroubledRouteCountForSignal ? 0.0 : sustainedUnprofitableCount / (double)routes.Count;

        var contestedCount = routes.Count(r =>
            (context.RivalCountByServedRoute?.GetValueOrDefault(QuarterEngine.NormalizedPairKey(r.Origin, r.Destination)) ?? 0) > 0);
        var contestedRatio = contestedCount < MinimumTroubledRouteCountForSignal ? 0.0 : contestedCount / (double)routes.Count;

        return Math.Max(unprofitableRatio, contestedRatio);
    }

    public IReadOnlyList<QuarterOrder> DecideOrders(StrategyContext context)
    {
        var orders = new List<QuarterOrder>();
        var reservedUnitIds = new HashSet<int>();
        var slotsReservedByCity = new Dictionary<string, int>();
        var citySpendReservedByCity = new Dictionary<string, int>();
        var spent = 0;
        var validOriginCityIds = AiToolkit.ValidOriginCityIds(context);

        // 1. Fix what's already broken - first claim on the budget,
        // unlike either existing strategy (both run this last).
        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        // 2. Grow the best-performing existing route - a real capability
        // DomesticProfitOptimizerStrategy doesn't have at all (its own
        // new-pair search excludes already-served pairs outright).
        // Mirrors RapidExpansionStrategy's own step 2 exactly, same M55
        // cooldown exclusion.
        var bestExisting = BaselineAi.FindBestPerformingRoute(
            context.CurrentRoutes, context.DemandModel,
            excludePairKeys: AiToolkit.RecentlyShrunkPairKeys(context, AiToolkit.GrowthCooldownQuarters));
        if (bestExisting is not null && spent + 1 <= context.ActionCap
            && BaselineAi.HasSlotAtEveryGateway(bestExisting.Origin, bestExisting.Destination, context.AvailableSlotsByCity, slotsReservedByCity)
            && AiToolkit.HasRoomAtEveryCity(bestExisting.Origin, bestExisting.Destination, context.CityWideSlotsRemaining, citySpendReservedByCity))
        {
            // reservedUnitIds is provably still empty here - step 1
            // (marketing) never touches fleet units, and this is the
            // first fleet-touching step in this strategy's own order -
            // so !reservedUnitIds.Contains(u.Id) is always true in
            // every real call. Kept anyway (a coverage gap, not a bug)
            // for parity with RapidExpansionStrategy's own identically-
            // shaped step, where the same check IS reachable both ways,
            // and as a defensive guard against a future edit that adds
            // an earlier fleet-touching step.
            var idleUnit = context.OwnFleet.Idle
                .Where(u => u.Type.Id == bestExisting.Aircraft.Id && !reservedUnitIds.Contains(u.Id))
                .OrderBy(u => u.Id)
                .FirstOrDefault();

            if (idleUnit is not null)
            {
                orders.Add(new RouteOrder(bestExisting.Origin, bestExisting.Destination, bestExisting.Aircraft));
                reservedUnitIds.Add(idleUnit.Id);
                BaselineAi.ReserveSlots(bestExisting.Origin, bestExisting.Destination, slotsReservedByCity);
                AiToolkit.ReserveCityWideSlots(bestExisting.Origin, bestExisting.Destination, citySpendReservedByCity);
                spent += 1;
            }
            else if (context.AvailableCash is not null && FinancialState.CanAfford(context.AvailableCash.Value, BaselineAi.ComfortableOrderCost(bestExisting.Aircraft)))
            {
                orders.Add(new AircraftOrder(bestExisting.Aircraft, Quantity: 1));
                spent += 1;
            }
            else if (spent + 2 <= context.ActionCap)
            {
                orders.Add(new LeaseAircraftOrder(bestExisting.Aircraft, Quantity: 1));
                orders.Add(new RouteOrder(bestExisting.Origin, bestExisting.Destination, bestExisting.Aircraft));
                BaselineAi.ReserveSlots(bestExisting.Origin, bestExisting.Destination, slotsReservedByCity);
                AiToolkit.ReserveCityWideSlots(bestExisting.Origin, bestExisting.Destination, citySpendReservedByCity);
                spent += 2;
            }
        }

        // 3. A new pair - last priority, only if budget remains. Reuses
        // DomesticProfitOptimizerStrategy's own already-proven search
        // unmodified; no MaxDomesticRoutes-style cap, since this
        // strategy is only ever selected once the network already has
        // real, plenty-sized substance (see Suitability above).
        if (spent + 1 <= context.ActionCap)
        {
            var idleBest = DomesticProfitOptimizerStrategy.FindMostProfitableDomesticPairUsingIdleFleet(context, reservedUnitIds, validOriginCityIds);

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
                var bestOrdered = DomesticProfitOptimizerStrategy.FindMostProfitableDomesticPairAcrossTypes(context, context.EraAvailableTypes, validOriginCityIds);
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

        // 4. Negotiate a domestic gateway - never cross-region, same
        // contract as DomesticProfitOptimizerStrategy.
        var bestDomesticGateway = DomesticProfitOptimizerStrategy.FindBestUnservedDomesticGateway(context) ?? DomesticProfitOptimizerStrategy.FindBestIdleContestableDomesticGateway(context);
        AiToolkit.NegotiateOrBorrow(context, bestDomesticGateway, orders, ref spent);

        // 5. Shared toolkit - free, always last, same as every strategy.
        AiToolkit.ApplyManagementToolkit(context, orders, reservedUnitIds, spent);

        return orders;
    }
}
