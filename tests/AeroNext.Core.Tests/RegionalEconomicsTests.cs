using AeroNext.Core.Setup;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class RegionalEconomicsTests
{
    private static string CitiesDataPath => Path.Combine(AppContext.BaseDirectory, "data", "cities.json");

    [Fact]
    public void Factor_Africa_Throws()
    {
        var cities = CityCatalog.LoadFile(CitiesDataPath);

        Assert.Throws<ArgumentException>(() => RegionalEconomics.Factor(Region.Africa, cities));
    }

    [Fact]
    public void Factor_EveryHomeRegion_IsStrictlyPositive()
    {
        var cities = CityCatalog.LoadFile(CitiesDataPath);

        foreach (var region in HomeRegions.All)
        {
            Assert.True(RegionalEconomics.Factor(region, cities) > 0, $"{region} should have a positive economic factor.");
        }
    }

    [Fact]
    public void Factor_TwoEquallyWeightedRegions_BothEqualOne()
    {
        var cities = new[]
        {
            new City("na1", "NA One", Region.NorthAmerica, 0, 0, 50, false, null),
            new City("na2", "NA Two", Region.NorthAmerica, 0, 0, 50, false, null),
            new City("eu1", "EU One", Region.Europe, 0, 0, 50, false, null),
            new City("eu2", "EU Two", Region.Europe, 0, 0, 50, false, null),
            new City("ea1", "EA One", Region.EastAsia, 0, 0, 50, false, null),
            new City("sw1", "SW One", Region.SouthWestAsia, 0, 0, 50, false, null),
            new City("au1", "AU One", Region.AustralAsia, 0, 0, 50, false, null),
            new City("sa1", "SA One", Region.SouthAmerica, 0, 0, 50, false, null),
        };

        foreach (var region in HomeRegions.All)
        {
            Assert.Equal(1.0, RegionalEconomics.Factor(region, cities), precision: 10);
        }
    }

    [Fact]
    public void Factor_RicherRegion_ScoresAboveOne_PoorerRegion_ScoresBelowOne()
    {
        var cities = new[]
        {
            new City("na1", "NA Rich", Region.NorthAmerica, 0, 0, 100, false, null),
            new City("eu1", "EU Poor", Region.Europe, 0, 0, 20, false, null),
            new City("as1", "AS Mid", Region.EastAsia, 0, 0, 60, false, null),
            new City("sa1", "SA Mid", Region.SouthAmerica, 0, 0, 60, false, null),
        };

        // Global average = (100 + 20 + 60 + 60) / 4 = 60.
        Assert.Equal(100.0 / 60.0, RegionalEconomics.Factor(Region.NorthAmerica, cities), precision: 10);
        Assert.Equal(20.0 / 60.0, RegionalEconomics.Factor(Region.Europe, cities), precision: 10);
        Assert.True(RegionalEconomics.Factor(Region.NorthAmerica, cities) > 1.0);
        Assert.True(RegionalEconomics.Factor(Region.Europe, cities) < 1.0);
    }

    [Fact]
    public void Factor_SouthAmerica_StaysAboveEurope_OnTheRepoDataset()
    {
        // docs/SOUTHAMERICA_TUNING_EXPERIMENTS.md: adding Belo Horizonte
        // (a lower-weight city) to SouthAmerica risked dropping its Factor
        // below Europe's, which flips GameSetup's AI-filler ranking and
        // silently reassigns seats between the two regions - a real,
        // measured confound at a lower weight choice (35) than the one
        // shipped (45). This locks the ordering so a future SouthAmerica
        // (or Europe) city edit can't quietly re-break it.
        var cities = CityCatalog.LoadFile(CitiesDataPath);

        Assert.True(
            RegionalEconomics.Factor(Region.SouthAmerica, cities) > RegionalEconomics.Factor(Region.Europe, cities),
            "SouthAmerica's Factor must stay above Europe's, or GameSetup's richest-unclaimed-region filler ranking silently reassigns seats between them.");
    }

    [Fact]
    public void Factor_IgnoresGatewayCities()
    {
        var withoutGateway = new[]
        {
            new City("na1", "NA One", Region.NorthAmerica, 0, 0, 50, false, null),
            new City("eu1", "EU One", Region.Europe, 0, 0, 50, false, null),
            new City("as1", "AS One", Region.EastAsia, 0, 0, 50, false, null),
            new City("sa1", "SA One", Region.SouthAmerica, 0, 0, 50, false, null),
        };

        var withGateway = withoutGateway
            .Append(new City("na-gate", "NA Gateway", Region.NorthAmerica, 0, 0, 1_000_000, true, 3))
            .ToArray();

        // A gateway city's enormous demand weight must not leak into the
        // economic factor at all — gateways stay outside the eligible set.
        Assert.Equal(
            RegionalEconomics.Factor(Region.NorthAmerica, withoutGateway),
            RegionalEconomics.Factor(Region.NorthAmerica, withGateway),
            precision: 10);
    }
}
