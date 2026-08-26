using AeroNext.Core;
using AeroNext.Core.Airlines;
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
    public void LoadFile_LaterAircraft_HaveGreaterRange()
    {
        var fleet = AircraftCatalog.LoadFile(DataPath);
        var byYear = fleet.OrderBy(a => a.AvailableFromYear).ToList();

        for (var i = 1; i < byYear.Count; i++)
        {
            Assert.True(byYear[i].RangeKm >= byYear[i - 1].RangeKm,
                $"{byYear[i].Id} (range {byYear[i].RangeKm}) should be no shorter-range than earlier {byYear[i - 1].Id} (range {byYear[i - 1].RangeKm}).");
        }
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
    public void Load_ThrowsOnInvalidEntry(string entry)
    {
        var json = $"[{entry}]";

        Assert.Throws<InvalidDataException>(() => AircraftCatalog.Load(json));
    }
}
