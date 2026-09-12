using AeroNext.Core;
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
    public void CreateSeats_TwoHumansClaimSameRegion_DifferentCities_Succeeds()
    {
        // Regions are no longer exclusive - a home region was never meant
        // to be a private lane (CLAUDE.md's "a home region cannot fund a
        // win" already assumes rivals converge on the same demand pool).
        // Only starting city needs to stay distinct.
        var choices = new[]
        {
            new HumanPlayerChoice("Atlas Air", "ord"),
            new HumanPlayerChoice("Meridian", "sfo"), // also NorthAmerica
        };

        var seats = GameSetup.CreateSeats(choices, RealCities);

        var atlas = seats.Single(s => s.AirlineName == "Atlas Air");
        var meridian = seats.Single(s => s.AirlineName == "Meridian");
        Assert.Equal(Region.NorthAmerica, atlas.HomeRegion);
        Assert.Equal(Region.NorthAmerica, meridian.HomeRegion);
        Assert.NotEqual(atlas.StartingCity.Id, meridian.StartingCity.Id);
    }

    [Fact]
    public void CreateSeats_AllFourHumansClaimSameRegion_DistinctCities_Succeeds()
    {
        // The extreme case: every region has at least 4 eligible cities
        // (the established parity floor), so a region can host all 4
        // seats at once now that region exclusivity is gone.
        var choices = new[]
        {
            new HumanPlayerChoice("A", "ord"),
            new HumanPlayerChoice("B", "sfo"),
            new HumanPlayerChoice("C", "atl"),
            new HumanPlayerChoice("D", "mia"),
        };

        var seats = GameSetup.CreateSeats(choices, RealCities);

        Assert.Equal(4, seats.Count);
        Assert.All(seats, s => Assert.Equal(Region.NorthAmerica, s.HomeRegion));
        Assert.Equal(4, seats.Select(s => s.StartingCity.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void CreateSeats_HumanSharesRegionWithAi_AiStillPicksRichestOtherUnclaimedRegion()
    {
        // AI region choice stays exactly as naive as before this change -
        // it never doubles up on its own, even though a human sharing a
        // region with it is now legal. One human takes NorthAmerica (the
        // richest region), so AI's 3 picks should be the next-richest
        // three distinct, still-unclaimed regions - same "richest
        // unclaimed" logic as the zero-human case, just starting from a
        // smaller pool.
        var choices = new[] { new HumanPlayerChoice("Atlas Air", "ord") };

        var seats = GameSetup.CreateSeats(choices, RealCities);

        Assert.Equal(4, seats.Count);
        Assert.Equal(1, seats.Count(s => s.HomeRegion == Region.NorthAmerica));
        Assert.Equal(4, seats.Select(s => s.HomeRegion).Distinct().Count());
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

    // --- fillerRegionRng (docs/OPEN_WORK.md's filler-seat investigation) ---

    [Fact]
    public void CreateSeats_NullFillerRng_MatchesTheTwoArgOverloadExactly()
    {
        var withExplicitNull = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities, fillerRegionRng: null);
        var twoArg = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities);

        Assert.Equal(twoArg, withExplicitNull);
    }

    [Fact]
    public void CreateSeats_WithFillerRng_IsDeterministic_ForTheSameSeed()
    {
        var first = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities, new SimRng(42));
        var second = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities, new SimRng(42));

        Assert.Equal(first, second);
    }

    [Fact]
    public void CreateSeats_WithFillerRng_StillProducesFourValidDistinctSeats_AcrossManySeeds()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var seats = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities, new SimRng(seed));

            Assert.Equal(4, seats.Count);
            Assert.All(seats, s => Assert.False(s.IsHuman));
            Assert.Equal(4, seats.Select(s => s.HomeRegion).Distinct().Count());
            Assert.Equal(4, seats.Select(s => s.StartingCity.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }
    }

    [Fact]
    public void CreateSeats_WithFillerRng_EveryRegionCanAppear_AcrossManySeeds()
    {
        // The whole point: AustralAsia and SouthWestAsia are permanently
        // excluded from the deterministic richest-first draft (see
        // CreateSeats_ZeroHumans_AllFourSeatsAreDeterministicAi above) -
        // weighted-random must give them a real, nonzero chance instead.
        var seenRegions = new HashSet<Region>();
        for (var seed = 0; seed < 300; seed++)
        {
            var seats = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities, new SimRng(seed));
            foreach (var s in seats) seenRegions.Add(s.HomeRegion);
        }

        Assert.Contains(Region.AustralAsia, seenRegions);
        Assert.Contains(Region.SouthWestAsia, seenRegions);
        Assert.Equal(HomeRegions.All.ToHashSet(), seenRegions);
    }

    [Fact]
    public void CreateSeats_WithFillerRng_RicherRegionsAreStillChosenMoreOften()
    {
        var appearances = HomeRegions.All.ToDictionary(r => r, _ => 0);
        const int trials = 500;
        for (var seed = 0; seed < trials; seed++)
        {
            var seats = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities, new SimRng(seed));
            foreach (var s in seats) appearances[s.HomeRegion]++;
        }

        // EastAsia/NorthAmerica (the two richest by RegionalEconomics.Factor)
        // should appear more often than AustralAsia (the poorest) - weighted,
        // not uniform, random.
        Assert.True(appearances[Region.EastAsia] > appearances[Region.AustralAsia]);
        Assert.True(appearances[Region.NorthAmerica] > appearances[Region.AustralAsia]);
        // But AustralAsia must appear a real, non-trivial fraction of the
        // time too - not just a rare fluke.
        Assert.True(appearances[Region.AustralAsia] > trials / 20);
    }

    [Fact]
    public void CreateSeats_WithFillerRng_StillExcludesHumanClaimedRegions()
    {
        var choices = new[] { new HumanPlayerChoice("Human One", "cdg") }; // Europe

        for (var seed = 0; seed < 50; seed++)
        {
            var seats = GameSetup.CreateSeats(choices, RealCities, new SimRng(seed));

            Assert.Equal(4, seats.Count);
            Assert.Equal(1, seats.Count(s => s.HomeRegion == Region.Europe)); // only the human's own seat
        }
    }

    [Fact]
    public void CreateSeats_FillerWeightExponentZero_IsUniformRandom_RegardlessOfFactor()
    {
        var appearances = HomeRegions.All.ToDictionary(r => r, _ => 0);
        const int trials = 600;
        for (var seed = 0; seed < trials; seed++)
        {
            var seats = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities, new SimRng(seed), fillerWeightExponent: 0.0);
            foreach (var s in seats) appearances[s.HomeRegion]++;
        }

        // Every region equally likely (weight = Factor^0 = 1 for all) -
        // AustralAsia (poorest) and EastAsia (richest) should land within a
        // generous band of each other; not the ~3x spread real weighting gives.
        var ratio = (double)appearances[Region.EastAsia] / appearances[Region.AustralAsia];
        Assert.InRange(ratio, 0.7, 1.4);
    }

    [Fact]
    public void CreateSeats_HigherFillerWeightExponent_SharpensTheBiasTowardRicherRegions()
    {
        double AustralAsiaShareAt(double exponent)
        {
            var appearances = HomeRegions.All.ToDictionary(r => r, _ => 0);
            const int trials = 400;
            for (var seed = 0; seed < trials; seed++)
            {
                var seats = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities, new SimRng(seed), exponent);
                foreach (var s in seats) appearances[s.HomeRegion]++;
            }
            return appearances[Region.AustralAsia];
        }

        var atExponentOne = AustralAsiaShareAt(1.0);
        var atExponentFour = AustralAsiaShareAt(4.0);

        // A steeper exponent pushes AustralAsia (the poorest region) toward
        // its old near-total exclusion; a flatter one (already covered by
        // the exponent-0 test above) pushes the other way.
        Assert.True(atExponentFour < atExponentOne);
    }

    [Fact]
    public void CreateSeats_FillerWeightExponent_DefaultsToOne_MatchingPriorBehavior()
    {
        var withDefault = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities, new SimRng(7));
        var withExplicitOne = GameSetup.CreateSeats(Array.Empty<HumanPlayerChoice>(), RealCities, new SimRng(7), fillerWeightExponent: 1.0);

        Assert.Equal(withExplicitOne, withDefault);
    }
}
