namespace AeroNext.Core.Ai;

/// <summary>
/// M55: one airline's own recent past on one route (keyed by
/// QuarterEngine.NormalizedPairKey in StrategyContext.RouteHistoryByPair) —
/// the missing piece M43 and M54 each separately diagnosed and deferred.
/// M43 traced a real oscillation (shrinking a route mechanically raises
/// its own load factor, so a route shrunk one quarter can rank as "best
/// performing" again the next and get grown right back) that a static
/// same-quarter threshold measurably made *worse*, not better — "a route
/// needs to remember it was just shrunk, not just prove itself strong in
/// isolation." M54 built real route closure, measured it firing on
/// nearly as many routes as were ever opened, and pulled it entirely —
/// "the real fix needs cross-quarter memory (has this route stayed
/// unprofitable for more than one quarter, not just this one)." Built
/// fresh in FullGameRunner.cs each quarter from state it already
/// resolves (which CloseRouteOrder actually applied, and real carried
/// seats from CompetitiveMarket.Resolve) — never a value the AI itself
/// writes, so a strategy can only ever read its own real history, not
/// fabricate one.
/// </summary>
/// <param name="LastShrunkQuarter">The quarter this pair's own AircraftCount was last reduced by one (still &gt; 0 afterward) via CloseRouteOrder — null if never.</param>
/// <param name="LastClosedQuarter">The quarter this pair was last closed entirely (AircraftCount reached 0) via CloseRouteOrder — null if never.</param>
/// <param name="ConsecutiveUnprofitableQuarters">How many quarters in a row, ending with the most recent one this airline actually served the pair, FareCalculator.EstimatedProfit read negative — reset to 0 the moment it doesn't.</param>
public sealed record AiRouteHistory(int? LastShrunkQuarter, int? LastClosedQuarter, int ConsecutiveUnprofitableQuarters);
