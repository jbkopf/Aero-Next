using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Setup;
using AeroNext.Core.World;
using AeroNext.QaHarness;

namespace AeroNext.QaHarness.Tests;

public class FullGameRunnerCampaignTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly IReadOnlyList<City> Cities = CityCatalog.LoadFile(Path.Combine(DataDir, "cities.json"));
    private static readonly IReadOnlyList<AircraftType> Fleet = AircraftCatalog.LoadFile(Path.Combine(DataDir, "aircraft.json"));
    private static readonly IReadOnlyList<DemandGrowthPoint> Growth = DemandGrowthCatalog.LoadCampaignSchedule(DataDir);
    private static readonly IReadOnlyList<TradeRestriction> Restrictions = TradeRestrictionCatalog.LoadFile(
        Path.Combine(DataDir, "trade-restrictions.json"), Cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));

    // Mirrors AeroNext.QaHarness/Program.cs's own sweep seating exactly.
    private static IReadOnlyList<AirlineSeat> SweepSeats(int configSeed)
    {
        var configRng = new SimRng(configSeed);
        return GameSetup.CreateSeats(GameConfigGenerator.Generate(configRng, Cities), Cities, configRng, fillerWeightExponent: 0.0);
    }

    private static FullGameResult Run(IReadOnlyList<AirlineSeat> seats, int seed, int quarters) =>
        FullGameRunner.Run(seed, seed, seed + 500_000, quarters, seats, Cities, Fleet, Growth, tradeRestrictions: Restrictions);

    [Fact]
    public void SweepConfiguredCampaign_EndsEarlyOnRealDominance()
    {
        // Seed 11 was picked by searching sweep-configured seeds for a game
        // that ends the way most real sweep games do - a dominance win
        // breaking out of the quarter loop early (here at quarter 6). If AI
        // tuning changes stop this seed producing that shape, re-pick a seed
        // rather than weakening the assertions.
        const int seed = 11;
        var result = Run(SweepSeats(seed), seed, quarters: 244);

        Assert.True(result.Success, string.Join("; ", result.Violations.Select(v => $"Q{v.Quarter} {v.AirlineName}: {v.Description}")));
        Assert.NotNull(result.Winner);
        Assert.NotNull(result.WinQuarter);
        Assert.True(result.WinQuarter < 244, "Expected an early win, not an end-of-campaign ranking.");
        Assert.Equal(result.WinQuarter, result.QuartersRun);
        Assert.True(result.WinnerRegionsDominated >= WinConditionChecker.RequiredRegionCount);
    }

    [Fact]
    public void SweepConfiguredCampaign_BankruptAirlineStaysOut_ForTheRestOfTheGame()
    {
        // Hard difficulty, seed 3: a Papeete-based airline goes bankrupt early
        // (quarter 13) and the game runs on long afterwards, so every later
        // quarter exercises the bankrupt-airline path and its invariant
        // checks (bankruptcy is sticky, routes and action cap stay frozen) -
        // Success would be false if any of them tripped. Bankruptcies became
        // rare enough (after the AI stopped putting oversized aircraft on thin
        // routes) that no Normal-difficulty seed in 1-100 had one early
        // enough to be followed by more play. Re-pick rather than weaken.
        const int seed = 3;
        var result = FullGameRunner.Run(seed, seed, seed + 500_000, 244, SweepSeats(seed), Cities, Fleet, Growth,
            difficulty: GameDifficulty.Hard, tradeRestrictions: Restrictions);

        Assert.True(result.Success, string.Join("; ", result.Violations.Select(v => $"Q{v.Quarter} {v.AirlineName}: {v.Description}")));
        var bankrupt = result.BankruptcyQuarter.Where(kv => kv.Value is not null).ToList();
        Assert.NotEmpty(bankrupt);
        Assert.All(bankrupt, kv =>
        {
            Assert.NotEqual(result.Winner, kv.Key);
            Assert.True(result.QuartersRun - kv.Value >= 20, $"{kv.Key} went bankrupt at Q{kv.Value}; expected plenty of play afterwards (game ran {result.QuartersRun} quarters).");
        });
        Assert.Equal(4 - bankrupt.Count, result.SolventAtEnd);
    }

    [Fact]
    public void SweepConfiguredCampaign_AppliesAircraftTypeSwaps_AndStaysInvariantClean()
    {
        // Seed 18 was picked by searching sweep-configured seeds for a game
        // in which NetworkOptimizerStrategy really applies AdjustRouteOrders
        // (15 of them), so FullGameRunner's own swap-history stamping runs
        // inside a real campaign. Re-picked after the marketing redesign
        // changed the previous seed's (14) game enough that it stopped
        // swapping - the test caught that, as designed. Same
        // re-pick-don't-weaken rule as above.
        const int seed = 18;
        var result = Run(SweepSeats(seed), seed, quarters: 244);

        Assert.True(result.Success, string.Join("; ", result.Violations.Select(v => $"Q{v.Quarter} {v.AirlineName}: {v.Description}")));
        Assert.True(result.TypeSwapsApplied > 0, "Expected this seed's game to apply at least one aircraft type swap.");
    }

    [Fact]
    public void SweepConfiguredCampaign_AppliesMarketingCampaigns()
    {
        // Marketing was effectively dormant in real play before its
        // value-based redesign (22 campaigns in a whole 300-game sweep).
        // Seed 3 is a short game (a dominance win by quarter 12) in which
        // campaigns really run - a real-game check that the AI uses the
        // lever at all, not just in AiToolkit's own unit tests.
        const int seed = 3;
        var result = Run(SweepSeats(seed), seed, quarters: 244);

        Assert.True(result.Success, string.Join("; ", result.Violations.Select(v => $"Q{v.Quarter} {v.AirlineName}: {v.Description}")));
        Assert.True(result.RouteCampaignsApplied > 0, "Expected at least one route-targeted marketing campaign.");
        Assert.True(result.NetworkCampaignsApplied > 0, "Expected at least one network-wide marketing campaign.");
    }

    [Fact]
    public void Run_FewerThanFourSeats_ReportsASeatViolation()
    {
        var result = Run(SweepSeats(1).Take(3).ToList(), seed: 1, quarters: 1);

        Assert.False(result.Success);
        Assert.Contains(result.Violations, v => v.Quarter == 0 && v.Description.Contains("4 distinct airline names"));
    }

    [Fact]
    public void Run_TwoSeatsShareAStartingCity_ReportsASeatViolation()
    {
        var seats = SweepSeats(1).ToList();
        seats[1] = seats[1] with { StartingCity = seats[0].StartingCity };

        var result = Run(seats, seed: 1, quarters: 1);

        Assert.False(result.Success);
        Assert.Contains(result.Violations, v => v.Quarter == 0 && v.Description.Contains("distinct starting cities"));
    }

    [Fact]
    public void Run_DuplicateAirlineNames_ReportsASeatViolation()
    {
        var seats = SweepSeats(1).ToList();
        seats[1] = seats[1] with { AirlineName = seats[0].AirlineName };

        var result = Run(seats, seed: 1, quarters: 1);

        Assert.False(result.Success);
        Assert.Contains(result.Violations, v => v.Quarter == 0 && v.Description.Contains("4 distinct airline names"));
    }
}
