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
/// </summary>
public sealed record StrategyContext(
    IReadOnlyList<Route> CurrentRoutes,
    int ActionCap,
    IReadOnlyList<City> AllCities,
    GravityDemandModel DemandModel,
    IReadOnlyList<AircraftType> EraAvailableTypes,
    AirlineFleet OwnFleet,
    Region HomeRegion,
    IReadOnlyDictionary<string, int>? AvailableSlotsByCity = null,
    Money? AvailableCash = null,
    Money? CurrentDebtBalance = null);
