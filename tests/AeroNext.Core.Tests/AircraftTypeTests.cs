using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class AircraftTypeTests
{
    [Fact]
    public void Constructor_StoresAllSpecs()
    {
        var type = new AircraftType("707", "Boeing 707", Seats: 140, RangeKm: 12_000, Cost: Money.FromDollars(5_000_000), ProductionRatePerQuarter: 6);

        Assert.Equal("707", type.Id);
        Assert.Equal("Boeing 707", type.Name);
        Assert.Equal(140, type.Seats);
        Assert.Equal(12_000, type.RangeKm);
        Assert.Equal(Money.FromDollars(5_000_000), type.Cost);
        Assert.Equal(6, type.ProductionRatePerQuarter);
    }

    [Fact]
    public void Defaults_AreGenerous_ForTestAircraftThatDoNotCareAboutFleetEconomics()
    {
        var type = new AircraftType("t", "Test", Seats: 100);

        Assert.Equal(20_000, type.RangeKm);
        Assert.Equal(Money.Zero, type.Cost);
        Assert.Equal(4, type.ProductionRatePerQuarter);
        Assert.Equal("", type.Manufacturer);
        Assert.Null(type.ManufacturerRegion);
    }

    [Fact]
    public void Constructor_StoresManufacturerInfo()
    {
        var type = new AircraftType("viscount", "Vickers Viscount", Seats: 48, Manufacturer: "Vickers", ManufacturerRegion: Region.Europe);

        Assert.Equal("Vickers", type.Manufacturer);
        Assert.Equal(Region.Europe, type.ManufacturerRegion);
    }
}
