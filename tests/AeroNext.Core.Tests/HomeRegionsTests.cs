using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class HomeRegionsTests
{
    private static string CitiesDataPath => Path.Combine(AppContext.BaseDirectory, "data", "cities.json");

    [Fact]
    public void All_ContainsExactlyTheSixHomeRegions()
    {
        // Asia split into three (EastAsia/SouthWestAsia/AustralAsia) once
        // its ~165 degrees of longitude sprawl made it the clear outlier
        // among home regions - CLAUDE.md's always-4-airlines rule is about
        // player slots, not region count, so 6 home regions for 4 slots is
        // a real draft (see GameSetup.CreateSeats), not a contradiction.
        Assert.Equal(
            new[] { Region.NorthAmerica, Region.Europe, Region.EastAsia, Region.SouthWestAsia, Region.AustralAsia, Region.SouthAmerica },
            HomeRegions.All);
    }

    [Theory]
    [InlineData(Region.NorthAmerica, true)]
    [InlineData(Region.Europe, true)]
    [InlineData(Region.EastAsia, true)]
    [InlineData(Region.SouthWestAsia, true)]
    [InlineData(Region.AustralAsia, true)]
    [InlineData(Region.SouthAmerica, true)]
    [InlineData(Region.Africa, false)]
    public void IsHomeRegion_MatchesExpectation(Region region, bool expected)
    {
        Assert.Equal(expected, HomeRegions.IsHomeRegion(region));
    }

    [Fact]
    public void EligibleStartingCities_Africa_Throws()
    {
        var cities = CityCatalog.LoadFile(CitiesDataPath);

        Assert.Throws<ArgumentException>(() => HomeRegions.EligibleStartingCities(Region.Africa, cities));
    }

    [Fact]
    public void EligibleStartingCities_EveryHomeRegion_HasAtLeastTwoNonGatewayCities()
    {
        var cities = CityCatalog.LoadFile(CitiesDataPath);

        foreach (var region in HomeRegions.All)
        {
            var eligible = HomeRegions.EligibleStartingCities(region, cities);

            Assert.True(eligible.Count >= 2,
                $"{region} has only {eligible.Count} eligible non-gateway starting cities; setup needs real choice.");
            Assert.All(eligible, c => Assert.False(c.IsGateway));
            Assert.All(eligible, c => Assert.Equal(region, c.Region));
        }
    }

    [Fact]
    public void EligibleStartingCities_EveryHomeRegion_HasAtLeastFiveEligibleCities()
    {
        // SouthAmerica was the thinnest region (2 eligible cities against
        // 5 for every other region) until the SouthAmerica city expansion
        // (Santiago, Bogota, Lima) brought it to parity. City expansion is
        // an ongoing, region-by-region series (per explicit direction) and
        // regions are expected to keep growing unevenly from here — a
        // floor, not an exact match, is the durable invariant: no future
        // region should ever end up thinner than the SouthAmerica-era
        // baseline every region had reached at that point.
        var cities = CityCatalog.LoadFile(CitiesDataPath);

        foreach (var region in HomeRegions.All)
        {
            var eligible = HomeRegions.EligibleStartingCities(region, cities);
            Assert.True(eligible.Count >= 5, $"{region} has only {eligible.Count} eligible cities, below the established baseline.");
        }
    }

    [Fact]
    public void EligibleStartingCities_NeverIncludesAfricanCities()
    {
        var cities = CityCatalog.LoadFile(CitiesDataPath);

        foreach (var region in HomeRegions.All)
        {
            var eligible = HomeRegions.EligibleStartingCities(region, cities);

            Assert.DoesNotContain(eligible, c => c.Region == Region.Africa);
        }
    }

    [Fact]
    public void EligibleStartingCities_IsSortedById_RegardlessOfSourceOrder()
    {
        var shuffled = new[]
        {
            new City("zzz", "Z", Region.EastAsia, 0, 0, 10, false, null),
            new City("aaa", "A", Region.EastAsia, 0, 0, 10, false, null),
            new City("mmm", "M", Region.EastAsia, 0, 0, 10, false, null),
        };

        var eligible = HomeRegions.EligibleStartingCities(Region.EastAsia, shuffled);

        Assert.Equal(new[] { "aaa", "mmm", "zzz" }, eligible.Select(c => c.Id));
    }

    [Fact]
    public void EligibleStartingCities_ExcludesGatewaysAndOtherRegions()
    {
        var cities = new[]
        {
            new City("home1", "Home One", Region.Europe, 0, 0, 50, false, null),
            new City("home2", "Home Two", Region.Europe, 0, 0, 40, false, null),
            new City("gate", "Gateway", Region.Europe, 0, 0, 90, true, 3),
            new City("other", "Other Region", Region.EastAsia, 0, 0, 60, false, null),
        };

        var eligible = HomeRegions.EligibleStartingCities(Region.Europe, cities);

        Assert.Equal(new[] { "home1", "home2" }, eligible.Select(c => c.Id));
    }
}
