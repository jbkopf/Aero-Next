using AeroNext.Core.Airlines;
using AeroNext.Core.Negotiations;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>docs/HUB_CAPACITY_GROWTH_DESIGN.md: hub capacity follows each hub's real traffic history.</summary>
public class HubCapacityProfileTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    private static HubCapacityProfile Profile(string id, params (int Year, double Value)[] points) =>
        new(id, "test", points.Select(p => new HubTrafficPoint(p.Year, p.Value)).ToList());

    /// <summary>The first quarter of a year (Timeline.YearForQuarter's inverse).</summary>
    private static int QuarterOf(int year) => (year - Timeline.StartYear) * 4 + 1;

    private static City MakeCity(string id, int autoCapacity, bool isGateway = false) =>
        new(id, id.ToUpperInvariant(), Region.Europe, 0, 0, 50, isGateway, isGateway ? autoCapacity : null, isGateway ? null : autoCapacity);

    // --- Profile arithmetic ---

    [Fact]
    public void TrafficIn_InterpolatesLogLinearly_AndHoldsFlatOutsideTheHistory()
    {
        var profile = Profile("x", (1960, 1), (1970, 100));

        Assert.Equal(1, profile.TrafficIn(1955), precision: 9);
        Assert.Equal(10, profile.TrafficIn(1965), precision: 9); // geometric midpoint
        Assert.Equal(100, profile.TrafficIn(1990), precision: 9);
    }

    [Fact]
    public void PeakTrafficBy_IsARatchet_ADipNeverLowersIt()
    {
        // Moscow-shaped: up, collapse, recovery.
        var profile = Profile("mow", (1985, 30), (1995, 14), (2005, 35));

        Assert.Equal(30, profile.PeakTrafficBy(1995), precision: 9);
        Assert.Equal(35, profile.PeakTrafficBy(2005), precision: 9);
    }

    [Fact]
    public void CapacityIn_IsScaleTimesTheSquareRootOfPeakTraffic_RoundedDown()
    {
        Assert.Equal(16, Profile("x", (2000, 100)).CapacityIn(2000)); // 1.6 x 10
        Assert.Equal(10, Profile("x", (2000, 40)).CapacityIn(2000));  // 1.6 x 6.32 = 10.1
        Assert.Equal(1, Profile("x", (2000, 0.5)).CapacityIn(2000));  // 1.6 x 0.71 = 1.13
    }

    // --- Loader ---

    [Fact]
    public void RealDataFile_LoadsEveryProfile_AgainstTheRealCities()
    {
        var cities = CityCatalog.LoadFile(Path.Combine(DataDir, "cities.json"));
        var profiles = HubTrafficHistoryCatalog.LoadFile(Path.Combine(DataDir, "hub_traffic_history.json"), cities.Select(c => c.Id).ToHashSet());

        Assert.True(profiles.Count >= 30);
        Assert.Contains("dxb", profiles.Keys);
        Assert.All(profiles.Values, p => Assert.False(string.IsNullOrWhiteSpace(p.History)));
    }

    [Theory]
    [InlineData("""[{ "cityId": "nowhere", "history": "h", "annualPassengersMillions": [{ "year": 1955, "value": 1 }] }]""")]
    [InlineData("""[{ "cityId": "a", "history": "h", "annualPassengersMillions": [] }]""")]
    [InlineData("""[{ "cityId": "a", "history": "h", "annualPassengersMillions": [{ "year": 1965, "value": 1 }, { "year": 1955, "value": 2 }] }]""")]
    [InlineData("""[{ "cityId": "a", "history": "h", "annualPassengersMillions": [{ "year": 1955, "value": 0 }] }]""")]
    [InlineData("""[{ "cityId": "a", "history": "h", "annualPassengersMillions": [{ "year": 1955, "value": 1 }] }, { "cityId": "a", "history": "h", "annualPassengersMillions": [{ "year": 1955, "value": 1 }] }]""")]
    [InlineData("""[{ "cityId": "", "history": "h", "annualPassengersMillions": [{ "year": 1955, "value": 1 }] }]""")]
    [InlineData("""null""")]
    public void Loader_RejectsInvalidData(string json)
    {
        Assert.Throws<InvalidDataException>(() => HubTrafficHistoryCatalog.Load(json, new HashSet<string> { "a" }));
    }

    // --- Capacity board ---

    [Fact]
    public void Initialize_AHubAlreadyBigIn1955_StartsAboveItsAuthoredCapacity_GatewaysIgnored()
    {
        // 25M passengers in 1955 -> profile capacity 8 (1.6 x 5).
        var hub = MakeCity("hub", autoCapacity: 3);
        var gateway = MakeCity("gw", autoCapacity: 3, isGateway: true);
        var plain = MakeCity("plain", autoCapacity: 3);
        var profiles = new Dictionary<string, HubCapacityProfile> { ["hub"] = Profile("hub", (1955, 25)), ["gw"] = Profile("gw", (1955, 25)) };

        var board = CityCapacityBoard.Initialize(new[] { hub, gateway, plain }, new GravityDemandModel(), profiles);

        Assert.Equal(5, board.CapacityBonusByCity["hub"]);
        Assert.Equal(0, board.CapacityBonusByCity["gw"]);
        Assert.Equal(0, board.CapacityBonusByCity["plain"]);
    }

    [Fact]
    public void AdvanceQuarter_WithProfiles_GrowsBetweenTheFiveYearChecks()
    {
        // 1M (cap 1, floored to the authored 3) rising to 100M (cap 16) by 1965.
        var hub = MakeCity("hub", autoCapacity: 3);
        var profiles = new Dictionary<string, HubCapacityProfile> { ["hub"] = Profile("hub", (1955, 1), (1965, 100)) };
        var board = CityCapacityBoard.Initialize(new[] { hub }, new GravityDemandModel(), profiles);

        var in1960 = board.AdvanceQuarter(QuarterOf(1960), new[] { hub }, new GravityDemandModel());
        var in1965 = in1960.AdvanceQuarter(QuarterOf(1965), new[] { hub }, new GravityDemandModel());

        Assert.Equal(0, board.CapacityBonusByCity["hub"]);
        Assert.Equal(Profile("hub", (1955, 1), (1965, 100)).CapacityIn(1960) - 3, in1960.CapacityBonusByCity["hub"]);
        Assert.Equal(13, in1965.CapacityBonusByCity["hub"]);
    }

    [Fact]
    public void AdvanceQuarter_TakesTheHigherOfTheRuleAndTheProfile()
    {
        // A bonus already earned under the demand rule stays when the
        // profile asks for less.
        var hub = MakeCity("hub", autoCapacity: 3);
        var profiles = new Dictionary<string, HubCapacityProfile> { ["hub"] = Profile("hub", (1955, 1)) };
        var board = CityCapacityBoard.Initialize(new[] { hub }, new GravityDemandModel(), profiles)
            with { CapacityBonusByCity = new Dictionary<string, int> { ["hub"] = 2 } };

        var next = board.AdvanceQuarter(3, new[] { hub }, new GravityDemandModel());

        Assert.Equal(2, next.CapacityBonusByCity["hub"]);
    }

    // --- Hub demand coverage ---

    private static readonly AircraftType Plane = new("p", "P", Seats: 100, RangeKm: 9_000);

    [Fact]
    public void DemandCoverageBonus_CoversEightyPercentOfDemand_AboveTheStartingCapacity()
    {
        var hub = MakeCity("hub", autoCapacity: 3);
        var profiles = new Dictionary<string, HubCapacityProfile> { ["hub"] = Profile("hub", (1955, 1)) };

        // 0.8 x 100,000 / 9,000 = 8.9 -> 9 aircraft, 6 above the starting 3.
        Assert.Equal(6, CityCapacityBoard.DemandCoverageBonus(hub, profiles, cityDemand: 100_000, seatsPerAircraft: 9_000));
        // Already covered by the starting capacity.
        Assert.Equal(0, CityCapacityBoard.DemandCoverageBonus(hub, profiles, cityDemand: 10_000, seatsPerAircraft: 9_000));
    }

    [Fact]
    public void DemandCoverageBonus_IsZeroForGatewaysUnprofiledCitiesAndNoSeatAverage()
    {
        var gateway = MakeCity("gw", autoCapacity: 3, isGateway: true);
        var plain = MakeCity("plain", autoCapacity: 3);
        var hub = MakeCity("hub", autoCapacity: 3);
        var profiles = new Dictionary<string, HubCapacityProfile> { ["gw"] = Profile("gw", (1955, 1)), ["hub"] = Profile("hub", (1955, 1)) };

        Assert.Equal(0, CityCapacityBoard.DemandCoverageBonus(gateway, profiles, 100_000, 9_000));
        Assert.Equal(0, CityCapacityBoard.DemandCoverageBonus(plain, profiles, 100_000, 9_000));
        Assert.Equal(0, CityCapacityBoard.DemandCoverageBonus(hub, null, 100_000, 9_000));
        Assert.Equal(0, CityCapacityBoard.DemandCoverageBonus(hub, profiles, 100_000, 0));
    }

    [Fact]
    public void SeatsPerAircraftByCity_AveragesRoutesEndingThere_IgnoresStops_FallsBackToTheNetwork()
    {
        var a = new City("a", "A", Region.Europe, 0, 0, 50, false, null, 3);
        var b = new City("b", "B", Region.Europe, 0, 10, 50, false, null, 3);
        var stop = new City("s", "S", Region.Europe, 0, 20, 50, true, 3, null);
        var c = new City("c", "C", Region.Europe, 0, 30, 50, false, null, 3);
        var idle = new City("idle", "IDLE", Region.Europe, 0, 40, 50, false, null, 3);
        var ab = new Route(a, b, Plane, AircraftCount: 2);
        var bc = new Route(b, c, Plane, AircraftCount: 1, Stops: StopList.Of(stop));

        var seats = CityCapacityBoard.SeatsPerAircraftByCity(new[] { ab, bc }, new[] { a, b, stop, c, idle });

        Assert.Equal(ab.SeatsPerQuarter / 2.0, seats["a"], precision: 6);
        Assert.Equal((ab.SeatsPerQuarter + bc.SeatsPerQuarter) / 3.0, seats["b"], precision: 6);
        Assert.Equal(bc.SeatsPerQuarter, seats["c"], precision: 6);
        Assert.Equal((ab.SeatsPerQuarter + bc.SeatsPerQuarter) / 3.0, seats["s"], precision: 6); // a stop only: network average
        Assert.Equal((ab.SeatsPerQuarter + bc.SeatsPerQuarter) / 3.0, seats["idle"], precision: 6);
        Assert.Empty(CityCapacityBoard.SeatsPerAircraftByCity(Array.Empty<Route>(), new[] { a }));
    }

    [Fact]
    public void AdvanceQuarter_WithSeatAverages_RaisesAProfiledHubToCoverItsDemand_AndNeverLowersIt()
    {
        var hub = MakeCity("hub", autoCapacity: 3);
        var other = new City("other", "OTHER", Region.Europe, 0, 10, 90, false, null, 3);
        var cities = new[] { hub, other };
        var model = new GravityDemandModel();
        var profiles = new Dictionary<string, HubCapacityProfile> { ["hub"] = Profile("hub", (1955, 1)) };
        var board = CityCapacityBoard.Initialize(cities, model, profiles);
        var demand = model.ForPair(hub, other).PassengersPerQuarter;
        var expected = (int)Math.Ceiling(0.8 * demand / 1_000.0) - 3;

        var covered = board.AdvanceQuarter(3, cities, model, new Dictionary<string, double> { ["hub"] = 1_000, ["other"] = 1_000 });
        var withoutSeats = board.AdvanceQuarter(3, cities, model);
        var biggerPlanes = covered.AdvanceQuarter(4, cities, model, new Dictionary<string, double> { ["hub"] = 1_000_000, ["other"] = 1_000_000 });

        Assert.True(expected > 0);
        Assert.Equal(expected, covered.CapacityBonusByCity["hub"]);
        Assert.Equal(0, covered.CapacityBonusByCity["other"]); // no profile
        Assert.Equal(0, withoutSeats.CapacityBonusByCity["hub"]);
        Assert.Equal(expected, biggerPlanes.CapacityBonusByCity["hub"]); // a ratchet
    }
}
