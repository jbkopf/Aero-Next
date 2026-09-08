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
///
/// M45: two more nullable/permissive-default fields, mirroring
/// AvailableSlotsByCity's own convention. CityWideSlotsRemaining is the
/// ownerless, shared capacity balance for non-gateway AutoCapacity
/// cities (see AiToolkit.ComputeCityWideSlotsRemaining) — the same value
/// for every airline asking, unlike AvailableSlotsByCity's per-airline
/// balance. IdleContestableSlotsByCity surfaces gateways with idle,
/// unused slots another airline has held past LandingRightsBoard's
/// IdleExpiryThreshold, so AI strategies can actually reach the new
/// idle-then-recontestable mechanic (see AiToolkit.FindBestIdleContestableGateway)
/// rather than it only being reachable by a human player bidding by hand.
///
/// M52: RivalCountByServedRoute, also nullable/permissive-default — null
/// means "no visibility into rivals," the same convention every prior
/// field here already follows. Keyed by QuarterEngine.NormalizedPairKey,
/// scoped to only this airline's own CurrentRoutes (not every pair in
/// the world) — the missing prerequisite for a strategy to know a
/// marketing campaign would have any visible effect at all:
/// CompetitiveMarket.Resolve's own boost only applies where a pair is
/// genuinely contested (competitors.Count > 1, M47's own design), and no
/// prior StrategyContext field told a strategy which of its own routes
/// that's true for. See AiToolkit.ApplyMarketingCampaign.
///
/// M54: LastQuarterSeatsCarriedByRoute, the first field here that's
/// *not* a same-quarter snapshot — real, competitively-resolved carried
/// seats from the *previous* quarter's own CompetitiveMarket.Resolve
/// (CompetitiveRouteUsage.SeatsUsed, the same real figure
/// FinancialResolver.ComputeRevenue itself uses), one quarter lagged.
/// Nullable/permissive-default like everything else here — null (or a
/// pair missing from it) means "no real data available," and every
/// consumer falls back to RouteEvaluator's own solo-carrier estimate,
/// which is already accurate for an uncontested route (no rival to split
/// demand with) and only genuinely needed for a contested one. See
/// FareCalculator.EstimatedProfit and AiToolkit.ApplyMarketingCampaign.
///
/// M55: QuarterNumber — every real caller (FullGameRunner.cs) always
/// knows and always passes this; it carries a default (1, not null,
/// since it's a plain int) purely so the many existing positional test
/// call sites up through HomeCity don't all need updating, not because
/// it's meant to be treated as optional the way every field after it
/// genuinely is. Needed for cooldown math: RouteHistoryByPair's own
/// timestamps only mean something relative to "how long ago." See
/// AiToolkit.RecentlyShrunkPairKeys/RecentlyClosedPairKeys.
///
/// M55: RouteHistoryByPair, nullable/permissive-default like everything
/// else, scoped to this airline's own pairs. The missing piece M43 and
/// M54 each separately diagnosed and deferred — see AiRouteHistory's own
/// doc comment for the full reasoning.
///
/// M58 (added, then removed): OwnRegionStreaks briefly existed here to
/// back a "close to winning" strategy-selection signal
/// (NetworkOptimizerStrategy's own original third Suitability trigger).
/// A real 300-game sweep found that signal dominated the strategy's own
/// selection 9-30x over the "network has real trouble" signal it was
/// meant to complement, and drove NorthAmerica's bankruptcy rate from
/// 20.8% to 65.0% — the opposite of the strategy's actual purpose. Removed
/// entirely, confirmed via direct grep to have zero other consumers
/// anywhere in the codebase before deletion — see
/// docs/BASELINE_AI_ROADMAP.md's M58 section for the full evidence chain.
///
/// Bankruptcy-conditions follow-up: InDistress mirrors FinancialState's
/// own field of the same name, defaults false like every other field
/// after HomeCity. Not consulted by any real strategy's own decision
/// logic or by BaselineAi.SelectStrategy's own rotation — it's read only
/// at the one real dispatch site (FullGameRunner.cs) to decide whether
/// DistressStrategy overrides whatever SelectStrategy's own rotation
/// would otherwise run this exact quarter, orthogonally to that
/// rotation's own year-boundary cadence. See DistressStrategy's own class
/// doc comment for the full design.
///
/// BalancedGrowthStrategy follow-up: RivalPresenceByPairKey/RivalPresenceByRegion
/// close a real, previously-nonexistent gap — no prior field told a
/// strategy anything about a pair or region it doesn't itself serve.
/// RivalCountByServedRoute above stays scoped to only this airline's own
/// CurrentRoutes (M52's own convention); these two are global, keyed
/// across every airline's own current routes, computed once per quarter
/// in FullGameRunner.cs from the same servingAirlineNamesByPairKey data
/// RivalCountByServedRoute is already narrowed from. Both nullable,
/// permissive-default like everything else here — null means "no
/// visibility," every candidate treated as uncontested, not "assume
/// zero rivals everywhere" silently baked into a non-nullable default.
///
/// Win-condition-awareness follow-up: RegionsLedLastQuarter - which of
/// HomeRegions.All this airline held sole leadership of (a real streak
/// >= 1 in RegionalDominanceState, the same state WinConditionChecker
/// itself reads) as of last quarter's own resolution. One-quarter
/// lagged like every other real-competitive field above, computed from
/// data both real loops (FullGameRunner.cs, AeroNext.Sim/Program.cs)
/// already track for the win-condition check itself - no new tracking
/// infrastructure, just read one step earlier. Nullable, permissive-
/// default: null means "no visibility," read as "leads nothing," the
/// same safe/conservative floor every field here already uses. Used
/// only as a tie-break *within* a search a strategy already runs for
/// its own reasons (see BaselineAi.FindBestPerformingRoute's own
/// regionsNotYetLed parameter) - deliberately never wired into any
/// Suitability score. A near-identical "close to winning" signal was
/// tried once before, as a third Suitability trigger
/// (NetworkOptimizerStrategy's own original design) - it dominated
/// strategy *selection* 9-30x over the signal it was meant to
/// complement and drove NorthAmerica's bankruptcy rate from 20.8% to
/// 65.0%, removed entirely (see this file's own M58 note above). This
/// field must never repeat that path.
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
    IReadOnlySet<string>? PendingNegotiationCityIds = null,
    IReadOnlyDictionary<string, int>? CityWideSlotsRemaining = null,
    IReadOnlyDictionary<string, int>? IdleContestableSlotsByCity = null,
    IReadOnlyDictionary<(string OriginId, string DestinationId), int>? RivalCountByServedRoute = null,
    IReadOnlyDictionary<(string OriginId, string DestinationId), long>? LastQuarterSeatsCarriedByRoute = null,
    int QuarterNumber = 1,
    IReadOnlyDictionary<(string OriginId, string DestinationId), AiRouteHistory>? RouteHistoryByPair = null,
    bool InDistress = false,
    IReadOnlyDictionary<(string OriginId, string DestinationId), int>? RivalPresenceByPairKey = null,
    IReadOnlyDictionary<Region, int>? RivalPresenceByRegion = null,
    IReadOnlySet<Region>? RegionsLedLastQuarter = null);
