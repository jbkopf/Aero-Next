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
    string? Winner,
    int? WinQuarter,
    IReadOnlyList<string> FinalRanking)
{
    public bool Success => Violations.Count == 0;
}
