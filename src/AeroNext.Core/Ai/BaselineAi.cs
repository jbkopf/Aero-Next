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
    internal const int AircraftOrderCashComfortMultiplier = 3;

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

    /// <summary>
    /// M33: true when availableSlotsByCity is null (permissive), or this
    /// airline has a slot left at every gateway the route lands at -
    /// origin, destination, and any refuelling stops
    /// (docs/MULTI_STOP_ROUTES_DESIGN.md).
    /// </summary>
    internal static bool HasSlotAtEveryGateway(City origin, City destination, IReadOnlyDictionary<string, int>? availableSlotsByCity, Dictionary<string, int> slotsReservedByCity, StopList stops = default)
    {
        if (availableSlotsByCity is null)
        {
            return true;
        }

        return RoutePath.Cities(origin, stops, destination)
            .Where(c => c.GateCapacity is not null)
            .All(c => availableSlotsByCity.GetValueOrDefault(c.Id) - slotsReservedByCity.GetValueOrDefault(c.Id) > 0);
    }

    internal static void ReserveSlots(City origin, City destination, Dictionary<string, int> slotsReservedByCity, StopList stops = default)
    {
        foreach (var city in RoutePath.Cities(origin, stops, destination).Where(c => c.GateCapacity is not null))
        {
            slotsReservedByCity[city.Id] = slotsReservedByCity.GetValueOrDefault(city.Id) + 1;
        }
    }
}
