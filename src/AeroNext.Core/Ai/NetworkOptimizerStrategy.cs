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
/// Gateway-negotiation logic is reused directly from DomesticProfitOptimizerStrategy
/// (FindBestUnservedDomesticGateway/FindBestIdleContestableDomesticGateway
/// — flipped internal for exactly this, zero behavior change to that
/// class) rather than re-derived. New-pair logic (step 3) originally
/// reused that same class's own domestic search too — see the
/// "NetworkOptimizerStrategy follow-up" paragraph below for why that
/// stopped being true. Like DomesticProfitOptimizerStrategy, never
/// proposes a cross-region RouteOrder or NegotiationOrder — "optimize what's
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
///
/// M58 follow-up: even with selection fixed, NorthAmerica's own
/// bankruptcy rate barely moved (85.0%→83.9%) - traced to this class's
/// own step 3 (new pair), not selection. NorthAmerica's uniquely dense
/// 22-city pool (M44) means step 3 - deliberately uncapped, see the
/// class doc comment above - always has another candidate, so it never
/// goes rare there the way the design assumed: a real 100-game
/// diagnostic found NorthAmerica's own network more than doubling
/// (routes 5.3→12.0, fleet 7.1→15.0) between years 18-26 while every
/// other region stayed flat, cash draining $372.9M→$106.3M funding it,
/// right into the 1979 oil shock (year ~24.75). A second diagnostic
/// (real cost lines from QuarterlyStatement) found this is two-stage:
/// purchases front-load into years 20-23 then hit $0 (capacity
/// exhausted), but growth doesn't stop - leased-unit count and lease
/// spend both jump right as purchases stop, because the new-pair lease
/// fallback had no cash gate at all, only an action-budget one.
///
/// First fix attempt: a stricter purchase-comfort multiplier
/// (NewPairCashComfortMultiplier, below) plus a *separate*, lease-fee-based
/// comfort gate on the lease fallback. Measured, and found to make
/// things *worse* - NorthAmerica's fleet ended up bigger (15.1 vs. 14.9
/// by year 26) and cash lower ($93.1M vs. $106.3M) than before this fix
/// existed at all. Root cause: a lease-fee-based bar (6 quarters of a
/// 4%/quarter fee) is only 0.24x the aircraft's own price - 25x weaker
/// than the purchase bar - so raising the purchase bar alone just made
/// leasing *relatively* easier to reach, and leasing is the worse
/// long-run deal (permanent recurring cost vs. one-time purchase), so
/// more growth funneled into the worse channel instead of slowing down.
/// A same-price-basis bar for both was tried next, but that makes the
/// lease branch's own condition a strict subset of the purchase
/// condition it's checked after - provably unreachable, not just
/// stricter - so the lease fallback for step 3 specifically is removed
/// outright instead of left as dead code behind an always-false gate:
/// opening a brand-new, unproven pair now only ever happens with real,
/// committed capital (NewPairComfortableOrderCost), never CLAUDE.md's
/// own "expensive instant-metal escape hatch." Step 2 (grow an existing,
/// already-flying route, including its own lease fallback) is
/// deliberately untouched throughout - a lower-risk action reinforcing
/// proven demand, not an open bet on a new one.
///
/// NetworkOptimizerStrategy follow-up, round 2: the cash-discipline fix
/// above genuinely stopped NorthAmerica's growth-driven collapse, but
/// only moved its bankruptcy rate 83.9%→80.7% - the wave shifted later
/// (years 33-35) rather than disappearing. Real per-quarter,
/// per-line-item instrumentation found a completely different mechanism
/// this time: once a network plateaus, Revenue and OperatingCost stay
/// almost perfectly balanced, but SalaryCost tips it into a small,
/// *structurally flat* net loss (-$3.0M to -$3.7M/quarter, barely
/// moving) sustained for 30+ consecutive quarters before cash runs out -
/// the flatness itself is diagnostic of the same routes staying bad
/// forever, not routine churn. Root cause, confirmed directly in
/// AiToolkit.ApplyManagementToolkit's own prior code: a persistently-
/// unprofitable multi-aircraft route already shrinks to exactly 1
/// aircraft via existing logic, at which point - before this round - it
/// could only ever get a fare cut, permanently, no matter how long it
/// stayed bad. Fixed via AiToolkit's own new, opt-in
/// enableSustainedSingleAircraftClosure parameter (this is the only
/// strategy that passes true) - see its own
/// SustainedSingleAircraftClosureThreshold doc comment for why this is
/// real, different territory from M54/M55's own two failed closure
/// attempts (both measured in a fast-growing, thrashing network this
/// strategy's own plateaued state never reaches).
///
/// NetworkOptimizerStrategy follow-up, round 3: step 3 (new pair) now
/// uses AiToolkit.FindMostProfitablePair/EstimatedRealProfit - the same
/// real-cost-aware, lease-fee-and-competition-priced search
/// BalancedGrowthStrategy already uses - domestic-only via an explicit
/// region filter, in place of the round-1/round-2 machinery above
/// (NewPairCashComfortMultiplier, NewPairComfortableOrderCost, the
/// no-lease-fallback rule). That machinery existed only because the old
/// demand/revenue-ranked search had no way to price a fresh unit's real
/// recurring lease cost into the decision at all; EstimatedRealProfit
/// prices the lease fee *inside* the profitability figure itself, making
/// the external multiplier and the outright lease ban redundant rather
/// than complementary. Given this class's own three-regression history
/// above - two of them in this exact machinery - the removal was treated
/// as a live regression risk, not a safe-by-reasoning refactor: verified
/// by a real 300-game sweep (seed 1, 244 quarters) against the current
/// baseline (post-BalancedGrowthStrategy) rather than assumed. Result:
/// NorthAmerica - this class's own historically vulnerable region across
/// all three prior rounds - unchanged at 0.4% bankrupt (the same single
/// bankruptcy, same game); Europe and EastAsia also unchanged at 9.6%
/// and 4.5%; SouthWestAsia improved 1.9%→1.0% and AustralAsia improved
/// 34.0%→29.9%; SouthAmerica unchanged at 0.4%. No region regressed, so
/// the old safety net was not reinstated. The shared search itself moved
/// out to AiToolkit (EstimatedRealProfit/EvaluateCandidate/
/// FindMostProfitablePair/TryProposeCandidate/ProfitabilityFloor, all
/// public there now) rather than being re-derived here - see
/// BalancedGrowthStrategy's own class doc comment for the extraction and
/// for why ranking candidate pairs by this real profit figure is safe
/// across pairs, not just within one.
///
/// Existing-route-evaluation audit follow-up: step 2 (grow the
/// best-performing existing route) now passes
/// enableRealProfitabilityGate: true to BaselineAi.FindBestPerformingRoute
/// — never grows a route that's a real loser at its own current
/// economics, and ranks survivors by their own real (competitively-
/// resolved, when available) load factor rather than the fare/
/// competition-agnostic solo estimate. See that method's own doc comment
/// for the full reasoning.
///
/// Win-condition-awareness follow-up: this class never opens a new
/// cross-region route or gateway at all (its own unbroken invariant,
/// above) - step 2 is the *only* lever it has toward the win condition,
/// so it's the only place this gets applied. Also passes
/// regionsNotYetLed (AiToolkit.RegionsNotYetLed) - a real diagnostic
/// found some regions' own home airlines almost never win despite
/// holding a real, profitable foothold abroad, because nothing ever
/// distinguished "a region I'm present in" from "a region I'm actually
/// leading," so a foreign route opened once was never reinforced past a
/// bare toehold. Tuned to this class's own stabilize-not-expand tenet:
/// it changes *which already-served route* gets reinforced, never opens
/// anything new, never changes whether step 2 fires at all, and never
/// touches Suitability - see StrategyContext.RegionsLedLastQuarter's own
/// doc comment for why that last point specifically matters here (a
/// near-identical signal, wired into Suitability instead, is the exact
/// thing this class's own M58 history already found and reverted once).
///
/// AI-type-swap follow-up (docs/AI_AIRCRAFT_TYPE_SWAP_DESIGN.md): the
/// first strategy given real awareness of AdjustRouteOrder, via
/// AiToolkit.ApplyManagementToolkit's own new enableAircraftTypeSwap
/// parameter - the chosen strategy specifically because it spends the
/// majority of a 60-year campaign managing an already-built network
/// rather than growing one, exactly the setting where "a newer,
/// better-fitting aircraft type became available since this route
/// opened" is a real, recurring situation rather than a one-off. Only
/// this strategy passes true this round; RapidExpansionStrategy and
/// DomesticProfitOptimizerStrategy keep the parameter at its default
/// false, byte-identical to before this existed.
/// </summary>
public sealed class NetworkOptimizerStrategy : IAiStrategy
{
    public string Name => "Network Optimizer";

    public IReadOnlySet<GameEra> ApplicableEras { get; } = new HashSet<GameEra> { GameEra.MidGame, GameEra.LateGame };

    /// <summary>
    /// BalancedGrowthStrategy follow-up: the actual troubled-network
    /// computation (Math.Max of unprofitableRatio/contestedRatio, both
    /// gated by a minimum troubled-route count and a minimum sustained-quarters
    /// requirement) moved to AiToolkit.NetworkTroubleRatio - a pure
    /// extraction, same formula, same constants, verified byte-identical
    /// against this class's own existing Suitability tests. It moved
    /// because the "never actually compared against a rival in practice"
    /// caveat this doc comment used to carry stopped being true the
    /// moment BalancedGrowthStrategy became MidGame/LateGame's second
    /// eligible strategy: rather than trust two independently-tuned
    /// scores to compare correctly (the exact M58 resume round 3 mistake
    /// - see the class doc comment above), BalancedGrowthStrategy's own
    /// Suitability is defined as the direct complement of this same
    /// shared value (nonzero here means real trouble, so it wins; exactly
    /// zero means BalancedGrowthStrategy wins instead) - a guaranteed
    /// non-overlapping partition, not a hoped-for one.
    /// </summary>
    public double Suitability(StrategyContext context) => AiToolkit.NetworkTroubleRatio(context);

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
            excludePairKeys: AiToolkit.RecentlyShrunkPairKeys(context, AiToolkit.GrowthCooldownQuarters),
            lastQuarterSeatsCarriedByRoute: context.LastQuarterSeatsCarriedByRoute,
            enableRealProfitabilityGate: true,
            regionsNotYetLed: AiToolkit.RegionsNotYetLed(context));
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
            else if (context.AvailableCash is not null && FinancialState.CanAfford(context.AvailableCash.Value, BaselineAi.ComfortableOrderCost(bestExisting.Aircraft))
                && (context.PurchasableTypes is null || context.PurchasableTypes.Contains(bestExisting.Aircraft)))
            {
                orders.Add(new AircraftOrder(bestExisting.Aircraft, Quantity: 1));
                spent += 1;
            }
            else if (context.LeasableTypes is null || context.LeasableTypes.Contains(bestExisting.Aircraft))
            {
                // Leasing resolves next quarter now (two-phase,
                // docs/AIRCRAFT_ACQUISITION_ROADMAP.md) - submit it alone,
                // 1 action, no paired RouteOrder. Once delivered it shows
                // up idle and a later quarter's own re-evaluation picks it
                // up via this same step's idleUnit branch above.
                orders.Add(new LeaseAircraftOrder(bestExisting.Aircraft, Quantity: 1));
                spent += 1;
            }
            // else: this route's own type is no longer legally
            // purchasable or leasable for this home city right now
            // (geopolitical-aircraft-restrictions follow-up,
            // docs/AI_ACQUISITION_AWARENESS_GAPS_ROADMAP.md) - do nothing
            // this quarter rather than submit an order QuarterEngine would
            // reject anyway. The route keeps flying regardless; it just
            // can't grow until something goes idle or the restriction
            // lifts.
        }

        // 3. A new pair - last priority, only if budget remains.
        // NetworkOptimizerStrategy follow-up: reuses AiToolkit.FindMostProfitablePair/
        // TryProposeCandidate directly (see their own doc comments and
        // this class's own doc comment for the real evidence chain) -
        // domestic-only, this class's own unbroken invariant, preserved
        // here via the region filter rather than by reusing
        // DomesticProfitOptimizerStrategy's own domestic-scoped methods.
        // No MaxDomesticRoutes-style cap, since this strategy is only
        // ever selected once the network already has real, plenty-sized
        // substance (see Suitability above).
        if (spent + 1 <= context.ActionCap)
        {
            var best = AiToolkit.FindMostProfitablePair(context, reservedUnitIds, validOriginCityIds,
                p => p.Origin.Region == context.HomeRegion && p.Destination.Region == context.HomeRegion);
            AiToolkit.TryProposeCandidate(context, best, validOriginCityIds, orders, reservedUnitIds, slotsReservedByCity, citySpendReservedByCity, ref spent);
        }

        // 4. Negotiate a domestic gateway - never cross-region, same
        // contract as DomesticProfitOptimizerStrategy.
        var bestDomesticGateway = DomesticProfitOptimizerStrategy.FindBestUnservedDomesticGateway(context) ?? DomesticProfitOptimizerStrategy.FindBestIdleContestableDomesticGateway(context);
        AiToolkit.NegotiateOrBorrow(context, bestDomesticGateway, orders, ref spent);

        // 5. Shared toolkit - free, always last, same as every strategy.
        // Follow-up: this is the one strategy passing
        // enableSustainedSingleAircraftClosure: true - see AiToolkit's own
        // SustainedSingleAircraftClosureThreshold doc comment for why, and
        // this class's own doc comment for the real evidence chain.
        AiToolkit.ApplyManagementToolkit(context, orders, reservedUnitIds, spent, enableSustainedSingleAircraftClosure: true, enableReactiveTypeSwap: true, enableProactiveTypeSwap: true);

        return orders;
    }
}
