using AeroNext.Core.Airlines;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// Geopolitical-aircraft-restrictions follow-up
/// (docs/GEOPOLITICAL_AIRCRAFT_RESTRICTIONS_ROADMAP.md): the headline
/// regression guard the whole design exists to make real - a Beijing-
/// based airline's own purchasable catalog genuinely narrows through the
/// Sino-Soviet-split/US-embargo overlap but never actually reaches zero,
/// against the real repo data rather than a synthetic fixture. Locks in
/// the corrected finding after this doc's own first version wrongly
/// claimed a full 48-quarter zero-aircraft window.
/// </summary>
public class GeopoliticalRestrictionsIntegrationTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");

    private static (IReadOnlyList<AircraftType> Fleet, City Beijing, IReadOnlyList<TradeRestriction> Restrictions) LoadRealData()
    {
        var cities = CityCatalog.LoadFile(Path.Combine(DataDir, "cities.json"));
        var knownCityIds = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fleet = AircraftCatalog.LoadFile(Path.Combine(DataDir, "aircraft.json"));
        var restrictions = TradeRestrictionCatalog.LoadFile(Path.Combine(DataDir, "trade-restrictions.json"), knownCityIds);
        var beijing = cities.Single(c => c.Id == "pek");

        return (fleet, beijing, restrictions);
    }

    private static IReadOnlyList<AircraftType> PurchasableFor(IReadOnlyList<AircraftType> fleet, City homeCity, IReadOnlyList<TradeRestriction> restrictions, int year) =>
        fleet.Where(a => Timeline.IsAvailableForPurchase(a, year) && !TradeRestriction.IsRestricted(a, homeCity, year, restrictions)).ToList();

    [Fact]
    public void Beijing_NeverHasZeroPurchasableTypes_AcrossTheWholeDoubleEmbargoWindow()
    {
        // This doc's own original (wrong) claim was a full zero-aircraft
        // window for 1960-1972. Checked here across a wider span - from
        // Timeline.StartYear (1955, the first year the campaign can ever
        // actually reach; the real embargo started in 1949, but no
        // quarter ever resolves that early) through 1989, the real end
        // of the Sino-Soviet split - so the guard covers both embargoes'
        // own edges, not just the overlap.
        var (fleet, beijing, restrictions) = LoadRealData();

        for (var year = Timeline.StartYear; year <= 1989; year++)
        {
            var purchasable = PurchasableFor(fleet, beijing, restrictions, year);

            Assert.True(purchasable.Count > 0, $"Beijing had zero purchasable aircraft types in {year} - this is the exact regression this test exists to catch.");
        }
    }

    [Fact]
    public void Beijing_1965_DeepInBothEmbargoes_AmericanAndSovietTypesAbsent_EuropeanTypesPresent()
    {
        var (fleet, beijing, restrictions) = LoadRealData();

        var purchasable = PurchasableFor(fleet, beijing, restrictions, 1965).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("707", purchasable);  // American, embargoed 1949-1972
        Assert.DoesNotContain("dc8", purchasable);  // American
        Assert.Contains("caravelle", purchasable);  // European (French) - never embargoed against China
        Assert.Contains("bac111", purchasable);     // European (British) - never embargoed against China
    }

    [Fact]
    public void Beijing_1958_BeforeSinoSovietSplit_SovietTypesAvailable_AmericanAlreadyEmbargoed()
    {
        var (fleet, beijing, restrictions) = LoadRealData();

        var purchasable = PurchasableFor(fleet, beijing, restrictions, 1958).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("il14", purchasable);   // Soviet, split hasn't happened yet (1960)
        Assert.Contains("tu104", purchasable);  // Soviet
        Assert.DoesNotContain("707", purchasable); // American embargo already active since 1949
    }

    [Fact]
    public void Beijing_1975_AfterUsEmbargoLifted_AmericanTypesAvailable_SovietStillEmbargoed()
    {
        var (fleet, beijing, restrictions) = LoadRealData();

        var purchasable = PurchasableFor(fleet, beijing, restrictions, 1975).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("707", purchasable);        // American embargo lifted 1972
        Assert.Contains("dc10", purchasable);
        Assert.DoesNotContain("il62", purchasable); // Soviet embargo still active until 1989
        Assert.DoesNotContain("tu154", purchasable);
    }

    [Fact]
    public void Beijing_1990_AfterBothEmbargoesLifted_AmericanAndSovietTypesBothAvailable()
    {
        var (fleet, beijing, restrictions) = LoadRealData();

        var purchasable = PurchasableFor(fleet, beijing, restrictions, 1990).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("md80", purchasable);  // American, still within its own 1980-1999 window
        Assert.Contains("il62", purchasable);  // Soviet, still within its own 1967-1995 window, embargo lifted 1989
    }

    [Fact]
    public void Moscow_NeverHasZeroPurchasableTypes_ThroughoutTheColdWar()
    {
        // The USSR's own continuous domestic industry means Moscow, unlike
        // Beijing, should never hit zero either - checked directly rather
        // than assumed, since it's cut off from both American and
        // European types for the entire window. This test caught a real
        // catalog gap on its first run: with only il14/tu104 (both ending
        // production in 1960) and il62 (not starting until 1967), Moscow
        // had nothing purchasable 1961-1966 - not a flaw in the
        // restriction logic, but a genuine missing entry (the Il-18,
        // Aeroflot's actual mainstay turboprop across exactly that
        // window, 1959-1985) now added to data/aircraft.json.
        var fleet = AircraftCatalog.LoadFile(Path.Combine(DataDir, "aircraft.json"));
        var cities = CityCatalog.LoadFile(Path.Combine(DataDir, "cities.json"));
        var knownCityIds = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var moscow = cities.Single(c => c.Id == "mow");
        var restrictions = TradeRestrictionCatalog.LoadFile(Path.Combine(DataDir, "trade-restrictions.json"), knownCityIds);

        for (var year = Timeline.StartYear; year <= 1991; year++)
        {
            var purchasable = PurchasableFor(fleet, moscow, restrictions, year);

            Assert.True(purchasable.Count > 0, $"Moscow had zero purchasable aircraft types in {year}.");
        }
    }
}
