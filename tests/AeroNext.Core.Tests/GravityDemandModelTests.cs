using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class GravityDemandModelTests
{
    private static City MakeCity(string id, double lat, double lon, int weight, bool isGateway = false, int? gateCapacity = null) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, isGateway, gateCapacity);

    private static City MakeCity(string id, double lat, double lon, int weight, Region region) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, false, null);

    [Fact]
    public void ForPair_IsSymmetric()
    {
        var model = new GravityDemandModel();
        var a = MakeCity("a", 40.6413, -73.7781, 100);
        var b = MakeCity("b", 51.4700, -0.4543, 95);

        var ab = model.ForPair(a, b);
        var ba = model.ForPair(b, a);

        Assert.Equal(ab.PassengersPerQuarter, ba.PassengersPerQuarter);
    }

    [Fact]
    public void ForPair_DemandFallsOffWithDistance_HoldingWeightConstant()
    {
        var model = new GravityDemandModel();
        var origin = MakeCity("origin", 0, 0, 50);
        var near = MakeCity("near", 1, 0, 50);
        var far = MakeCity("far", 40, 0, 50);

        var nearDemand = model.ForPair(origin, near).PassengersPerQuarter;
        var farDemand = model.ForPair(origin, far).PassengersPerQuarter;

        Assert.True(nearDemand > farDemand);
    }

    [Fact]
    public void ForPair_DemandGrowsWithWeight_HoldingDistanceConstant()
    {
        var model = new GravityDemandModel();
        var origin = MakeCity("origin", 0, 0, 50);
        var smallDest = MakeCity("small", 10, 0, 5);
        var bigDest = MakeCity("big", 10, 0, 100);

        var smallDemand = model.ForPair(origin, smallDest).PassengersPerQuarter;
        var bigDemand = model.ForPair(origin, bigDest).PassengersPerQuarter;

        Assert.True(bigDemand > smallDemand);
    }

    [Fact]
    public void ForPair_AppliesCrossRegionMultiplier_OnlyAcrossRegionBoundary()
    {
        var model = new GravityDemandModel(crossRegionMultiplier: 6.0);
        var a1 = MakeCity("a1", 0, 0, 50, Region.NorthAmerica);
        var a2 = MakeCity("a2", 1, 1, 50, Region.NorthAmerica); // same region as a1
        var b = MakeCity("b", 1, 1, 50, Region.Europe); // same distance/weight as a2, different region

        var withinRegion = model.ForPair(a1, a2).PassengersPerQuarter;
        var crossRegion = model.ForPair(a1, b).PassengersPerQuarter;

        // Allow ±1 for independent rounding of each result.
        Assert.InRange(crossRegion, withinRegion * 6 - 1, withinRegion * 6 + 1);
    }

    [Fact]
    public void ForPair_GlobalMultiplier_ScalesDemandProportionally()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 10, 10, 50);

        var baseline = new GravityDemandModel(globalMultiplier: 1.0).ForPair(a, b).PassengersPerQuarter;
        var grown = new GravityDemandModel(globalMultiplier: 4.0).ForPair(a, b).PassengersPerQuarter;

        Assert.InRange(grown, baseline * 4 - 1, baseline * 4 + 1);
    }

    [Fact]
    public void ForPair_CrossRegionMultiplier_DoesNotAffectSameRegionPairs()
    {
        var a = MakeCity("a", 0, 0, 50, Region.NorthAmerica);
        var b = MakeCity("b", 10, 10, 50, Region.NorthAmerica);

        var low = new GravityDemandModel(crossRegionMultiplier: 1.0).ForPair(a, b).PassengersPerQuarter;
        var high = new GravityDemandModel(crossRegionMultiplier: 20.0).ForPair(a, b).PassengersPerQuarter;

        Assert.Equal(low, high);
    }

    [Fact]
    public void AllPairs_ReturnsCombinationCount()
    {
        var model = new GravityDemandModel();
        var cities = new[]
        {
            MakeCity("a", 0, 0, 10),
            MakeCity("b", 1, 1, 10),
            MakeCity("c", 2, 2, 10),
            MakeCity("d", 3, 3, 10),
        };

        var pairs = model.AllPairs(cities);

        Assert.Equal(6, pairs.Count); // C(4, 2)
    }

    [Fact]
    public void AllPairs_IsDeterministic_RegardlessOfInputOrder()
    {
        var model = new GravityDemandModel();
        var cities = new[]
        {
            MakeCity("a", 0, 0, 10),
            MakeCity("b", 1, 1, 20),
            MakeCity("c", 2, 2, 30),
        };
        var shuffled = new[] { cities[2], cities[0], cities[1] };

        var fromOriginal = model.AllPairs(cities);
        var fromShuffled = model.AllPairs(shuffled);

        Assert.Equal(
            fromOriginal.Select(p => (p.Origin.Id, p.Destination.Id, p.PassengersPerQuarter)),
            fromShuffled.Select(p => (p.Origin.Id, p.Destination.Id, p.PassengersPerQuarter)));
    }

    [Fact]
    public void ForPair_ScheduleWithOnlyAGlobalEntry_MatchesFlatGlobalMultiplier_ByteIdentical()
    {
        // The core M20 backward-compatibility proof: a schedule with no
        // region/city scoping produces the exact same output as today's
        // flat globalMultiplier constructor path - not just "close",
        // byte-identical.
        var a = MakeCity("a", 0, 0, 50, Region.NorthAmerica);
        var b = MakeCity("b", 10, 10, 60, Region.Europe);
        var schedule = new[] { new DemandGrowthPoint(1955, 2.5) };

        var flat = new GravityDemandModel(globalMultiplier: 2.5).ForPair(a, b);
        var scheduled = new GravityDemandModel(schedule: schedule, year: 1955).ForPair(a, b);

        Assert.Equal(flat.PassengersPerQuarter, scheduled.PassengersPerQuarter);
    }

    [Fact]
    public void ForPair_RegionScopedBreakpoint_OnlyAffectsPairsTouchingThatRegion()
    {
        var home = MakeCity("home", 0, 0, 50, Region.NorthAmerica);
        var eastAsiaCity = MakeCity("hnd", 10, 10, 60, Region.EastAsia);
        var europeCity = MakeCity("lhr", 10, 10, 60, Region.Europe); // same distance/weight as hnd - only region differs
        var schedule = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1955, 3.0, Region: Region.EastAsia),
        };
        var model = new GravityDemandModel(schedule: schedule, year: 1955);
        var baseline = new GravityDemandModel(schedule: new[] { new DemandGrowthPoint(1955, 1.0) }, year: 1955);

        var affected = model.ForPair(home, eastAsiaCity).PassengersPerQuarter;
        var unaffected = model.ForPair(home, europeCity).PassengersPerQuarter;
        var unaffectedBaseline = baseline.ForPair(home, europeCity).PassengersPerQuarter;

        // EastAsia-touching pair scales by the region factor (3.0); the
        // Europe-touching pair is completely untouched by EastAsia's own
        // scoped breakpoint.
        Assert.InRange(affected, unaffectedBaseline * 3 - 1, unaffectedBaseline * 3 + 1);
        Assert.Equal(unaffectedBaseline, unaffected);
    }

    [Fact]
    public void ForPair_CityScopedBreakpoint_OnlyAffectsPairsTouchingThatCity()
    {
        var home = MakeCity("home", 0, 0, 50, Region.NorthAmerica);
        var dubai = MakeCity("dxb", 10, 10, 60, Region.SouthWestAsia);
        var otherSameRegion = MakeCity("ruh", 10, 10, 60, Region.SouthWestAsia); // same region, distance, weight - only the city id differs
        var schedule = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1955, 5.0, CityId: "dxb"),
        };
        var model = new GravityDemandModel(schedule: schedule, year: 1955);
        var baseline = new GravityDemandModel(schedule: new[] { new DemandGrowthPoint(1955, 1.0) }, year: 1955);

        var affected = model.ForPair(home, dubai).PassengersPerQuarter;
        var unaffected = model.ForPair(home, otherSameRegion).PassengersPerQuarter;
        var unaffectedBaseline = baseline.ForPair(home, otherSameRegion).PassengersPerQuarter;

        Assert.InRange(affected, unaffectedBaseline * 5 - 5, unaffectedBaseline * 5 + 5);
        Assert.Equal(unaffectedBaseline, unaffected);
    }

    [Fact]
    public void ForPair_BothEndpointsCityScoped_MultipliersCompound()
    {
        // Two cities that each individually boomed - the pair between
        // them should reflect both, not just one (the real "two thriving
        // hubs connect to each other" story M21 wants to author later).
        var dubai = MakeCity("dxb", 0, 0, 50, Region.SouthWestAsia);
        var shanghai = MakeCity("pvg", 10, 10, 60, Region.EastAsia);
        var schedule = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1955, 2.0, CityId: "dxb"),
            new DemandGrowthPoint(1955, 3.0, CityId: "pvg"),
        };
        var model = new GravityDemandModel(schedule: schedule, year: 1955);
        var baseline = new GravityDemandModel(schedule: new[] { new DemandGrowthPoint(1955, 1.0) }, year: 1955);

        var compounded = model.ForPair(dubai, shanghai).PassengersPerQuarter;
        var unscaled = baseline.ForPair(dubai, shanghai).PassengersPerQuarter;

        // 2.0 x 3.0 = 6x, not 2x or 3x alone.
        Assert.InRange(compounded, unscaled * 6 - 6, unscaled * 6 + 6);
    }

    [Fact]
    public void ForPair_RegionAndCityScopeOnTheSameCity_BothApply()
    {
        var home = MakeCity("home", 0, 0, 50, Region.NorthAmerica);
        var dubai = MakeCity("dxb", 10, 10, 60, Region.SouthWestAsia);
        var schedule = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1955, 2.0, Region: Region.SouthWestAsia),
            new DemandGrowthPoint(1955, 3.0, CityId: "dxb"),
        };
        var model = new GravityDemandModel(schedule: schedule, year: 1955);
        var baseline = new GravityDemandModel(schedule: new[] { new DemandGrowthPoint(1955, 1.0) }, year: 1955);

        var affected = model.ForPair(home, dubai).PassengersPerQuarter;
        var unscaled = baseline.ForPair(home, dubai).PassengersPerQuarter;

        // Dubai's own region (2.0) x its own city entry (3.0) = 6x.
        Assert.InRange(affected, unscaled * 6 - 6, unscaled * 6 + 6);
    }

    [Fact]
    public void ForPair_ScheduleGrowsAcrossYears_PiecewiseConstant()
    {
        var a = MakeCity("home", 0, 0, 50, Region.NorthAmerica);
        var dubai = MakeCity("dxb", 10, 10, 60, Region.SouthWestAsia);
        var schedule = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1955, 1.0, CityId: "dxb"),
            new DemandGrowthPoint(1985, 8.0, CityId: "dxb"), // Dubai's own later inflection
        };

        var before = new GravityDemandModel(schedule: schedule, year: 1980).ForPair(a, dubai).PassengersPerQuarter;
        var after = new GravityDemandModel(schedule: schedule, year: 1985).ForPair(a, dubai).PassengersPerQuarter;

        Assert.InRange(after, before * 8 - 8, before * 8 + 8);
    }

    [Fact]
    public void ForPair_GlobalEvent_ComposesWithTrend_NotReplacesIt()
    {
        // The whole point of the trend/event split: a global Event dip
        // multiplies on top of whatever the trend already says, rather
        // than the two clobbering each other in one "latest wins" pool.
        // Trend stays flat at 1.0 the entire time here, isolating the
        // event's own effect.
        var a = MakeCity("a", 0, 0, 50, Region.NorthAmerica);
        var b = MakeCity("b", 10, 10, 60, Region.NorthAmerica);
        var schedule = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1973, 0.5, Kind: DemandGrowthPointKind.Event, EventGroup: "shock"),
        };

        var beforeEvent = new GravityDemandModel(schedule: schedule, year: 1970).ForPair(a, b).PassengersPerQuarter;
        var duringEvent = new GravityDemandModel(schedule: schedule, year: 1973).ForPair(a, b).PassengersPerQuarter;

        Assert.InRange(duringEvent, beforeEvent / 2 - 1, beforeEvent / 2 + 1);
    }

    [Fact]
    public void ForPair_GlobalEvent_WithReversion_ReturnsExactlyToTrendOnlyValue()
    {
        var a = MakeCity("a", 0, 0, 50, Region.NorthAmerica);
        var b = MakeCity("b", 10, 10, 60, Region.NorthAmerica);
        var trendOnly = new[] { new DemandGrowthPoint(1955, 1.0) };
        var withEvent = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1973, 0.5, Kind: DemandGrowthPointKind.Event, EventGroup: "shock"),
            new DemandGrowthPoint(1976, 1.0, Kind: DemandGrowthPointKind.Event, EventGroup: "shock"), // reverts to neutral
        };

        var trendOnlyDemand = new GravityDemandModel(schedule: trendOnly, year: 1980).ForPair(a, b).PassengersPerQuarter;
        var duringEvent = new GravityDemandModel(schedule: withEvent, year: 1973).ForPair(a, b).PassengersPerQuarter;
        var afterReversion = new GravityDemandModel(schedule: withEvent, year: 1980).ForPair(a, b).PassengersPerQuarter;

        Assert.NotEqual(trendOnlyDemand, duringEvent); // the dip is real while active
        Assert.Equal(trendOnlyDemand, afterReversion); // exactly back to trend-only once reverted, not approximately
    }

    [Fact]
    public void ForPair_GlobalEvent_WithNoReversion_StaysPermanentlyActive()
    {
        // The 747's own real shape: a structural step-up with no
        // reversion breakpoint at all - it should still be active
        // decades later, not silently expire.
        var a = MakeCity("a", 0, 0, 50, Region.NorthAmerica);
        var b = MakeCity("b", 10, 10, 60, Region.NorthAmerica);
        var schedule = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1970, 1.15, Kind: DemandGrowthPointKind.Event, EventGroup: "747"),
        };
        var trendOnly = new[] { new DemandGrowthPoint(1955, 1.0) };

        var trendOnlyDemand = new GravityDemandModel(schedule: trendOnly, year: 2015).ForPair(a, b).PassengersPerQuarter;
        var farFuture = new GravityDemandModel(schedule: schedule, year: 2015).ForPair(a, b).PassengersPerQuarter;

        Assert.InRange(farFuture, (long)(trendOnlyDemand * 1.15) - 2, (long)(trendOnlyDemand * 1.15) + 2);
    }

    [Fact]
    public void ForPair_TwoIndependentGlobalEvents_ComposeInstead_OfOneOverwritingTheOther()
    {
        // The real bug found and fixed while verifying this round: a
        // permanent event (the 747, no reversion) and a later, entirely
        // unrelated temporary event (an oil crisis) both being global
        // must resolve *independently* (grouped by EventGroup) and
        // multiply together - not share one "latest wins" pool, which
        // would let the oil crisis breakpoint silently erase the 747's
        // own effect, permanently, even past the crisis's own reversion.
        var a = MakeCity("a", 0, 0, 50, Region.NorthAmerica);
        var b = MakeCity("b", 10, 10, 60, Region.NorthAmerica);
        var schedule = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1970, 1.15, Kind: DemandGrowthPointKind.Event, EventGroup: "747"),
            new DemandGrowthPoint(1973, 0.85, Kind: DemandGrowthPointKind.Event, EventGroup: "oil-crisis"),
            new DemandGrowthPoint(1976, 1.0, Kind: DemandGrowthPointKind.Event, EventGroup: "oil-crisis"),
        };
        var only747 = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1970, 1.15, Kind: DemandGrowthPointKind.Event, EventGroup: "747"),
        };

        var during747Only = new GravityDemandModel(schedule: schedule, year: 1971).ForPair(a, b).PassengersPerQuarter;
        var duringBoth = new GravityDemandModel(schedule: schedule, year: 1973).ForPair(a, b).PassengersPerQuarter;
        var after747Alone = new GravityDemandModel(schedule: only747, year: 1980).ForPair(a, b).PassengersPerQuarter; // 747-only reference
        var afterCrisisReverts = new GravityDemandModel(schedule: schedule, year: 1980).ForPair(a, b).PassengersPerQuarter;

        // Both active: strictly less than the 747-alone figure, since
        // the oil crisis's own 0.85 factor is genuinely multiplying in,
        // not just being ignored.
        Assert.True(duringBoth < during747Only);
        // The 747's own effect is still real once the crisis passes -
        // back to exactly the 747-only reference, not the plain
        // baseline (proves the 747 was never actually lost).
        Assert.Equal(after747Alone, afterCrisisReverts);
    }

    [Fact]
    public void ForPair_CityTrendAndCityEvent_ComposeTogether()
    {
        // A city can carry both a sustained trend (Dubai's own shape)
        // and a separate, composable event on top - not one pool where
        // the two would clobber each other.
        var home = MakeCity("home", 0, 0, 50, Region.NorthAmerica);
        var dubai = MakeCity("dxb", 10, 10, 60, Region.SouthWestAsia);
        var trendOnly = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1955, 2.0, CityId: "dxb"),
        };
        var withEvent = new[]
        {
            new DemandGrowthPoint(1955, 1.0),
            new DemandGrowthPoint(1955, 2.0, CityId: "dxb"),
            new DemandGrowthPoint(1991, 0.5, CityId: "dxb", Kind: DemandGrowthPointKind.Event, EventGroup: "local-shock"), // a hypothetical local shock
        };

        var trendOnlyDemand = new GravityDemandModel(schedule: trendOnly, year: 1991).ForPair(home, dubai).PassengersPerQuarter;
        var withEventDemand = new GravityDemandModel(schedule: withEvent, year: 1991).ForPair(home, dubai).PassengersPerQuarter;

        Assert.InRange(withEventDemand, trendOnlyDemand / 2 - 2, trendOnlyDemand / 2 + 2);
    }

    [Fact]
    public void RealDataset_MajorMarketDwarfsChokepointPair()
    {
        var dataPath = Path.Combine(AppContext.BaseDirectory, "data", "cities.json");
        var cities = CityCatalog.LoadFile(dataPath);
        var byId = cities.ToDictionary(c => c.Id);
        var model = new GravityDemandModel();

        var jfkLhr = model.ForPair(byId["jfk"], byId["lhr"]);
        var yqxTer = model.ForPair(byId["yqx"], byId["ter"]); // Gander-Azores: both chokepoints

        Assert.True(jfkLhr.PassengersPerQuarter > yqxTer.PassengersPerQuarter * 100);
    }

    [Fact]
    public void RealDataset_Dubai_NegligibleEarly_MaterialyHigherLate()
    {
        // M21's own worked example, measured against the real repo data
        // (data/cities.json + data/historical_events.json), not a
        // synthetic city - the same "actually run it and read the
        // numbers" standard this session has held to throughout.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var farAway = byId["gru"]; // a real, distant, unrelated city - not scoped by Dubai's own entries

        var early = new GravityDemandModel(schedule: schedule, year: 1955).ForPair(farAway, byId["dxb"]).PassengersPerQuarter;
        var late = new GravityDemandModel(schedule: schedule, year: 2015).ForPair(farAway, byId["dxb"]).PassengersPerQuarter;

        // 1955's 0.3x vs 2015's 10.0x is a real ~33x swing in Dubai's own
        // factor alone (global growth on top of it too) - a wide,
        // unambiguous margin, not a borderline check.
        Assert.True(late > early * 20);
    }

    [Fact]
    public void RealDataset_SuezCrisis_DipsCairoIn1956_RecoveredBy1958()
    {
        // The first window of the 5-year historical sweep: the Suez
        // Crisis (Egypt nationalizes the canal, invasion, canal blocked
        // ~5 months) is modeled as a Cairo-scoped dip, not a global one -
        // it was a real, Egypt-centered event, not a worldwide shock on
        // the scale of an oil crisis or 9/11.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var farAway = byId["gru"];

        var before = new GravityDemandModel(schedule: schedule, year: 1955).ForPair(farAway, byId["cai"]).PassengersPerQuarter;
        var duringCrisis = new GravityDemandModel(schedule: schedule, year: 1956).ForPair(farAway, byId["cai"]).PassengersPerQuarter;
        var afterReopening = new GravityDemandModel(schedule: schedule, year: 1958).ForPair(farAway, byId["cai"]).PassengersPerQuarter;

        Assert.True(duringCrisis < before, "1956 should dip below 1955");
        // The trend curve itself is flat between 1955 and 1970 (no
        // breakpoint in between), so 1958 should land back at exactly
        // the same value as 1955, not just "recovered somewhat" -
        // proving the reversion is complete, not partial.
        Assert.Equal(before, afterReopening);
    }

    [Fact]
    public void RealDataset_CubaEmbargo_PermanentlyDipsHavanaFrom1962_NeverReverts()
    {
        // Window 2 of the 5-year historical sweep: the US embargo on
        // Cuba (Kennedy's Feb 1962 proclamation) collapsed the
        // dominant US-Cuba passenger market and, unlike every other
        // event in this series so far, never reverted within the
        // campaign's own timeframe - modeled with no reversion
        // breakpoint at all, the same permanent shape as the 747's own
        // step-up.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var withoutEmbargo = schedule.Where(p => p.EventGroup != "cuba-embargo-1962").ToList();
        var farAway = byId["gru"];

        var before1961 = new GravityDemandModel(schedule: schedule, year: 1961).ForPair(farAway, byId["hav"]).PassengersPerQuarter;
        var during1962 = new GravityDemandModel(schedule: schedule, year: 1962).ForPair(farAway, byId["hav"]).PassengersPerQuarter;
        Assert.True(during1962 < before1961, "1962 should dip below 1961");

        // Permanence, proven against a same-year reference with the
        // embargo's own event group removed entirely (not just an
        // earlier pre-embargo year, since the trend curve itself keeps
        // moving) - the ratio should stay pinned at the authored 0.65
        // factor decades later, in 2015, not drift back toward 1.0.
        var embargoed2015 = new GravityDemandModel(schedule: schedule, year: 2015).ForPair(farAway, byId["hav"]).PassengersPerQuarter;
        var unaffected2015 = new GravityDemandModel(schedule: withoutEmbargo, year: 2015).ForPair(farAway, byId["hav"]).PassengersPerQuarter;

        var ratio = embargoed2015 / (double)unaffected2015;
        Assert.InRange(ratio, 0.64, 0.66);
    }

    [Fact]
    public void RealDataset_SuezClosure1967_DipsCairoForEightYears_RecoveredBy1980()
    {
        // Window 3 (spanning into 4 and 5) of the historical sweep: the
        // Six-Day War (June 1967) triggers Egypt closing the Suez
        // Canal, and sustained regional conflict/tension suppresses
        // travel to Cairo until the canal reopens in June 1975 - a
        // second, longer, later Cairo-scoped episode than round 6's
        // own 1956 Suez Crisis, modeled the same way (city-scoped
        // Event, explicit reversion) since it's the same real entity
        // (Egypt/the canal), just a longer episode.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var farAway = byId["gru"];

        // Isolate the event's own effect against a same-year reference
        // with both Suez-related event groups removed - the trend
        // curve itself also rises across this span (1955->1970 alone
        // is a step from 1.0x to 2.2x), so a raw before/after
        // comparison wouldn't isolate the closure's own effect.
        var withoutAnySuezEvent = schedule
            .Where(p => p.EventGroup != "suez-crisis-1956" && p.EventGroup != "suez-closure-1967")
            .ToList();

        var during1970 = new GravityDemandModel(schedule: schedule, year: 1970).ForPair(farAway, byId["cai"]).PassengersPerQuarter;
        var during1970Reference = new GravityDemandModel(schedule: withoutAnySuezEvent, year: 1970).ForPair(farAway, byId["cai"]).PassengersPerQuarter;
        Assert.True(during1970 < during1970Reference, "1970 (mid-closure) should be suppressed relative to the same year with no Suez-related event active");

        // Full recovery, well after the 1975 reopening - exact
        // equality against the same event-free reference, proving
        // both Suez episodes (1956 and 1967) are fully and correctly
        // resolved back to neutral by 1980.
        var after1980 = new GravityDemandModel(schedule: schedule, year: 1980).ForPair(farAway, byId["cai"]).PassengersPerQuarter;
        var after1980Reference = new GravityDemandModel(schedule: withoutAnySuezEvent, year: 1980).ForPair(farAway, byId["cai"]).PassengersPerQuarter;
        Assert.Equal(after1980Reference, after1980);
    }

    [Fact]
    public void RealDataset_CampDavidPeace1979_PermanentlyBoostsCairo_NeverReverts()
    {
        // Window 5 of the historical sweep: the Egypt-Israel Peace
        // Treaty (March 1979) normalizes relations and ends the state
        // of war - a genuine, permanent structural improvement in
        // Cairo's own stability, not a shock with a recovery arc, so
        // it's modeled the same permanent shape as the 747's own
        // step-up and the Cuba embargo: a single Event breakpoint with
        // no reversion, ever.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var withoutPeaceTreaty = schedule.Where(p => p.EventGroup != "camp-david-1979").ToList();
        var farAway = byId["gru"];

        var before1978 = new GravityDemandModel(schedule: schedule, year: 1978).ForPair(farAway, byId["cai"]).PassengersPerQuarter;
        var before1978Reference = new GravityDemandModel(schedule: withoutPeaceTreaty, year: 1978).ForPair(farAway, byId["cai"]).PassengersPerQuarter;
        Assert.Equal(before1978Reference, before1978);

        var during1979 = new GravityDemandModel(schedule: schedule, year: 1979).ForPair(farAway, byId["cai"]).PassengersPerQuarter;
        Assert.True(during1979 > before1978, "1979 should boost above the pre-treaty baseline");

        // Permanence, proven against a same-year reference with the
        // treaty's own event group removed, decades later in 2015 -
        // the with/without ratio should stay pinned at the authored
        // 1.15 factor, not fade back toward 1.0.
        var boosted2015 = new GravityDemandModel(schedule: schedule, year: 2015).ForPair(farAway, byId["cai"]).PassengersPerQuarter;
        var unaffected2015 = new GravityDemandModel(schedule: withoutPeaceTreaty, year: 2015).ForPair(farAway, byId["cai"]).PassengersPerQuarter;

        var ratio = boosted2015 / (double)unaffected2015;
        Assert.InRange(ratio, 1.14, 1.16);
    }

    [Fact]
    public void RealDataset_IranIraqWar_DipsBothTehranAndBaghdad_RecoveredAfter1988Ceasefire()
    {
        // Window 6 (spanning into window 7) of the historical sweep:
        // the Iran-Iraq War (Sept 1980 - Aug 1988) is the first event
        // in this sweep to touch two cities at once - Tehran and
        // Baghdad, both real dataset cities, both in SouthWestAsia.
        // Same EventGroup name for both city-scoped entries (the same
        // real historical episode), which is safe: GravityDemandModel
        // resolves each city's own event layer from only that city's
        // own schedule entries, so there's no cross-city grouping to
        // worry about.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var farAway = byId["gru"];

        foreach (var warCityId in new[] { "thr", "bgw" })
        {
            var warCity = byId[warCityId];

            // 1977 and 1989 both fall within the global trend's own
            // flat 1970-1990 step *and* outside every other global
            // event's own window (the second oil shock runs
            // 1979-1984, so 1979 itself is not a clean "before" year -
            // 1977 is) - a plain before/during/after comparison then
            // isolates the war's own effect without needing a
            // reference-schedule trick.
            var before1977 = new GravityDemandModel(schedule: schedule, year: 1977).ForPair(farAway, warCity).PassengersPerQuarter;
            var during1983 = new GravityDemandModel(schedule: schedule, year: 1983).ForPair(farAway, warCity).PassengersPerQuarter;
            var after1989 = new GravityDemandModel(schedule: schedule, year: 1989).ForPair(farAway, warCity).PassengersPerQuarter;

            Assert.True(during1983 < before1977, $"1983 should dip below 1977 for {warCityId}");
            Assert.Equal(before1977, after1989);
        }
    }

    [Fact]
    public void RealDataset_Chernobyl1986_DipsEuropeOnly_RecoveredBy1988()
    {
        // Window 7 of the historical sweep: Chernobyl (April 1986) is
        // the sweep's first Region-scoped event - unlike every prior
        // episode (all city-scoped), the fallout and the resulting
        // fear-driven tourism dip were genuinely pan-European, not
        // centered on one city, so it's modeled on Region.Europe
        // directly rather than picked city-by-city.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var farAway = byId["gru"];
        // London (lhr) - Europe, no other city-scoped entries active
        // in this window (its own Olympics event is 2012) - isolates
        // the region event cleanly.
        var europeCity = byId["lhr"];

        var before1985 = new GravityDemandModel(schedule: schedule, year: 1985).ForPair(farAway, europeCity).PassengersPerQuarter;
        var during1986 = new GravityDemandModel(schedule: schedule, year: 1986).ForPair(farAway, europeCity).PassengersPerQuarter;
        var after1988 = new GravityDemandModel(schedule: schedule, year: 1988).ForPair(farAway, europeCity).PassengersPerQuarter;

        Assert.True(during1986 < before1985, "1986 should dip below 1985 for a Europe pair");
        Assert.Equal(before1985, after1988);

        // Confirms this is genuinely Region-scoped, not accidentally
        // global: a same-year South America-South America pair
        // shouldn't move at all.
        var southAmericaControl = byId["scl"];
        var southAmericaBefore = new GravityDemandModel(schedule: schedule, year: 1985).ForPair(farAway, southAmericaControl).PassengersPerQuarter;
        var southAmericaDuring = new GravityDemandModel(schedule: schedule, year: 1986).ForPair(farAway, southAmericaControl).PassengersPerQuarter;
        Assert.Equal(southAmericaBefore, southAmericaDuring);
    }

    [Fact]
    public void RealDataset_BerlinWallFalls1989_PermanentlyBoostsBerlin_GrowingThroughReunification()
    {
        // Also window 7: unlike Chernobyl, the Berlin Wall falling is
        // modeled as a city Trend (Berlin's own multi-decade story of
        // rejoining the world and becoming the reunified capital),
        // not an Event - the same classification precedent as Dubai's
        // own 4-point trajectory, since this is a sustained structural
        // shift, not a shock with a recovery arc.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var farAway = byId["gru"];

        var dividedEra = new GravityDemandModel(schedule: schedule, year: 1955).ForPair(farAway, byId["ber"]).PassengersPerQuarter;
        var reunification = new GravityDemandModel(schedule: schedule, year: 1989).ForPair(farAway, byId["ber"]).PassengersPerQuarter;
        var reunifiedCapital = new GravityDemandModel(schedule: schedule, year: 2015).ForPair(farAway, byId["ber"]).PassengersPerQuarter;

        // A real, monotonic rise across the divided->reunified arc,
        // not just noise - global growth alone is already folded into
        // each of these (same as every other real-dataset check in
        // this file), so the ordering itself is the proof.
        Assert.True(reunification > dividedEra);
        Assert.True(reunifiedCapital > reunification);
    }

    [Fact]
    public void RealDataset_MoscowUssrDissolution_DipsIn1991ThenRecoversAndGrows()
    {
        // Window 8 of the historical sweep: unlike Dubai/Berlin's
        // clean monotonic rise, Moscow's real trajectory is genuinely
        // non-monotonic - the USSR's Dec 1991 dissolution opened
        // borders politically, but the immediate "shock therapy"
        // economic collapse (hyperinflation, ~40% GDP contraction)
        // made things *worse* before Putin-era stabilization made
        // them better. A pure city Trend, same classification as
        // Berlin, but shaped dip-then-recover rather than a straight
        // climb.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        // Moscow's own city-Trend effect has to be isolated against a
        // same-year reference with its entries removed - the global
        // trend curve (and, at 1991, the Gulf War's own global Event)
        // move independently across these same years and would
        // otherwise swamp a raw before/after comparison, the same
        // confound the Iran-Iraq test caught earlier in this series.
        var withoutMoscowTrend = schedule.Where(p => p.CityId != "mow").ToList();
        var farAway = byId["gru"];
        var moscow = byId["mow"];

        double MoscowRatio(int year)
        {
            var withTrend = new GravityDemandModel(schedule: schedule, year: year).ForPair(farAway, moscow).PassengersPerQuarter;
            var withoutTrend = new GravityDemandModel(schedule: withoutMoscowTrend, year: year).ForPair(farAway, moscow).PassengersPerQuarter;
            return withTrend / (double)withoutTrend;
        }

        var sovietEra = MoscowRatio(1955);
        var dissolution = MoscowRatio(1991);
        var stabilized = MoscowRatio(2000);
        var established = MoscowRatio(2015);

        // The dip: 1991 lands lower than the already-constrained
        // Soviet-era baseline, not higher - the newly opened borders
        // don't outweigh the economic collapse in this authored shape.
        Assert.True(dissolution < sovietEra, "1991 should dip below the Soviet-era baseline");
        Assert.True(stabilized > dissolution, "2000 should recover above 1991");
        Assert.True(established > stabilized, "2015 should keep growing past 2000");
    }

    [Fact]
    public void RealDataset_HongKongHandover1997_MildDip_RecoveredBy1999()
    {
        // Window 9 of the historical sweep: the July 1997 handover of
        // Hong Kong to China is real but historically mild in effect -
        // brief transitional uncertainty, quickly resolved, with the
        // city's role as a global aviation/financial hub continuing
        // essentially uninterrupted. Deliberately the smallest
        // magnitude authored anywhere in this sweep, and deliberately
        // kept separate from the unrelated, same-year Asian Financial
        // Crisis, which stays its own reserved future round.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var farAway = byId["gru"];

        var before1996 = new GravityDemandModel(schedule: schedule, year: 1996).ForPair(farAway, byId["hkg"]).PassengersPerQuarter;
        var during1997 = new GravityDemandModel(schedule: schedule, year: 1997).ForPair(farAway, byId["hkg"]).PassengersPerQuarter;
        var after1999 = new GravityDemandModel(schedule: schedule, year: 1999).ForPair(farAway, byId["hkg"]).PassengersPerQuarter;

        Assert.True(during1997 < before1996, "1997 should dip below 1996");
        Assert.Equal(before1996, after1999);
    }

    [Fact]
    public void RealDataset_Sars2003_DipsEastAsiaOnly_RecoveredBy2004()
    {
        // Window 10 of the historical sweep: SARS (Nov 2002 - July
        // 2003) is the sweep's second Region-scoped event - like
        // Chernobyl, the outbreak and the resulting collapse in air
        // travel were genuinely pan-regional (East Asia), not centered
        // on one city. 2003 also falls inside the unrelated global
        // 9/11 event's own 2001-2005 window, so a ratio against a
        // same-year SARS-free reference isolates the effect cleanly -
        // the 9/11 factor applies identically to both sides of the
        // ratio and cancels.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var withoutSars = schedule.Where(p => p.EventGroup != "sars-2003").ToList();
        var farAway = byId["gru"];
        // Tokyo (hnd) - EastAsia, no other city-scoped entry active in
        // this window (its own Olympics events are 1964 and 1998).
        var eastAsiaCity = byId["hnd"];

        double SarsRatio(int year)
        {
            var withEvent = new GravityDemandModel(schedule: schedule, year: year).ForPair(farAway, eastAsiaCity).PassengersPerQuarter;
            var withoutEvent = new GravityDemandModel(schedule: withoutSars, year: year).ForPair(farAway, eastAsiaCity).PassengersPerQuarter;
            return withEvent / (double)withoutEvent;
        }

        Assert.True(SarsRatio(2003) < 1.0, "2003 should be suppressed relative to a SARS-free reference");
        Assert.Equal(1.0, SarsRatio(2004));

        // Confirms this is genuinely Region-scoped, not accidentally
        // global: a same-region South America-South America pair
        // shouldn't move between 2002 and 2003.
        var southAmericaControl = byId["scl"];
        var southAmericaBefore = new GravityDemandModel(schedule: schedule, year: 2002).ForPair(farAway, southAmericaControl).PassengersPerQuarter;
        var southAmericaDuring = new GravityDemandModel(schedule: schedule, year: 2003).ForPair(farAway, southAmericaControl).PassengersPerQuarter;
        Assert.Equal(southAmericaBefore, southAmericaDuring);
    }

    [Fact]
    public void RealDataset_IraqWar2003_PermanentlyCollapsesBaghdad_SeparateFromThe1980sWar()
    {
        // Also window 10: the 2003 Iraq War is a second, later, far
        // more severe episode on Baghdad than the already-reverted
        // 1980-88 Iran-Iraq War - a real exercise of the same
        // multi-independent-event-group composition the Cairo events
        // already prove, this time with no reversion at all (unlike
        // the 1980s war's own ceasefire, the invasion's aftermath -
        // occupation, insurgency, ISIS's 2014 gains - never resolves
        // within the campaign's own 2015 end year).
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var withoutIraqWar2003 = schedule.Where(p => p.EventGroup != "iraq-war-2003").ToList();
        var farAway = byId["gru"];
        var baghdad = byId["bgw"];

        double IraqWarRatio(int year)
        {
            var withEvent = new GravityDemandModel(schedule: schedule, year: year).ForPair(farAway, baghdad).PassengersPerQuarter;
            var withoutEvent = new GravityDemandModel(schedule: withoutIraqWar2003, year: year).ForPair(farAway, baghdad).PassengersPerQuarter;
            return withEvent / (double)withoutEvent;
        }

        // Before the 2003 war (but after the 1980s war's own 1988
        // reversion), the ratio should be neutral - this event group
        // hasn't started yet.
        Assert.Equal(1.0, IraqWarRatio(2002));
        Assert.True(IraqWarRatio(2004) < 1.0, "2004 should be suppressed relative to a war-free reference");

        // Permanence: the ratio stays pinned at the authored 0.50
        // factor as late as 2015, not drifting back toward 1.0.
        Assert.InRange(IraqWarRatio(2015), 0.49, 0.51);
    }

    [Fact]
    public void RealDataset_Eyjafjallajokull2010_DipsEuropeOnly_RecoveredBy2011()
    {
        // Window 12 of the historical sweep: Eyjafjallajokull's April
        // 2010 eruption shut down European airspace for over a week -
        // the sweep's third Region-scoped event, and the first purely
        // aviation-driven one (unlike Chernobyl's fear-driven tourism
        // dip or SARS's outbreak-driven collapse). 2010 also falls
        // inside the unrelated global financial crisis's own
        // 2009-2011 window, so a ratio against a same-year event-free
        // reference isolates the effect cleanly.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var withoutEruption = schedule.Where(p => p.EventGroup != "eyjafjallajokull-2010").ToList();
        var farAway = byId["gru"];
        var europeCity = byId["lhr"];

        double EruptionRatio(int year)
        {
            var withEvent = new GravityDemandModel(schedule: schedule, year: year).ForPair(farAway, europeCity).PassengersPerQuarter;
            var withoutEvent = new GravityDemandModel(schedule: withoutEruption, year: year).ForPair(farAway, europeCity).PassengersPerQuarter;
            return withEvent / (double)withoutEvent;
        }

        Assert.True(EruptionRatio(2010) < 1.0, "2010 should be suppressed relative to an eruption-free reference");
        Assert.Equal(1.0, EruptionRatio(2011));
    }

    [Fact]
    public void RealDataset_ChinasOpening_GrowsBeijingAndShanghai_AcrossReformEra()
    {
        // China's opening - reserved since round 1/2, authored here:
        // Deng Xiaoping's reforms (Third Plenum, Dec 1978) through WTO
        // accession (2001) to 2015, modeled as the same permanent
        // Trend shape on both real Chinese cities in the dataset,
        // Beijing and Shanghai - the same multi-city, same-shape
        // pattern already established for the Iran-Iraq War.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var farAway = byId["gru"];

        foreach (var chinaCityId in new[] { "pek", "pvg" })
        {
            var chinaCity = byId[chinaCityId];
            // Remove only this city's own Trend entries - Beijing also
            // carries its own 2008 Olympics Event, which must stay in
            // place so the ratio isolates the Trend layer specifically.
            var withoutChinaTrend = schedule
                .Where(p => !(p.CityId == chinaCityId && p.Kind == DemandGrowthPointKind.Trend))
                .ToList();

            double ChinaRatio(int year)
            {
                var withTrend = new GravityDemandModel(schedule: schedule, year: year).ForPair(farAway, chinaCity).PassengersPerQuarter;
                var withoutTrend = new GravityDemandModel(schedule: withoutChinaTrend, year: year).ForPair(farAway, chinaCity).PassengersPerQuarter;
                return withTrend / (double)withoutTrend;
            }

            var maoistEra = ChinaRatio(1955);
            var reformsBegin = ChinaRatio(1978);
            var wtoEra = ChinaRatio(2001);
            var established = ChinaRatio(2015);

            Assert.True(reformsBegin > maoistEra, $"{chinaCityId}: 1978 should exceed 1955");
            Assert.True(wtoEra > reformsBegin, $"{chinaCityId}: 2001 should exceed 1978");
            Assert.True(established > wtoEra, $"{chinaCityId}: 2015 should exceed 2001");
        }
    }

    [Fact]
    public void RealDataset_BeijingTrendAndOlympicsEvent_ComposeIndependently()
    {
        // First real case in this series of a Trend and an Event on
        // the SAME city: Beijing carries both China's opening (a
        // permanent Trend) and its own 2008 Olympics host-year boost
        // (a temporary Event). They should compose multiplicatively,
        // exactly like Trend x Event already compose at every other
        // scope - confirmed directly here rather than assumed from the
        // global-scope tests alone.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var farAway = byId["gru"];
        var beijing = byId["pek"];

        var withoutOlympics = schedule.Where(p => p.EventGroup != "olympics-2008-summer").ToList();

        var during2008WithBoth = new GravityDemandModel(schedule: schedule, year: 2008).ForPair(farAway, beijing).PassengersPerQuarter;
        var during2008TrendOnly = new GravityDemandModel(schedule: withoutOlympics, year: 2008).ForPair(farAway, beijing).PassengersPerQuarter;

        // The Olympics boost is still visible on top of whatever the
        // China-opening Trend alone contributes in 2008 - the trend
        // doesn't swallow or get swallowed by the event.
        Assert.True(during2008WithBoth > during2008TrendOnly);

        var ratio = during2008WithBoth / (double)during2008TrendOnly;
        Assert.InRange(ratio, 1.19, 1.21); // the Olympics' own authored 1.2x, isolated
    }

    [Fact]
    public void RealDataset_AsianFinancialCrisis1997_DipsEastAsiaOnly_RecoveredBy1999()
    {
        // The 1997 Asian Financial Crisis - reserved since round 1/2,
        // authored here: deliberately kept separate from the same-year
        // Hong Kong Handover, a different real cause. The sweep's
        // fourth Region-scoped event.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var withoutCrisis = schedule.Where(p => p.EventGroup != "asian-financial-crisis-1997").ToList();
        var farAway = byId["gru"];
        // Seoul (icn) - EastAsia, own Olympics event is 1988, well
        // clear of this window.
        var eastAsiaCity = byId["icn"];

        double CrisisRatio(int year)
        {
            var withEvent = new GravityDemandModel(schedule: schedule, year: year).ForPair(farAway, eastAsiaCity).PassengersPerQuarter;
            var withoutEvent = new GravityDemandModel(schedule: withoutCrisis, year: year).ForPair(farAway, eastAsiaCity).PassengersPerQuarter;
            return withEvent / (double)withoutEvent;
        }

        Assert.True(CrisisRatio(1997) < 1.0, "1997 should be suppressed relative to a crisis-free reference");
        Assert.Equal(1.0, CrisisRatio(1999));

        // Confirms this composes independently from Hong Kong's own
        // city-scoped Handover event, both active in 1997: Hong Kong
        // should be suppressed further by the region-wide crisis on
        // top of its own handover dip, not have one silently override
        // the other.
        var hongKong = byId["hkg"];
        var hongKongBoth1997 = new GravityDemandModel(schedule: schedule, year: 1997).ForPair(farAway, hongKong).PassengersPerQuarter;
        var hongKongHandoverOnly = new GravityDemandModel(schedule: withoutCrisis, year: 1997).ForPair(farAway, hongKong).PassengersPerQuarter;
        Assert.True(hongKongBoth1997 < hongKongHandoverOnly, "Hong Kong in 1997 should be suppressed further by the region-wide crisis on top of its own handover dip");
    }

    [Fact]
    public void RealDataset_ArabSpring2011_PermanentlyCollapsesCairo_FourthIndependentLayer()
    {
        // Also window 12: the Arab Spring is Cairo's fourth
        // independent event layer (alongside the 1956 Suez Crisis, the
        // 1967-75 closure, and the 1979 Camp David boost) - the
        // deepest real exercise yet of the multi-independent-group
        // composition architecture. Political instability following
        // Mubarak's ouster doesn't meaningfully resolve within the
        // campaign's own 2015 end year, so - like the 2003 Iraq War -
        // it's modeled with no reversion.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var withoutArabSpring = schedule.Where(p => p.EventGroup != "arab-spring-2011").ToList();
        var farAway = byId["gru"];
        var cairo = byId["cai"];

        double ArabSpringRatio(int year)
        {
            var withEvent = new GravityDemandModel(schedule: schedule, year: year).ForPair(farAway, cairo).PassengersPerQuarter;
            var withoutEvent = new GravityDemandModel(schedule: withoutArabSpring, year: year).ForPair(farAway, cairo).PassengersPerQuarter;
            return withEvent / (double)withoutEvent;
        }

        Assert.Equal(1.0, ArabSpringRatio(2010));
        Assert.True(ArabSpringRatio(2012) < 1.0, "2012 should be suppressed relative to an Arab-Spring-free reference");
        Assert.InRange(ArabSpringRatio(2015), 0.69, 0.71);
    }

    [Fact]
    public void RealDataset_OlympicsEvents_BoostHostYearOnly_NotAdjacentYears()
    {
        // Stress-tests the trend/event architecture against a real,
        // large batch of independent city-scoped events (every Olympic
        // Games 1956-2015 whose host city exists in cities.json) rather
        // than just one hand-picked example - every one of them uses the
        // same EventGroup-based composition this round's own bug fix
        // depends on, and this proves it holds up at scale, against the
        // real repo data, not just a small scripted scenario.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var schedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data"));
        var farAway = byId["gru"]; // unrelated to every host city below

        // (hostCityId, hostYear) - one representative per era, not
        // exhaustively every one of the 16 authored events (that's what
        // the data file itself documents); early, middle, and late
        // enough to also prove the pattern holds across very different
        // trend/other-event backdrops.
        var hosts = new[]
        {
            ("mel", 1956), // Melbourne - earliest, right after the file's own baseline year
            ("lax", 1984), // Los Angeles - mid-campaign
            ("pek", 2008), // Beijing - overlaps the very next year with the global financial-crisis event
            ("lhr", 2012), // London - latest, a city with its own real gateway weight already high
        };

        foreach (var (hostCityId, hostYear) in hosts)
        {
            var host = byId[hostCityId];
            var before = new GravityDemandModel(schedule: schedule, year: hostYear - 1).ForPair(farAway, host).PassengersPerQuarter;
            var during = new GravityDemandModel(schedule: schedule, year: hostYear).ForPair(farAway, host).PassengersPerQuarter;
            var after = new GravityDemandModel(schedule: schedule, year: hostYear + 2).ForPair(farAway, host).PassengersPerQuarter; // +2, past the reversion year

            Assert.True(during > before, $"{hostCityId} {hostYear}: host year should exceed the year before it");
            Assert.True(during > after, $"{hostCityId} {hostYear}: host year should exceed the year after reversion");
        }
    }

    // --- M63: hub-and-spoke demand, step 2 - bounded, additive, off-by-default HubQuality bonus ---

    [Fact]
    public void ForPair_HubDemandBonusDisabledByDefault_MatchesPreExistingFormula_ByteIdentical()
    {
        // Every pre-M63 call site (and any new one that doesn't opt in)
        // omits enableHubDemandBonus - HubQuality must never be read, and
        // output must be exactly what it always was, even for a city that
        // carries a nonzero HubQuality.
        var a = MakeCity("a", 0, 0, 50);
        var b = new City("hub", "HUB", Region.EastAsia, 0, 10, 50, false, null, AutoCapacity: null, HubQuality: 40);
        var withoutHubQualityField = MakeCity("hub_noquality", 0, 10, 50);

        var model = new GravityDemandModel();

        Assert.Equal(
            model.ForPair(a, withoutHubQualityField).PassengersPerQuarter,
            model.ForPair(a, b).PassengersPerQuarter);
    }

    [Fact]
    public void ForPair_HubDemandBonusEnabled_ButHubQualityZero_MatchesDisabledOutput_ByteIdentical()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 0, 10, 50); // HubQuality defaults to 0

        var disabled = new GravityDemandModel();
        var enabled = new GravityDemandModel(enableHubDemandBonus: true);

        Assert.Equal(disabled.ForPair(a, b).PassengersPerQuarter, enabled.ForPair(a, b).PassengersPerQuarter);
    }

    [Fact]
    public void ForPair_HubDemandBonusEnabled_BoostsPassengersOnHighHubQualitySide()
    {
        // weight=50, HubQuality=20 -> effective weight 70, a 1.4x factor on
        // that side of the product alone (the other side, a, is unchanged).
        // Compared with a tolerance, not exact equality: enabledDemand and
        // disabledDemand are each independently rounded from their own raw
        // (unrounded) figure, and 1.4x the already-rounded disabled value
        // can legitimately land 1 passenger off from the true (pre-rounding)
        // 1.4x relationship - confirmed empirically before writing this
        // assertion, not assumed.
        var a = MakeCity("a", 0, 0, 50);
        var hub = new City("hub", "HUB", Region.EastAsia, 0, 10, 50, false, null, AutoCapacity: null, HubQuality: 20);

        var disabled = new GravityDemandModel();
        var enabled = new GravityDemandModel(enableHubDemandBonus: true);

        var disabledDemand = disabled.ForPair(a, hub).PassengersPerQuarter;
        var enabledDemand = enabled.ForPair(a, hub).PassengersPerQuarter;
        var expected = disabledDemand * 1.4;

        Assert.InRange(enabledDemand, expected - 2, expected + 2);
    }

    [Fact]
    public void ForPair_HubDemandBonusEnabled_BothSidesHaveHubQuality_MultipliesBothRatios()
    {
        // a: weight 40 + HubQuality 10 -> 1.25x. b: weight 50 + HubQuality
        // 20 -> 1.4x. Combined effect on the pair should be 1.25 * 1.4 = 1.75x.
        // Tolerance for the same double-rounding reason as the single-side
        // test above.
        var a = new City("a", "A", Region.EastAsia, 0, 0, 40, false, null, AutoCapacity: null, HubQuality: 10);
        var b = new City("b", "B", Region.EastAsia, 0, 10, 50, false, null, AutoCapacity: null, HubQuality: 20);

        var disabled = new GravityDemandModel();
        var enabled = new GravityDemandModel(enableHubDemandBonus: true);

        var disabledDemand = disabled.ForPair(a, b).PassengersPerQuarter;
        var enabledDemand = enabled.ForPair(a, b).PassengersPerQuarter;
        var expected = disabledDemand * 1.75;

        Assert.InRange(enabledDemand, expected - 2, expected + 2);
    }

    [Fact]
    public void RealDataset_HubDemandBonus_NeverExceedsBoundedRatio()
    {
        // The exact check that would have caught the earlier, reverted M60
        // exponent-discount attempt's 31x world-demand inflation on its
        // first run - now a permanent regression test. Every real pair's
        // enabled/disabled ratio is bounded in advance by
        // CityCatalog's own ratio cap on each side of the pair: at most
        // (1 + MaxHubQualityToDemandWeightRatio)^2.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var disabled = new GravityDemandModel();
        var enabled = new GravityDemandModel(enableHubDemandBonus: true);
        var maxPossibleRatio = Math.Pow(1 + CityCatalog.MaxHubQualityToDemandWeightRatio, 2);

        foreach (var pairDisabled in disabled.AllPairs(cities))
        {
            var a = pairDisabled.Origin;
            var b = pairDisabled.Destination;
            var pairEnabled = enabled.ForPair(a, b);

            if (pairDisabled.PassengersPerQuarter == 0) continue;

            var ratio = (double)pairEnabled.PassengersPerQuarter / pairDisabled.PassengersPerQuarter;
            Assert.True(ratio <= maxPossibleRatio + 0.01,
                $"{a.Id}-{b.Id}: ratio {ratio:N3} exceeds the theoretical max {maxPossibleRatio:N3}.");
        }
    }

    [Fact]
    public void RealDataset_AllPairs_HubDemandBonus_TotalWorldDemandStaysWithinBoundedMultiplier()
    {
        // The exact aggregate-magnitude check the M60 exponent-discount
        // attempt skipped before shipping to a real 300-game sweep (which
        // caught a 31x inflation only after the fact). Measured directly
        // against the real dataset before this test was written: 1.157x -
        // asserting a generous 1.5x ceiling here, comfortably above the
        // measured figure but nowhere near the earlier failure's scale.
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var disabled = new GravityDemandModel();
        var enabled = new GravityDemandModel(enableHubDemandBonus: true);

        var totalDisabled = disabled.AllPairs(cities).Sum(p => p.PassengersPerQuarter);
        var totalEnabled = enabled.AllPairs(cities).Sum(p => p.PassengersPerQuarter);

        Assert.True(totalEnabled < totalDisabled * 1.5,
            $"Total world demand inflated {totalEnabled / (double)totalDisabled:N3}x - should stay well under 1.5x.");
    }

    [Fact]
    public void RealDataset_HubDemandBonusEnabled_MajorMarketStillDwarfsChokepointPair()
    {
        // The same ranking-preservation property RealDataset_MajorMarketDwarfsChokepointPair
        // already proves for the disabled model - confirms enabling the
        // bonus doesn't disturb it (neither JFK/LHR nor Gander/Azores
        // carry meaningful HubQuality, so this should hold essentially
        // unchanged).
        var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
        var byId = cities.ToDictionary(c => c.Id);
        var model = new GravityDemandModel(enableHubDemandBonus: true);

        var jfkLhr = model.ForPair(byId["jfk"], byId["lhr"]);
        var yqxTer = model.ForPair(byId["yqx"], byId["ter"]);

        Assert.True(jfkLhr.PassengersPerQuarter > yqxTer.PassengersPerQuarter * 100);
    }
}
