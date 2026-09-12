using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Setup;
using AeroNext.Core.World;
using AeroNext.QaHarness;

namespace AeroNext.QaHarness.Tests;

/// <summary>
/// The byte-identical guarantee: <see cref="FullGameRunner.Run"/> with no
/// tuning, with <see cref="ExperimentTuning.Baseline"/>, and with a
/// default-constructed <see cref="ExperimentTuning"/> all produce the same
/// campaign — and a live knob visibly moves the result it is scoped to.
/// </summary>
public class FullGameRunnerTuningTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly IReadOnlyList<City> Cities = CityCatalog.LoadFile(Path.Combine(DataDir, "cities.json"));
    private static readonly IReadOnlyList<AircraftType> Fleet = AircraftCatalog.LoadFile(Path.Combine(DataDir, "aircraft.json"));
    private static readonly IReadOnlyList<DemandGrowthPoint> Growth = DemandGrowthCatalog.LoadCampaignSchedule(DataDir);

    private const int Quarters = 16;

    private static IReadOnlyList<AirlineSeat> Seats(int configSeed) =>
        GameSetup.CreateSeats(GameConfigGenerator.Generate(new SimRng(configSeed), Cities), Cities);

    private static FullGameResult Run(int seed, ExperimentTuning? tuning) =>
        FullGameRunner.Run(seed, seed, seed + 500_000, Quarters, Seats(seed), Cities, Fleet, Growth, tuning);

    private static void AssertSameOutcome(FullGameResult a, FullGameResult b)
    {
        Assert.Equal(a.Winner, b.Winner);
        Assert.Equal(a.WinQuarter, b.WinQuarter);
        Assert.Equal(a.QuartersRun, b.QuartersRun);
        Assert.Equal(a.Violations.Count, b.Violations.Count);
        Assert.Equal(a.FinalRanking, b.FinalRanking);
        Assert.Equal(a.FinalCash.OrderBy(kv => kv.Key), b.FinalCash.OrderBy(kv => kv.Key));
        Assert.Equal(a.BankruptcyQuarter.OrderBy(kv => kv.Key), b.BankruptcyQuarter.OrderBy(kv => kv.Key));
        Assert.Equal(a.StartingCityIdByName.OrderBy(kv => kv.Key), b.StartingCityIdByName.OrderBy(kv => kv.Key));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void Run_NullTuning_BaselineTuning_AndDefaultInstance_AllMatch(int seed)
    {
        var noTuning = Run(seed, null);
        var baseline = Run(seed, ExperimentTuning.Baseline);
        var freshDefault = Run(seed, new ExperimentTuning());

        AssertSameOutcome(noTuning, baseline);
        AssertSameOutcome(noTuning, freshDefault);
    }

    [Fact]
    public void Run_AustralAsiaCashFloor_IsThreadedThrough_AndScopedToAustralAsia()
    {
        // A seed whose config seats at least one AustralAsia airline.
        var seed = Enumerable.Range(1, 60).First(s =>
            Seats(s).Any(seat => seat.HomeRegion == Region.AustralAsia));
        var seats = Seats(seed);

        var untuned = FullGameRunner.Run(seed, seed, seed + 500_000, 1, seats, Cities, Fleet, Growth, null);
        var floored = FullGameRunner.Run(seed, seed, seed + 500_000, 1, seats, Cities, Fleet, Growth,
            new ExperimentTuning { AustralAsiaCashFloorFactor = 1.117 }); // ~$22.3M vs the ~$15.1M default

        foreach (var seat in seats)
        {
            var moved = floored.FinalCash[seat.AirlineName].Cents > untuned.FinalCash[seat.AirlineName].Cents;
            if (seat.HomeRegion == Region.AustralAsia)
            {
                Assert.True(moved, $"{seat.AirlineName} (AustralAsia) starting cash should have risen under the floor.");
            }
            else
            {
                Assert.Equal(untuned.FinalCash[seat.AirlineName], floored.FinalCash[seat.AirlineName]);
            }
        }
    }

    [Fact]
    public void Baseline_ToString_ListsItsKnobs()
    {
        var text = ExperimentTuning.Baseline.ToString();
        Assert.Contains(nameof(ExperimentTuning.AustralAsiaAircraft), text);
        Assert.Contains(nameof(ExperimentTuning.AustralAsiaCashFloorFactor), text);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void NoWinnerYet_ImpliesZeroRegionsDominated(int seed)
    {
        // 16 quarters is far too short for any real win path - the M65
        // win-classification fields should read as their null-winner defaults.
        var result = Run(seed, null);

        if (result.Winner is null)
        {
            Assert.Equal(0, result.WinnerRegionsDominated);
        }
    }

    [Fact]
    public void RealWinner_HasAValidRegionCount_AndAtLeastOneSolventAirline()
    {
        // A full-length campaign, long enough that a real win path
        // (dominance, sole-survivor, or end-of-campaign ranking) is reached.
        var seed = 1;
        var result = FullGameRunner.Run(seed, seed, seed + 500_000, 244, Seats(seed), Cities, Fleet, Growth, null);

        Assert.NotNull(result.Winner);
        Assert.InRange(result.WinnerRegionsDominated, 0, HomeRegions.All.Count);
        Assert.True(result.SolventAtEnd >= 1, "A declared winner implies at least one solvent airline when the game ended.");
    }

    [Fact]
    public void NegotiationSpendAndBidCount_ArePopulated_ForEverySeat_OverTheFirst20Quarters()
    {
        // docs/OPEN_WORK.md's filler-seat investigation: every airline with
        // enough cash bids on unserved gateways every quarter it can afford
        // to (BaselineAi.NegotiationBidAmount), so a real game of any
        // decent length should show real, positive spend for every seat.
        var seed = 1;
        var seats = Seats(seed);
        var result = FullGameRunner.Run(seed, seed, seed + 500_000, 24, seats, Cities, Fleet, Growth, null);

        Assert.NotNull(result.NegotiationSpendCentsFirst20QByName);
        Assert.NotNull(result.NegotiationBidCountFirst20QByName);

        foreach (var seat in seats)
        {
            Assert.True(result.NegotiationSpendCentsFirst20QByName!.ContainsKey(seat.AirlineName));
            Assert.True(result.NegotiationBidCountFirst20QByName!.ContainsKey(seat.AirlineName));
            Assert.True(result.NegotiationSpendCentsFirst20QByName![seat.AirlineName] > 0,
                $"{seat.AirlineName} should have placed at least one real gateway bid in 24 quarters.");
            Assert.True(result.NegotiationBidCountFirst20QByName![seat.AirlineName] > 0);
        }
    }

    [Fact]
    public void NegotiationSpend_OnlyAccumulatesThroughQuarter20()
    {
        // A longer run's first-20-quarters totals must match a run truncated
        // at exactly 20 quarters - proving the window is a hard cutoff, not
        // just "whatever happened in a short game."
        var seed = 1;
        var seats = Seats(seed);
        var at20 = FullGameRunner.Run(seed, seed, seed + 500_000, 20, seats, Cities, Fleet, Growth, null);
        var at40 = FullGameRunner.Run(seed, seed, seed + 500_000, 40, seats, Cities, Fleet, Growth, null);

        foreach (var seat in seats)
        {
            Assert.Equal(
                at20.NegotiationSpendCentsFirst20QByName![seat.AirlineName],
                at40.NegotiationSpendCentsFirst20QByName![seat.AirlineName]);
            Assert.Equal(
                at20.NegotiationBidCountFirst20QByName![seat.AirlineName],
                at40.NegotiationBidCountFirst20QByName![seat.AirlineName]);
        }
    }
}
