using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// M39 (docs/BASELINE_AI_ROADMAP.md): expand as fast as possible and
/// secure cross-region reach early. Inverts BaselineAi.DecideOrders's own
/// priority order — attempts opening a new pair *before* growing an
/// existing route, with cross-region candidates (and cross-region
/// gateways) weighted ahead of intra-region ones — and is more willing to
/// lease/order than the default posture. No route cap, no region
/// restriction. Reuses AiToolkit's fare/debt/hire/retire logic
/// unchanged — only growth prioritization genuinely differs.
///
/// Because the growth order is inverted from BaselineAi's own (where
/// grow-existing runs first and reserves what it needs before new-pair
/// runs), both steps here explicitly check and reserve idle units
/// against the same reservedUnitIds set — BaselineAi's original code
/// only worked correctly *because* of its specific order; reusing that
/// same one-directional assumption here would let both steps pick the
/// same idle unit and get one of the two RouteOrders rejected at
/// resolution, exactly the "attempt and fail" outcome this whole AI is
/// built to avoid.
///
/// M41: gateway negotiation now skips any city already pursued (a
/// pending bid in flight, or a won-but-unused slot) instead of re-bidding
/// the same top-ranked gateway every quarter — diagnosed as the real
/// driver of this strategy's own early cash burn (one QA-harness game
/// traced directly: $2M bid on the same gateway 6 consecutive quarters),
/// not aircraft spending. See AiToolkit.CitiesAlreadyPursuedForNegotiation.
///
/// Existing-route-evaluation audit follow-up: BaselineAi.FindBestPerformingRoute's
/// new enableRealProfitabilityGate was tried here too (gate step 2's
/// growth pick on real profitability, like NetworkOptimizerStrategy/
/// BalancedGrowthStrategy below now do) and measured, isolated via a real
/// 300-game sweep, to be a genuine regression specific to this strategy:
/// Europe bankruptcy rose 12.2%→13.7% with zero offsetting improvement
/// anywhere else - reproduced exactly whether this strategy's own flag
/// was the only one enabled or all three were together, while
/// NetworkOptimizerStrategy/BalancedGrowthStrategy's own use of the same
/// flag independently measured as a true no-op (identical numbers with
/// either or both on vs. fully off). This strategy's whole reason to
/// exist is expanding as fast as possible (see the class doc comment
/// above) during the Starting era specifically - gating its growth on
/// real profitability cuts directly against that design intent in a way
/// the two later-era strategies' own more measured growth postures don't
/// share. Stays false; not shipped here.
///
/// Network-profitability follow-up: the user's own direct request - stay
/// primarily concerned with reaching regions outside the home one, worry
/// less about individual route profitability, more about the network as
/// a whole. Step 1 (new pair) and step 3 (gateway) already were and stay
/// cross-region-first, untouched. Step 2 (grow an existing route)
/// switches from BaselineAi.FindBestPerformingRoute's own real load
/// factor to FindBestExistingRouteByDemand's own AiToolkit.EffectiveDemandScore
/// - the same demand-based signal step 1 already uses, not a second,
/// different per-route economics metric - and now only fires at all when
/// AiToolkit.NetworkTroubleRatio reads zero real trouble this quarter
/// (the same binary threshold NetworkOptimizerStrategy/BalancedGrowthStrategy
/// already use), deliberately leaving steps 1/3 untouched by that guard:
/// a troubled network should spend its remaining budget reaching a new
/// region, this strategy's actual mission, not reactively topping up a
/// route that might not even be one. Byte-identical to before at zero
/// trouble throughout. A third change - reducing Suitability itself as
/// network trouble rises - was also tried and measured to be a real
/// regression (Europe/EastAsia bankruptcy both rose); see Suitability's
/// own doc comment below for the full story. Reverted; this strategy's
/// own selection stays plain CashRatio.
/// </summary>
public sealed class RapidExpansionStrategy : IAiStrategy
{
    /// <summary>(Proposed, tunable) — more willing to order than BaselineAi's own default (3x), reflecting a more aggressive risk posture.</summary>
    private const int CashComfortMultiplier = 2;

    public string Name => "Rapid Expansion";

    public IReadOnlySet<GameEra> ApplicableEras { get; } = new HashSet<GameEra> { GameEra.Starting };

    /// <summary>
    /// (Proposed, tunable) — richer affords the aggressive spending this
    /// strategy leans on; see docs/BASELINE_AI_ROADMAP.md's M39 section.
    /// Network-profitability follow-up: multiplying this down as
    /// AiToolkit.NetworkTroubleRatio rises (ceding ground to
    /// DomesticProfitOptimizerStrategy's own exact-complement score,
    /// 1 - CashRatio) was tried and measured, isolated via a real
    /// 300-game sweep, to be a real, substantial regression - Europe
    /// 12.2%→16.2% and EastAsia 4.5%→6.6% bankruptcy, reproduced exactly
    /// by this change alone. Root cause: switching a troubled airline
    /// from this strategy's own aggressive cross-region expansion to
    /// DomesticProfitOptimizerStrategy's domestic-only conservatism is
    /// itself the wrong response under CLAUDE.md's own "a home region
    /// cannot fund a win" pillar - retreating into domestic-only play
    /// during real trouble starves exactly the cross-region diversification
    /// a struggling airline needs most, making things worse rather than
    /// better. Reverted; stays plain CashRatio. The step-2 throttle below
    /// carries the real "worry about the whole network" behavior instead,
    /// measured clean on its own.
    /// </summary>
    public double Suitability(StrategyContext context) => AiSuitability.CashRatio(context);

    public IReadOnlyList<QuarterOrder> DecideOrders(StrategyContext context)
    {
        var orders = new List<QuarterOrder>();
        var reservedUnitIds = new HashSet<int>();
        var slotsReservedByCity = new Dictionary<string, int>();
        var citySpendReservedByCity = new Dictionary<string, int>();
        var spent = 0;
        var validOriginCityIds = AiToolkit.ValidOriginCityIds(context);

        // 1. New pair first, cross-region preferred - the inversion from
        // BaselineAi's own order. A gateway already won but not yet built
        // out takes priority over chasing a fresh one - see
        // AiToolkit.FindBestUnservedPairTouchingWonSlot's own doc comment
        // for the diagnosed problem this closes (a contested win, spent
        // real cash to secure, otherwise just sits there while more bids
        // keep going out). M44: every candidate below is already filtered
        // to touch a valid origin; TryOrientFromValidOrigin fixes up
        // Origin/Destination right before each RouteOrder is built, since
        // AllPairs's own canonical ordering has no idea which side that is.
        if (spent + 1 <= context.ActionCap)
        {
            var idleBest = AiToolkit.FindBestUnservedPairTouchingWonSlot(context, reservedUnitIds)
                ?? FindBestUnservedPairCrossRegionFirstUsingIdleFleet(context, reservedUnitIds, validOriginCityIds);

            if (idleBest is not null && AiToolkit.TryOrientFromValidOrigin(idleBest.Value.Pair, validOriginCityIds, out var origin, out var destination)
                && BaselineAi.HasSlotAtEveryGateway(origin, destination, context.AvailableSlotsByCity, slotsReservedByCity)
                && AiToolkit.HasRoomAtEveryCity(origin, destination, context.CityWideSlotsRemaining, citySpendReservedByCity))
            {
                var aircraft = idleBest.Value.Aircraft;
                var idleUnit = context.OwnFleet.Idle.Where(u => u.Type.Id == aircraft.Id && !reservedUnitIds.Contains(u.Id)).OrderBy(u => u.Id).First();
                orders.Add(new RouteOrder(origin, destination, aircraft));
                reservedUnitIds.Add(idleUnit.Id);
                BaselineAi.ReserveSlots(origin, destination, slotsReservedByCity);
                AiToolkit.ReserveCityWideSlots(origin, destination, citySpendReservedByCity);
                spent += 1;
            }
            else if (idleBest is null && spent + 1 <= context.ActionCap)
            {
                var bestAircraft = context.EraAvailableTypes
                    .OrderByDescending(a => a.RangeKm)
                    .ThenBy(a => a.Id, StringComparer.Ordinal)
                    .FirstOrDefault();

                var bestUnserved = bestAircraft is null ? null : FindBestUnservedPairCrossRegionFirst(context, bestAircraft, validOriginCityIds);
                var oriented = bestUnserved is not null && AiToolkit.TryOrientFromValidOrigin(bestUnserved, validOriginCityIds, out var newOrigin, out var newDestination)
                    ? (Origin: newOrigin, Destination: newDestination)
                    : ((City Origin, City Destination)?)null;

                if (bestAircraft is not null && oriented is not null
                    && context.AvailableCash is not null && FinancialState.CanAfford(context.AvailableCash.Value, ComfortableOrderCost(bestAircraft)))
                {
                    orders.Add(new AircraftOrder(bestAircraft, Quantity: 1));
                    spent += 1;
                }
                else if (bestAircraft is not null && oriented is not null && spent + 2 <= context.ActionCap
                    && BaselineAi.HasSlotAtEveryGateway(oriented.Value.Origin, oriented.Value.Destination, context.AvailableSlotsByCity, slotsReservedByCity)
                    && AiToolkit.HasRoomAtEveryCity(oriented.Value.Origin, oriented.Value.Destination, context.CityWideSlotsRemaining, citySpendReservedByCity))
                {
                    orders.Add(new LeaseAircraftOrder(bestAircraft, Quantity: 1));
                    orders.Add(new RouteOrder(oriented.Value.Origin, oriented.Value.Destination, bestAircraft));
                    BaselineAi.ReserveSlots(oriented.Value.Origin, oriented.Value.Destination, slotsReservedByCity);
                    AiToolkit.ReserveCityWideSlots(oriented.Value.Origin, oriented.Value.Destination, citySpendReservedByCity);
                    spent += 2;
                }
            }
        }

        // 2. Grow the highest-demand existing route - runs second here,
        // so it must exclude whatever step 1 already reserved. M55:
        // also excludes any route shrunk within GrowthCooldownQuarters -
        // the real fix for the oscillation M43 traced (shrinking a route
        // mechanically raises its own load factor, so without this a
        // route shrunk one quarter could rank "best performing" again
        // the very next and get grown right back).
        //
        // Network-profitability follow-up (the user's own direct
        // request): only acts when AiToolkit.NetworkTroubleRatio reads
        // zero real trouble this quarter - the exact same binary
        // threshold NetworkOptimizerStrategy/BalancedGrowthStrategy
        // already use for "is the network in real trouble," reused
        // rather than inventing a new one. Steps 1/3 (this strategy's
        // actual cross-region mission) are deliberately untouched by
        // this guard - a troubled network should spend its remaining
        // budget reaching a new region, not reactively topping up a
        // route that might not even be one.
        var bestExisting = AiToolkit.NetworkTroubleRatio(context) <= 0.0
            ? FindBestExistingRouteByDemand(context, AiToolkit.RecentlyShrunkPairKeys(context, AiToolkit.GrowthCooldownQuarters))
            : null;
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
            else if (context.AvailableCash is not null && FinancialState.CanAfford(context.AvailableCash.Value, ComfortableOrderCost(bestExisting.Aircraft)))
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

        // 3. Negotiate for the best gateway, cross-region-reachable ones
        // weighted first - CLAUDE.md's own chokepoint pillar. M45: falls
        // back to a gateway with idle-expired contestable inventory when
        // no fully-unserved one remains, so this strategy can actually
        // reach that mechanic rather than only a human bidding by hand.
        var bestGateway = FindBestUnservedGatewayCrossRegionFirst(context) ?? AiToolkit.FindBestIdleContestableGateway(context);
        AiToolkit.NegotiateOrBorrow(context, bestGateway, orders, ref spent);

        // 4. M52: a marketing campaign on whichever already-served
        // route(s) are genuinely contested.
        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        // 5. Shared toolkit - unchanged from DomesticProfitOptimizerStrategy's own use of it.
        AiToolkit.ApplyManagementToolkit(context, orders, reservedUnitIds, spent);

        return orders;
    }

    private static Money ComfortableOrderCost(AircraftType type) => type.Cost * CashComfortMultiplier;

    /// <summary>
    /// Network-profitability follow-up: replaces
    /// BaselineAi.FindBestPerformingRoute's own real, *observed*
    /// load-factor ranking for this strategy specifically - the user's
    /// own direct request to de-emphasize individual route profitability
    /// for growth decisions, matching step 1's own demand-based ranking
    /// (AiToolkit.EffectiveDemandScore) instead of a second, different
    /// metric. Kept private here, not extracted to AiToolkit - no second
    /// caller wants this shape yet. Same excludePairKeys contract
    /// FindBestPerformingRoute already had (RecentlyShrunkPairKeys, M55's
    /// own oscillation fix) - same safety, new metric.
    /// </summary>
    private static Route? FindBestExistingRouteByDemand(StrategyContext context, IReadOnlySet<(string, string)> excludePairKeys)
    {
        var shareByCityId = NetworkPlanner.ComputeRouteShareByCityId(context.CurrentRoutes);

        return context.CurrentRoutes
            .Where(r => !excludePairKeys.Contains(QuarterEngine.NormalizedPairKey(r.Origin, r.Destination)))
            .OrderByDescending(r => AiToolkit.EffectiveDemandScore(context.DemandModel.ForPair(r.Origin, r.Destination), shareByCityId))
            .ThenBy(r => r.Origin.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Destination.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>Mirrors BaselineAi.FindBestUnservedPairUsingIdleFleet's shape, but cross-region candidates outrank intra-region ones before demand is even compared.</summary>
    private static (PairDemand Pair, AircraftType Aircraft)? FindBestUnservedPairCrossRegionFirstUsingIdleFleet(StrategyContext context, IReadOnlySet<int> reservedUnitIds, IReadOnlySet<string> validOriginCityIds)
    {
        var candidates = context.OwnFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Select(u => u.Type)
            .Distinct()
            .Select(type => (Pair: FindBestUnservedPairCrossRegionFirst(context, type, validOriginCityIds), Aircraft: type))
            .Where(c => c.Pair is not null)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var shareByCityId = NetworkPlanner.ComputeRouteShareByCityId(context.CurrentRoutes);

        var best = candidates
            .OrderByDescending(c => IsCrossRegion(c.Pair!, context))
            .ThenByDescending(c => AiToolkit.EffectiveDemandScore(c.Pair!, shareByCityId))
            .ThenBy(c => c.Pair!.Origin.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Pair!.Destination.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Aircraft.Id, StringComparer.Ordinal)
            .First();

        return (best.Pair!, best.Aircraft);
    }

    /// <summary>
    /// M44: validOriginCityIds filters candidates to those touching a
    /// city this airline can legally originate a new route from - see
    /// AiToolkit.ValidOriginCityIds/TryOrientFromValidOrigin. M55: also
    /// excludes any pair this airline closed within ReopenCooldownQuarters
    /// - this strategy's own demand-only ranking (no profit awareness,
    /// unlike DomesticProfitOptimizerStrategy) was the specific "known,
    /// accepted risk" M54 named but didn't fix: it could otherwise
    /// immediately re-propose a pair the shared toolkit just closed for
    /// real, structural reasons.
    /// </summary>
    private static PairDemand? FindBestUnservedPairCrossRegionFirst(StrategyContext context, AircraftType aircraft, IReadOnlySet<string> validOriginCityIds)
    {
        var served = context.CurrentRoutes
            .SelectMany(r => new[] { (r.Origin.Id, r.Destination.Id), (r.Destination.Id, r.Origin.Id) })
            .ToHashSet();
        var recentlyClosed = AiToolkit.RecentlyClosedPairKeys(context, AiToolkit.ReopenCooldownQuarters);
        // M64: cheap (context.CurrentRoutes is a handful of routes, not
        // the O(cities^2) scale NetworkPlanner's own M62 precompute cares
        // about) - recomputed per aircraft type tried here rather than
        // threaded through as a parameter, for a small, contained change.
        var shareByCityId = NetworkPlanner.ComputeRouteShareByCityId(context.CurrentRoutes);

        return context.DemandModel.AllPairs(context.AllCities)
            .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
            .Where(p => !recentlyClosed.Contains(QuarterEngine.NormalizedPairKey(p.Origin, p.Destination)))
            .Where(p => p.DistanceKm <= aircraft.RangeKm)
            .Where(p => validOriginCityIds.Contains(p.Origin.Id) || validOriginCityIds.Contains(p.Destination.Id))
            .OrderByDescending(p => IsCrossRegion(p, context))
            .ThenByDescending(p => AiToolkit.EffectiveDemandScore(p, shareByCityId))
            .ThenBy(p => p.Origin.Id, StringComparer.Ordinal)
            .ThenBy(p => p.Destination.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static bool IsCrossRegion(PairDemand pair, StrategyContext context) =>
        pair.Origin.Region != context.HomeRegion || pair.Destination.Region != context.HomeRegion;

    private static City? FindBestUnservedGatewayCrossRegionFirst(StrategyContext context)
    {
        var touchedCityIds = context.CurrentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet();
        var alreadyPursued = AiToolkit.CitiesAlreadyPursuedForNegotiation(context);

        return context.AllCities
            .Where(c => c.IsGateway && !touchedCityIds.Contains(c.Id) && !alreadyPursued.Contains(c.Id))
            .OrderByDescending(c => c.Region != context.HomeRegion)
            .ThenByDescending(c => c.DemandWeight)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
