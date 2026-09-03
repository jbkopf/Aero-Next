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
/// </summary>
public sealed class RapidExpansionStrategy : IAiStrategy
{
    /// <summary>(Proposed, tunable) — more willing to order than BaselineAi's own default (3x), reflecting a more aggressive risk posture.</summary>
    private const int CashComfortMultiplier = 2;

    public string Name => "Rapid Expansion";

    public IReadOnlySet<GameEra> ApplicableEras { get; } = new HashSet<GameEra> { GameEra.Starting };

    /// <summary>(Proposed, tunable) — richer affords the aggressive spending this strategy leans on; see docs/BASELINE_AI_ROADMAP.md's M39 section.</summary>
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

        // 2. Grow the best-performing existing route - runs second here,
        // so it must exclude whatever step 1 already reserved. M55:
        // also excludes any route shrunk within GrowthCooldownQuarters -
        // the real fix for the oscillation M43 traced (shrinking a route
        // mechanically raises its own load factor, so without this a
        // route shrunk one quarter could rank "best performing" again
        // the very next and get grown right back).
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
