using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class CityCatalogTests
{
    private static string DataPath => Path.Combine(AppContext.BaseDirectory, "data", "cities.json");

    [Fact]
    public void LoadFile_LoadsRepoDataset_WithNoDuplicateIds()
    {
        var cities = CityCatalog.LoadFile(DataPath);

        Assert.NotEmpty(cities);
        Assert.Equal(cities.Count, cities.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void LoadFile_IncludesNamedChokepoints()
    {
        var cities = CityCatalog.LoadFile(DataPath);
        var ids = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Wake, Honolulu, Anchorage, Gander, Azores, Dakar — named explicitly
        // in CLAUDE.md as the chokepoints that generate clashes.
        Assert.Contains("awk", ids);
        Assert.Contains("hnl", ids);
        Assert.Contains("anc", ids);
        Assert.Contains("yqx", ids);
        Assert.Contains("ter", ids);
        Assert.Contains("dss", ids);
    }

    [Fact]
    public void GatewayCities_AllHavePositiveGateCapacity()
    {
        var cities = CityCatalog.LoadFile(DataPath);

        foreach (var city in cities.Where(c => c.IsGateway))
        {
            Assert.True(city.GateCapacity is > 0, $"{city.Id} is a gateway but has no positive gate capacity.");
        }
    }

    [Fact]
    public void Load_ReturnsCitiesSortedById_RegardlessOfSourceOrder()
    {
        const string json = """
        [
          { "id": "zzz", "name": "Z City", "region": "Asia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null },
          { "id": "aaa", "name": "A City", "region": "Asia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null },
          { "id": "mmm", "name": "M City", "region": "Asia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null }
        ]
        """;

        var cities = CityCatalog.Load(json);

        Assert.Equal(new[] { "aaa", "mmm", "zzz" }, cities.Select(c => c.Id));
    }

    [Fact]
    public void Load_ThrowsOnDuplicateIds()
    {
        const string json = """
        [
          { "id": "dup", "name": "First", "region": "Asia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null },
          { "id": "dup", "name": "Second", "region": "Asia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => CityCatalog.Load(json));
    }

    [Fact]
    public void Load_ThrowsWhenGatewayHasNoGateCapacity()
    {
        const string json = """
        [
          { "id": "bad", "name": "Bad Gateway", "region": "Asia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": true, "gateCapacity": null }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => CityCatalog.Load(json));
    }

    [Fact]
    public void Load_ThrowsOnNonPositiveDemandWeight()
    {
        const string json = """
        [
          { "id": "bad", "name": "Bad Demand", "region": "Asia", "latitude": 0, "longitude": 0, "demandWeight": 0, "isGateway": false, "gateCapacity": null }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => CityCatalog.Load(json));
    }
}
