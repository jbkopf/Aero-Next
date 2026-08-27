using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class AircraftCatalogTests
{
    private static string DataPath => Path.Combine(AppContext.BaseDirectory, "data", "aircraft.json");

    [Fact]
    public void LoadFile_LoadsRepoFleet_WithNoDuplicateIds()
    {
        var fleet = AircraftCatalog.LoadFile(DataPath);

        Assert.NotEmpty(fleet);
        Assert.Equal(fleet.Count, fleet.Select(a => a.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void LoadFile_SpansEarlyToLateCampaignYears()
    {
        var fleet = AircraftCatalog.LoadFile(DataPath);

        Assert.Contains(fleet, a => a.AvailableFromYear <= 1955);
        Assert.Contains(fleet, a => a.AvailableFromYear > 1955);
    }

    [Fact]
    public void LoadFile_MainlineLongHaulLineage_RangeIncreasesOverTime()
    {
        // dc3 and viscount (M16) are short/medium-haul regional types,
        // deliberately excluded here: a regional aircraft introduced later
        // than an existing long-haul flagship legitimately has less range,
        // since it serves a different route class — not a fleet regression.
        // The invariant that actually matters (per CLAUDE.md's "chokepoints
        // obsolete as range improves") is the long-haul lineage specifically.
        var fleet = AircraftCatalog.LoadFile(DataPath);
        var longHaulLineage = fleet
            .Where(a => a.Id is "l049" or "707" or "747" or "777")
            .OrderBy(a => a.AvailableFromYear)
            .ToList();

        Assert.True(longHaulLineage.Count >= 2, "Expected the long-haul lineage aircraft to still be present in the dataset.");

        for (var i = 1; i < longHaulLineage.Count; i++)
        {
            Assert.True(longHaulLineage[i].RangeKm >= longHaulLineage[i - 1].RangeKm,
                $"{longHaulLineage[i].Id} (range {longHaulLineage[i].RangeKm}) should be no shorter-range than earlier {longHaulLineage[i - 1].Id} (range {longHaulLineage[i - 1].RangeKm}).");
        }
    }

    [Fact]
    public void LoadFile_EveryHomeRegion_HasAtLeastOneManufacturedType()
    {
        // M16 could only assert this for NorthAmerica/Europe — Asia and
        // South America had no indigenous manufacturer that early in the
        // campaign. The later aircraft-catalog expansion (Embraer for
        // SouthAmerica, 1985; COMAC for Asia, 2008) closes that gap for
        // real: every home region now has its own manufactured type
        // *eventually*, even though (per RegionalFleetTests) neither has
        // one at the 1955 campaign start.
        var fleet = AircraftCatalog.LoadFile(DataPath);

        foreach (var region in HomeRegions.All)
        {
            Assert.Contains(fleet, a => a.ManufacturerRegion == region);
        }
    }

    [Fact]
    public void LoadFile_IncludesUnmodeledManufacturerRegionAircraft()
    {
        // Soviet aircraft (Tupolev, Ilyushin): real, era-available, flyable
        // by anyone once the era arrives, but deliberately not tagged to
        // any of the 4 home regions — the USSR doesn't map cleanly onto
        // this game's coarse world-region model, and no home region should
        // get them handed to it as a "starting" type by fiat. See
        // RegionalFleetTests for how the null ManufacturerRegion behaves.
        var fleet = AircraftCatalog.LoadFile(DataPath);

        Assert.Contains(fleet, a => a.ManufacturerRegion is null);
    }

    [Fact]
    public void Load_ParsesManufacturerFields_WhenPresent()
    {
        const string json = """
        [{ "id": "t", "name": "Test", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960, "manufacturer": "Vickers", "manufacturerRegion": "Europe" }]
        """;

        var fleet = AircraftCatalog.Load(json);

        Assert.Equal("Vickers", fleet.Single().Manufacturer);
        Assert.Equal(Region.Europe, fleet.Single().ManufacturerRegion);
    }

    [Fact]
    public void Load_ManufacturerFields_AreOptional()
    {
        const string json = """
        [{ "id": "t", "name": "Test", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960 }]
        """;

        var fleet = AircraftCatalog.Load(json);

        Assert.Equal("", fleet.Single().Manufacturer);
        Assert.Null(fleet.Single().ManufacturerRegion);
    }

    [Fact]
    public void Load_ParsesCostPerAvailableSeatKm_WhenPresent()
    {
        const string json = """
        [{ "id": "t", "name": "Test", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960, "costPerAvailableSeatKm": 0.07 }]
        """;

        var fleet = AircraftCatalog.Load(json);

        Assert.Equal(0.07, fleet.Single().CostPerAvailableSeatKm);
    }

    [Fact]
    public void Load_CostPerAvailableSeatKm_DefaultsTo009_WhenAbsent()
    {
        const string json = """
        [{ "id": "t", "name": "Test", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960 }]
        """;

        var fleet = AircraftCatalog.Load(json);

        Assert.Equal(0.09, fleet.Single().CostPerAvailableSeatKm);
    }

    [Fact]
    public void LoadFile_LaterAircraft_GenerallyHaveLowerCostPerAvailableSeatKm()
    {
        // Not strictly monotonic (a small regional jet is less efficient
        // per seat than a contemporary mainline aircraft, same as real
        // aviation economics) - checked here as "the newest, biggest
        // mainline jets end up cheaper per seat-km than the earliest
        // piston aircraft," not a strict ordering over every entry.
        var fleet = AircraftCatalog.LoadFile(DataPath);
        var dc3 = fleet.Single(a => a.Id == "dc3");
        var modernMainline = fleet.Where(a => a.Id is "a320" or "777").ToList();

        Assert.All(modernMainline, a => Assert.True(a.CostPerAvailableSeatKm < dc3.CostPerAvailableSeatKm,
            $"{a.Id} ({a.CostPerAvailableSeatKm}) should be more efficient per seat-km than the 1935 DC-3 ({dc3.CostPerAvailableSeatKm})."));
    }

    [Fact]
    public void Load_ConvertsCostDollarsToMoney()
    {
        const string json = """
        [{ "id": "t", "name": "Test", "seats": 100, "rangeKm": 1000, "costDollars": 5000000, "productionRatePerQuarter": 4, "availableFromYear": 1960 }]
        """;

        var fleet = AircraftCatalog.Load(json);

        Assert.Equal(Money.FromDollars(5_000_000), fleet.Single().Cost);
    }

    [Fact]
    public void Load_ReturnsSortedById_RegardlessOfSourceOrder()
    {
        const string json = """
        [
          { "id": "zzz", "name": "Z", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960 },
          { "id": "aaa", "name": "A", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960 }
        ]
        """;

        var fleet = AircraftCatalog.Load(json);

        Assert.Equal(new[] { "aaa", "zzz" }, fleet.Select(a => a.Id));
    }

    [Fact]
    public void Load_ThrowsOnEmptyFleet()
    {
        Assert.Throws<InvalidDataException>(() => AircraftCatalog.Load("[]"));
    }

    [Fact]
    public void Load_ThrowsWhenJsonDeserializesToNull()
    {
        Assert.Throws<InvalidDataException>(() => AircraftCatalog.Load("null"));
    }

    [Fact]
    public void Load_ThrowsOnDuplicateIds()
    {
        const string json = """
        [
          { "id": "dup", "name": "First", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960 },
          { "id": "dup", "name": "Second", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960 }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => AircraftCatalog.Load(json));
    }

    [Theory]
    [InlineData("""{ "id": "", "name": "N", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960 }""")]
    [InlineData("""{ "id": "x", "name": "  ", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960 }""")]
    [InlineData("""{ "id": "x", "name": "N", "seats": 0, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960 }""")]
    [InlineData("""{ "id": "x", "name": "N", "seats": 100, "rangeKm": 0, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960 }""")]
    [InlineData("""{ "id": "x", "name": "N", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 0, "availableFromYear": 1960 }""")]
    [InlineData("""{ "id": "x", "name": "N", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960, "costPerAvailableSeatKm": 0 }""")]
    public void Load_ThrowsOnInvalidEntry(string entry)
    {
        var json = $"[{entry}]";

        Assert.Throws<InvalidDataException>(() => AircraftCatalog.Load(json));
    }
}
