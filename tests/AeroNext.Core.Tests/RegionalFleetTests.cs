using AeroNext.Core.Airlines;
using AeroNext.Core.Setup;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class RegionalFleetTests
{
    private static string AircraftDataPath => Path.Combine(AppContext.BaseDirectory, "data", "aircraft.json");

    [Fact]
    public void AppropriateAircraft_Africa_Throws()
    {
        var fleet = AircraftCatalog.LoadFile(AircraftDataPath);

        Assert.Throws<ArgumentException>(() => RegionalFleet.AppropriateAircraft(Region.Africa, fleet, 1955));
    }

    [Fact]
    public void AppropriateAircraft_EveryHomeRegion_HasAtLeastOneOption_At1955()
    {
        var fleet = AircraftCatalog.LoadFile(AircraftDataPath);

        foreach (var region in HomeRegions.All)
        {
            var appropriate = RegionalFleet.AppropriateAircraft(region, fleet, 1955);

            Assert.True(appropriate.Count > 0, $"{region} has no appropriate 1955-available aircraft.");
            Assert.All(appropriate, a => Assert.True(a.AvailableFromYear <= 1955));
        }
    }

    [Fact]
    public void AppropriateAircraft_Europe_PrefersOwnManufacturer_At1955()
    {
        var fleet = AircraftCatalog.LoadFile(AircraftDataPath);

        var appropriate = RegionalFleet.AppropriateAircraft(Region.Europe, fleet, 1955);

        Assert.All(appropriate, a => Assert.Equal(Region.Europe, a.ManufacturerRegion));
        Assert.Contains(appropriate, a => a.Id == "viscount");
    }

    [Fact]
    public void AppropriateAircraft_AsiaAndSouthAmerica_FallBackToBestAvailableImport_At1955()
    {
        var fleet = AircraftCatalog.LoadFile(AircraftDataPath);

        foreach (var region in new[] { Region.Asia, Region.SouthAmerica })
        {
            var appropriate = RegionalFleet.AppropriateAircraft(region, fleet, 1955);

            // No home-manufactured type exists yet, so every era-available
            // type is offered regardless of manufacturer.
            Assert.DoesNotContain(appropriate, a => a.ManufacturerRegion == region);
            Assert.NotEmpty(appropriate);
        }
    }

    [Fact]
    public void AppropriateAircraft_RespectsEraGating()
    {
        var fleet = new[]
        {
            new AircraftType("early", "Early", Seats: 50, AvailableFromYear: 1950, ManufacturerRegion: Region.Europe),
            new AircraftType("late", "Late", Seats: 50, AvailableFromYear: 1999, ManufacturerRegion: Region.Europe),
        };

        var appropriate = RegionalFleet.AppropriateAircraft(Region.Europe, fleet, 1955);

        Assert.Equal(new[] { "early" }, appropriate.Select(a => a.Id));
    }

    [Fact]
    public void AppropriateAircraft_NoTypesAvailableYet_ReturnsEmpty()
    {
        var fleet = new[]
        {
            new AircraftType("future", "Future", Seats: 50, AvailableFromYear: 2000, ManufacturerRegion: Region.Europe),
        };

        var appropriate = RegionalFleet.AppropriateAircraft(Region.Europe, fleet, 1955);

        Assert.Empty(appropriate);
    }

    [Fact]
    public void AppropriateAircraft_IsSortedById()
    {
        var fleet = new[]
        {
            new AircraftType("zzz", "Z", Seats: 50, AvailableFromYear: 1900, ManufacturerRegion: Region.Europe),
            new AircraftType("aaa", "A", Seats: 50, AvailableFromYear: 1900, ManufacturerRegion: Region.Europe),
        };

        var appropriate = RegionalFleet.AppropriateAircraft(Region.Europe, fleet, 1955);

        Assert.Equal(new[] { "aaa", "zzz" }, appropriate.Select(a => a.Id));
    }
}
