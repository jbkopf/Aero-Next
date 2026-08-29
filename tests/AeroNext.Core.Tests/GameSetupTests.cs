using AeroNext.Core.Setup;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class GameSetupTests
{
    private static string CitiesDataPath => Path.Combine(AppContext.BaseDirectory, "data", "cities.json");
    private static IReadOnlyList<City> RealCities => CityCatalog.LoadFile(CitiesDataPath);

    [Fact]
    public void CreateSeats_ZeroHumans_AllFourSeatsAreDeterministicAi()
    {
        var seats = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities);

        Assert.Equal(4, seats.Count);
        Assert.All(seats, s => Assert.False(s.IsHuman));

        // With 6 home regions for 4 slots, an all-AI game is a real draft
        // by RegionalEconomics.Factor - verified against the real dataset:
        // NorthAmerica and EastAsia are the two richest (a near-exact tie,
        // broken alphabetically), then SouthAmerica, then Europe.
        // SouthWestAsia and AustralAsia are the two that sit out.
        Assert.Equal(
            new[] { Region.Europe, Region.EastAsia, Region.NorthAmerica, Region.SouthAmerica }.OrderBy(r => r),
            seats.Select(s => s.HomeRegion).OrderBy(r => r));
    }

    [Fact]
    public void CreateSeats_ZeroHumans_PicksHighestDemandEligibleCityPerRegion()
    {
        var seats = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities);
        var byRegion = seats.ToDictionary(s => s.HomeRegion);

        // Verified against the real dataset: highest-demandWeight eligible
        // (non-gateway) city in each home region.
        Assert.Equal("atl", byRegion[Region.NorthAmerica].StartingCity.Id);
        Assert.Equal("cdg", byRegion[Region.Europe].StartingCity.Id);
        Assert.Equal("pek", byRegion[Region.EastAsia].StartingCity.Id);
        Assert.Equal("gig", byRegion[Region.SouthAmerica].StartingCity.Id);
    }

    [Fact]
    public void CreateSeats_ZeroHumans_IsDeterministicAcrossRepeatedCalls()
    {
        var first = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities);
        var second = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities);

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateSeats_FourHumans_NoAiSeats_AllFourClaimedRegionsDistinct()
    {
        // Only 4 of the 6 home regions get claimed here (sin/Singapore is
        // AustralAsia) - with 6 regions for 4 slots, "all regions claimed"
        // no longer applies; what still must hold is that all 4 humans'
        // regions are pairwise distinct and no AI seat is needed.
        var choices = new[]
        {
            new HumanPlayerChoice("Atlas Air", "ord"),
            new HumanPlayerChoice("Meridian", "cdg"),
            new HumanPlayerChoice("Skyward", "sin"),
            new HumanPlayerChoice("Continental Wings", "eze"),
        };

        var seats = GameSetup.CreateSeats(choices, RealCities);

        Assert.Equal(4, seats.Count);
        Assert.All(seats, s => Assert.True(s.IsHuman));
        Assert.Equal(
            new[] { Region.NorthAmerica, Region.Europe, Region.AustralAsia, Region.SouthAmerica }.OrderBy(r => r),
            seats.Select(s => s.HomeRegion).OrderBy(r => r));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void CreateSeats_PartialHumanCount_FillsRemainingSeatsWithAi(int humanCount)
    {
        var allChoices = new[]
        {
            new HumanPlayerChoice("Atlas Air", "ord"),
            new HumanPlayerChoice("Meridian", "cdg"),
            new HumanPlayerChoice("Skyward", "sin"),
        };
        var choices = allChoices.Take(humanCount).ToArray();

        var seats = GameSetup.CreateSeats(choices, RealCities);

        Assert.Equal(4, seats.Count);
        Assert.Equal(humanCount, seats.Count(s => s.IsHuman));
        Assert.Equal(4 - humanCount, seats.Count(s => !s.IsHuman));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void CreateSeats_EveryHumanCount_ProducesFourPairwiseDistinctStartingCities(int humanCount)
    {
        var allChoices = new[]
        {
            new HumanPlayerChoice("Atlas Air", "ord"),
            new HumanPlayerChoice("Meridian", "cdg"),
            new HumanPlayerChoice("Skyward", "sin"),
            new HumanPlayerChoice("Continental Wings", "eze"),
        };
        var choices = allChoices.Take(humanCount).ToArray();

        var seats = GameSetup.CreateSeats(choices, RealCities);

        var distinctCityIds = seats.Select(s => s.StartingCity.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(4, distinctCityIds);
    }

    [Fact]
    public void CreateSeats_TooManyHumans_Throws()
    {
        var choices = new[]
        {
            new HumanPlayerChoice("A", "ord"),
            new HumanPlayerChoice("B", "cdg"),
            new HumanPlayerChoice("C", "sin"),
            new HumanPlayerChoice("D", "eze"),
            new HumanPlayerChoice("E", "gig"),
        };

        Assert.Throws<ArgumentException>(() => GameSetup.CreateSeats(choices, RealCities));
    }

    [Fact]
    public void CreateSeats_BlankAirlineName_Throws()
    {
        var choices = new[] { new HumanPlayerChoice("   ", "ord") };

        Assert.Throws<ArgumentException>(() => GameSetup.CreateSeats(choices, RealCities));
    }

    [Fact]
    public void CreateSeats_DuplicateAirlineName_Throws()
    {
        var choices = new[]
        {
            new HumanPlayerChoice("Atlas Air", "ord"),
            new HumanPlayerChoice("Atlas Air", "cdg"),
        };

        Assert.Throws<ArgumentException>(() => GameSetup.CreateSeats(choices, RealCities));
    }

    [Fact]
    public void CreateSeats_DuplicateAirlineName_CaseInsensitive_Throws()
    {
        var choices = new[]
        {
            new HumanPlayerChoice("Atlas Air", "ord"),
            new HumanPlayerChoice("ATLAS AIR", "cdg"),
        };

        Assert.Throws<ArgumentException>(() => GameSetup.CreateSeats(choices, RealCities));
    }

    [Fact]
    public void CreateSeats_TwoHumansClaimSameRegion_DifferentCities_Throws()
    {
        var choices = new[]
        {
            new HumanPlayerChoice("Atlas Air", "ord"),
            new HumanPlayerChoice("Meridian", "sfo"), // also NorthAmerica
        };

        Assert.Throws<ArgumentException>(() => GameSetup.CreateSeats(choices, RealCities));
    }

    [Fact]
    public void CreateSeats_TwoHumansClaimSameCity_Throws()
    {
        var choices = new[]
        {
            new HumanPlayerChoice("Atlas Air", "ord"),
            new HumanPlayerChoice("Meridian", "ord"),
        };

        Assert.Throws<ArgumentException>(() => GameSetup.CreateSeats(choices, RealCities));
    }

    [Fact]
    public void CreateSeats_UnknownCityId_Throws()
    {
        var choices = new[] { new HumanPlayerChoice("Atlas Air", "nope") };

        Assert.Throws<ArgumentException>(() => GameSetup.CreateSeats(choices, RealCities));
    }

    [Fact]
    public void CreateSeats_GatewayCity_IsNotEligible_Throws()
    {
        var choices = new[] { new HumanPlayerChoice("Atlas Air", "jfk") };

        Assert.Throws<ArgumentException>(() => GameSetup.CreateSeats(choices, RealCities));
    }

    [Fact]
    public void CreateSeats_NonHomeRegionCity_Africa_Throws()
    {
        var choices = new[] { new HumanPlayerChoice("Atlas Air", "cai") };

        Assert.Throws<ArgumentException>(() => GameSetup.CreateSeats(choices, RealCities));
    }

    [Fact]
    public void CreateSeats_HumanNameCollidesWithGeneratedAiName_Throws()
    {
        // "NorthAmerica Air" is exactly what the AI auto-name would be for
        // an unclaimed NorthAmerica seat.
        var choices = new[] { new HumanPlayerChoice("NorthAmerica Air", "cdg") };

        Assert.Throws<ArgumentException>(() => GameSetup.CreateSeats(choices, RealCities));
    }
}
