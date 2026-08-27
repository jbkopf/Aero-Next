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
        // Round-2 aircraft expansion: the de Havilland Comet (1952) is
        // Europe's own manufacturer too, alongside the Viscount.
        Assert.Contains(appropriate, a => a.Id == "viscount");
        Assert.Contains(appropriate, a => a.Id == "comet");
    }

    [Fact]
    public void AppropriateAircraft_NorthAmerica_HasMultipleOwnManufacturedTypes_At1955()
    {
        var fleet = AircraftCatalog.LoadFile(AircraftDataPath);

        var appropriate = RegionalFleet.AppropriateAircraft(Region.NorthAmerica, fleet, 1955);

        Assert.All(appropriate, a => Assert.Equal(Region.NorthAmerica, a.ManufacturerRegion));
        // Round-2 pre-1965 expansion: DC-6, DC-7, and Convair 340 all join
        // DC-3/Constellation as real 1955-available NorthAmerica options.
        Assert.Equal(
            new[] { "cv340", "dc3", "dc6", "dc7", "l049" },
            appropriate.Select(a => a.Id).OrderBy(id => id, StringComparer.Ordinal));
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
    public void AppropriateAircraft_SouthAmerica_GetsOwnManufacturerOnceEmbraerArrives()
    {
        var fleet = AircraftCatalog.LoadFile(AircraftDataPath);

        // EMB-120 Brasilia (1985): the first SouthAmerica-manufactured type,
        // closing the gap RegionalFleetTests documented at the 1955 start.
        var appropriate = RegionalFleet.AppropriateAircraft(Region.SouthAmerica, fleet, 1985);

        Assert.All(appropriate, a => Assert.Equal(Region.SouthAmerica, a.ManufacturerRegion));
        Assert.Contains(appropriate, a => a.Id == "emb120");
    }

    [Fact]
    public void AppropriateAircraft_Asia_GetsOwnManufacturerOnceComacArrives()
    {
        var fleet = AircraftCatalog.LoadFile(AircraftDataPath);

        // COMAC ARJ21 (2008): the first Asia-manufactured type.
        var appropriate = RegionalFleet.AppropriateAircraft(Region.Asia, fleet, 2008);

        Assert.All(appropriate, a => Assert.Equal(Region.Asia, a.ManufacturerRegion));
        Assert.Contains(appropriate, a => a.Id == "arj21");
    }

    [Fact]
    public void AppropriateAircraft_UnmodeledManufacturerRegion_NeverPreferredOnceEveryRegionHasItsOwn()
    {
        var fleet = AircraftCatalog.LoadFile(AircraftDataPath);

        // By 2015 every home region has its own manufactured type
        // available (Asia's COMAC ARJ21, 2008, is the last to arrive), so
        // every region's "appropriate" set comes from the own-manufacturer
        // branch — Tu-104/Il-62 (Soviet, ManufacturerRegion left null; see
        // AircraftCatalogTests) never surface as anyone's preferred pick.
        foreach (var region in HomeRegions.All)
        {
            var appropriate = RegionalFleet.AppropriateAircraft(region, fleet, 2015);

            Assert.DoesNotContain(appropriate, a => a.Id is "tu104" or "il62");
        }
    }

    [Fact]
    public void AppropriateAircraft_UnmodeledManufacturerRegion_StillAppearsInGenericFallback()
    {
        var fleet = AircraftCatalog.LoadFile(AircraftDataPath);

        // SouthAmerica has no manufacturer of its own until Embraer (1985),
        // so at 1960 - after Tu-104 (1956) but before any regional
        // manufacturer - it should fall back to every available type,
        // including the null-region Tu-104. Confirms null ManufacturerRegion
        // means "unmodeled," not "invisible to the whole game."
        var appropriate = RegionalFleet.AppropriateAircraft(Region.SouthAmerica, fleet, 1960);

        Assert.Contains(appropriate, a => a.Id == "tu104");
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
