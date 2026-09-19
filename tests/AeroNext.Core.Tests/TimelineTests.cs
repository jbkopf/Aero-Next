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

    // --- Aircraft-acquisition-limits follow-up: IsAvailableForPurchase / IsAvailableForLease ---

    [Fact]
    public void IsAvailableForPurchase_NoEndOfManufactureYear_MatchesPlainIsAvailable()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);

        Assert.False(Timeline.IsAvailableForPurchase(aircraft, 1969));
        Assert.True(Timeline.IsAvailableForPurchase(aircraft, 1970));
        Assert.True(Timeline.IsAvailableForPurchase(aircraft, 2015));
    }

    [Fact]
    public void IsAvailableForPurchase_WithEndOfManufactureYear_FalseAfterIt()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970, EndOfManufactureYear: 1985);

        Assert.True(Timeline.IsAvailableForPurchase(aircraft, 1985));
        Assert.False(Timeline.IsAvailableForPurchase(aircraft, 1986));
    }

    [Fact]
    public void IsAvailableForLease_NoEndOfManufactureYear_StartsTwoYearsAfterAvailableFromYear()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);

        Assert.False(Timeline.IsAvailableForLease(aircraft, 1970)); // purchasable, not yet leasable
        Assert.False(Timeline.IsAvailableForLease(aircraft, 1971));
        Assert.True(Timeline.IsAvailableForLease(aircraft, 1972));
        Assert.True(Timeline.IsAvailableForLease(aircraft, 2015)); // never stops, EndOfManufactureYear null
    }

    [Fact]
    public void IsAvailableForLease_WithEndOfManufactureYear_ExtendsFiveYearsPastIt()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970, EndOfManufactureYear: 1985);

        Assert.True(Timeline.IsAvailableForLease(aircraft, 1990)); // 1985 + 5
        Assert.False(Timeline.IsAvailableForLease(aircraft, 1991));
    }

    [Fact]
    public void IsAvailableForLease_TwoYearDelayConstant_MatchesLeaseEligibilityDelayYears()
    {
        Assert.Equal(2, Timeline.LeaseEligibilityDelayYears);
    }

    [Fact]
    public void IsAvailableForLease_FiveYearExtensionConstant_MatchesLeaseAvailabilityExtensionYears()
    {
        Assert.Equal(5, Timeline.LeaseAvailabilityExtensionYears);
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
    public void DemandGrowthCatalog_Load_AllowsTheSameYearForTrendAndEventInTheSameScope()
    {
        // M21's trend/event split's own real shape: 1970 legitimately
        // carries both a global Trend breakpoint (the base curve) and a
        // global Event breakpoint (the 747) - not a duplicate, since
        // they resolve and compose independently.
        const string json = """
        [
          { "year": 1955, "globalMultiplier": 1.0 },
          { "year": 1970, "globalMultiplier": 2.2 },
          { "year": 1970, "globalMultiplier": 1.15, "kind": "Event", "eventGroup": "747" }
        ]
        """;

        var schedule = DemandGrowthCatalog.Load(json);

        Assert.Equal(3, schedule.Count);
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ThrowsOnDuplicateYearForTheSameKindScopeAndEventGroup()
    {
        const string json = """
        [
          { "year": 1955, "globalMultiplier": 1.0 },
          { "year": 1970, "globalMultiplier": 1.15, "kind": "Event", "eventGroup": "747" },
          { "year": 1970, "globalMultiplier": 1.20, "kind": "Event", "eventGroup": "747" }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => DemandGrowthCatalog.Load(json));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_AllowsTheSameYearForTwoIndependentEventGroups()
    {
        // Two entirely independent events can legitimately both start
        // the same year at the same scope - not a duplicate, each is
        // its own group.
        const string json = """
        [
          { "year": 1955, "globalMultiplier": 1.0 },
          { "year": 1973, "globalMultiplier": 0.85, "kind": "Event", "eventGroup": "oil-crisis" },
          { "year": 1973, "globalMultiplier": 1.10, "kind": "Event", "eventGroup": "unrelated-boom" }
        ]
        """;

        var schedule = DemandGrowthCatalog.Load(json);

        Assert.Equal(3, schedule.Count);
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ThrowsWhenAnEventBreakpointHasNoEventGroup()
    {
        // The real fix this round's own verification required: without
        // a required EventGroup, independent events would share one
        // flat pool and silently overwrite each other instead of
        // composing.
        const string json = """
        [
          { "year": 1955, "globalMultiplier": 1.0 },
          { "year": 1970, "globalMultiplier": 1.15, "kind": "Event" }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => DemandGrowthCatalog.Load(json));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ThrowsWhenATrendBreakpointHasAnEventGroup()
    {
        const string json = """
        [
          { "year": 1955, "globalMultiplier": 1.0 },
          { "year": 1970, "globalMultiplier": 2.2, "eventGroup": "should-not-be-here" }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => DemandGrowthCatalog.Load(json));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_ThrowsWhenOnlyGlobalEventsExist_NoGlobalTrend()
    {
        // A global Event alone doesn't satisfy "at least one global
        // breakpoint" - it composes on top of a trend, it doesn't
        // replace the need for one. Timeline.DemandMultiplierForYear's
        // own fallback has nothing to fall back to otherwise.
        const string json = """[{ "year": 1970, "globalMultiplier": 1.15, "kind": "Event", "eventGroup": "747" }]""";

        Assert.Throws<InvalidDataException>(() => DemandGrowthCatalog.Load(json));
    }

    [Fact]
    public void DemandGrowthCatalog_Load_RoundTripsKindAndEventGroup_DefaultsToTrendWhenAbsent()
    {
        const string json = """
        [
          { "year": 1955, "globalMultiplier": 1.0 },
          { "year": 1970, "globalMultiplier": 1.15, "kind": "Event", "eventGroup": "747" }
        ]
        """;

        var schedule = DemandGrowthCatalog.Load(json);
        Assert.Equal("747", schedule.Single(p => p.Year == 1970).EventGroup);

        Assert.Equal(DemandGrowthPointKind.Trend, schedule.Single(p => p.Year == 1955).Kind);
        Assert.Equal(DemandGrowthPointKind.Event, schedule.Single(p => p.Year == 1970).Kind);
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
        Assert.Contains(schedule, p => p.Region is null && p.CityId is null && p.Kind == DemandGrowthPointKind.Trend); // the global baseline trend is still present
        Assert.Contains(schedule, p => p.Region is null && p.CityId is null && p.Kind == DemandGrowthPointKind.Event); // and at least one real global event (747, an oil crisis, ...)
    }

    [Fact]
    public void DemandGrowthCatalog_LoadFile_LoadsRepoSchedule_SmoothTrend_StartsLow_EndsHigh()
    {
        // M21's trend/event split: demand_growth.json alone (loaded via
        // LoadFile, not the combined LoadCampaignSchedule) is the base
        // *trend* only - a smooth secular curve, no shocks baked in
        // anymore (those moved to historical_events.json as a separate
        // Event layer - see LoadCampaignSchedule_RealRepoFiles_ below
        // for where the real dips actually show up once composed).
        var schedule = DemandGrowthCatalog.LoadFile(DemandGrowthDataPath);

        Assert.NotEmpty(schedule);
        Assert.Equal(1.0, schedule[0].GlobalMultiplier);
        Assert.True(schedule[^1].GlobalMultiplier > schedule[0].GlobalMultiplier);
        Assert.All(schedule, p => Assert.Equal(DemandGrowthPointKind.Trend, p.Kind));

        for (var i = 1; i < schedule.Count; i++)
        {
            Assert.True(schedule[i].Year > schedule[i - 1].Year);
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

    // --- Geopolitical-aircraft-restrictions follow-up: AvailableForPurchase / AvailableForLease ---

    private static City MakeCity(string id) => new(id, id.ToUpperInvariant(), Region.EastAsia, 0, 0, 50, false, null);

    [Fact]
    public void AvailableForPurchase_NoHomeCity_NoRestrictions_MatchesPlainEraWindow()
    {
        var american = new AircraftType("707", "707", Seats: 140, AvailableFromYear: 1958, EndOfManufactureYear: 1979, Bloc: PoliticalBloc.American);
        var notYetAvailable = new AircraftType("a350", "A350", Seats: 300, AvailableFromYear: 2015);
        var fleet = new[] { american, notYetAvailable };

        var purchasable = Timeline.AvailableForPurchase(fleet, homeCity: null, year: 1965);

        Assert.Equal(new[] { american }, purchasable);
    }

    [Fact]
    public void AvailableForPurchase_RestrictedBlocForHomeCity_Excluded()
    {
        var beijing = MakeCity("pek");
        var american = new AircraftType("707", "707", Seats: 140, AvailableFromYear: 1958, EndOfManufactureYear: 1979, Bloc: PoliticalBloc.American);
        var european = new AircraftType("caravelle", "Caravelle", Seats: 80, AvailableFromYear: 1959, EndOfManufactureYear: 1972, Bloc: PoliticalBloc.European);
        var restrictions = new[] { new TradeRestriction("pek", PoliticalBloc.American, 1949, 1972) };

        var purchasable = Timeline.AvailableForPurchase(new[] { american, european }, beijing, year: 1965, restrictions);

        Assert.DoesNotContain(american, purchasable);
        Assert.Contains(european, purchasable);
    }

    [Fact]
    public void AvailableForPurchase_NullTradeRestrictions_PermissiveDefault()
    {
        var beijing = MakeCity("pek");
        var american = new AircraftType("707", "707", Seats: 140, AvailableFromYear: 1958, EndOfManufactureYear: 1979, Bloc: PoliticalBloc.American);

        var purchasable = Timeline.AvailableForPurchase(new[] { american }, beijing, year: 1965, tradeRestrictions: null);

        Assert.Contains(american, purchasable);
    }

    [Fact]
    public void AvailableForLease_UsesTheLeaseWindow_NotThePurchaseWindow()
    {
        // AvailableFromYear + LeaseEligibilityDelayYears (2) - not yet
        // leasable at year 1, purchasable and leasable at year 2.
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);

        Assert.Empty(Timeline.AvailableForLease(new[] { type }, homeCity: null, year: 1971));
        Assert.Single(Timeline.AvailableForLease(new[] { type }, homeCity: null, year: 1972));
    }

    [Fact]
    public void AvailableForLease_RestrictedBlocForHomeCity_Excluded()
    {
        var beijing = MakeCity("pek");
        var soviet = new AircraftType("il62", "Il-62", Seats: 186, AvailableFromYear: 1967, Bloc: PoliticalBloc.Soviet);
        var restrictions = new[] { new TradeRestriction("pek", PoliticalBloc.Soviet, 1960, 1989) };

        var leasable = Timeline.AvailableForLease(new[] { soviet }, beijing, year: 1975, restrictions);

        Assert.Empty(leasable);
    }
}
