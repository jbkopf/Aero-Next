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
    public void LoadFile_IncludesAsiaExpansionCities()
    {
        // Asia already had 5 eligible cities and 2 flagship gateways
        // (hnd/hkg) - matching the post-Europe-round baseline, so this
        // round was depth only, no new gateway: Beijing, Mumbai, Dubai,
        // Kuala Lumpur, and Taipei, all non-gateway.
        var cities = CityCatalog.LoadFile(DataPath);
        var ids = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Region reassigned by the later Asia split: Beijing/Taipei are
        // EastAsia, Mumbai/Dubai are SouthWestAsia, Kuala Lumpur is
        // AustralAsia (grouped with Southeast Asia) - this test only
        // locks in that the cities themselves still exist.
        Assert.Contains("pek", ids);
        Assert.Contains("bom", ids);
        Assert.Contains("dxb", ids);
        Assert.Contains("kul", ids);
        Assert.Contains("tpe", ids);
        Assert.All(new[] { "pek", "bom", "dxb", "kul", "tpe" }, id => Assert.False(cities.Single(c => c.Id == id).IsGateway));
    }

    [Fact]
    public void LoadFile_IncludesNorthAmericaExpansionCities()
    {
        // NorthAmerica already had 5 eligible cities and 2 flagship
        // gateways (jfk/lax) - the established baseline - so this round
        // was depth only, no new gateway: Atlanta, Dallas, Denver,
        // Houston, and Vancouver, all non-gateway. Atlanta in particular
        // closed a real gap - one of the world's busiest airports for
        // much of this campaign's timeline wasn't represented at all.
        var cities = CityCatalog.LoadFile(DataPath);
        var ids = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("atl", ids);
        Assert.Contains("dfw", ids);
        Assert.Contains("den", ids);
        Assert.Contains("iah", ids);
        Assert.Contains("yvr", ids);
        Assert.All(new[] { "atl", "dfw", "den", "iah", "yvr" }, id =>
        {
            var city = cities.Single(c => c.Id == id);
            Assert.Equal(Region.NorthAmerica, city.Region);
            Assert.False(city.IsGateway);
        });
    }

    [Fact]
    public void LoadFile_IncludesAerobizSupersonicCities()
    {
        // Cross-referenced against Aerobiz Supersonic's own city roster
        // (the game this project is a spiritual successor to, per
        // CLAUDE.md) and added the clearest, best-attested gaps: Moscow
        // and Berlin (Europe), Washington and Havana (NorthAmerica), and
        // Tehran, Manila, and Sydney (added under the old single Asia
        // region, tagged for lack of a finer Middle East/Oceania split at
        // the time - the later Asia split moved them to SouthWestAsia,
        // AustralAsia, and AustralAsia respectively).
        var cities = CityCatalog.LoadFile(DataPath);
        var ids = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("mow", ids);
        Assert.Contains("ber", ids);
        Assert.Contains("was", ids);
        Assert.Contains("hav", ids);
        Assert.Contains("thr", ids);
        Assert.Contains("mnl", ids);
        Assert.Contains("syd", ids);
    }

    [Fact]
    public void LoadFile_IncludesAerobizSupersonicMajorTierCities()
    {
        // The user supplied Aerobiz Supersonic's actual, complete city
        // list (the earlier round's cross-reference had to work from
        // incomplete secondary sources - the game's own wiki 402'd on
        // every fetch attempt). Comparing the full list against the
        // roster found 43 genuine gaps; this round adds the 24
        // clearly-major ones, split from the ~19 smaller/chokepoint-tier
        // ones by explicit user direction (major first, minor later).
        var cities = CityCatalog.LoadFile(DataPath);
        var ids = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expectedByRegion = new (string Id, Region Region)[]
        {
            ("man", Region.Europe), ("bru", Region.Europe), ("mil", Region.Europe),
            ("cph", Region.Europe), ("sto", Region.Europe), ("vie", Region.Europe),
            ("hel", Region.Europe), ("osl", Region.Europe), ("ath", Region.Europe),
            ("kie", Region.Europe),
            ("los", Region.Africa), ("alg", Region.Africa), ("nbo", Region.Africa),
            ("add", Region.Africa),
            ("bgw", Region.SouthWestAsia), ("khi", Region.SouthWestAsia), ("ccu", Region.SouthWestAsia),
            ("osa", Region.EastAsia), ("mel", Region.AustralAsia), ("bne", Region.AustralAsia),
            ("akl", Region.AustralAsia),
            ("sea", Region.NorthAmerica), ("phx", Region.NorthAmerica), ("phl", Region.NorthAmerica),
        };

        Assert.Equal(24, expectedByRegion.Length);
        Assert.All(expectedByRegion, entry =>
        {
            Assert.Contains(entry.Id, ids);
            Assert.Equal(entry.Region, cities.Single(c => c.Id == entry.Id).Region);
        });
    }

    [Fact]
    public void Sydney_IsAGateway_NotEligibleStartingCity()
    {
        // A real regression this addition surfaced: Sydney is ~7,000 km
        // from every other Asia-tagged city - too remote for any 1955-era
        // aircraft to reach from it, which broke
        // StartingConditionsTests.Resolve_EveryEligibleStartingCity_
        // ProducesAcceptedQuarter1Route when Sydney was first added as
        // non-gateway. Tagging it a gateway (reflecting its real role as
        // Oceania's isolated entry point, the same way Cairo/Dakar gate
        // Africa) removes it from HomeRegions.EligibleStartingCities
        // without inventing new exclusion logic, and it's still fully
        // flyable once an airline has long-enough-range aircraft (M18
        // already found QuarterEngine never gates routes to gateways).
        // Sydney's region became AustralAsia once Asia split.
        var cities = CityCatalog.LoadFile(DataPath);
        var sydney = cities.Single(c => c.Id == "syd");

        Assert.True(sydney.IsGateway);
        Assert.True(sydney.GateCapacity is > 0);
        Assert.DoesNotContain(HomeRegions.EligibleStartingCities(Region.AustralAsia, cities), c => c.Id == "syd");
    }

    [Fact]
    public void LoadFile_IncludesAerobizSupersonicMinorTierCities()
    {
        // Second half of the "by tier, not region" pacing the user chose
        // for the 43-city gap found against Aerobiz Supersonic's full
        // list: the ~19 smaller/chokepoint-tier cities left after the
        // major-tier round. Mostly secondary gateways and regional
        // stepping-stones (Pacific islands, North African coastal cities)
        // rather than demand centers in their own right.
        var cities = CityCatalog.LoadFile(DataPath);
        var ids = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expectedByRegion = new (string Id, Region Region)[]
        {
            ("kin", Region.NorthAmerica),
            ("nce", Region.Europe), ("msq", Region.Europe), ("rov", Region.Europe),
            ("tun", Region.Africa), ("tip", Region.Africa),
            ("per", Region.AustralAsia), ("fuk", Region.EastAsia), ("tas", Region.SouthWestAsia),
            ("isb", Region.SouthWestAsia), ("adl", Region.AustralAsia), ("ceb", Region.AustralAsia),
            ("spr", Region.EastAsia), ("nan", Region.AustralAsia), ("gum", Region.AustralAsia),
            ("khv", Region.EastAsia), ("nou", Region.AustralAsia), ("ppt", Region.AustralAsia),
            ("spn", Region.AustralAsia),
        };

        Assert.Equal(19, expectedByRegion.Length);
        Assert.All(expectedByRegion, entry =>
        {
            Assert.Contains(entry.Id, ids);
            Assert.Equal(entry.Region, cities.Single(c => c.Id == entry.Id).Region);
            Assert.False(cities.Single(c => c.Id == entry.Id).IsGateway);
        });
    }

    [Fact]
    public void LoadFile_IncludesJakarta()
    {
        // A real gap noticed while scoping a possible Asia region split:
        // Indonesia had zero cities in the roster despite being the
        // world's 4th most populous country and Southeast Asia's largest
        // aviation market for most of the campaign's timeline. Weighted
        // alongside Bangkok/Mumbai/Osaka (65) rather than the
        // Manila/Taipei/Kuala Lumpur tier (55), reflecting Jakarta's own
        // real-world scale. Landed in AustralAsia once Asia split -
        // grouped with Southeast Asia rather than Australia/Pacific alone.
        var cities = CityCatalog.LoadFile(DataPath);
        var jakarta = cities.Single(c => c.Id == "cgk");

        Assert.Equal("Jakarta", jakarta.Name);
        Assert.Equal(Region.AustralAsia, jakarta.Region);
        Assert.Equal(65, jakarta.DemandWeight);
        Assert.False(jakarta.IsGateway);
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
          { "id": "zzz", "name": "Z City", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null },
          { "id": "aaa", "name": "A City", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null },
          { "id": "mmm", "name": "M City", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null }
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
          { "id": "dup", "name": "First", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null },
          { "id": "dup", "name": "Second", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => CityCatalog.Load(json));
    }

    [Fact]
    public void Load_ThrowsWhenGatewayHasNoGateCapacity()
    {
        const string json = """
        [
          { "id": "bad", "name": "Bad Gateway", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": true, "gateCapacity": null }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => CityCatalog.Load(json));
    }

    [Fact]
    public void Load_ThrowsOnNonPositiveDemandWeight()
    {
        const string json = """
        [
          { "id": "bad", "name": "Bad Demand", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 0, "isGateway": false, "gateCapacity": null }
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
          { "id": "", "name": "No Id", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => CityCatalog.Load(json));
    }

    [Fact]
    public void Load_ThrowsOnMissingName()
    {
        const string json = """
        [
          { "id": "bad", "name": "  ", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => CityCatalog.Load(json));
    }

    // --- M45: AutoCapacity ---

    [Fact]
    public void LoadFile_NonGatewayCities_AllHavePositiveAutoCapacity()
    {
        var cities = CityCatalog.LoadFile(DataPath);

        foreach (var city in cities.Where(c => !c.IsGateway))
        {
            Assert.True(city.AutoCapacity is > 0, $"{city.Id} is not a gateway but has no positive AutoCapacity.");
        }
    }

    [Fact]
    public void LoadFile_GatewayCities_NeverHaveAutoCapacity()
    {
        var cities = CityCatalog.LoadFile(DataPath);

        foreach (var city in cities.Where(c => c.IsGateway))
        {
            Assert.Null(city.AutoCapacity);
        }
    }

    [Fact]
    public void Load_MissingAutoCapacityField_DefaultsToNull_NotAThrow()
    {
        // Pre-M45 JSON literals (this file's own, and any older fixture)
        // never include autoCapacity at all - City's own default parameter
        // must keep deserializing them, not require the field.
        const string json = """
        [
          { "id": "old", "name": "Old Fixture", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null }
        ]
        """;

        var cities = CityCatalog.Load(json);

        Assert.Null(cities.Single().AutoCapacity);
    }

    [Fact]
    public void Load_ThrowsWhenBothGateCapacityAndAutoCapacitySet()
    {
        const string json = """
        [
          { "id": "bad", "name": "Bad", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": true, "gateCapacity": 3, "autoCapacity": 2 }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => CityCatalog.Load(json));
    }

    [Fact]
    public void Load_ThrowsOnNonPositiveAutoCapacity()
    {
        const string json = """
        [
          { "id": "bad", "name": "Bad", "region": "EastAsia", "latitude": 0, "longitude": 0, "demandWeight": 10, "isGateway": false, "gateCapacity": null, "autoCapacity": 0 }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => CityCatalog.Load(json));
    }
}
