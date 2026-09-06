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
/// Both cases share AiToolkit.EstimatedRealProfit/FindMostProfitablePair
/// - see their own doc comments for the full cost accounting (operating,
/// salary, lease, competition) and for why ranking *across* pairs by a
/// real profit figure is safe here in a way M56's own four reverted
/// attempts (docs/BASELINE_AI_ROADMAP.md) were not: every candidate is
/// evaluated at its *own* best-fitting aircraft type before pairs are
/// ever compared against each other, reusing the exact
/// single-peaked-in-capacity property DomesticProfitOptimizerStrategy's
/// own M57 fix (EstimatedProfitForAircraft) already established is safe
/// *within* one pair - applying it per-candidate-pair first, then
/// comparing already-optimized pairs, is mechanically different from
/// every one of M56's reverted designs (none of which found each pair's
/// own peak before comparing).
///
/// NetworkOptimizerStrategy follow-up: this search moved to AiToolkit
/// (EstimatedRealProfit/EvaluateCandidate/FindMostProfitablePair/
/// TryProposeCandidate/ProfitabilityFloor, all now public there) once
/// NetworkOptimizerStrategy's own step 3 needed the identical real-profit
/// search, domestic-only - see that class's own doc comment. A pure
/// extraction, verified byte-identical via this class's own existing
/// test suite. TouchesUntouchedRegion/BothEndpointsTouched/FindBestUnservedGateway
/// stayed here - genuinely specific to this class's own region-phased
/// design, not needed by the domestic-only caller.
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
                ? AiToolkit.EvaluateCandidate(context, reservedUnitIds, wonSlotPair.Value.Pair, wonSlotPair.Value.Aircraft)
                : AiToolkit.FindMostProfitablePair(context, reservedUnitIds, validOriginCityIds, p => TouchesUntouchedRegion(p, touchedRegions));

            AiToolkit.TryProposeCandidate(context, best, validOriginCityIds, orders, reservedUnitIds, slotsReservedByCity, citySpendReservedByCity, ref spent);
        }

        var newRegionGateway = FindBestUnservedGateway(context, c => !touchedRegions.Contains(c.Region)) ?? AiToolkit.FindBestIdleContestableGateway(context);

        // Case 2: within regions already touched, real profit again.
        if (spent + 1 <= context.ActionCap)
        {
            var best = AiToolkit.FindMostProfitablePair(context, reservedUnitIds, validOriginCityIds, p => BothEndpointsTouched(p, touchedRegions));
            AiToolkit.TryProposeCandidate(context, best, validOriginCityIds, orders, reservedUnitIds, slotsReservedByCity, citySpendReservedByCity, ref spent);
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
