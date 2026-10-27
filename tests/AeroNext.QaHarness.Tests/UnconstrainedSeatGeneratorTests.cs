using AeroNext.Core;
using AeroNext.Core.World;

namespace AeroNext.QaHarness.Tests;

/// <summary>
/// The --unconstrained-regions sweep used to draw each seat's starting city
/// independently, and a real 20-game Hard sweep (base seed 1001) put two
/// seats on the same city in 3 games - each an invalid game that failed
/// FullGameRunner's Q0 invariant instead of being played. Regions may still
/// repeat; cities may not.
/// </summary>
public class UnconstrainedSeatGeneratorTests
{
    private static readonly IReadOnlyList<City> Cities =
        CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));

    [Theory]
    [InlineData(1010)] // the three config seeds that collided in the sweep
    [InlineData(1016)]
    [InlineData(1017)]
    public void Generate_KnownCollisionSeeds_GiveDistinctStartingCities(int configSeed)
    {
        var seats = UnconstrainedSeatGenerator.Generate(new SimRng(configSeed), Cities);

        Assert.Equal(4, seats.Select(s => s.StartingCity.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Generate_AcrossManySeeds_NeverRepeatsACity_ButStillRepeatsRegions()
    {
        var anyRepeatedRegion = false;

        for (var seed = 1; seed <= 2000; seed++)
        {
            var seats = UnconstrainedSeatGenerator.Generate(new SimRng(seed), Cities);

            Assert.Equal(4, seats.Select(s => s.StartingCity.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(seats, s => Assert.Equal(s.HomeRegion, s.StartingCity.Region));
            anyRepeatedRegion |= seats.Select(s => s.HomeRegion).Distinct().Count() < 4;
        }

        Assert.True(anyRepeatedRegion, "Unconstrained seating should still let seats share a home region.");
    }

    [Fact]
    public void Generate_RegionWithNoFreeCityLeft_DropsOutOfTheRegionDraw()
    {
        // A one-city region can host only one seat; the other three must go
        // elsewhere rather than collide on it.
        var region = HomeRegions.All[0];
        var onlyCity = HomeRegions.EligibleStartingCities(region, Cities)[0];
        var cities = Cities.Where(c => c.Region != region || c.Id == onlyCity.Id || c.IsGateway).ToList();

        for (var seed = 1; seed <= 200; seed++)
        {
            var seats = UnconstrainedSeatGenerator.Generate(new SimRng(seed), cities);

            Assert.True(seats.Count(s => s.HomeRegion == region) <= 1);
            Assert.Equal(4, seats.Select(s => s.StartingCity.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }
}
