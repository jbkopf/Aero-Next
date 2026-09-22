using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// M39 (docs/BASELINE_AI_ROADMAP.md): home of the real per-airline AI —
/// the <see cref="Strategies"/> roster and <see cref="SelectStrategy"/>
/// dispatcher every live game seat actually runs through — plus the
/// shared static helpers and tunable constants every concrete
/// <see cref="IAiStrategy"/> (NetworkOptimizerStrategy,
/// BalancedGrowthStrategy, RapidExpansionStrategy,
/// DomesticProfitOptimizerStrategy) draws on: best-performing-route
/// search, gateway-slot bookkeeping, fare-raise/cut targets, and the
/// cash-comfort check for ordering outright instead of leasing.
///
/// This class used to also hold `DecideOrders`, a single flat heuristic
/// that was the entire AI before M39 split per-airline behavior into the
/// `StrategyContext`/`IAiStrategy` path above — greedy, no hub-awareness,
/// one global-best pick per concern per quarter. Real gameplay stopped
/// calling it at M39; its last caller anywhere (a small league demo in
/// `AeroNext.Sim/Program.cs`) was removed in a later codebase-cleanup
/// pass, at which point it had no caller left at all outside its own unit
/// tests — genuinely orphaned code, not just legacy, so it was deleted
/// rather than kept frozen. Three of its own private helpers
/// (`FindBestUnservedPair`, `FindBestUnservedPairUsingIdleFleet`,
/// `FindBestUnservedGateway`) turned out to have no caller left either
/// once `DecideOrders` was gone — every concrete `IAiStrategy` that
/// searches for an unserved pair/gateway today has always used its own,
/// separately-evolved search (see e.g. RapidExpansionStrategy's own
/// `FindBestUnservedGatewayCrossRegionFirst`, BalancedGrowthStrategy's own
/// `FindBestUnservedGateway(StrategyContext, Func&lt;City,bool&gt;)`) rather
/// than this class's plain global-best version — so those three were
/// deleted too, not kept as unreferenced dead code. See
/// `docs/AI_ACQUISITION_AWARENESS_GAPS_ROADMAP.md` for the reasoning trail.
/// </summary>
public static class BaselineAi
{
    internal static readonly Money NegotiationBidAmount = Money.FromDollars(2_000_000);

    /// <summary>
    /// M36 (proposed, tunable): pay down debt only once cash comfortably
    /// clears this — five times NegotiationBidAmount, deliberately, so
    /// cash can never be simultaneously short of the bid (the borrow
    /// trigger below) and above this buffer (the paydown trigger) in the
    /// same quarter. No explicit mutual-exclusion check needed; the
    /// thresholds themselves guarantee it.
    /// </summary>
    internal static readonly Money PayDownCashBuffer = Money.FromDollars(10_000_000);

    /// <summary>M37 (proposed, tunable): at or above this, a served route reads as essentially sold out — the reactive correction is to raise its fare toward FareRaiseFraction above the computed default.</summary>
    internal const double SaturatedLoadFactorCeiling = 0.95;

    /// <summary>M37 (proposed, tunable): how far above the computed default a saturated route's fare gets raised.</summary>
    internal const double FareRaiseFraction = 0.15;

    /// <summary>M37 (proposed, tunable): how far below the computed default an underperforming route's fare gets cut — see the class doc comment for why this is currently revenue-negative under today's demand model, built anyway to exercise the full mechanic.</summary>
    internal const double FareCutFraction = 0.20;

    /// <summary>
    /// M38 (proposed, tunable): order instead of lease only when cash
    /// clears this multiple of the aircraft's own purchase price —
    /// deliberately more conservative than QuarterEngine's own
    /// affordability gate for AircraftOrder (1x cost), so this AI never
    /// gets its own order rejected and keeps comfortable margin for the
    /// same quarter's salary/operating cost.
    /// </summary>
    private const int AircraftOrderCashComfortMultiplier = 3;

    internal static Money ComfortableOrderCost(AircraftType type) => type.Cost * AircraftOrderCashComfortMultiplier;

    /// <summary>
    /// M39 (docs/BASELINE_AI_ROADMAP.md): the strategy roster. Order
    /// doesn't matter — Strategies() is only ever consulted through
    /// SelectStrategy, which sorts by Suitability with a deterministic
    /// Name tie-break, never by roster position. M58: NetworkOptimizerStrategy
    /// added, the first strategy to cover GameEra.LateGame at all.
    /// BalancedGrowthStrategy follow-up: the second MidGame/LateGame
    /// strategy - see its own class doc comment and
    /// AiToolkit.NetworkTroubleRatio's for why the two are a guaranteed
    /// non-overlapping Suitability partition, not independently-tuned
    /// scores hoping to compare correctly (the exact bug M58 resume round
    /// 3 already found and fixed once for the prior pairing).
    /// </summary>
    public static IReadOnlyList<IAiStrategy> Strategies { get; } = new IAiStrategy[]
    {
        new DomesticProfitOptimizerStrategy(),
        new RapidExpansionStrategy(),
        new NetworkOptimizerStrategy(),
        new BalancedGrowthStrategy(),
    };

    /// <summary>
    /// M39: unifies initial assignment and later switching into one
    /// path. Re-evaluates only when nothing is assigned yet (current is
    /// null — quarter 1, or a seat that's never run a strategy before)
    /// or quarterNumber starts a new in-game year (4 quarters); otherwise
    /// returns current unchanged. Filters the roster to strategies whose
    /// ApplicableEras contains the current GameEra, picks the highest
    /// Suitability among eligible ones (deterministic Name tie-break).
    /// If nothing is eligible for this era (a real, deliberate gap with
    /// only two strategies, both Starting-only or Starting/MidGame —
    /// nothing yet exists for a LateGame-only slot), keeps current rather
    /// than throwing — the era tag exists so a future milestone can add
    /// era-native strategies into this same roster without touching this
    /// method.
    /// </summary>
    public static IAiStrategy SelectStrategy(IAiStrategy? current, int quarterNumber, StrategyContext context)
    {
        var isYearBoundary = (quarterNumber - 1) % 4 == 0;

        if (current is not null && !isYearBoundary)
        {
            return current;
        }

        var era = GameEras.ForQuarter(quarterNumber);
        var eligible = Strategies
            .Where(s => s.ApplicableEras.Contains(era))
            .OrderByDescending(s => s.Suitability(context))
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        if (eligible.Count == 0)
        {
            return current ?? throw new InvalidOperationException(
                $"No strategy is eligible for {era} and no current strategy exists to fall back to — the roster must cover every era at least once for quarter 1.");
        }

        return eligible[0];
    }

    /// <param name="excludePairKeys">
    /// M55: normalized pair keys (QuarterEngine.NormalizedPairKey) to
    /// skip entirely, regardless of load factor - null (the default)
    /// preserves every pre-M55 call site's exact behavior (including the
    /// now-deleted BaselineAi.DecideOrders, this method's own frozen
    /// legacy caller until it was removed as orphaned code).
    /// RapidExpansionStrategy passes AiToolkit.RecentlyShrunkPairKeys
    /// here, the real fix for the oscillation M43 traced: shrinking a
    /// route mechanically raises its own load factor, so without this a
    /// route shrunk one quarter could rank as "best performing" again the
    /// very next and get grown right back.
    /// </param>
    /// <param name="lastQuarterSeatsCarriedByRoute">
    /// Existing-route-evaluation audit follow-up: real, competitively-
    /// resolved carried seats from last quarter (StrategyContext's own
    /// field of the same name, forwarded unchanged - see its own doc
    /// comment). Null (no real figure for a given pair) falls back to
    /// RouteEvaluator's own solo/theoretical estimate for that pair - see
    /// RealCarriedOrSoloEstimate.
    /// </param>
    /// <remarks>
    /// Existing-route-evaluation audit follow-up: real economics gate the
    /// candidate set here unconditionally - a served route that's
    /// genuinely losing money at its real economics (FareCalculator.
    /// EstimatedProfit &lt; Money.Zero, using lastQuarterSeatsCarriedByRoute
    /// when a real figure exists, the solo estimate otherwise) is excluded
    /// from growth entirely, and survivors are ranked by their own real
    /// load factor rather than RouteEvaluator's fare/competition-agnostic
    /// solo estimate - the same real-data-over-solo-estimate swap
    /// AiToolkit.ApplyManagementToolkit's own fare-raise loop already
    /// makes, for the identical reason: a route genuinely losing a share
    /// fight to a rival can look "full" under the solo estimate while
    /// carrying only a fraction of that in reality. This used to be
    /// optional (an `enableRealProfitabilityGate` parameter, off by
    /// default) while BaselineAi.DecideOrders and RapidExpansionStrategy
    /// still called this method without wanting the gate; both are gone
    /// now - DecideOrders was deleted as orphaned code, and
    /// RapidExpansionStrategy moved to its own demand-ranked
    /// FindBestExistingRouteByDemand instead, specifically because a real
    /// 300-game sweep found the profitability gate a genuine regression
    /// for that strategy's rapid-growth posture (Europe bankruptcy
    /// 12.2%→13.7%, see its own class doc comment) - so profitability-
    /// gated ranking is what every real remaining caller
    /// (NetworkOptimizerStrategy, BalancedGrowthStrategy) always wants,
    /// measured as a true no-op for both via the same sweep. The floor is
    /// deliberately never a cross-pair ranking signal (docs/BASELINE_AI_ROADMAP.md's
    /// own M56 section found ranking growth *candidates* by profit across
    /// different pairs always collapses into favoring the smallest
    /// aircraft available, unbounded) - it only ever excludes a real loser
    /// from being grown at all, the same shape
    /// AiToolkit.ProfitabilityFloor/ApplyMarketingCampaign's own
    /// Profit &lt; Money.Zero filter already use safely.
    /// </remarks>
    /// <param name="regionsNotYetLed">
    /// Win-condition-awareness follow-up (default null, "ablation lever,
    /// off is exactly old behavior") - a real diagnostic found the reason
    /// some regions' own home airlines almost never win despite a real
    /// foothold abroad: nothing in growth selection ever distinguished
    /// "a region I'm present in" from "a region I'm actually leading," so
    /// a foreign route gets opened once and never reinforced past a bare
    /// toehold. When provided, a route touching a region in this set
    /// sorts strictly ahead of one that doesn't - a new leading tier, with
    /// the existing real-load-factor ranking staying the tie-break
    /// *within* that tier, never overridden. Null or empty (every
    /// existing caller) makes the tier a no-op - every route reads as
    /// "not preferred" equally, so the ordering degenerates to exactly
    /// today's behavior. Never influences whether this method returns a
    /// route at all, only which one wins among real candidates already
    /// being ranked for their own reasons - deliberately never a
    /// Suitability signal. See StrategyContext.RegionsLedLastQuarter's
    /// own doc comment for why that distinction matters here specifically.
    /// </param>
    internal static Route? FindBestPerformingRoute(
        IReadOnlyList<Route> currentRoutes, GravityDemandModel demandModel, IReadOnlySet<(string, string)>? excludePairKeys = null,
        IReadOnlyDictionary<(string OriginId, string DestinationId), long>? lastQuarterSeatsCarriedByRoute = null,
        IReadOnlySet<Region>? regionsNotYetLed = null)
    {
        var routeLoads = RouteEvaluator.EvaluateNetwork(currentRoutes, demandModel)
            .Where(l => excludePairKeys is null || !excludePairKeys.Contains(QuarterEngine.NormalizedPairKey(l.Route.Origin, l.Route.Destination)));

        return routeLoads
            .Select(l => (Load: l, RealCarried: RealCarriedOrSoloEstimate(l, lastQuarterSeatsCarriedByRoute)))
            .Where(x => FareCalculator.EstimatedProfit(x.Load.Route, x.RealCarried) >= Money.Zero)
            .OrderByDescending(x => TouchesRegionNotYetLed(x.Load.Route, regionsNotYetLed))
            .ThenByDescending(x => x.Load.SeatsPerQuarter == 0 ? 0.0 : (double)x.RealCarried / x.Load.SeatsPerQuarter)
            .ThenBy(x => x.Load.Route.Origin.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Load.Route.Destination.Id, StringComparer.Ordinal)
            .Select(x => x.Load.Route)
            .FirstOrDefault();
    }

    /// <summary>Win-condition-awareness follow-up: true when either endpoint is a region FindBestPerformingRoute's own caller flagged as not yet led - see that parameter's own doc comment.</summary>
    private static bool TouchesRegionNotYetLed(Route route, IReadOnlySet<Region>? regionsNotYetLed) =>
        regionsNotYetLed is not null && (regionsNotYetLed.Contains(route.Origin.Region) || regionsNotYetLed.Contains(route.Destination.Region));

    /// <summary>
    /// Existing-route-evaluation audit follow-up: a deliberate, small
    /// duplicate of AiToolkit.BestAvailableCarried's own real-data-or-
    /// solo-estimate fallback - not a shared call, because AiToolkit
    /// depends on BaselineAi (not the reverse) and this method must not
    /// introduce the opposite dependency.
    /// </summary>
    private static long RealCarriedOrSoloEstimate(RouteLoad load, IReadOnlyDictionary<(string OriginId, string DestinationId), long>? lastQuarterSeatsCarriedByRoute)
    {
        if (lastQuarterSeatsCarriedByRoute is null)
        {
            return load.PassengersCarried;
        }

        var key = QuarterEngine.NormalizedPairKey(load.Route.Origin, load.Route.Destination);
        return lastQuarterSeatsCarriedByRoute.TryGetValue(key, out var real) ? real : load.PassengersCarried;
    }

    /// <summary>M37: FareCalculator's own default, grossed up by FareRaiseFraction — the standard double-then-round-once cents pattern FareCalculator itself already uses.</summary>
    internal static Money RaisedFare(Route route)
    {
        var cents = (long)Math.Round(FareCalculator.SuggestedFarePerPassenger(route).Cents * (1 + FareRaiseFraction), MidpointRounding.AwayFromZero);
        return new Money(cents);
    }

    /// <summary>M37: FareCalculator's own default, cut by FareCutFraction — same rounding pattern as RaisedFare.</summary>
    internal static Money CutFare(Route route)
    {
        var cents = (long)Math.Round(FareCalculator.SuggestedFarePerPassenger(route).Cents * (1 - FareCutFraction), MidpointRounding.AwayFromZero);
        return new Money(cents);
    }

    /// <summary>M33: true when availableSlotsByCity is null (permissive), or this airline has a slot left at every gateway origin/destination touches.</summary>
    internal static bool HasSlotAtEveryGateway(City origin, City destination, IReadOnlyDictionary<string, int>? availableSlotsByCity, Dictionary<string, int> slotsReservedByCity)
    {
        if (availableSlotsByCity is null)
        {
            return true;
        }

        return new[] { origin, destination }
            .Where(c => c.GateCapacity is not null)
            .All(c => availableSlotsByCity.GetValueOrDefault(c.Id) - slotsReservedByCity.GetValueOrDefault(c.Id) > 0);
    }

    internal static void ReserveSlots(City origin, City destination, Dictionary<string, int> slotsReservedByCity)
    {
        foreach (var city in new[] { origin, destination }.Where(c => c.GateCapacity is not null))
        {
            slotsReservedByCity[city.Id] = slotsReservedByCity.GetValueOrDefault(city.Id) + 1;
        }
    }
}
