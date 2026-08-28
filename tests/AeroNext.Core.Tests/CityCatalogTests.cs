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
    public void LoadFile_IncludesSouthAmericaExpansionCities()
    {
        // SouthAmerica was the thinnest home region (3 cities total, 2
        // eligible non-gateway starting cities) - Santiago, Bogota, and
        // Lima bring its non-gateway count to 5, matching every other
        // home region.
        var cities = CityCatalog.LoadFile(DataPath);
        var ids = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("scl", ids);
        Assert.Contains("bog", ids);
        Assert.Contains("lim", ids);
        Assert.All(new[] { "scl", "bog", "lim" }, id =>
            Assert.Equal(Region.SouthAmerica, cities.Single(c => c.Id == id).Region));
    }

    [Fact]
    public void LoadFile_IncludesEuropeExpansionCities()
    {
        // Europe already had 5 eligible cities (parity, not a gap like
        // SouthAmerica) - this round is depth, not equalization: Amsterdam
        // joins London as Europe's second flagship gateway (matching
        // NorthAmerica's jfk+lax and Asia's hnd+hkg 2-gateway pattern),
        // plus Munich, Istanbul, Barcelona, and Zurich as new non-gateway
        // options.
        var cities = CityCatalog.LoadFile(DataPath);
        var ids = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("ams", ids);
        Assert.Contains("muc", ids);
        Assert.Contains("ist", ids);
        Assert.Contains("bcn", ids);
        Assert.Contains("zrh", ids);
        Assert.All(new[] { "ams", "muc", "ist", "bcn", "zrh" }, id =>
            Assert.Equal(Region.Europe, cities.Single(c => c.Id == id).Region));

        var amsterdam = cities.Single(c => c.Id == "ams");
        Assert.True(amsterdam.IsGateway);
        Assert.True(amsterdam.GateCapacity is > 0);
    }

    [Fact]
    public void Europe_HasTwoFlagshipGateways_MatchingNorthAmericaAndAsia()
    {
        // lhr/ams for Europe now mirrors jfk/lax (NorthAmerica) and
        // hnd/hkg (Asia) - Azores (ter) is a chokepoint gateway, not a
        // flagship, so it's deliberately excluded from this count.
        var cities = CityCatalog.LoadFile(DataPath);
        var europeanFlagshipGateways = cities
            .Where(c => c.Region == Region.Europe && c.IsGateway && c.Id != "ter")
            .Select(c => c.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "lhr", "ams" }, europeanFlagshipGateways);
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

    [Fact]
    public void Load_ThrowsOnEmptyCityList()
    {
        Assert.Throws<InvalidDataException>(() => CityCatalog.Load("[]"));
    }

    [Fact]
    public void Load_ThrowsWhenJsonDeserializesToNull()
    {
        Assert.Throws<InvalidDataException>(() => CityCatalog.Load("null"));
    }

    [Fact]
    public void Load_ThrowsOnMissingId()
    {
        const string json = """
        [
          { "id": "", "name": "No Id", "region": "Asia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => CityCatalog.Load(json));
    }

    [Fact]
    public void Load_ThrowsOnMissingName()
    {
        const string json = """
        [
          { "id": "bad", "name": "  ", "region": "Asia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => CityCatalog.Load(json));
    }
}
