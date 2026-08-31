using AeroNext.Core.Airlines;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class TimelineTests
{
    private static string DemandGrowthDataPath => Path.Combine(AppContext.BaseDirectory, "data", "demand_growth.json");
    private static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "data");

    [Theory]
    [InlineData(1, 1955)]
    [InlineData(4, 1955)]
    [InlineData(5, 1956)]
    [InlineData(244, 2015)] // ~240-quarter campaign end
    public void YearForQuarter_ConvertsCorrectly(int quarter, int expectedYear)
    {
        Assert.Equal(expectedYear, Timeline.YearForQuarter(quarter));
    }

    [Fact]
    public void IsAvailable_TrueWhenYearAtOrAfterAvailableFromYear()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);

        Assert.False(Timeline.IsAvailable(aircraft, 1969));
        Assert.True(Timeline.IsAvailable(aircraft, 1970));
        Assert.True(Timeline.IsAvailable(aircraft, 2000));
    }

    [Fact]
    public void AvailableAircraft_FiltersOutFutureAircraft()
    {
        var early = new AircraftType("early", "Early", Seats: 50, AvailableFromYear: 1950);
        var late = new AircraftType("late", "Late", Seats: 300, AvailableFromYear: 1995);
        var fleet = new[] { early, late };

        var available1960 = Timeline.AvailableAircraft(fleet, 1960);
        var available2000 = Timeline.AvailableAircraft(fleet, 2000);

        Assert.Equal(new[] { "early" }, available1960.Select(a => a.Id));
        Assert.Equal(new[] { "early", "late" }, available2000.Select(a => a.Id).OrderBy(id => id));
    }

    [Fact]
    public void DemandMultiplierForYear_UsesLatestBreakpointAtOrBeforeYear()
    {
        var schedule = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1990, 2.5),
            new DemandGrowthPoint(2015, 4.0),
        };

        Assert.Equal(1.0, Timeline.DemandMultiplierForYear(schedule, 1955));
        Assert.Equal(1.0, Timeline.DemandMultiplierForYear(schedule, 1989));
        Assert.Equal(2.5, Timeline.DemandMultiplierForYear(schedule, 1990));
        Assert.Equal(2.5, Timeline.DemandMultiplierForYear(schedule, 2014));
        Assert.Equal(4.0, Timeline.DemandMultiplierForYear(schedule, 2015));
        Assert.Equal(4.0, Timeline.DemandMultiplierForYear(schedule, 2030)); // beyond the last breakpoint: holds
    }

    [Fact]
    public void DemandMultiplierForYear_BeforeFirstBreakpoint_UsesEarliestAnyway()
    {
        var schedule = new[] { new DemandGrowthPoint(1970, 1.5) };

        Assert.Equal(1.5, Timeline.DemandMultiplierForYear(schedule, 1955));
    }

    [Fact]
    public void DemandMultiplierForYear_IgnoresRegionAndCityScopedBreakpoints()
    {
        // M20: a schedule can now mix scopes - DemandMultiplierForYear
        // must resolve the *global* portion only, even when a
        // region/city-scoped breakpoint would otherwise look like the
        // latest one for that year.
        var schedule = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1990, 2.5),
            new DemandGrowthPoint(1995, 99.0, Region: Region.EastAsia), // would win "latest at or before 2000" if scope were ignored
            new DemandGrowthPoint(1996, 88.0, CityId: "dxb"),
        };

        Assert.Equal(2.5, Timeline.DemandMultiplierForYear(schedule, 2000));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ThrowsWhenBothRegionAndCityIdSet()
    {
        const string json = """
        [
          { "year": 1955, "globalMultiplier": 1.0 },
          { "year": 1970, "globalMultiplier": 2.0, "region": "EastAsia", "cityId": "dxb" }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => DemandGrowthCatalog.Load(json));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ThrowsWhenNoGlobalBreakpointExists()
    {
        const string json = """[{ "year": 1970, "globalMultiplier": 2.0, "cityId": "dxb" }]""";

        Assert.Throws<InvalidDataException>(() => DemandGrowthCatalog.Load(json));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_AllowsTheSameYearAcrossDifferentScopes()
    {
        const string json = """
        [
          { "year": 1955, "globalMultiplier": 1.0 },
          { "year": 1970, "globalMultiplier": 1.5 },
          { "year": 1970, "globalMultiplier": 3.0, "region": "EastAsia" },
          { "year": 1970, "globalMultiplier": 5.0, "cityId": "dxb" }
        ]
        """;

        var schedule = DemandGrowthCatalog.Load(json);

        Assert.Equal(4, schedule.Count);
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ThrowsOnDuplicateYearWithinTheSameCityScope()
    {
        const string json = """
        [
          { "year": 1955, "globalMultiplier": 1.0 },
          { "year": 1970, "globalMultiplier": 2.0, "cityId": "dxb" },
          { "year": 1970, "globalMultiplier": 3.0, "cityId": "dxb" }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => DemandGrowthCatalog.Load(json));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_DoesNotThrowOnSameYearDifferentRegions()
    {
        const string json = """
        [
          { "year": 1955, "globalMultiplier": 1.0 },
          { "year": 1970, "globalMultiplier": 2.0, "region": "EastAsia" },
          { "year": 1970, "globalMultiplier": 3.0, "region": "Europe" }
        ]
        """;

        var schedule = DemandGrowthCatalog.Load(json);

        Assert.Equal(3, schedule.Count);
    }

    [Fact]
    public void DemandGrowthCatalog_Load_RoundTripsNameAndDefaultsToNullWhenAbsent()
    {
        const string json = """
        [
          { "year": 1955, "globalMultiplier": 1.0 },
          { "year": 1985, "globalMultiplier": 2.0, "cityId": "dxb", "name": "Dubai: aviation-hub strategy begins" }
        ]
        """;

        var schedule = DemandGrowthCatalog.Load(json);

        Assert.Null(schedule.Single(p => p.CityId is null).Name);
        Assert.Equal("Dubai: aviation-hub strategy begins", schedule.Single(p => p.CityId == "dxb").Name);
    }

    [Fact]
    public void LoadCampaignSchedule_MergesBaselineAndEvents_ValidatingTheCombinedResult()
    {
        // The important claim, not just a happy-path check: a
        // historical_events.json that's entirely city-scoped (M21's own
        // real shape - it never carries a global entry itself) would fail
        // DemandGrowthCatalog's own "at least one global breakpoint"
        // rule if validated alone. LoadCampaignSchedule must merge
        // before validating, not after, for this to ever succeed.
        var tempDir = Path.Combine(Path.GetTempPath(), "aeronext-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "demand_growth.json"), """[{ "year": 1955, "globalMultiplier": 1.0 }]""");
            File.WriteAllText(Path.Combine(tempDir, "historical_events.json"), """[{ "year": 1985, "globalMultiplier": 5.0, "cityId": "dxb" }]""");

            var schedule = DemandGrowthCatalog.LoadCampaignSchedule(tempDir);

            Assert.Equal(2, schedule.Count);
            Assert.Contains(schedule, p => p.CityId == "dxb" && p.GlobalMultiplier == 5.0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void LoadCampaignSchedule_LoadsTheRealRepoFiles_IncludingDubai()
    {
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(DataDirectory);

        Assert.Contains(schedule, p => p.CityId == "dxb");
        Assert.Contains(schedule, p => p.Region is null && p.CityId is null); // the global baseline is still present
    }

    [Fact]
    public void DemandGrowthCatalog_LoadFile_LoadsRepoSchedule_Increasing()
    {
        var schedule = DemandGrowthCatalog.LoadFile(DemandGrowthDataPath);

        Assert.NotEmpty(schedule);
        for (var i = 1; i < schedule.Count; i++)
        {
            Assert.True(schedule[i].GlobalMultiplier >= schedule[i - 1].GlobalMultiplier);
        }
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ThrowsOnEmptySchedule()
    {
        Assert.Throws<InvalidDataException>(() => DemandGrowthCatalog.Load("[]"));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ThrowsWhenJsonDeserializesToNull()
    {
        Assert.Throws<InvalidDataException>(() => DemandGrowthCatalog.Load("null"));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ThrowsOnDuplicateYears()
    {
        const string json = """
        [
          { "year": 1970, "globalMultiplier": 1.0 },
          { "year": 1970, "globalMultiplier": 2.0 }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => DemandGrowthCatalog.Load(json));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ThrowsOnNonPositiveMultiplier()
    {
        const string json = """[{ "year": 1970, "globalMultiplier": 0 }]""";

        Assert.Throws<InvalidDataException>(() => DemandGrowthCatalog.Load(json));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ReturnsSortedByYear_RegardlessOfSourceOrder()
    {
        const string json = """
        [
          { "year": 2000, "globalMultiplier": 3.0 },
          { "year": 1960, "globalMultiplier": 1.0 }
        ]
        """;

        var schedule = DemandGrowthCatalog.Load(json);

        Assert.Equal(new[] { 1960, 2000 }, schedule.Select(p => p.Year));
    }
}
