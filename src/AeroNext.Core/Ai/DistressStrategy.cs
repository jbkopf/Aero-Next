using AeroNext.Core.Turns;

namespace AeroNext.Core.Ai;

/// <summary>
/// Bankruptcy-conditions follow-up: the emergency posture an airline runs
/// for exactly as long as StrategyContext.InDistress is true — the
/// one-quarter grace window FinancialResolver.Resolve gives cash going
/// negative before it actually freezes (see FinancialState's own class
/// doc comment). Selected by direct dispatch on that flag at the one real
/// call site (FullGameRunner.cs), not through BaselineAi.Strategies'
/// normal Suitability-based roster scan — BaselineAi.SelectStrategy keeps
/// ticking its own year-boundary rotation underneath, completely
/// oblivious to distress, so the moment InDistress clears the airline
/// resumes whatever that rotation already independently arrived at,
/// rather than a fresh Suitability re-roll landing on something
/// different. This class is never added to that roster and never needs
/// to be — Name/ApplicableEras/Suitability exist only to satisfy
/// IAiStrategy's contract.
///
/// INVESTIGATION (real 300-game sweeps, not assumed): the original design
/// also suppressed all growth and retired every idle unit unconditionally.
/// Measured against the grace-period fix alone (NorthAmerica 0.4%, Europe
/// 10.7%, EastAsia 4.5%, SouthWestAsia 2.9%, AustralAsia 37.1%,
/// SouthAmerica 12.2% bankrupt), that version made things *worse* in two
/// regions (Europe 14.7%, EastAsia 6.2%) for two separate, now-isolated
/// reasons:
///
/// 1. This class's own call to AiToolkit.ApplyManagementToolkit passed
///    enableSustainedSingleAircraftClosure: false (every real strategy's
///    own default except NetworkOptimizerStrategy's). NetworkOptimizerStrategy
///    is the assigned strategy for 72% of all real distress events (367 of
///    507 in the same sweep, instrumented directly) - substituting this
///    class in for even one quarter silently downgraded away its own
///    patient single-aircraft-route closure capability, for exactly the
///    quarter an airline is most vulnerable. Passing true here instead
///    fully closed EastAsia's entire regression (6.2%->4.5%, an exact
///    match) with zero effect anywhere else.
/// 2. Retiring every idle unit unconditionally (not just
///    ApplyManagementToolkit's own narrower "genuinely stranded" check)
///    cost real value in Europe specifically - some of those units really
///    were useful again once solvent, and losing them cost more than the
///    resale proceeds gained. Disabling it recovered most (not all) of
///    Europe's own regression (14.7%->11.7%). The remaining ~1 point
///    (still above the 10.7% baseline) is the one real, irreducible cost
///    of suppressing growth at all during the grace quarter - direct
///    instrumentation of the grace-period-only baseline found growth
///    attempts during distress are rare (14 of 507 events, 2.8%) and
///    correlate with *worse*, not better, outcomes (64.3% recovery vs
///    80.1% when the normal strategy didn't attempt growth) - so
///    suppressing growth isn't the villain either; it's a small, mostly
///    separate cost, not the double-digit regression it first looked
///    like.
///
/// Net result, this version (closure flag fixed, aggressive retirement
/// dropped): NorthAmerica/SouthWestAsia/SouthAmerica unaffected, EastAsia
/// exactly matches baseline, AustralAsia a noise-level point better
/// (37.1%->36.1%), Europe a real but small point worse (10.7%->11.7%).
/// Essentially a wash in aggregate, not the clear win the grace-period fix
/// itself was - reported honestly rather than shipped as a clean success.
///
/// The entire remaining behavioral difference from every normal strategy
/// is an absence, not a new mechanism: no growth phase at all (no
/// RouteOrder, LeaseAircraftOrder, AircraftOrder, NegotiationOrder,
/// AllianceCourtOrder). Everything else is AiToolkit.ApplyManagementToolkit,
/// reused wholesale with enableSustainedSingleAircraftClosure: true -
/// MaintainCashRunway's borrow-rescue, fare cuts and shrink on genuinely
/// unprofitable routes, cash-gated debt paydown, cash-gated executive
/// hiring, and ApplyManagementToolkit's own narrower stranded-unit
/// retirement (idle, no served route uses its type, no unserved pair
/// worth opening needs it either) - the same call every real strategy
/// already makes at the end of its own DecideOrders, just with that one
/// flag corrected.
///
/// M67 (docs/AI_AIRCRAFT_TYPE_SWAP_DESIGN.md): extends M66's
/// AdjustRouteOrder awareness here too, per the user's own direct
/// follow-up request, at a deliberately narrower shape than
/// NetworkOptimizerStrategy/BalancedGrowthStrategy: enableReactiveTypeSwap
/// only (never proactive - a distressed airline shouldn't be disrupting
/// an already-healthy route to chase a marginal efficiency gain), and
/// allowTypeSwapLeaseOrBuy: false (never a fresh lease/purchase
/// specifically to enable a swap - real fresh capital while already in
/// financial trouble is its own decision, not a default). A real
/// 300-game ablation confirmed this shape is clean and mildly beneficial
/// (AustralAsia bankruptcy 7→5 with it enabled, no other region worse).
/// </summary>
public sealed class DistressStrategy : IAiStrategy
{
    /// <summary>The one instance every real caller should use — this strategy carries no per-airline state of its own, so there's nothing a fresh instance would ever buy over reusing one.</summary>
    public static readonly DistressStrategy Instance = new();

    public string Name => "Distress";

    public IReadOnlySet<GameEra> ApplicableEras { get; } = new HashSet<GameEra> { GameEra.Starting, GameEra.MidGame, GameEra.LateGame };

    /// <summary>
    /// Never actually consulted — this strategy is selected by direct
    /// dispatch on StrategyContext.InDistress (see this class's own doc
    /// comment), never through BaselineAi.SelectStrategy's Suitability
    /// comparison. NegativeInfinity is a defensive, safe-by-construction
    /// answer: if this were ever accidentally added to BaselineAi.Strategies,
    /// it could never win a real comparison and silently hijack a normal
    /// quarter's selection.
    /// </summary>
    public double Suitability(StrategyContext context) => double.NegativeInfinity;

    public IReadOnlyList<QuarterOrder> DecideOrders(StrategyContext context)
    {
        var orders = new List<QuarterOrder>();
        var reservedUnitIds = new HashSet<int>();

        // No growth phase at all - see this class's own doc comment.
        // enableSustainedSingleAircraftClosure: true matches
        // NetworkOptimizerStrategy's own choice (see the doc comment's
        // real evidence for why every other strategy's own false is wrong
        // to inherit here).
        AiToolkit.ApplyManagementToolkit(context, orders, reservedUnitIds, spentBeforeToolkit: 0, enableSustainedSingleAircraftClosure: true,
            enableReactiveTypeSwap: true, enableProactiveTypeSwap: false, allowTypeSwapLeaseOrBuy: false);

        return orders;
    }
}
