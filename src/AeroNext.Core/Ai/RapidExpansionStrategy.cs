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
/// Existing-route-evaluation audit follow-up: gating BaselineAi.
/// FindBestPerformingRoute's step-2 growth pick on real profitability
/// (like NetworkOptimizerStrategy/BalancedGrowthStrategy below do) was
/// tried here too and measured, isolated via a real 300-game sweep, to be
/// a genuine regression specific to this strategy: Europe bankruptcy rose
/// 12.2%→13.7% with zero offsetting improvement anywhere else - reproduced
/// exactly whether this strategy's own gate was the only one enabled or
/// all three were together, while NetworkOptimizerStrategy/
/// BalancedGrowthStrategy's own use of the same gate independently
/// measured as a true no-op (identical numbers with either or both on vs.
/// fully off). This strategy's whole reason to exist is expanding as fast
/// as possible (see the class doc comment above) during the Starting era
/// specifically - gating its growth on real profitability cuts directly
/// against that design intent in a way the two later-era strategies' own
/// more measured growth postures don't share. Not shipped here - see the
/// Network-profitability follow-up below for what step 2 uses instead.
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
///
/// Aircraft fit (2026-09-27): once the new-route searches stopped choosing
/// routes that couldn't be opened (AiToolkit.CanOpenNow), this strategy
/// finally grew real networks - and its acquisition step exposed a flaw it
/// had always had: for a new route with nothing idle, it bought or leased
/// the *longest-range* type available, whatever the route's demand. Large,
/// expensive long-range aircraft on thin routes lost money badly - one such
/// route could flip a young network from profit to a loss it couldn't
/// survive (traced directly: a single route added ~$9.5M/quarter operating
/// cost for ~$0.6M revenue). Now every acquirable type proposes its own
/// best route (each screened by its own range and load factor), and the
/// aircraft acquired for the winning route is the type that flies it most
/// profitably; nothing is acquired if none would. Under the default
/// cost-plus-30% fare, "profitable" means the aircraft is at least ~77%
/// full. A first cut that kept the longest-range type as the route picker
/// cut bankrupt airlines 19 -> 2 on 100 Hard-difficulty games (the two left
/// were Papeete starts, a known structurally weak start). A network-level
/// brake (no expansion while the network loses money) was tried alongside
/// it and measured as adding nothing, so it wasn't kept. The cross-type
/// version, on 2x300 Hard games (seeds 1 and 301) against the same code
/// without it: games with a bankruptcy 51/55 -> 15/21, bankrupt airlines
/// 56/61 -> 20/24; SouthWestAsia 24/25 -> 4/1, SouthAmerica 14/14 -> 0/2,
/// NorthAmerica 5/9 -> 0/4. AustralAsia (9/8 -> 10/12) didn't move - its
/// early bankruptcies are the weak-start problem, not aircraft fit.
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
        var stopPaths = new StopPathFinder(context.AllCities);

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
            var idleTypes = context.OwnFleet.Idle.Where(u => !reservedUnitIds.Contains(u.Id)).Select(u => u.Type).Distinct().ToList();
            // Both searches only return routes that can be opened right now
            // (AiToolkit.CanOpenNow), so no separate slot/capacity check here.
            //
            // Multi-stop routes (docs/MULTI_STOP_ROUTES_DESIGN.md, Phase 3):
            // a won gateway's own best pair only goes ahead if it's
            // profitable (AiToolkit.EvaluateCandidate, the same check
            // BalancedGrowthStrategy already applied). A won refuelling
            // stop like Gander has almost no demand of its own; flying a
            // lone leg there was the single largest early-game loss
            // (676 routes, $2.6B a quarter across 600 games). The slot now
            // waits for a multi-stop route through it, which the search
            // below finds once the slot is held.
            var wonSlot = AiToolkit.FindBestUnservedPairTouchingWonSlot(context, reservedUnitIds, slotsReservedByCity, citySpendReservedByCity);
            var profitableWonSlot = wonSlot is null ? null : AiToolkit.EvaluateCandidate(context, reservedUnitIds, wonSlot.Value.Pair, wonSlot.Value.Aircraft);
            var idleBest = profitableWonSlot is not null
                ? (profitableWonSlot.Value.Pair, profitableWonSlot.Value.Aircraft, StopList.Empty)
                : FindBestUnservedPairCrossRegionFirstAcrossTypes(context, idleTypes, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity, stopPaths);

            if (idleBest is not null && AiToolkit.TryOrientFromValidOrigin(idleBest.Value.Pair, validOriginCityIds, out var origin, out var destination))
            {
                var aircraft = idleBest.Value.Aircraft;
                var stops = AiToolkit.OrientStops(idleBest.Value.Pair, origin, idleBest.Value.Stops);
                var idleUnit = context.OwnFleet.Idle.Where(u => u.Type.Id == aircraft.Id && !reservedUnitIds.Contains(u.Id)).OrderBy(u => u.Id).First();
                orders.Add(new RouteOrder(origin, destination, aircraft, stops));
                reservedUnitIds.Add(idleUnit.Id);
                BaselineAi.ReserveSlots(origin, destination, slotsReservedByCity, stops);
                AiToolkit.ReserveCityWideSlots(origin, destination, citySpendReservedByCity);
                spent += 1;
            }
            else if (idleBest is null && spent + 1 <= context.ActionCap)
            {
                // Geopolitical-aircraft-restrictions follow-up: nothing
                // idle, so this candidate will end up bought or leased -
                // the union of both stays legal for either branch below.
                var acquirableTypes = context.PurchasableOrEraTypes.Union(context.LeasableOrEraTypes).ToList();

                // Every acquirable type proposes its own best route (each
                // screened by its own range and capacity fit, cross-region
                // first), and the aircraft actually acquired for the winner
                // is whichever type flies it most profitably - nothing is
                // acquired if no type would make it profitable. Taking the
                // longest-range type outright put large, expensive aircraft
                // on thin routes, one of which could flip a young network
                // into losses on its own (see the class doc comment's
                // "Aircraft fit" section).
                var bestUnserved = FindBestUnservedPairCrossRegionFirstAcrossTypes(context, acquirableTypes, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity, stopPaths)?.Pair;
                var bestAircraft = bestUnserved is null
                    ? null
                    : BestFittingProfitableType(bestUnserved, acquirableTypes, stopPaths,
                        stops => AiToolkit.CanOpenNow(context, bestUnserved, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity, stops));
                var oriented = bestUnserved is not null && AiToolkit.TryOrientFromValidOrigin(bestUnserved, validOriginCityIds, out var newOrigin, out var newDestination)
                    ? (Origin: newOrigin, Destination: newDestination)
                    : ((City Origin, City Destination)?)null;

                if (bestAircraft is not null && oriented is not null
                    && context.AvailableCash is not null && FinancialState.CanAfford(context.AvailableCash.Value, ComfortableOrderCost(bestAircraft)))
                {
                    orders.Add(new AircraftOrder(bestAircraft, Quantity: 1));
                    spent += 1;
                }
                else if (bestAircraft is not null && oriented is not null && spent + 1 <= context.ActionCap)
                {
                    // Leasing resolves next quarter now (two-phase,
                    // docs/AIRCRAFT_ACQUISITION_ROADMAP.md) - submit it
                    // alone, 1 action, no paired RouteOrder yet. Once
                    // delivered it shows up idle and a later quarter's
                    // own re-evaluation opens the pair via this same
                    // step's idle-fleet path above.
                    orders.Add(new LeaseAircraftOrder(bestAircraft, Quantity: 1));
                    spent += 1;
                }
            }
        }

        // 2. Grow an existing route (AiToolkit.GrowExistingRoute: idle
        // aircraft first, buy or lease only when none has a profitable
        // home) - runs second here, so it must exclude whatever step 1
        // already reserved. M55:
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
        if (AiToolkit.NetworkTroubleRatio(context) <= 0.0)
        {
            AiToolkit.GrowExistingRoute(context, orders, ref spent, reservedUnitIds, slotsReservedByCity, citySpendReservedByCity, CashComfortMultiplier);
        }

        // 3. Negotiate for the best gateway, cross-region-reachable ones
        // weighted first - CLAUDE.md's own chokepoint pillar. M45: falls
        // back to a gateway with idle-expired contestable inventory when
        // no fully-unserved one remains, so this strategy can actually
        // reach that mechanic rather than only a human bidding by hand.
        //
        // Multi-stop routes (docs/MULTI_STOP_ROUTES_DESIGN.md, Phase 3): a
        // refuelling stop its best profitable multi-stop route still needs
        // comes first - bidding on what a real plan uses, rather than on
        // whichever reachable gateway is biggest.
        var bestGateway = FindStopNeededByBestMultiStopPlan(context, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity, stopPaths)
            ?? FindBestUnservedGatewayCrossRegionFirst(context)
            ?? AiToolkit.FindBestIdleContestableGateway(context);
        AiToolkit.NegotiateOrBorrow(context, bestGateway, orders, ref spent);

        // 4. A marketing campaign, if one is estimated to pay for itself
        // (see AiToolkit.ApplyMarketingCampaign).
        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        // 5. Shared toolkit - unchanged from DomesticProfitOptimizerStrategy's own use of it.
        AiToolkit.ApplyManagementToolkit(context, orders, reservedUnitIds, spent);

        return orders;
    }

    private static Money ComfortableOrderCost(AircraftType type) => type.Cost * CashComfortMultiplier;

    /// <summary>
    /// Mirrors BaselineAi.FindBestUnservedPairUsingIdleFleet's shape, but
    /// cross-region candidates outrank intra-region ones before demand is
    /// even compared. Each type's own candidate comes from
    /// <see cref="FindBestUnservedPairCrossRegionFirst"/>, which screens
    /// out any pair that type's own capacity can't come close to filling.
    /// Used for both idle-fleet reuse and the acquisition path. The
    /// acquisition path once searched only with the longest-range type,
    /// because a 2026 attempt to search every type there over-expanded;
    /// that attempt predated <see cref="BestFittingProfitableType"/> and
    /// AiToolkit.CanOpenNow, and the search across every type was
    /// re-measured with both in place (see the class doc comment's
    /// "Aircraft fit" section).
    /// </summary>
    private static (PairDemand Pair, AircraftType Aircraft, StopList Stops)? FindBestUnservedPairCrossRegionFirstAcrossTypes(
        StrategyContext context, IReadOnlyList<AircraftType> types, IReadOnlySet<string> validOriginCityIds,
        Dictionary<string, int> slotsReservedByCity, Dictionary<string, int> citySpendReservedByCity, StopPathFinder stopPaths)
    {
        var candidates = types
            .Distinct()
            .Select(type => (Found: FindBestUnservedPairCrossRegionFirst(context, type, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity, stopPaths), Aircraft: type))
            .Where(c => c.Found is not null)
            .Select(c => (Pair: c.Found!.Value.Pair, c.Aircraft, c.Found!.Value.Stops))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var shareByCityId = NetworkPlanner.ComputeRouteShareByCityId(context.CurrentRoutes);

        var heldStopIds = HeldUnusedSlotCityIds(context);
        var best = candidates
            .OrderByDescending(c => UsesHeldStop(c.Stops, heldStopIds))
            .ThenByDescending(c => IsCrossRegion(c.Pair, context))
            .ThenByDescending(c => AiToolkit.EffectiveDemandScore(c.Pair, shareByCityId))
            .ThenBy(c => c.Pair.Origin.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Pair.Destination.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Aircraft.Id, StringComparer.Ordinal)
            .First();

        return (best.Pair, best.Aircraft, best.Stops);
    }

    /// <summary>
    /// Minimum load factor (real demand vs. this specific aircraft's own
    /// quarter-1-style capacity, <c>Math.Min(demand, capacity) / capacity</c>)
    /// a candidate pair must clear for <paramref name="aircraft"/> before
    /// this method will ever propose it — the fix for a real, traced
    /// failure mode (docs/OPEN_WORK.md): an idle aircraft reused (or a new
    /// one bought) for whichever reachable pair has the highest *raw*
    /// demand, with zero regard for whether that aircraft's own capacity
    /// can come anywhere near filling it — traced directly: Saipan's own
    /// idle second dc7 (80 seats, ~52,000 seats/quarter) pointed at a pair
    /// real demand filled to a fraction of that, a structural
    /// ≈$1M/quarter loss from the quarter it opened, not a slow-growing
    /// one, because operating cost is charged on capacity offered, not
    /// seats filled. Deliberately well below FareCalculator's own
    /// break-even load factor (~77%, 1/1.3) — matching that bar would be
    /// the "real profitability gate" already measured and rejected for
    /// this strategy's own step 2 (see this class's own top-level doc
    /// comment); this only screens out catastrophic mismatches, not
    /// merely mediocre ones, keeping "expand fast, don't chase per-route
    /// profit" intact everywhere above the floor.
    /// </summary>
    private const double MinLoadFactorForNewPair = 0.4;

    /// <summary>
    /// M44: validOriginCityIds filters candidates to those touching a
    /// city this airline can legally originate a new route from - see
    /// AiToolkit.ValidOriginCityIds/TryOrientFromValidOrigin. M55: also
    /// excludes any pair this airline closed within ReopenCooldownQuarters
    /// - this strategy's own demand-only ranking (no profit awareness,
    /// unlike DomesticProfitOptimizerStrategy) was the specific "known,
    /// accepted risk" M54 named but didn't fix: it could otherwise
    /// immediately re-propose a pair the shared toolkit just closed for
    /// real, structural reasons. Capacity-mismatch follow-up: also
    /// requires <paramref name="aircraft"/>'s own load factor on the pair
    /// to clear <see cref="MinLoadFactorForNewPair"/> - see that constant's
    /// own doc comment.
    ///
    /// Multi-stop routes (docs/MULTI_STOP_ROUTES_DESIGN.md, Phase 3): a
    /// pair this aircraft can't fly nonstop is still a candidate if a path
    /// of refuelling stops reaches it (<see cref="StopPathFinder"/>), with
    /// its seats and costs taken from the route as flown, stops included,
    /// and a free slot needed at every stop. Unlike a nonstop candidate, a
    /// multi-stop one must also be profitable on its own: it ties up a
    /// scarce chokepoint slot, and an unprofitable route through one is
    /// the very loss this replaces. A candidate through a stop the airline
    /// already holds an unused slot at ranks first - the multi-stop
    /// counterpart of "build out a won gateway before chasing a new one":
    /// a held slot left idle for LandingRightsBoard.IdleExpiryThreshold
    /// quarters becomes open to rivals.
    /// </summary>
    private static (PairDemand Pair, StopList Stops)? FindBestUnservedPairCrossRegionFirst(
        StrategyContext context, AircraftType aircraft, IReadOnlySet<string> validOriginCityIds,
        Dictionary<string, int> slotsReservedByCity, Dictionary<string, int> citySpendReservedByCity, StopPathFinder stopPaths)
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
        var heldStopIds = HeldUnusedSlotCityIds(context);

        var best = context.DemandModel.AllPairs(context.AllCities)
            .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
            .Where(p => !recentlyClosed.Contains(QuarterEngine.NormalizedPairKey(p.Origin, p.Destination)))
            .Where(p => validOriginCityIds.Contains(p.Origin.Id) || validOriginCityIds.Contains(p.Destination.Id))
            .Select(p => (Pair: p, Stops: stopPaths.ShortestStops(p.Origin, p.Destination, aircraft.RangeKm)))
            .Where(c => c.Stops is not null)
            .Select(c => (c.Pair, Stops: c.Stops!.Value, Route: new Route(c.Pair.Origin, c.Pair.Destination, aircraft, AircraftCount: 1, Stops: c.Stops!.Value)))
            .Where(c => c.Pair.PassengersPerQuarter >= MinLoadFactorForNewPair * c.Route.SeatsPerQuarter)
            .Where(c => c.Stops.IsEmpty || FareCalculator.EstimatedProfit(c.Route, Math.Min(c.Pair.PassengersPerQuarter, c.Route.SeatsPerQuarter)) >= Money.Zero)
            .Where(c => AiToolkit.CanOpenNow(context, c.Pair, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity, c.Stops))
            .OrderByDescending(c => UsesHeldStop(c.Stops, heldStopIds))
            .ThenByDescending(c => IsCrossRegion(c.Pair, context))
            .ThenByDescending(c => AiToolkit.EffectiveDemandScore(c.Pair, shareByCityId))
            .ThenBy(c => c.Pair.Origin.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Pair.Destination.Id, StringComparer.Ordinal)
            .Select(c => ((PairDemand Pair, StopList Stops)?)(c.Pair, c.Stops))
            .FirstOrDefault();

        return best;
    }

    /// <summary>
    /// Among <paramref name="types"/> that can reach <paramref name="pair"/>,
    /// the one with the highest estimated operating profit on it - a single
    /// aircraft carrying the pair's demand up to its own seats
    /// (FareCalculator.EstimatedProfit) - or null if none would be
    /// profitable. Deliberately a solo estimate with no rival adjustment,
    /// matching this strategy's own demand-first ranking.
    ///
    /// Each type is priced over its own path (docs/MULTI_STOP_ROUTES_DESIGN.md):
    /// nonstop if it can, otherwise through the stops it needs, and only if
    /// <paramref name="canOpenWith"/> confirms the airline could open the
    /// route with those stops right now.
    /// </summary>
    private static AircraftType? BestFittingProfitableType(PairDemand pair, IReadOnlyList<AircraftType> types, StopPathFinder stopPaths, Func<StopList, bool> canOpenWith) =>
        types
            .Select(type => (Type: type, Stops: stopPaths.ShortestStops(pair.Origin, pair.Destination, type.RangeKm)))
            .Where(x => x.Stops is not null && canOpenWith(x.Stops.Value))
            .Select(x =>
            {
                var route = new Route(pair.Origin, pair.Destination, x.Type, AircraftCount: 1, Stops: x.Stops!.Value);
                return (x.Type, Profit: FareCalculator.EstimatedProfit(route, Math.Min(pair.PassengersPerQuarter, route.SeatsPerQuarter)));
            })
            .Where(x => x.Profit >= Money.Zero)
            .OrderByDescending(x => x.Profit.Cents)
            .ThenBy(x => x.Type.Id, StringComparer.Ordinal)
            .Select(x => x.Type)
            .FirstOrDefault();

    /// <summary>Cities where this airline holds a slot no route uses yet.</summary>
    private static IReadOnlySet<string> HeldUnusedSlotCityIds(StrategyContext context) =>
        context.AvailableSlotsByCity is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : context.AvailableSlotsByCity.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

    private static bool UsesHeldStop(StopList stops, IReadOnlySet<string> heldStopIds) =>
        stops.Any(stop => heldStopIds.Contains(stop.Id));

    /// <summary>
    /// The refuelling stop this airline's best profitable multi-stop route
    /// still needs a slot at (docs/MULTI_STOP_ROUTES_DESIGN.md, Phase 3) -
    /// what step 3 bids on before anything else, so chokepoint bids follow
    /// a real plan. A plan is any unserved pair, touching a valid origin,
    /// that some idle or acquirable type can fly only with stops, passes
    /// the same load-factor screen as step 1, is profitable over its whole
    /// path, and whose endpoints the airline could open now. Plans rank by
    /// estimated profit. A stop already being negotiated for is skipped,
    /// so the airline doesn't wait on a bid it already placed. Null when no
    /// plan needs a stop the airline lacks.
    /// </summary>
    private static City? FindStopNeededByBestMultiStopPlan(
        StrategyContext context, IReadOnlySet<string> validOriginCityIds,
        Dictionary<string, int> slotsReservedByCity, Dictionary<string, int> citySpendReservedByCity, StopPathFinder stopPaths)
    {
        var served = context.CurrentRoutes
            .SelectMany(r => new[] { (r.Origin.Id, r.Destination.Id), (r.Destination.Id, r.Origin.Id) })
            .ToHashSet();
        var recentlyClosed = AiToolkit.RecentlyClosedPairKeys(context, AiToolkit.ReopenCooldownQuarters);
        var alreadyPursued = AiToolkit.CitiesAlreadyPursuedForNegotiation(context);
        var types = context.OwnFleet.Idle.Select(u => u.Type)
            .Concat(context.PurchasableOrEraTypes)
            .Concat(context.LeasableOrEraTypes)
            .Distinct()
            .ToList();

        // Permissive mode (no slot data) never lacks a slot anywhere.
        City? NeededStop(StopList stops) => context.AvailableSlotsByCity is null
            ? null
            : stops.FirstOrDefault(stop =>
                !alreadyPursued.Contains(stop.Id)
                && context.AvailableSlotsByCity.GetValueOrDefault(stop.Id) - slotsReservedByCity.GetValueOrDefault(stop.Id) <= 0);

        return context.DemandModel.AllPairs(context.AllCities)
            .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
            .Where(p => !recentlyClosed.Contains(QuarterEngine.NormalizedPairKey(p.Origin, p.Destination)))
            .Where(p => validOriginCityIds.Contains(p.Origin.Id) || validOriginCityIds.Contains(p.Destination.Id))
            .Where(p => AiToolkit.CanOpenNow(context, p, validOriginCityIds, slotsReservedByCity, citySpendReservedByCity))
            .SelectMany(p => types.Select(type => (Pair: p, Type: type, Stops: stopPaths.ShortestStops(p.Origin, p.Destination, type.RangeKm))))
            .Where(c => c.Stops is { IsEmpty: false })
            .Select(c => (c.Pair, c.Type, Route: new Route(c.Pair.Origin, c.Pair.Destination, c.Type, AircraftCount: 1, Stops: c.Stops!.Value)))
            .Where(c => c.Pair.PassengersPerQuarter >= MinLoadFactorForNewPair * c.Route.SeatsPerQuarter)
            .Select(c => (c.Pair, c.Type, c.Route, Profit: FareCalculator.EstimatedProfit(c.Route, Math.Min(c.Pair.PassengersPerQuarter, c.Route.SeatsPerQuarter)), Needed: NeededStop(c.Route.Stops)))
            .Where(c => c.Profit >= Money.Zero && c.Needed is not null)
            .OrderByDescending(c => c.Profit.Cents)
            .ThenBy(c => c.Pair.Origin.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Pair.Destination.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Type.Id, StringComparer.Ordinal)
            .Select(c => c.Needed)
            .FirstOrDefault();
    }

    private static bool IsCrossRegion(PairDemand pair, StrategyContext context) =>
        pair.Origin.Region != context.HomeRegion || pair.Destination.Region != context.HomeRegion;

    /// <summary>
    /// Gateway-relevance follow-up (docs/OPEN_WORK.md's filler-seat
    /// investigation): this used to rank every unserved gateway on Earth
    /// by "is it cross-region" before demand weight, with no reachability
    /// check at all — meaning the home region's *own* gateway sorted dead
    /// last, behind every other region's, however far away. Traced
    /// directly: one airline spent $2M/quarter for 15 straight quarters
    /// working through Tokyo, JFK, Hong Kong, LA, São Paulo, Sydney,
    /// Cairo, Honolulu, Dakar, Anchorage, Gander, and Wake Island — none
    /// remotely useful yet — before ever reaching London or Amsterdam,
    /// its own region's gateways. Now filtered to gateways within
    /// AiToolkit.MaxEraAircraftRangeKm of the airline's own network
    /// (AiToolkit.DistanceFromNetworkKm) first — "cross-region reach" only
    /// ever meant regions actually reachable soon, not literally anywhere
    /// — so this still tries the farthest *reachable* region ahead of a
    /// closer intra-region one, exactly as before, just never bids on a
    /// gateway the airline has no plausible way to use for years.
    ///
    /// Multi-stop routes (docs/MULTI_STOP_ROUTES_DESIGN.md, Phase 3): also
    /// only a gateway the airline could fly a profitable nonstop route to
    /// from its network (AiToolkit.HasProfitableNonstopRouteTo) - the
    /// same check a won gateway's first route now has to pass. Measured
    /// without it, refuelling stops were bid on simply because nothing
    /// bigger was left in reach: every chokepoint slot in every game was
    /// held by quarter 40, and 1,026 of 1,102 sat unused. A refuelling
    /// stop is bid on through FindStopNeededByBestMultiStopPlan instead.
    /// </summary>
    private static City? FindBestUnservedGatewayCrossRegionFirst(StrategyContext context)
    {
        var touchedCityIds = context.CurrentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet();
        var alreadyPursued = AiToolkit.CitiesAlreadyPursuedForNegotiation(context);
        var maxRangeKm = AiToolkit.MaxEraAircraftRangeKm(context);

        return context.AllCities
            .Where(c => c.IsGateway && !touchedCityIds.Contains(c.Id) && !alreadyPursued.Contains(c.Id))
            .Where(c => AiToolkit.DistanceFromNetworkKm(context, c) <= maxRangeKm)
            .Where(c => AiToolkit.HasProfitableNonstopRouteTo(context, c))
            .OrderByDescending(c => c.Region != context.HomeRegion)
            .ThenByDescending(c => c.DemandWeight)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
