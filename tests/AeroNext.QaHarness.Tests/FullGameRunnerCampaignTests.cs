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
    public void SweepConfiguredCampaign_EndsEarlyOnRealDominance_AfterABankruptcy()
    {
        // Seed 33 was picked by searching sweep-configured seeds for a game
        // that exercises the path nearly every real sweep game ends on - an
        // early dominance win breaking out of the quarter loop - with an
        // airline going bankrupt well beforehand, so the bankrupt-airline
        // quarters (frozen routes, sticky bankruptcy) run too. If AI tuning
        // changes stop this seed producing that shape, re-pick a seed rather
        // than weakening the assertions.
        const int seed = 33;
        var result = Run(SweepSeats(seed), seed, quarters: 244);

        Assert.True(result.Success, string.Join("; ", result.Violations.Select(v => $"Q{v.Quarter} {v.AirlineName}: {v.Description}")));
        Assert.NotNull(result.Winner);
        Assert.NotNull(result.WinQuarter);
        Assert.True(result.WinQuarter < 244, "Expected an early win, not an end-of-campaign ranking.");
        Assert.Equal(result.WinQuarter, result.QuartersRun);
        Assert.True(result.WinnerRegionsDominated >= WinConditionChecker.RequiredRegionCount);

        var bankrupt = result.BankruptcyQuarter.Where(kv => kv.Value is not null).ToList();
        Assert.NotEmpty(bankrupt);
        Assert.All(bankrupt, kv =>
        {
            Assert.NotEqual(result.Winner, kv.Key);
            Assert.True(kv.Value < result.WinQuarter, $"{kv.Key} went bankrupt at Q{kv.Value}, expected before the Q{result.WinQuarter} win.");
        });
        Assert.Equal(4 - bankrupt.Count, result.SolventAtEnd);
    }

    [Fact]
    public void SweepConfiguredCampaign_AppliesAircraftTypeSwaps_AndStaysInvariantClean()
    {
        // Seed 14 was picked by searching sweep-configured seeds for a short
        // game in which NetworkOptimizerStrategy really applies
        // AdjustRouteOrders, so FullGameRunner's own swap-history stamping
        // runs inside a real campaign. Same re-pick-don't-weaken rule as above.
        const int seed = 14;
        var result = Run(SweepSeats(seed), seed, quarters: 244);

        Assert.True(result.Success, string.Join("; ", result.Violations.Select(v => $"Q{v.Quarter} {v.AirlineName}: {v.Description}")));
        Assert.True(result.TypeSwapsApplied > 0, "Expected this seed's game to apply at least one aircraft type swap.");
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
