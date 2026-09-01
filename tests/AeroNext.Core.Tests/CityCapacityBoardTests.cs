using AeroNext.Core.Negotiations;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>M46: periodic, demand-driven slot-capacity expansion, now on a per-city schedule sized to each city's own starting capacity.</summary>
public class CityCapacityBoardTests
{
    private static City MakeCity(string id, double lat, double lon, int weight, int autoCapacity) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, false, null, autoCapacity);

    private static City MakeGateway(string id, double lat, double lon, int weight, int gateCapacity) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, true, gateCapacity);

    private static readonly GravityDemandModel DemandModel = new();

    // --- GrowthBreakpoints: the per-city schedule itself ---

    [Fact]
    public void GrowthBreakpoints_StartingCapacityOne_SingleBreakpointAtDoubled()
    {
        var city = MakeCity("a", 0, 0, 50, autoCapacity: 1);

        var breakpoints = CityCapacityBoard.GrowthBreakpoints(city);

        Assert.Equal(new[] { 2.0 }, breakpoints);
    }

    [Fact]
    public void GrowthBreakpoints_StartingCapacityAtReference_MatchesTheOriginalFlat50PercentPace()
    {
        // ReferenceBaseCapacity's own defining property: a city exactly
        // this size reproduces the original single-fraction behavior.
        var city = MakeCity("a", 0, 0, 50, autoCapacity: CityCapacityBoard.ReferenceBaseCapacity);

        var breakpoints = CityCapacityBoard.GrowthBreakpoints(city);

        Assert.Equal(new[] { 1.5, 2.0 }, breakpoints);
    }

    [Fact]
    public void GrowthBreakpoints_LargerStartingCapacity_MoreBreakpoints_MoreCloselySpaced()
    {
        var small = MakeCity("small", 0, 0, 50, autoCapacity: 1);
        var large = MakeCity("large", 0, 0, 50, autoCapacity: 4);

        var smallBreakpoints = CityCapacityBoard.GrowthBreakpoints(small);
        var largeBreakpoints = CityCapacityBoard.GrowthBreakpoints(large);

        Assert.Single(smallBreakpoints);
        Assert.Equal(4, largeBreakpoints.Count);
        Assert.Equal(new[] { 1.25, 1.5, 1.75, 2.0 }, largeBreakpoints);
        // Both end at exactly double baseline - every city's own last
        // breakpoint is always "demand doubled," regardless of size;
        // only how many chances it gets on the way there differs.
        Assert.Equal(2.0, smallBreakpoints[^1]);
        Assert.Equal(2.0, largeBreakpoints[^1]);
    }

    [Fact]
    public void GrowthBreakpoints_EveryEntry_IsBetweenOneAndTwo_NeverBelowBaseline()
    {
        var city = MakeCity("a", 0, 0, 50, autoCapacity: 3);

        Assert.All(CityCapacityBoard.GrowthBreakpoints(city), bp => Assert.InRange(bp, 1.0, 2.0));
    }

    [Fact]
    public void GrowthBreakpoints_NeitherCapacityFieldSet_ReturnsEmpty()
    {
        var city = new City("a", "A", Region.EastAsia, 0, 0, 50, IsGateway: false, GateCapacity: null); // AutoCapacity also null

        Assert.Empty(CityCapacityBoard.GrowthBreakpoints(city));
    }

    [Fact]
    public void GrowthBreakpoints_UsesGateCapacity_ForGatewayCities()
    {
        var gateway = MakeGateway("gw", 0, 0, 50, gateCapacity: 3);

        Assert.Equal(3, CityCapacityBoard.GrowthBreakpoints(gateway).Count);
    }

    // --- Initialize ---

    [Fact]
    public void Initialize_ComputesTotalDemandPerCity_SummedAcrossEveryTouchingPair()
    {
        var a = MakeCity("a", 0, 0, 50, autoCapacity: 2);
        var b = MakeCity("b", 1, 1, 50, autoCapacity: 2);
        var c = MakeCity("c", 2, 2, 50, autoCapacity: 2);
        var cities = new[] { a, b, c };

        var board = CityCapacityBoard.Initialize(cities, DemandModel);

        var expectedAtA = DemandModel.ForPair(a, b).PassengersPerQuarter + DemandModel.ForPair(a, c).PassengersPerQuarter;
        Assert.Equal(expectedAtA, board.BaselineDemandByCity["a"]);
    }

    [Fact]
    public void Initialize_EveryCityStartsWithZeroBonus()
    {
        var cities = new[] { MakeCity("a", 0, 0, 50, autoCapacity: 2), MakeCity("b", 1, 1, 50, autoCapacity: 2) };

        var board = CityCapacityBoard.Initialize(cities, DemandModel);

        Assert.All(cities, c => Assert.Equal(0, board.CapacityBonusByCity[c.Id]));
    }

    // --- AdvanceQuarter ---

    [Fact]
    public void AdvanceQuarter_NotOnTheCadence_ReturnsTheSameBoardInstance_TrueNoOp()
    {
        var cities = new[] { MakeCity("a", 0, 0, 50, autoCapacity: 2), MakeCity("b", 1, 1, 50, autoCapacity: 2) };
        var board = CityCapacityBoard.Initialize(cities, DemandModel);

        var advanced = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters - 1, cities, DemandModel);

        Assert.Same(board, advanced);
    }

    [Fact]
    public void AdvanceQuarter_OnTheCadence_NoGrowthSinceBaseline_NoBonusEarned()
    {
        var cities = new[] { MakeCity("a", 0, 0, 50, autoCapacity: 2), MakeCity("b", 1, 1, 50, autoCapacity: 2) };
        var board = CityCapacityBoard.Initialize(cities, DemandModel);

        var advanced = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters, cities, DemandModel);

        Assert.Equal(0, advanced.CapacityBonusByCity["a"]);
    }

    [Theory]
    [InlineData(1.0, 0)]   // no growth
    [InlineData(1.49, 0)]  // just below the first breakpoint (1.5, at ReferenceBaseCapacity)
    [InlineData(1.5, 1)]   // exactly at the first breakpoint
    [InlineData(2.5, 2)]   // past the last breakpoint (2.0) - capped at 2 breakpoints total, never more
    public void AdvanceQuarter_AtReferenceCapacity_EarnedBonus_MatchesTheOriginalTwoBreakpointSchedule(double growthMultiplier, int expectedBonus)
    {
        var a = MakeCity("a", 0, 0, 50, autoCapacity: CityCapacityBoard.ReferenceBaseCapacity);
        var b = MakeCity("b", 1, 1, 50, autoCapacity: CityCapacityBoard.ReferenceBaseCapacity);
        var cities = new[] { a, b };
        var baselineModel = new GravityDemandModel(globalMultiplier: 1.0);
        var currentModel = new GravityDemandModel(globalMultiplier: growthMultiplier);

        var board = CityCapacityBoard.Initialize(cities, baselineModel);
        var advanced = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters, cities, currentModel);

        Assert.Equal(expectedBonus, advanced.CapacityBonusByCity["a"]);
    }

    [Fact]
    public void AdvanceQuarter_SmallCity_NeverEarnsMoreThanItsOwnStartingCapacity_EvenWithHugeGrowth()
    {
        var small = MakeCity("small", 0, 0, 50, autoCapacity: 1);
        var partner = MakeCity("partner", 1, 1, 50, autoCapacity: 1);
        var cities = new[] { small, partner };
        var baselineModel = new GravityDemandModel(globalMultiplier: 1.0);
        var hugeGrowthModel = new GravityDemandModel(globalMultiplier: 100.0); // wildly past double

        var board = CityCapacityBoard.Initialize(cities, baselineModel);
        var advanced = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters, cities, hugeGrowthModel);

        Assert.Equal(1, advanced.CapacityBonusByCity["small"]); // capped at its own starting capacity of 1
    }

    [Fact]
    public void AdvanceQuarter_SmallCity_JustBelowDoubling_StillEarnsNothing()
    {
        // Directly exercises the user's own stated intent: a small city
        // needs real, substantial growth (its one breakpoint sits at
        // double baseline) before it earns anything - a modest relative
        // swing that would have earned a bonus at ReferenceBaseCapacity
        // earns a small city nothing.
        var small = MakeCity("small", 0, 0, 50, autoCapacity: 1);
        var partner = MakeCity("partner", 1, 1, 50, autoCapacity: 1);
        var cities = new[] { small, partner };
        var baselineModel = new GravityDemandModel(globalMultiplier: 1.0);
        var modestGrowthModel = new GravityDemandModel(globalMultiplier: 1.5); // would earn a bonus at ReferenceBaseCapacity

        var board = CityCapacityBoard.Initialize(cities, baselineModel);
        var advanced = board.AdvanceQuarter(CityCapacityBoard.ExpansionPeriodQuarters, cities, modestGrowthModel);

        Assert.Equal(0, advanced.CapacityBonusByCity["small"]);
    }

    [Fact]
    public void AdvanceQuarter_IsARatchet_NeverDecreasesAnAlreadyEarnedBonus()
    {
        var a = MakeCity("a", 0, 0, 50, autoCapacity: 4);
        var b = MakeCity("b", 1, 1, 50, autoCapacity: 4);
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
        var cities = new[] { MakeCity("a", 0, 0, 50, autoCapacity: 2), MakeCity("b", 1, 1, 50, autoCapacity: 2) };

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
        var grew = MakeCity("grew", 0, 0, 50, autoCapacity: 2);
        var growPartner = MakeCity("gpt", 1, 1, 50, autoCapacity: 2);
        var flat = MakeCity("flat", 80, 80, 50, autoCapacity: 2);
        var flatPartner = MakeCity("fpt", 79, 79, 50, autoCapacity: 2);
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
