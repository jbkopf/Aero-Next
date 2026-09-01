using AeroNext.Core.Negotiations;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>M46: periodic, demand-driven slot-capacity expansion.</summary>
public class CityCapacityBoardTests
{
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, false, null);

    private static readonly GravityDemandModel DemandModel = new();

    [Fact]
    public void Initialize_ComputesTotalDemandPerCity_SummedAcrossEveryTouchingPair()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var c = MakeCity("c", 2, 2, 50);
        var cities = new[] { a, b, c };

        var board = CityCapacityBoard.Initialize(cities, DemandModel);

        var expectedAtA = DemandModel.ForPair(a, b).PassengersPerQuarter + DemandModel.ForPair(a, c).PassengersPerQuarter;
        Assert.Equal(expectedAtA, board.BaselineDemandByCity["a"]);
    }

    [Fact]
    public void Initialize_EveryCityStartsWithZeroBonus()
    {
        var cities = new[] { MakeCity("a", 0, 0, 50), MakeCity("b", 1, 1, 50) };

        var board = CityCapacityBoard.Initialize(cities, DemandModel);

        Assert.All(cities, c => Assert.Equal(0, board.CapacityBonusByCity[c.Id]));
    }

    [Fact]
    public void AdvanceQuarter_NotOnTheCadence_ReturnsTheSameBoardInstance_TrueNoOp()
    {
        var cities = new[] { MakeCity("a", 0, 0, 50), MakeCity("b", 1, 1, 50) };
        var board = CityCapacityBoard.Initialize(cities, DemandModel);

        var advanced = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters - 1, cities, DemandModel);

        Assert.Same(board, advanced);
    }

    [Fact]
    public void AdvanceQuarter_OnTheCadence_NoGrowthSinceBaseline_NoBonusEarned()
    {
        var cities = new[] { MakeCity("a", 0, 0, 50), MakeCity("b", 1, 1, 50) };
        var board = CityCapacityBoard.Initialize(cities, DemandModel);

        // Same demand model passed again - zero growth by construction.
        var advanced = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters, cities, DemandModel);

        Assert.Equal(0, advanced.CapacityBonusByCity["a"]);
    }

    [Theory]
    [InlineData(1.0, 0)]   // no growth
    [InlineData(1.49, 0)]  // just below the 50% threshold
    [InlineData(1.5, 1)]   // exactly at the threshold
    [InlineData(2.5, 3)]   // 150% growth = 3 increments of 50%
    public void AdvanceQuarter_EarnedBonus_MatchesTheGrowthIncrementFormula(double growthMultiplier, int expectedBonus)
    {
        // Two models differing only by globalMultiplier - PassengersPerQuarter
        // scales linearly with it (GravityDemandModel.ForPair), so "current"
        // demand is exactly growthMultiplier x "baseline" demand, with no
        // approximation to worry about.
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var cities = new[] { a, b };
        var baselineModel = new GravityDemandModel(globalMultiplier: 1.0);
        var currentModel = new GravityDemandModel(globalMultiplier: growthMultiplier);

        var board = CityCapacityBoard.Initialize(cities, baselineModel);
        var advanced = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters, cities, currentModel);

        Assert.Equal(expectedBonus, advanced.CapacityBonusByCity["a"]);
    }

    [Fact]
    public void AdvanceQuarter_IsARatchet_NeverDecreasesAnAlreadyEarnedBonus()
    {
        // Advance once with strong growth to earn a real bonus, then
        // advance again (next cadence tick) with demand back down to
        // baseline - the bonus must survive; you don't demolish a
        // terminal already built because a later quarter is quieter.
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 1, 1, 50);
        var cities = new[] { a, b };
        var baselineModel = new GravityDemandModel(globalMultiplier: 1.0);

        var board = CityCapacityBoard.Initialize(cities, baselineModel);
        board = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters, cities, new GravityDemandModel(globalMultiplier: 3.0));
        var earnedBonus = board.CapacityBonusByCity["a"];
        Assert.True(earnedBonus > 0, "test setup should have earned a real bonus to begin with");

        var advanced = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters * 2, cities, baselineModel);

        Assert.Equal(earnedBonus, advanced.CapacityBonusByCity["a"]);
    }

    [Fact]
    public void AdvanceQuarter_ZeroBaselineDemand_NeverThrows_NoBonusEarned()
    {
        var board = new CityCapacityBoard(
            new Dictionary<string, long> { ["a"] = 0 },
            new Dictionary<string, int> { ["a"] = 0 });
        var cities = new[] { MakeCity("a", 0, 0, 50), MakeCity("b", 1, 1, 50) };

        var advanced = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters, cities, DemandModel);

        Assert.Equal(0, advanced.CapacityBonusByCity["a"]);
    }

    [Fact]
    public void AdvanceQuarter_CitiesEvaluatedIndependently_OneGrowingDoesNotAffectAnother()
    {
        // Two geographically-separate pairs (grew/growPartner near the
        // equator, flat/flatPartner far to the north) so cross-pair
        // demand between the two clusters is negligible by comparison -
        // only "grew" carries a city-scoped growth breakpoint.
        var grew = MakeCity("grew", 0, 0, 50);
        var growPartner = MakeCity("gpt", 1, 1, 50);
        var flat = MakeCity("flat", 80, 80, 50);
        var flatPartner = MakeCity("fpt", 79, 79, 50);
        var cities = new[] { grew, growPartner, flat, flatPartner };

        var baselineModel = new GravityDemandModel();
        var board = CityCapacityBoard.Initialize(cities, baselineModel);

        var schedule = new[]
        {
            new DemandGrowthPoint(Year: Timeline.StartYear, GlobalMultiplier: 1.0), // required global trend baseline - Timeline.DemandMultiplierForYear needs at least one
            new DemandGrowthPoint(Year: Timeline.StartYear, GlobalMultiplier: 4.0, CityId: "grew"),
        };
        var grownModel = new GravityDemandModel(schedule: schedule, year: Timeline.StartYear);

        var advanced = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters, cities, grownModel);

        Assert.True(advanced.CapacityBonusByCity["grew"] > 0);
        Assert.Equal(0, advanced.CapacityBonusByCity["flat"]);
    }
}
