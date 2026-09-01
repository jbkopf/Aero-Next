using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>M45: direct tests for the new AiToolkit statics the two real strategies wire alongside their existing gateway-slot checks.</summary>
public class AiToolkitTests
{
    private static City MakeCity(string id, Region region, double lat, double lon, int weight, bool isGateway = false, int? autoCapacity = null) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, isGateway, isGateway ? 3 : null, isGateway ? null : autoCapacity);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly AircraftType Aircraft = new("t", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);

    private static StrategyContext MakeContext(IReadOnlyList<City> cities, City homeCity, System.Collections.Generic.IReadOnlyDictionary<string, int>? idleContestableSlotsByCity = null) =>
        new(Array.Empty<Route>(), 3, cities, DemandModel, new[] { Aircraft }, AirlineFleet.Empty, homeCity.Region, homeCity, IdleContestableSlotsByCity: idleContestableSlotsByCity);

    // --- ComputeCityWideSlotsRemaining ---

    [Fact]
    public void ComputeCityWideSlotsRemaining_NoRoutesAnywhere_ReturnsFullCapacity()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>>
        {
            ["A"] = Array.Empty<Route>(),
        };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy });

        Assert.Equal(3, result["bz"]);
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_SumsAircraftCountAcrossEveryAirline_NotJustOne()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 5);
        var elsewhere = MakeCity("el", Region.NorthAmerica, 1, 1, 50);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>>
        {
            ["A"] = new[] { new Route(busy, elsewhere, Aircraft, AircraftCount: 2) },
            ["B"] = new[] { new Route(elsewhere, busy, Aircraft, AircraftCount: 1) }, // busy is the destination here
        };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy, elsewhere });

        Assert.Equal(2, result["bz"]); // 5 - (2 + 1)
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_UsageExceedsCapacity_FloorsAtZero_NeverNegative()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var elsewhere = MakeCity("el", Region.NorthAmerica, 1, 1, 50);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>>
        {
            ["A"] = new[] { new Route(busy, elsewhere, Aircraft, AircraftCount: 5) },
        };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy });

        Assert.Equal(0, result["bz"]);
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_CityWithNoAutoCapacity_NeverAppearsInResult()
    {
        var plain = MakeCity("pl", Region.NorthAmerica, 0, 0, 50); // AutoCapacity: null
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>> { ["A"] = Array.Empty<Route>() };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { plain });

        Assert.False(result.ContainsKey("pl"));
    }

    // --- HasRoomAtEveryCity ---

    [Fact]
    public void HasRoomAtEveryCity_NullBalance_AlwaysTrue()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50, autoCapacity: 1);

        Assert.True(AiToolkit.HasRoomAtEveryCity(a, b, null, new System.Collections.Generic.Dictionary<string, int>()));
    }

    [Fact]
    public void HasRoomAtEveryCity_PositiveRemainingBalance_True()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 1 };

        Assert.True(AiToolkit.HasRoomAtEveryCity(a, b, remaining, new System.Collections.Generic.Dictionary<string, int>()));
    }

    [Fact]
    public void HasRoomAtEveryCity_ZeroRemainingBalance_False()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 0 };

        Assert.False(AiToolkit.HasRoomAtEveryCity(a, b, remaining, new System.Collections.Generic.Dictionary<string, int>()));
    }

    [Fact]
    public void HasRoomAtEveryCity_AlreadyReservedThisPass_ConsumesTheBalance()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 1 };
        var reserved = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 1 };

        Assert.False(AiToolkit.HasRoomAtEveryCity(a, b, remaining, reserved));
    }

    [Fact]
    public void HasRoomAtEveryCity_CityWithNoAutoCapacity_NeverGated()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50); // AutoCapacity: null
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int>(); // "a" absent - would read as 0 if checked

        Assert.True(AiToolkit.HasRoomAtEveryCity(a, b, remaining, new System.Collections.Generic.Dictionary<string, int>()));
    }

    // --- ReserveCityWideSlots ---

    [Fact]
    public void ReserveCityWideSlots_IncrementsBothEndpoints_WhenBothAreAutoCapacityCities()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50, autoCapacity: 3);
        var reserved = new System.Collections.Generic.Dictionary<string, int>();

        AiToolkit.ReserveCityWideSlots(a, b, reserved);

        Assert.Equal(1, reserved["a"]);
        Assert.Equal(1, reserved["b"]);
    }

    [Fact]
    public void ReserveCityWideSlots_SkipsEndpointWithNoAutoCapacity()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50); // AutoCapacity: null
        var reserved = new System.Collections.Generic.Dictionary<string, int>();

        AiToolkit.ReserveCityWideSlots(a, b, reserved);

        Assert.Equal(1, reserved["a"]);
        Assert.False(reserved.ContainsKey("b"));
    }

    // --- FindBestIdleContestableGateway ---

    [Fact]
    public void FindBestIdleContestableGateway_NullField_ReturnsNull()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, idleContestableSlotsByCity: null);

        Assert.Null(AiToolkit.FindBestIdleContestableGateway(context));
    }

    [Fact]
    public void FindBestIdleContestableGateway_PicksHighestDemandWeight()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var small = MakeCity("small", Region.NorthAmerica, 1, 1, 40, isGateway: true);
        var big = MakeCity("big", Region.NorthAmerica, 2, 2, 90, isGateway: true);
        var idle = new System.Collections.Generic.Dictionary<string, int> { ["small"] = 1, ["big"] = 1 };
        var context = MakeContext(new[] { home, small, big }, home, idleContestableSlotsByCity: idle);

        var result = AiToolkit.FindBestIdleContestableGateway(context);

        Assert.Equal("big", result!.Id);
    }

    [Fact]
    public void FindBestIdleContestableGateway_ZeroCountCity_NeverPicked()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var zero = MakeCity("zero", Region.NorthAmerica, 1, 1, 90, isGateway: true);
        var idle = new System.Collections.Generic.Dictionary<string, int> { ["zero"] = 0 };
        var context = MakeContext(new[] { home, zero }, home, idleContestableSlotsByCity: idle);

        Assert.Null(AiToolkit.FindBestIdleContestableGateway(context));
    }

    [Fact]
    public void FindBestIdleContestableGateway_AlreadyPursued_Excluded()
    {
        // "target" has idle-expired inventory but this airline already
        // holds an available (won-but-unused) slot there - same exclusion
        // every other negotiation-target search already applies via
        // AiToolkit.CitiesAlreadyPursuedForNegotiation.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var target = MakeCity("target", Region.NorthAmerica, 1, 1, 90, isGateway: true);
        var idle = new System.Collections.Generic.Dictionary<string, int> { ["target"] = 1 };
        var context = MakeContext(new[] { home, target }, home, idleContestableSlotsByCity: idle) with
        {
            AvailableSlotsByCity = new System.Collections.Generic.Dictionary<string, int> { ["target"] = 1 },
        };

        Assert.Null(AiToolkit.FindBestIdleContestableGateway(context));
    }
}
