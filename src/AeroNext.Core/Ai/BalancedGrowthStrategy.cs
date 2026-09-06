using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// The user's own direct request: NetworkOptimizerStrategy is deliberately
/// a stabilize-what-exists strategy (see its own class doc comment) - it
/// never proposes a cross-region RouteOrder or NegotiationOrder at all,
/// so nothing keeps deliberately growing an airline's reach once MidGame
/// starts. This class is the growth counterpart.
///
/// REDESIGN (real profit gate, replacing raw-demand ranking): the prior
/// EffectiveDemandScore-ranked version, even after a 6x cash bar and
/// dropping the lease fallback, still only partially fixed a real,
/// measured growth-binge regression (SouthWestAsia bankruptcy
/// 2.9%->41.0%, the same shape NetworkOptimizerStrategy's own class doc
/// comment already documents for NorthAmerica). The user's own direct
/// redesign: open a new route in exactly two cases, both gated on real,
/// cost-aware projected profit rather than raw demand or a cash-comfort
/// multiplier.
///
/// **Case 1 (fewer than TargetRegionCount regions touched):** among
/// candidate pairs reaching a region not yet touched, open the one with
/// the best real profit - "choose a region based on potential
/// profitability of routes there" literally falls out of this, since the
/// winning pair's own destination region *is* the region chosen. Does
/// nothing if no such candidate clears the profitability floor - reaching
/// a third region is never forced against the economics.
///
/// **Case 2 (always available):** among candidate pairs *within* regions
/// already touched, open the best real-profit one, same floor.
///
/// Both cases share EstimatedRealProfit below - see its own doc comment
/// for the full cost accounting (operating, salary, lease, competition)
/// and for why ranking *across* pairs by a real profit figure is safe
/// here in a way M56's own four reverted attempts
/// (docs/BASELINE_AI_ROADMAP.md) were not: every candidate is evaluated
/// at its *own* best-fitting aircraft type before pairs are ever compared
/// against each other, reusing the exact single-peaked-in-capacity
/// property DomesticProfitOptimizerStrategy's own M57 fix
/// (EstimatedProfitForAircraft) already established is safe *within* one
/// pair - applying it per-candidate-pair first, then comparing already-optimized
/// pairs, is mechanically different from every one of M56's reverted
/// designs (none of which found each pair's own peak before comparing).
///
/// Growing an *existing* route (adding another aircraft to an
/// already-served pair) is untouched - still BaselineAi.FindBestPerformingRoute's
/// own real, *observed* load-factor ranking, a stronger signal than any
/// estimate this class could compute for a pair no one has flown yet.
/// The user's own two cases were both about *opening* routes.
///
/// Passes enableSustainedSingleAircraftClosure: false (the default) to
/// AiToolkit.ApplyManagementToolkit, like every strategy except
/// NetworkOptimizerStrategy - that mechanic is narrowly scoped to the
/// specific plateaued-network evidence documented on its own
/// SustainedSingleAircraftClosureThreshold, not something this class
/// inherits without its own equally real justification.
///
/// Suitability is the direct complement of AiToolkit.NetworkTroubleRatio
/// - see that method's own doc comment, and NetworkOptimizerStrategy's
/// class doc comment, for why a guaranteed non-overlapping partition
/// (not two independently-tuned scores) is how this codebase already
/// learned to let two MidGame/LateGame strategies coexist safely.
/// </summary>
public sealed class BalancedGrowthStrategy : IAiStrategy
{
    /// <summary>The user's own explicit target - CLAUDE.md's win condition itself requires leading 3 of 6 regions simultaneously; this is the much lower "has a real foothold there at all" precursor, not that dominance bar.</summary>
    private const int TargetRegionCount = 3;

    /// <summary>
    /// (Proposed, tunable) — the user's own "sufficient projected profit
    /// margins" as the plainest possible bar: genuinely profitable, not
    /// merely the best of a bad lot. Not M56's own reverted Money.Zero
    /// floor in the same sense - that one applied to an *arbitrary*
    /// candidate aircraft on every domestic pair every quarter for the
    /// game's main growth strategy; this applies only after
    /// EstimatedRealProfit has already picked each candidate's own
    /// best-fitting aircraft (see the class doc comment), so a candidate
    /// only fails here when *no* available aircraft type can profitably
    /// serve it at all - a genuinely weak candidate, exactly what
    /// "sufficient" should filter. Still a real, live risk given M56's
    /// own history - see the class doc comment's own REDESIGN paragraph
    /// and this round's own sweep verification.
    /// </summary>
    private static readonly Money ProfitabilityFloor = Money.Zero;

    public string Name => "Balanced Growth";

    public IReadOnlySet<GameEra> ApplicableEras { get; } = new HashSet<GameEra> { GameEra.MidGame, GameEra.LateGame };

    public double Suitability(StrategyContext context) => AiToolkit.NetworkTroubleRatio(context) > 0.0 ? 0.0 : 1.0;

    public IReadOnlyList<QuarterOrder> DecideOrders(StrategyContext context)
    {
        var orders = new List<QuarterOrder>();
        var reservedUnitIds = new HashSet<int>();
        var slotsReservedByCity = new Dictionary<string, int>();
        var citySpendReservedByCity = new Dictionary<string, int>();
        var spent = 0;
        var validOriginCityIds = AiToolkit.ValidOriginCityIds(context);
        var touchedRegions = AiToolkit.TouchedRegions(context);

        // Reinforce what's already there first - the same "proven demand
        // beats an open bet" priority NetworkOptimizerStrategy's own step
        // 2 and RapidExpansionStrategy's own step 2 already use, real
        // observed load factor rather than any estimate.
        var bestExisting = BaselineAi.FindBestPerformingRoute(
            context.CurrentRoutes, context.DemandModel,
            excludePairKeys: AiToolkit.RecentlyShrunkPairKeys(context, AiToolkit.GrowthCooldownQuarters));
        if (bestExisting is not null && spent + 1 <= context.ActionCap
            && BaselineAi.HasSlotAtEveryGateway(bestExisting.Origin, bestExisting.Destination, context.AvailableSlotsByCity, slotsReservedByCity)
            && AiToolkit.HasRoomAtEveryCity(bestExisting.Origin, bestExisting.Destination, context.CityWideSlotsRemaining, citySpendReservedByCity))
        {
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

        // Case 1: reach a region not yet touched, chosen by real profit.
        if (touchedRegions.Count < TargetRegionCount && spent + 1 <= context.ActionCap)
        {
            var wonSlotPair = AiToolkit.FindBestUnservedPairTouchingWonSlot(context, reservedUnitIds);
            var best = wonSlotPair is not null
                ? EvaluateCandidate(context, reservedUnitIds, wonSlotPair.Value.Pair, wonSlotPair.Value.Aircraft)
                : FindMostProfitablePair(context, reservedUnitIds, validOriginCityIds, p => TouchesUntouchedRegion(p, touchedRegions));

            TryProposeCandidate(context, best, validOriginCityIds, orders, reservedUnitIds, slotsReservedByCity, citySpendReservedByCity, ref spent);
        }

        var newRegionGateway = FindBestUnservedGateway(context, c => !touchedRegions.Contains(c.Region)) ?? AiToolkit.FindBestIdleContestableGateway(context);

        // Case 2: within regions already touched, real profit again.
        if (spent + 1 <= context.ActionCap)
        {
            var best = FindMostProfitablePair(context, reservedUnitIds, validOriginCityIds, p => BothEndpointsTouched(p, touchedRegions));
            TryProposeCandidate(context, best, validOriginCityIds, orders, reservedUnitIds, slotsReservedByCity, citySpendReservedByCity, ref spent);
        }

        var withinRegionGateway = FindBestUnservedGateway(context, c => touchedRegions.Contains(c.Region));
        AiToolkit.NegotiateOrBorrow(context, touchedRegions.Count < TargetRegionCount ? newRegionGateway : withinRegionGateway, orders, ref spent);

        // Low priority, same placement as RapidExpansionStrategy - this
        // is a growth-focused strategy, not a fix-what's-broken one
        // (that's NetworkOptimizerStrategy's own job).
        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        AiToolkit.ApplyManagementToolkit(context, orders, reservedUnitIds, spent);

        return orders;
    }

    /// <summary>
    /// The real cost accounting the user asked for, for one specific
    /// (pair, aircraft) combination: FareCalculator.EstimatedProfit
    /// (real operating cost, M12, plus this aircraft's own prorated
    /// salary share, M30, already folded in there) with
    /// includeLeaseFee: requiresLease so a candidate needing a fresh unit
    /// carries FinancialResolver.LeaseFeeRatePerQuarter's own real
    /// recurring cost, and none is added when an idle unit already
    /// covers it.
    ///
    /// Existing competition ("on the route, or in the region," the
    /// user's own wording): StrategyContext.RivalPresenceByPairKey/RivalPresenceByRegion
    /// are the real, previously-nonexistent visibility this needed (see
    /// StrategyContext's own doc comment) - both derate this airline's
    /// own assumed captured demand before the capacity cap is even
    /// applied. Honestly a heuristic, not CompetitiveMarket.Resolve's own
    /// real weighted-share formula (which depends on a rival's own
    /// Quality/capacity this game has no visibility into for a pair this
    /// airline doesn't fly) - an even split among (rival presence + 1)
    /// is the plainest defensible assumption, documented as exactly that,
    /// not offered as a precise model.
    ///
    /// Single-peaked in aircraft capacity for this one fixed pair (see
    /// DomesticProfitOptimizerStrategy.EstimatedProfitForAircraft's own
    /// doc comment for the proof) - callers use this to find each
    /// candidate pair's own best-fitting aircraft *before* ever comparing
    /// different pairs against each other, the real reason this stays
    /// safe where M56's own four reverted attempts were not (see the
    /// class doc comment).
    /// </summary>
    private static Money EstimatedRealProfit(StrategyContext context, PairDemand pair, AircraftType aircraft, bool requiresLease)
    {
        var hypotheticalRoute = new Route(pair.Origin, pair.Destination, aircraft, AircraftCount: 1);
        var pairKey = QuarterEngine.NormalizedPairKey(pair.Origin, pair.Destination);
        var rivalPresence = (context.RivalPresenceByPairKey?.GetValueOrDefault(pairKey) ?? 0)
            + (context.RivalPresenceByRegion?.GetValueOrDefault(pair.Destination.Region) ?? 0);
        var assumedCaptured = pair.PassengersPerQuarter / (rivalPresence + 1);
        var carried = Math.Min(assumedCaptured, hypotheticalRoute.SeatsPerQuarter);

        return FareCalculator.EstimatedProfit(hypotheticalRoute, carried, includeLeaseFee: requiresLease);
    }

    /// <summary>Same real profit/floor treatment as FindMostProfitablePair, for a single already-identified (pair, aircraft) candidate - AiToolkit.FindBestUnservedPairTouchingWonSlot's own priority result specifically, which already picked its own aircraft type from this airline's own idle fleet.</summary>
    private static (PairDemand Pair, AircraftType Aircraft, Money Profit, bool UsesIdleUnit)? EvaluateCandidate(
        StrategyContext context, IReadOnlySet<int> reservedUnitIds, PairDemand pair, AircraftType aircraft)
    {
        var usesIdleUnit = context.OwnFleet.Idle.Any(u => u.Type.Id == aircraft.Id && !reservedUnitIds.Contains(u.Id));
        var profit = EstimatedRealProfit(context, pair, aircraft, requiresLease: !usesIdleUnit);

        return profit >= ProfitabilityFloor ? (pair, aircraft, profit, usesIdleUnit) : null;
    }

    /// <summary>
    /// The real search behind both cases: every unserved, reachable,
    /// region-filtered candidate pair, evaluated at *every* era-available
    /// aircraft type (using an idle unit when one exists - no lease fee -
    /// otherwise assuming a fresh lease, per EstimatedRealProfit), then
    /// the single best (pair, aircraft) combination by real profit,
    /// gated by ProfitabilityFloor. Returns null when nothing clears it -
    /// "does nothing" is a completely valid, expected outcome here, not a
    /// search failure.
    /// </summary>
    private static (PairDemand Pair, AircraftType Aircraft, Money Profit, bool UsesIdleUnit)? FindMostProfitablePair(
        StrategyContext context, IReadOnlySet<int> reservedUnitIds, IReadOnlySet<string> validOriginCityIds, Func<PairDemand, bool> regionFilter)
    {
        var served = context.CurrentRoutes
            .SelectMany(r => new[] { (r.Origin.Id, r.Destination.Id), (r.Destination.Id, r.Origin.Id) })
            .ToHashSet();
        var recentlyClosed = AiToolkit.RecentlyClosedPairKeys(context, AiToolkit.ReopenCooldownQuarters);
        var idleTypeIds = context.OwnFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Select(u => u.Type.Id)
            .ToHashSet(StringComparer.Ordinal);

        var candidates = context.DemandModel.AllPairs(context.AllCities)
            .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
            .Where(p => !recentlyClosed.Contains(QuarterEngine.NormalizedPairKey(p.Origin, p.Destination)))
            .Where(p => validOriginCityIds.Contains(p.Origin.Id) || validOriginCityIds.Contains(p.Destination.Id))
            .Where(regionFilter)
            .SelectMany(p => context.EraAvailableTypes
                .Where(type => p.DistanceKm <= type.RangeKm)
                .Select(type =>
                {
                    var usesIdleUnit = idleTypeIds.Contains(type.Id);
                    var profit = EstimatedRealProfit(context, p, type, requiresLease: !usesIdleUnit);
                    return (Pair: p, Aircraft: type, Profit: profit, UsesIdleUnit: usesIdleUnit);
                }))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .OrderByDescending(c => c.Profit.Cents)
            .ThenBy(c => c.Pair.Origin.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Pair.Destination.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Aircraft.Id, StringComparer.Ordinal)
            .First();

        return best.Profit >= ProfitabilityFloor ? best : null;
    }

    /// <summary>Builds the real order(s) for a winning candidate from FindMostProfitablePair - a RouteOrder alone against an idle unit (no fee, already ours), or a LeaseAircraftOrder alongside it otherwise (2 actions, real recurring fee already reflected in the profit this candidate already cleared).</summary>
    private static void TryProposeCandidate(
        StrategyContext context, (PairDemand Pair, AircraftType Aircraft, Money Profit, bool UsesIdleUnit)? best, IReadOnlySet<string> validOriginCityIds,
        List<QuarterOrder> orders, HashSet<int> reservedUnitIds, Dictionary<string, int> slotsReservedByCity, Dictionary<string, int> citySpendReservedByCity, ref int spent)
    {
        if (best is null || !AiToolkit.TryOrientFromValidOrigin(best.Value.Pair, validOriginCityIds, out var origin, out var destination)
            || !BaselineAi.HasSlotAtEveryGateway(origin, destination, context.AvailableSlotsByCity, slotsReservedByCity)
            || !AiToolkit.HasRoomAtEveryCity(origin, destination, context.CityWideSlotsRemaining, citySpendReservedByCity))
        {
            return;
        }

        var actionsNeeded = best.Value.UsesIdleUnit ? 1 : 2;
        if (spent + actionsNeeded > context.ActionCap)
        {
            return;
        }

        if (best.Value.UsesIdleUnit)
        {
            var idleUnit = context.OwnFleet.Idle.Where(u => u.Type.Id == best.Value.Aircraft.Id && !reservedUnitIds.Contains(u.Id)).OrderBy(u => u.Id).First();
            orders.Add(new RouteOrder(origin, destination, best.Value.Aircraft));
            reservedUnitIds.Add(idleUnit.Id);
        }
        else
        {
            orders.Add(new LeaseAircraftOrder(best.Value.Aircraft, Quantity: 1));
            orders.Add(new RouteOrder(origin, destination, best.Value.Aircraft));
        }

        BaselineAi.ReserveSlots(origin, destination, slotsReservedByCity);
        AiToolkit.ReserveCityWideSlots(origin, destination, citySpendReservedByCity);
        spent += actionsNeeded;
    }

    private static bool TouchesUntouchedRegion(PairDemand pair, IReadOnlySet<Region> touchedRegions) =>
        !touchedRegions.Contains(pair.Origin.Region) || !touchedRegions.Contains(pair.Destination.Region);

    private static bool BothEndpointsTouched(PairDemand pair, IReadOnlySet<Region> touchedRegions) =>
        touchedRegions.Contains(pair.Origin.Region) && touchedRegions.Contains(pair.Destination.Region);

    private static City? FindBestUnservedGateway(StrategyContext context, Func<City, bool> regionFilter)
    {
        var touchedCityIds = context.CurrentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet();
        var alreadyPursued = AiToolkit.CitiesAlreadyPursuedForNegotiation(context);

        return context.AllCities
            .Where(c => c.IsGateway && !touchedCityIds.Contains(c.Id) && !alreadyPursued.Contains(c.Id))
            .Where(regionFilter)
            .OrderByDescending(c => c.DemandWeight)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
