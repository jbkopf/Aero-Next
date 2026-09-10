using AeroNext.Core.Airlines;
using AeroNext.Core.Setup;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class StartingConditionsTests
{
    private static string CitiesDataPath => Path.Combine(AppContext.BaseDirectory, "data", "cities.json");
    private static string AircraftDataPath => Path.Combine(AppContext.BaseDirectory, "data", "aircraft.json");
    private static IReadOnlyList<City> RealCities => CityCatalog.LoadFile(CitiesDataPath);
    private static IReadOnlyList<AircraftType> RealFleet => AircraftCatalog.LoadFile(AircraftDataPath);
    private static readonly GravityDemandModel DemandModel = new();

    [Fact]
    public void Resolve_EveryEligibleStartingCity_ProducesAcceptedQuarter1Route()
    {
        // M18's own "Done when": not one hand-picked example, but every
        // (home region, eligible starting city) pair the real data offers.
        var cities = RealCities;
        var fleet = RealFleet;

        foreach (var region in HomeRegions.All)
        {
            foreach (var startingCity in HomeRegions.EligibleStartingCities(region, cities))
            {
                var seat = new AirlineSeat("Test Airline", IsHuman: true, region, startingCity);

                var conditions = StartingConditionsResolver.Resolve(seat, cities, fleet);

                Assert.True(conditions.StartingCash.Cents > 0,
                    $"{startingCity.Id}: starting cash must be positive.");
                Assert.Equal(StartingConditionsResolver.StartingAircraftQuantity, conditions.AircraftQuantity);
                Assert.NotEqual(startingCity.Id, conditions.GuaranteedRouteDestination.Id);
                Assert.Equal(region, conditions.GuaranteedRouteDestination.Region);
                Assert.True(Timeline.IsAvailable(conditions.StartingAircraft, Timeline.StartYear));

                var order = new RouteOrder(startingCity, conditions.GuaranteedRouteDestination, conditions.StartingAircraft);
                var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, cities, DemandModel);

                Assert.Empty(result.RejectedOrders);
                Assert.Contains(result.Routes, r => r.Origin.Id == startingCity.Id && r.Destination.Id == conditions.GuaranteedRouteDestination.Id);
            }
        }
    }

    [Fact]
    public void Resolve_Cash_ScalesWithRegionalEconomicFactor()
    {
        var cities = RealCities;
        var seat = new AirlineSeat("Test", IsHuman: true, Region.EastAsia, HomeRegions.EligibleStartingCities(Region.EastAsia, cities)[0]);

        var conditions = StartingConditionsResolver.Resolve(seat, cities, RealFleet);
        var expectedFactor = RegionalEconomics.Factor(Region.EastAsia, cities);

        var expectedCash = Money.FromDollars((long)Math.Round(
            StartingConditionsResolver.BaseStartingCashDollars * expectedFactor, MidpointRounding.AwayFromZero));

        Assert.Equal(expectedCash, conditions.StartingCash);
    }

    [Fact]
    public void Resolve_IsDeterministic_AcrossRepeatedCalls()
    {
        var cities = RealCities;
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, HomeRegions.EligibleStartingCities(Region.Europe, cities)[0]);

        var first = StartingConditionsResolver.Resolve(seat, cities, RealFleet);
        var second = StartingConditionsResolver.Resolve(seat, cities, RealFleet);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Resolve_PicksTheMostProfitableAircraftAndDestinationCombination()
    {
        // Guaranteed-route profitability follow-up: two eligible
        // destinations - "near" is close and thin (its route can't fill a
        // plane); "far" is farther but has heavy demand. The
        // profitability-blind alphabetical rule would have picked "far"
        // anyway here (f < n), but with the demand flipped it would have
        // picked the loss-making one. The new rule always lands on the
        // profitable combination regardless of city id ordering.
        var home = new City("home", "HOME", Region.Europe, 0, 0, 40, false, null);
        var thin = new City("aaa", "AAA", Region.Europe, 0, 3, 3, false, null);   // alphabetically first, tiny demand
        var rich = new City("zzz", "ZZZ", Region.Europe, 0, 2, 400, false, null); // alphabetically last, heavy demand
        var cities = new[] { home, thin, rich };

        var small = new AircraftType("small", "Small", Seats: 40, RangeKm: 5000, ManufacturerRegion: Region.Europe, CostPerAvailableSeatKm: 0.1, CruiseSpeedKmh: 500);
        var large = new AircraftType("large", "Large", Seats: 200, RangeKm: 5000, ManufacturerRegion: Region.Europe, CostPerAvailableSeatKm: 0.1, CruiseSpeedKmh: 500);
        var fleet = new[] { small, large };

        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, home);
        var conditions = StartingConditionsResolver.Resolve(seat, cities, fleet);

        Assert.Equal("zzz", conditions.GuaranteedRouteDestination.Id); // profitable, not alphabetically-first
    }

    [Fact]
    public void Resolve_SmallAircraftOutOfRange_FallsBackToLargerOne()
    {
        var a = new City("a", "A", Region.Europe, 0, 0, 60, false, null);
        var b = new City("b", "B", Region.Europe, 20, 20, 60, false, null); // far away
        var cities = new[] { a, b };

        var tooSmall = new AircraftType("small", "Small", Seats: 40, RangeKm: 500, ManufacturerRegion: Region.Europe);
        var bigEnough = new AircraftType("large", "Large", Seats: 200, RangeKm: 10_000, ManufacturerRegion: Region.Europe);
        var fleet = new[] { tooSmall, bigEnough };

        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, a);
        var conditions = StartingConditionsResolver.Resolve(seat, cities, fleet);

        Assert.Equal("large", conditions.StartingAircraft.Id);
        Assert.Equal("b", conditions.GuaranteedRouteDestination.Id);
    }

    [Fact]
    public void Resolve_NoReachableNeighborForAnyAppropriateAircraft_Throws()
    {
        var a = new City("a", "A", Region.Europe, 0, 0, 60, false, null);
        var b = new City("b", "B", Region.Europe, 60, 60, 60, false, null); // unreachably far
        var cities = new[] { a, b };

        var tooSmall = new AircraftType("small", "Small", Seats: 40, RangeKm: 500, ManufacturerRegion: Region.Europe);
        var fleet = new[] { tooSmall };

        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, a);

        Assert.Throws<InvalidOperationException>(() => StartingConditionsResolver.Resolve(seat, cities, fleet));
    }

    [Fact]
    public void MostProfitableRoute_NoReachableNeighborForAnyAppropriateAircraft_Throws()
    {
        var a = new City("a", "A", Region.Europe, 0, 0, 60, false, null);
        var b = new City("b", "B", Region.Europe, 60, 60, 60, false, null); // unreachably far
        var cities = new[] { a, b };

        var tooSmall = new AircraftType("small", "Small", Seats: 40, RangeKm: 500, ManufacturerRegion: Region.Europe);
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, a);

        Assert.Throws<InvalidOperationException>(
            () => StartingConditionsResolver.MostProfitableRoute(seat, cities, new[] { tooSmall }));
    }
}
