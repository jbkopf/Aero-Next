using AeroNext.Core;
using AeroNext.Core.World;

namespace AeroNext.QaHarness.Tests;

/// <summary>
/// Correctness follow-up (docs/OPEN_WORK.md): "Wins" used to be a
/// final-cash comparison, predating M23/M24's own real win condition and
/// never updated once that landed - this file didn't exist before that
/// bug was found, which is exactly how it survived undetected for so
/// long. These tests pin "Wins" to FullGameResult.Winner directly, not
/// final cash, so the regression can't silently return.
/// </summary>
public class RegionalOutcomeReportTests
{
    private static FullGameResult MakeResult(
        IReadOnlyDictionary<string, Region> homeRegionByName,
        IReadOnlyDictionary<string, Money> finalCash,
        string? winner,
        IReadOnlyDictionary<string, int?>? bankruptcyQuarterByName = null) =>
        new(
            GameIndex: 0, ConfigSeed: 0, CampaignSeed: 0, HumanCount: 0, QuartersRun: 1,
            Violations: Array.Empty<GameViolation>(),
            FinalCash: finalCash,
            BankruptcyQuarter: bankruptcyQuarterByName ?? homeRegionByName.Keys.ToDictionary(n => n, _ => (int?)null),
            HomeRegionByName: homeRegionByName,
            StartingCityIdByName: homeRegionByName.Keys.ToDictionary(n => n, _ => "x"),
            Winner: winner,
            WinQuarter: winner is null ? null : 1,
            FinalRanking: homeRegionByName.Keys.ToList());

    [Fact]
    public void Aggregate_WinsFollowsTheRealWinner_NotWhoeverEndedRichest()
    {
        // "Poor" wins the real game (WinConditionChecker's own dominance
        // condition, reflected here as FullGameResult.Winner) despite
        // "Rich" ending with more cash - the exact scenario the old
        // final-cash-comparison bug would have gotten backwards.
        var homeRegionByName = new Dictionary<string, Region>
        {
            ["Rich"] = Region.EastAsia,
            ["Poor"] = Region.Europe,
        };
        var finalCash = new Dictionary<string, Money>
        {
            ["Rich"] = Money.FromDollars(1_000_000_000),
            ["Poor"] = Money.FromDollars(1_000_000),
        };
        var result = MakeResult(homeRegionByName, finalCash, winner: "Poor");

        var outcomes = RegionalOutcomeReport.Aggregate(new[] { result });

        Assert.Equal(1, outcomes.Single(o => o.Region == Region.Europe).Wins);
        Assert.Equal(0, outcomes.Single(o => o.Region == Region.EastAsia).Wins);
    }

    [Fact]
    public void Aggregate_NoWinner_ContributesZeroWinsEverywhere()
    {
        var homeRegionByName = new Dictionary<string, Region>
        {
            ["A"] = Region.NorthAmerica,
            ["B"] = Region.SouthAmerica,
        };
        var finalCash = new Dictionary<string, Money>
        {
            ["A"] = Money.FromDollars(500_000),
            ["B"] = Money.FromDollars(500_000),
        };
        var result = MakeResult(homeRegionByName, finalCash, winner: null);

        var outcomes = RegionalOutcomeReport.Aggregate(new[] { result });

        Assert.All(outcomes, o => Assert.Equal(0, o.Wins));
    }

    [Fact]
    public void Aggregate_MultipleGames_SumsWinsAcrossGames()
    {
        var gameOne = MakeResult(
            new Dictionary<string, Region> { ["A"] = Region.NorthAmerica, ["B"] = Region.Europe },
            new Dictionary<string, Money> { ["A"] = Money.FromDollars(1), ["B"] = Money.FromDollars(2) },
            winner: "A");
        var gameTwo = MakeResult(
            new Dictionary<string, Region> { ["C"] = Region.NorthAmerica, ["D"] = Region.Europe },
            new Dictionary<string, Money> { ["C"] = Money.FromDollars(1), ["D"] = Money.FromDollars(2) },
            winner: "C");

        var outcomes = RegionalOutcomeReport.Aggregate(new[] { gameOne, gameTwo });

        Assert.Equal(2, outcomes.Single(o => o.Region == Region.NorthAmerica).Wins);
        Assert.Equal(0, outcomes.Single(o => o.Region == Region.Europe).Wins);
    }

    [Fact]
    public void Aggregate_SomeBankruptcies_AveragesTheirQuartersSeparatelyFromWins()
    {
        var homeRegionByName = new Dictionary<string, Region>
        {
            ["A"] = Region.NorthAmerica,
            ["B"] = Region.NorthAmerica,
        };
        var finalCash = new Dictionary<string, Money>
        {
            ["A"] = Money.FromDollars(0),
            ["B"] = Money.FromDollars(1_000_000),
        };
        var bankruptcyQuarterByName = new Dictionary<string, int?>
        {
            ["A"] = 20,
            ["B"] = null,
        };
        var result = MakeResult(homeRegionByName, finalCash, winner: "B", bankruptcyQuarterByName);

        var outcome = RegionalOutcomeReport.Aggregate(new[] { result }).Single(o => o.Region == Region.NorthAmerica);

        Assert.Equal(1, outcome.BankruptcyCount);
        Assert.Equal(20.0, outcome.AverageBankruptcyQuarter);
        Assert.Equal(1, outcome.Wins);
    }
}
