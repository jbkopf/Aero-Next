using AeroNext.Core;
using AeroNext.Core.World;

namespace AeroNext.QaHarness;

public sealed record GameViolation(int Quarter, string AirlineName, string Description);

public sealed record FullGameResult(
    int GameIndex,
    int ConfigSeed,
    int CampaignSeed,
    int HumanCount,
    int QuartersRun,
    IReadOnlyList<GameViolation> Violations,
    IReadOnlyDictionary<string, Money> FinalCash,
    IReadOnlyDictionary<string, int?> BankruptcyQuarter,
    IReadOnlyDictionary<string, Region> HomeRegionByName,
    // Each airline's quarter-1 starting city id. Bankruptcy and win share
    // vary sharply by starting city within a region (e.g. AustralAsia:
    // Papeete/Auckland ~100% bankrupt, Sydney/Melbourne ~0%), so
    // region-level aggregates hide the real signal - this lets a sweep
    // break outcomes down per starting city.
    IReadOnlyDictionary<string, string> StartingCityIdByName,
    string? Winner,
    int? WinQuarter,
    IReadOnlyList<string> FinalRanking,
    // How the winner actually won - the M65 AustralAsia investigation's own
    // "pillar check": a rising win share is only healthy if it comes from
    // real WinConditionChecker.RequiredRegionCount-region dominance, not
    // sole-survival or (weakest) an end-of-campaign CampaignRanking
    // tie-break. WinnerRegionsDominated is the count of regions the winner
    // held with a >=RequiredStreakQuarters streak when the game ended (0
    // when Winner is null); SolventAtEnd is how many airlines were still
    // solvent at that point (1 => the win path was sole-survivor, not
    // necessarily dominance). Classify a game's win as dominance when
    // WinnerRegionsDominated >= WinConditionChecker.RequiredRegionCount,
    // sole-survivor when that's false and SolventAtEnd <= 1, else
    // end-of-campaign ranking.
    int WinnerRegionsDominated = 0,
    int SolventAtEnd = 0,
    // Each airline's own gateway-negotiation spend/count over its first 20
    // quarters. docs/OPEN_WORK.md's filler-seat investigation traced a real
    // bankrupt trajectory and found BaselineAi.NegotiationBidAmount (a flat
    // $2M) plus AiToolkit's FindBestUnservedGateway (globally richest
    // untouched gateway, region-blind, no pending-bid throttle) drives
    // *every* airline into $24-33M of speculative gateway bids in this
    // window regardless of home region - these two fields let a sweep
    // quantify that pattern instead of re-deriving it from a one-off trace
    // each time.
    IReadOnlyDictionary<string, long>? NegotiationSpendCentsFirst20QByName = null,
    IReadOnlyDictionary<string, int>? NegotiationBidCountFirst20QByName = null,
    // Total AdjustRouteOrders (aircraft type swaps) applied across every
    // airline this game - lets a sweep, or a test, confirm the type-swap
    // mechanic actually fires in real play rather than only in isolation.
    int TypeSwapsApplied = 0)
{
    public bool Success => Violations.Count == 0;
}
