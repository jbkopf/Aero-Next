using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// M39: bundles what were BaselineAi.DecideOrders's 9 loose parameters,
/// plus one no version of this AI has ever had — HomeRegion, needed for
/// "domestic" (DomesticProfitOptimizerStrategy) to mean anything.
/// HomeRegion is deliberately non-nullable: every real caller into the
/// strategy-based API (both AeroNext.Sim's M13 loop and
/// AeroNext.QaHarness/FullGameRunner.cs) always has a real one. Cash/debt
/// stay nullable, matching the permissive-by-default convention every
/// prior milestone's own new parameter followed on the legacy
/// BaselineAi.DecideOrders signature, which this record does not replace
/// — see BaselineAi's own class doc comment.
///
/// M41: PendingNegotiationCityIds, also nullable/permissive-default —
/// null (the pre-M41 default) reproduces the exact bug M41 fixes
/// (re-bidding a gateway with a negotiation already in flight there), so
/// every existing test/caller that doesn't populate it keeps its old
/// behavior unchanged rather than silently gaining the fix. Only
/// FullGameRunner.cs populates it for real today — AeroNext.Sim's M13
/// loop still never builds a real LandingRightsBoard at all (a separate,
/// already-documented gap), so it has nothing real to populate this
/// from.
///
/// M44: HomeCity, deliberately non-nullable like HomeRegion — every real
/// caller already has one (AirlineSeat.StartingCity). Together with
/// CurrentRoutes this defines the network-contiguity valid-origin set
/// (see AiToolkit.ValidOriginCityIds): a new route may only originate
/// from the home city or a city the airline already serves, the same
/// rule QuarterEngine.RunQuarter's own homeCity parameter enforces
/// authoritatively. A strategy has to know this to avoid proposing an
/// order that would predictably be rejected, the same "never attempt
/// what will fail" principle every other StrategyContext field already
/// serves.
/// </summary>
public sealed record StrategyContext(
    IReadOnlyList<Route> CurrentRoutes,
    int ActionCap,
    IReadOnlyList<City> AllCities,
    GravityDemandModel DemandModel,
    IReadOnlyList<AircraftType> EraAvailableTypes,
    AirlineFleet OwnFleet,
    Region HomeRegion,
    City HomeCity,
    IReadOnlyDictionary<string, int>? AvailableSlotsByCity = null,
    Money? AvailableCash = null,
    Money? CurrentDebtBalance = null,
    IReadOnlySet<string>? PendingNegotiationCityIds = null);
