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
    IReadOnlyList<string> FinalRanking)
{
    public bool Success => Violations.Count == 0;
}
