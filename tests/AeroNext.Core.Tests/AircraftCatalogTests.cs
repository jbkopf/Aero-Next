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
    public void LoadFile_NoPre1958Aircraft_ExceedsConstellationsRange()
    {
        // Regression guard for a real risk checked during the round-2
        // pre-1965 expansion: M13's campaign narrative and BaselineAi's
        // aircraft selection both hinge on the Constellation (l049, 4,200
        // km) being 1955's longest-range available aircraft until the
        // Boeing 707 (1958) arrives. Adding period-real DC-6/DC-7 without
        // checking this could have silently swapped the campaign's
        // starting aircraft to whichever new type has the longest "typical
        // payload" range - verified once by actually running the campaign
        // (docs/SIM_CORE_ROADMAP.md's M13 section), locked in here so a
        // future data edit can't silently reintroduce it.
        var fleet = AircraftCatalog.LoadFile(DataPath);
        var constellation = fleet.Single(a => a.Id == "l049");

        foreach (var aircraft in fleet.Where(a => a.AvailableFromYear < 1958 && a.Id != "l049"))
        {
            Assert.True(aircraft.RangeKm <= constellation.RangeKm,
                $"{aircraft.Id} ({aircraft.RangeKm} km, available {aircraft.AvailableFromYear}) exceeds the Constellation's " +
                $"{constellation.RangeKm} km before the 707 arrives in 1958 - this would change BaselineAi's aircraft pick " +
                "and M13's campaign trajectory. Confirm that's intended, then update docs/SIM_CORE_ROADMAP.md's M13 section.");
        }
    }

    [Fact]
    public void LoadFile_NoPre1970Aircraft_ExceedsThe747sRange()
    {
        // Same regression class as the Constellation check above, extended
        // to the next "longest available" leader: M13's campaign and
        // BaselineAi both rely on the 747 (13,000 km) being the era's best
        // from 1970 until the 777 (1995). Round 3's widebodies (DC-10,
        // L-1011) came close to this in real life - checked deliberately,
        // not assumed safe.
        var fleet = AircraftCatalog.LoadFile(DataPath);
        var boeing747 = fleet.Single(a => a.Id == "747");

        foreach (var aircraft in fleet.Where(a => a.AvailableFromYear < 1970 && a.Id != "747"))
        {
            Assert.True(aircraft.RangeKm <= boeing747.RangeKm,
                $"{aircraft.Id} ({aircraft.RangeKm} km, available {aircraft.AvailableFromYear}) exceeds the 747's " +
                $"{boeing747.RangeKm} km before it arrives in 1970 - this would change BaselineAi's aircraft pick.");
        }
    }

    [Fact]
    public void LoadFile_NoPre1995Aircraft_ExceedsThe777sRange()
    {
        // Third guard in the same "longest available" regression class:
        // round 4's A330/A340 (both widebodies with real-world range close
        // to the 747's 13,000 km) are the aircraft this specifically
        // protects against for the 747->777 transition.
        var fleet = AircraftCatalog.LoadFile(DataPath);
        var boeing777 = fleet.Single(a => a.Id == "777");

        foreach (var aircraft in fleet.Where(a => a.AvailableFromYear < 1995 && a.Id != "777"))
        {
            Assert.True(aircraft.RangeKm <= boeing777.RangeKm,
                $"{aircraft.Id} ({aircraft.RangeKm} km, available {aircraft.AvailableFromYear}) exceeds the 777's " +
                $"{boeing777.RangeKm} km before it arrives in 1995 - this would change BaselineAi's aircraft pick.");
        }
    }

    [Fact]
    public void LoadFile_NoAircraft_ExceedsThe777sRange()
    {
        // Completes the "longest available" regression class: the 777
        // (1995) is the campaign's all-time range champion through 2015 -
        // round 5's widebodies (A380, 787, A350) all came respectably
        // close in real life without exceeding it. Checked directly rather
        // than assumed, same as every earlier round in this class.
        var fleet = AircraftCatalog.LoadFile(DataPath);
        var boeing777 = fleet.Single(a => a.Id == "777");

        foreach (var aircraft in fleet.Where(a => a.Id != "777"))
        {
            Assert.True(aircraft.RangeKm <= boeing777.RangeKm,
                $"{aircraft.Id} ({aircraft.RangeKm} km) exceeds the 777's {boeing777.RangeKm} km - " +
                "it would become the campaign's new all-time longest-range aircraft.");
        }
    }

    [Fact]
    public void LoadFile_Includes2000sAnd2010sAircraft()
    {
        // Round 5: the final stretch of the campaign timeline (2000-2015)
        // had only ARJ21 (2008) before this.
        var fleet = AircraftCatalog.LoadFile(DataPath);
        var ids = fleet.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("e190", ids);   // Embraer E190 (2004)
        Assert.Contains("a380", ids);   // Airbus A380 (2005) - largest passenger airliner ever built
        Assert.Contains("ssj100", ids); // Sukhoi Superjet 100 (2011)
        Assert.Contains("787", ids);    // Boeing 787 Dreamliner (2011)
        Assert.Contains("a350", ids);   // Airbus A350 (2015) - available for only the campaign's final quarters
    }

    [Fact]
    public void LoadFile_Includes1980sAnd1990sAircraft()
    {
        // Round 4: deepens what had been a thin era (only emb120/a320/
        // crj100/777 covered two full decades).
        var fleet = AircraftCatalog.LoadFile(DataPath);
        var ids = fleet.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("md80", ids);  // McDonnell Douglas MD-80 (1980)
        Assert.Contains("il86", ids);  // Ilyushin Il-86 (1980) - first Soviet widebody
        Assert.Contains("767", ids);   // Boeing 767 (1982)
        Assert.Contains("757", ids);   // Boeing 757 (1983)
        Assert.Contains("atr72", ids); // ATR 72 (1988) - regional turboprop
        Assert.Contains("a340", ids);  // Airbus A340 (1993)
        Assert.Contains("a330", ids);  // Airbus A330 (1994)
    }

    [Fact]
    public void LoadFile_Includes1960sAnd1970sAircraft()
    {
        // Round 3: the era between the Comet/707 (round 1-2) and the
        // widebody age was previously thin (only il62/737/747/a300).
        var fleet = AircraftCatalog.LoadFile(DataPath);
        var ids = fleet.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("727", ids);   // Boeing 727 (1963) - best-selling airliner of its era
        Assert.Contains("dc9", ids);   // McDonnell Douglas DC-9 (1965)
        Assert.Contains("bac111", ids); // BAC One-Eleven (1965)
        Assert.Contains("dc10", ids);  // McDonnell Douglas DC-10 (1971)
        Assert.Contains("l1011", ids); // Lockheed L-1011 TriStar (1972)
        Assert.Contains("tu154", ids); // Tupolev Tu-154 (1972) - most-produced Soviet jet airliner
        Assert.Contains("concorde", ids); // Concorde (1976)
    }

    [Fact]
    public void LoadFile_Concorde_HasNotablyPoorEfficiencyDespiteHighCost()
    {
        // A deliberate piece of flavor the CostPerAvailableSeatKm stat can
        // capture even without a speed mechanic: Concorde was famously
        // expensive to both buy and fly per seat, which is exactly why
        // only two heavily-subsidized carriers ever operated it.
        var fleet = AircraftCatalog.LoadFile(DataPath);
        var concorde = fleet.Single(a => a.Id == "concorde");
        var worstOtherEfficiency = fleet.Where(a => a.Id != "concorde").Max(a => a.CostPerAvailableSeatKm);

        Assert.True(concorde.CostPerAvailableSeatKm > worstOtherEfficiency,
            "Concorde should be less efficient per seat-km than every other aircraft in the catalog.");
    }

    [Fact]
    public void LoadFile_EveryHomeRegion_HasAtLeastOneManufacturedType()
    {
        // M16 could only assert this for NorthAmerica/Europe — Asia and
        // South America had no indigenous manufacturer that early in the
        // campaign. The later aircraft-catalog expansion (Embraer for
        // SouthAmerica, 1985; COMAC for Asia/EastAsia, 2008) closes that
        // gap for real: NorthAmerica/Europe/EastAsia/SouthAmerica each have
        // their own manufactured type *eventually*, even though (per
        // RegionalFleetTests) none of them has one at the 1955 campaign
        // start. SouthWestAsia and AustralAsia (post Asia-split) have no
        // manufacturer under current data at all — an honest reflection of
        // real aviation history, not a gap this test expects closed.
        var fleet = AircraftCatalog.LoadFile(DataPath);

        foreach (var region in new[] { Region.NorthAmerica, Region.Europe, Region.EastAsia, Region.SouthAmerica })
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
    public void Load_ParsesCruiseSpeedKmh_WhenPresent()
    {
        const string json = """
        [{ "id": "t", "name": "Test", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960, "cruiseSpeedKmh": 850 }]
        """;

        var fleet = AircraftCatalog.Load(json);

        Assert.Equal(850, fleet.Single().CruiseSpeedKmh);
    }

    [Fact]
    public void Load_CruiseSpeedKmh_DefaultsTo900_WhenAbsent()
    {
        const string json = """
        [{ "id": "t", "name": "Test", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960 }]
        """;

        var fleet = AircraftCatalog.Load(json);

        Assert.Equal(900, fleet.Single().CruiseSpeedKmh);
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
    [InlineData("""{ "id": "x", "name": "N", "seats": 100, "rangeKm": 1000, "costDollars": 1, "productionRatePerQuarter": 4, "availableFromYear": 1960, "cruiseSpeedKmh": 0 }""")]
    public void Load_ThrowsOnInvalidEntry(string entry)
    {
        var json = $"[{entry}]";

        Assert.Throws<InvalidDataException>(() => AircraftCatalog.Load(json));
    }
}
