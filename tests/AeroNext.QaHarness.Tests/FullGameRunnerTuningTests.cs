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
        Assert.Contains(nameof(ExperimentTuning.AustralAsiaIntraRegionDemandMultiplier), text);
    }
}
