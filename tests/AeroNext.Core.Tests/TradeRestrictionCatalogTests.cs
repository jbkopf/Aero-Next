using AeroNext.Core;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class TradeRestrictionCatalogTests
{
    private static string DataPath => Path.Combine(AppContext.BaseDirectory, "data", "trade-restrictions.json");
    private static string CitiesPath => Path.Combine(AppContext.BaseDirectory, "data", "cities.json");

    private static IReadOnlySet<string> RealCityIds =>
        CityCatalog.LoadFile(CitiesPath).Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void LoadFile_LoadsRepoData_AgainstRealCityIds()
    {
        var restrictions = TradeRestrictionCatalog.LoadFile(DataPath, RealCityIds);

        Assert.NotEmpty(restrictions);
    }

    [Fact]
    public void LoadFile_IncludesBeijingsDoubleEmbargo()
    {
        var restrictions = TradeRestrictionCatalog.LoadFile(DataPath, RealCityIds);
        var beijingRestrictions = restrictions.Where(r => r.CityId == "pek").ToList();

        Assert.Contains(beijingRestrictions, r => r.RestrictedBloc == PoliticalBloc.American && r.StartYear == 1949 && r.EndYear == 1972);
        Assert.Contains(beijingRestrictions, r => r.RestrictedBloc == PoliticalBloc.Soviet && r.StartYear == 1960 && r.EndYear == 1989);
    }

    [Fact]
    public void LoadFile_HavanaAndTehran_AreOpenEnded()
    {
        var restrictions = TradeRestrictionCatalog.LoadFile(DataPath, RealCityIds);

        Assert.Contains(restrictions, r => r.CityId == "hav" && r.EndYear is null);
        Assert.Contains(restrictions, r => r.CityId == "thr" && r.EndYear is null);
    }

    [Fact]
    public void Load_ParsesAllFields()
    {
        const string json = """
        [{ "cityId": "pek", "restrictedBloc": "American", "startYear": 1949, "endYear": 1972 }]
        """;

        var restriction = TradeRestrictionCatalog.Load(json, new HashSet<string> { "pek" }).Single();

        Assert.Equal("pek", restriction.CityId);
        Assert.Equal(PoliticalBloc.American, restriction.RestrictedBloc);
        Assert.Equal(1949, restriction.StartYear);
        Assert.Equal(1972, restriction.EndYear);
    }

    [Fact]
    public void Load_NullEndYear_ParsesAsNeverLifted()
    {
        const string json = """
        [{ "cityId": "hav", "restrictedBloc": "American", "startYear": 1960, "endYear": null }]
        """;

        var restriction = TradeRestrictionCatalog.Load(json, new HashSet<string> { "hav" }).Single();

        Assert.Null(restriction.EndYear);
    }

    [Fact]
    public void Load_ReturnsSortedByCityIdThenBloc()
    {
        const string json = """
        [
          { "cityId": "pek", "restrictedBloc": "Soviet", "startYear": 1960, "endYear": 1989 },
          { "cityId": "hav", "restrictedBloc": "American", "startYear": 1960, "endYear": null },
          { "cityId": "pek", "restrictedBloc": "American", "startYear": 1949, "endYear": 1972 }
        ]
        """;

        var restrictions = TradeRestrictionCatalog.Load(json, new HashSet<string> { "pek", "hav" });

        Assert.Equal(
            new[] { ("hav", PoliticalBloc.American), ("pek", PoliticalBloc.American), ("pek", PoliticalBloc.Soviet) },
            restrictions.Select(r => (r.CityId, r.RestrictedBloc)));
    }

    [Fact]
    public void Load_ThrowsOnBlankCityId()
    {
        const string json = """
        [{ "cityId": "  ", "restrictedBloc": "American", "startYear": 1949, "endYear": 1972 }]
        """;

        Assert.Throws<InvalidDataException>(() => TradeRestrictionCatalog.Load(json, new HashSet<string> { "pek" }));
    }

    [Fact]
    public void Load_ThrowsOnUnknownCityId()
    {
        const string json = """
        [{ "cityId": "nowhere", "restrictedBloc": "American", "startYear": 1949, "endYear": 1972 }]
        """;

        Assert.Throws<InvalidDataException>(() => TradeRestrictionCatalog.Load(json, new HashSet<string> { "pek" }));
    }

    [Fact]
    public void Load_ThrowsWhenEndYearBeforeStartYear()
    {
        const string json = """
        [{ "cityId": "pek", "restrictedBloc": "American", "startYear": 1972, "endYear": 1949 }]
        """;

        Assert.Throws<InvalidDataException>(() => TradeRestrictionCatalog.Load(json, new HashSet<string> { "pek" }));
    }

    [Fact]
    public void Load_ThrowsOnExactDuplicateRow()
    {
        const string json = """
        [
          { "cityId": "pek", "restrictedBloc": "American", "startYear": 1949, "endYear": 1972 },
          { "cityId": "pek", "restrictedBloc": "American", "startYear": 1949, "endYear": 1972 }
        ]
        """;

        Assert.Throws<InvalidDataException>(() => TradeRestrictionCatalog.Load(json, new HashSet<string> { "pek" }));
    }

    [Fact]
    public void Load_AllowsTwoDifferentBlocsForTheSameCity()
    {
        const string json = """
        [
          { "cityId": "pek", "restrictedBloc": "American", "startYear": 1949, "endYear": 1972 },
          { "cityId": "pek", "restrictedBloc": "Soviet", "startYear": 1960, "endYear": 1989 }
        ]
        """;

        var restrictions = TradeRestrictionCatalog.Load(json, new HashSet<string> { "pek" });

        Assert.Equal(2, restrictions.Count);
    }

    [Fact]
    public void Load_ThrowsWhenJsonDeserializesToNull()
    {
        Assert.Throws<InvalidDataException>(() => TradeRestrictionCatalog.Load("null", new HashSet<string>()));
    }

    [Fact]
    public void Load_EmptyList_IsValid()
    {
        var restrictions = TradeRestrictionCatalog.Load("[]", new HashSet<string>());

        Assert.Empty(restrictions);
    }
}
