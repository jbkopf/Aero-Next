using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Setup;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class StartingConditionsTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");
    private static IReadOnlyList<City> RealCities => CityCatalog.LoadFile(Path.Combine(DataDir, "cities.json"));
    private static IReadOnlyList<AircraftType> RealFleet => AircraftCatalog.LoadFile(Path.Combine(DataDir, "aircraft.json"));
    private static IReadOnlyList<DemandGrowthPoint> RealGrowthSchedule => DemandGrowthCatalog.LoadCampaignSchedule(DataDir);
    private static readonly GravityDemandModel DemandModel = new();

    /// <summary>The exact demand model Quarter1DemandModel builds internally — real-data growth schedule works fine against synthetic test cities too (no matching id, so their city/region multipliers stay neutral).</summary>
    private static GravityDemandModel RealQuarter1DemandModel =>
        new(schedule: RealGrowthSchedule, year: Timeline.StartYear, enableHubDemandBonus: true,
            intraRegionMultipliers: GravityDemandModel.ShippedIntraRegionMultipliers,
            groundCompetitionDistanceKm: GravityDemandModel.ShippedGroundCompetitionDistanceKm);

    [Fact]
    public void Resolve_EveryEligibleStartingCity_ProducesAcceptedQuarter1Route()
    {
        // M18's own "Done when": not one hand-picked example, but every
        // (home region, eligible starting city) pair the real data offers.
        var cities = RealCities;
        var fleet = RealFleet;
        var growth = RealGrowthSchedule;

        foreach (var region in HomeRegions.All)
        {
            foreach (var startingCity in HomeRegions.EligibleStartingCities(region, cities))
            {
                var seat = new AirlineSeat("Test Airline", IsHuman: true, region, startingCity);

                var conditions = StartingConditionsResolver.Resolve(seat, cities, fleet, growth);

                Assert.True(conditions.StartingCash.Cents > 0,
                    $"{startingCity.Id}: starting cash must be positive.");
                Assert.Equal(StartingConditionsResolver.StartingAircraftQuantity, conditions.AircraftQuantity);
                Assert.NotEqual(startingCity.Id, conditions.GuaranteedRouteDestination.Id);
                Assert.Equal(region, conditions.GuaranteedRouteDestination.Region);
                Assert.True(Timeline.IsAvailable(conditions.StartingAircraft, Timeline.StartYear));

                var order = new RouteOrder(startingCity, conditions.GuaranteedRouteDestination, conditions.StartingAircraft);
                var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, cities, DemandModel);

                Assert.Empty(result.RejectedOrders);
                Assert.Contains(result.Routes, r => r.Origin.Id == startingCity.Id && r.Destination.Id == conditions.GuaranteedRouteDestination.Id);
            }
        }
    }

    [Fact]
    public void Resolve_Cash_ScalesWithRegionalEconomicFactor()
    {
        var cities = RealCities;
        var seat = new AirlineSeat("Test", IsHuman: true, Region.EastAsia, HomeRegions.EligibleStartingCities(Region.EastAsia, cities)[0]);

        var conditions = StartingConditionsResolver.Resolve(seat, cities, RealFleet, RealGrowthSchedule);
        var expectedFactor = RegionalEconomics.Factor(Region.EastAsia, cities);

        var expectedCash = Money.FromDollars((long)Math.Round(
            StartingConditionsResolver.BaseStartingCashDollars * expectedFactor, MidpointRounding.AwayFromZero));

        Assert.Equal(expectedCash, conditions.StartingCash);
    }

    [Fact]
    public void Resolve_IsDeterministic_AcrossRepeatedCalls()
    {
        var cities = RealCities;
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, HomeRegions.EligibleStartingCities(Region.Europe, cities)[0]);

        var first = StartingConditionsResolver.Resolve(seat, cities, RealFleet, RealGrowthSchedule);
        var second = StartingConditionsResolver.Resolve(seat, cities, RealFleet, RealGrowthSchedule);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Resolve_PicksTheMostProfitableAircraftAndDestinationCombination()
    {
        // Guaranteed-route profitability follow-up: two eligible
        // destinations - "near" is close and thin (its route can't fill a
        // plane); "far" is farther but has heavy demand. The
        // profitability-blind alphabetical rule would have picked "far"
        // anyway here (f < n), but with the demand flipped it would have
        // picked the loss-making one. The new rule always lands on the
        // profitable combination regardless of city id ordering.
        var home = new City("home", "HOME", Region.Europe, 0, 0, 40, false, null);
        var thin = new City("aaa", "AAA", Region.Europe, 0, 3, 3, false, null);   // alphabetically first, tiny demand
        var rich = new City("zzz", "ZZZ", Region.Europe, 0, 2, 400, false, null); // alphabetically last, heavy demand
        var cities = new[] { home, thin, rich };

        var small = new AircraftType("small", "Small", Seats: 40, RangeKm: 5000, ManufacturerRegion: Region.Europe, CostPerAvailableSeatKm: 0.1, CruiseSpeedKmh: 500);
        var large = new AircraftType("large", "Large", Seats: 200, RangeKm: 5000, ManufacturerRegion: Region.Europe, CostPerAvailableSeatKm: 0.1, CruiseSpeedKmh: 500);
        var fleet = new[] { small, large };

        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, home);
        var conditions = StartingConditionsResolver.Resolve(seat, cities, fleet, RealGrowthSchedule);

        Assert.Equal("zzz", conditions.GuaranteedRouteDestination.Id); // profitable, not alphabetically-first
    }

    [Fact]
    public void Resolve_SmallAircraftOutOfRange_FallsBackToLargerOne()
    {
        var a = new City("a", "A", Region.Europe, 0, 0, 60, false, null);
        var b = new City("b", "B", Region.Europe, 20, 20, 60, false, null); // far away
        var cities = new[] { a, b };

        var tooSmall = new AircraftType("small", "Small", Seats: 40, RangeKm: 500, ManufacturerRegion: Region.Europe);
        var bigEnough = new AircraftType("large", "Large", Seats: 200, RangeKm: 10_000, ManufacturerRegion: Region.Europe);
        var fleet = new[] { tooSmall, bigEnough };

        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, a);
        var conditions = StartingConditionsResolver.Resolve(seat, cities, fleet, RealGrowthSchedule);

        Assert.Equal("large", conditions.StartingAircraft.Id);
        Assert.Equal("b", conditions.GuaranteedRouteDestination.Id);
    }

    [Fact]
    public void Resolve_NoReachableNeighborForAnyAppropriateAircraft_Throws()
    {
        var a = new City("a", "A", Region.Europe, 0, 0, 60, false, null);
        var b = new City("b", "B", Region.Europe, 60, 60, 60, false, null); // unreachably far
        var cities = new[] { a, b };

        var tooSmall = new AircraftType("small", "Small", Seats: 40, RangeKm: 500, ManufacturerRegion: Region.Europe);
        var fleet = new[] { tooSmall };

        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, a);

        Assert.Throws<InvalidOperationException>(() => StartingConditionsResolver.Resolve(seat, cities, fleet, RealGrowthSchedule));
    }

    [Fact]
    public void MostProfitableRoute_NoReachableNeighborForAnyAppropriateAircraft_Throws()
    {
        var a = new City("a", "A", Region.Europe, 0, 0, 60, false, null);
        var b = new City("b", "B", Region.Europe, 60, 60, 60, false, null); // unreachably far
        var cities = new[] { a, b };

        var tooSmall = new AircraftType("small", "Small", Seats: 40, RangeKm: 500, ManufacturerRegion: Region.Europe);
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, a);

        Assert.Throws<InvalidOperationException>(
            () => StartingConditionsResolver.MostProfitableRoute(seat, cities, new[] { tooSmall }, RealGrowthSchedule));
    }

    [Fact]
    public void MostProfitableRoute_PicksACombinationSizedForTheRealSuppressedDemand_NotTheBareModelFigure()
    {
        // The root-cause fix (docs/OPEN_WORK.md): Shanghai's own 1955
        // "Maoist-era closed economy" entry (historical_events.json) must
        // suppress its demand here exactly the way it suppresses every
        // real per-quarter call site's own demand model - a bare
        // GravityDemandModel with no schedule/year can't see it at all.
        var cities = RealCities;
        var fleet = RealFleet;
        var fukuoka = cities.First(c => c.Id == "fuk");
        var shanghai = cities.First(c => c.Id == "pvg");
        var seat = new AirlineSeat("Test", IsHuman: true, Region.EastAsia, fukuoka);

        var bareModelDemand = new GravityDemandModel(enableHubDemandBonus: true).ForPair(fukuoka, shanghai).PassengersPerQuarter;
        var scheduleAwareDemand = RealQuarter1DemandModel.ForPair(fukuoka, shanghai).PassengersPerQuarter;
        Assert.True(scheduleAwareDemand < bareModelDemand,
            "Shanghai's 1955 suppression multiplier should meaningfully reduce its real quarter-1 demand versus an unscheduled model.");

        var (aircraft, destination) = StartingConditionsResolver.MostProfitableRoute(seat, cities, fleet, RealGrowthSchedule);

        // Whatever combination wins, its own aircraft must be sized so the
        // real (suppressed) demand for that specific pair actually fills a
        // real majority of its capacity - not oversized for a phantom
        // number the bare model would have reported.
        var demandForChosenPair = RealQuarter1DemandModel.ForPair(fukuoka, destination).PassengersPerQuarter;
        var route = new Route(fukuoka, destination, aircraft, AircraftCount: 1);
        Assert.True(demandForChosenPair >= route.SeatsPerQuarter / 2,
            $"chosen combination {aircraft.Id}->{destination.Id} is oversized for its own real demand ({demandForChosenPair} vs {route.SeatsPerQuarter} seats).");
    }

    // --- ResolveWithMinProfitFraction (docs/OPEN_WORK.md's filler-seat investigation) ---

    private static (City Home, City Thin, City Rich, AircraftType Aircraft) ThinVsRichFixture()
    {
        // "thin" is alphabetically first with positive (but small) demand,
        // so the heuristic always lands there; "rich" is the real best pick.
        var home = new City("home", "HOME", Region.Europe, 0, 0, 40, false, null);
        // Weight 172: both pairs sit inside the ground-competition distance
        // (GravityDemandModel.ShippedGroundCompetitionDistanceKm), so the
        // formula prices them at 800km, and Europe-to-Europe pairs are
        // halved (GravityDemandModel.ShippedIntraRegionMultipliers) - 172
        // keeps thin's demand where weight 15 put it at its real 333km.
        var thin = new City("aaa", "THIN", Region.Europe, 0, 3, 172, false, null); // alphabetically first; small but real positive profit
        var rich = new City("zzz", "RICH", Region.Europe, 0, 2, 400, false, null);
        var aircraft = new AircraftType("ac", "AC", Seats: 100, RangeKm: 5000, ManufacturerRegion: Region.Europe, CostPerAvailableSeatKm: 0.1, CruiseSpeedKmh: 500);
        return (home, thin, rich, aircraft);
    }

    private static long ProfitFor(City origin, City destination, AircraftType aircraft, GravityDemandModel demandModel)
    {
        var route = new Route(origin, destination, aircraft, AircraftCount: 1);
        var carried = Math.Min(demandModel.ForPair(origin, destination).PassengersPerQuarter, route.SeatsPerQuarter);
        return FareCalculator.EstimatedProfit(route, carried).Cents;
    }

    [Fact]
    public void ResolveWithMinProfitFraction_AtOne_AlwaysMatchesMostProfitableRoute()
    {
        var (home, thin, rich, aircraft) = ThinVsRichFixture();
        var cities = new[] { home, thin, rich };
        var fleet = new[] { aircraft };
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, home);

        var expected = StartingConditionsResolver.MostProfitableRoute(seat, cities, fleet, RealGrowthSchedule);
        var result = StartingConditionsResolver.ResolveWithMinProfitFraction(seat, cities, fleet, minProfitFraction: 1.0, RealGrowthSchedule);

        Assert.Equal(expected.Aircraft, result.StartingAircraft);
        Assert.Equal(expected.Destination, result.GuaranteedRouteDestination);
        Assert.Equal("zzz", result.GuaranteedRouteDestination.Id);
    }

    [Fact]
    public void ResolveWithMinProfitFraction_AtZero_KeepsAnyNonNegativeHeuristicPick()
    {
        var (home, thin, rich, aircraft) = ThinVsRichFixture();
        var cities = new[] { home, thin, rich };
        var fleet = new[] { aircraft };
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, home);

        var result = StartingConditionsResolver.ResolveWithMinProfitFraction(seat, cities, fleet, minProfitFraction: 0.0, RealGrowthSchedule);

        Assert.Equal("aaa", result.GuaranteedRouteDestination.Id); // the heuristic's own (alphabetically-first) pick, kept
    }

    [Fact]
    public void ResolveWithMinProfitFraction_SwitchesAtTheMeasuredRatio()
    {
        var (home, thin, rich, aircraft) = ThinVsRichFixture();
        var cities = new[] { home, thin, rich };
        var fleet = new[] { aircraft };
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, home);
        var demandModel = RealQuarter1DemandModel; // the exact model ResolveWithMinProfitFraction now builds internally

        var heuristicProfit = ProfitFor(home, thin, aircraft, demandModel);
        var bestProfit = ProfitFor(home, rich, aircraft, demandModel);
        var ratio = (double)heuristicProfit / bestProfit;
        Assert.InRange(ratio, 0.0, 0.99); // sanity: thin really is much weaker than rich

        var justBelow = StartingConditionsResolver.ResolveWithMinProfitFraction(seat, cities, fleet, ratio - 0.01, RealGrowthSchedule);
        var justAbove = StartingConditionsResolver.ResolveWithMinProfitFraction(seat, cities, fleet, ratio + 0.01, RealGrowthSchedule);

        Assert.Equal("aaa", justBelow.GuaranteedRouteDestination.Id);
        Assert.Equal("zzz", justAbove.GuaranteedRouteDestination.Id);
    }

    [Fact]
    public void ResolveWithMinProfitFraction_Cash_MatchesResolve()
    {
        var cities = RealCities;
        var seat = new AirlineSeat("Test", IsHuman: true, Region.EastAsia, HomeRegions.EligibleStartingCities(Region.EastAsia, cities)[0]);

        var resolved = StartingConditionsResolver.Resolve(seat, cities, RealFleet, RealGrowthSchedule);
        var tuned = StartingConditionsResolver.ResolveWithMinProfitFraction(seat, cities, RealFleet, 0.5, RealGrowthSchedule);

        Assert.Equal(resolved.StartingCash, tuned.StartingCash);
        Assert.Equal(resolved.AircraftQuantity, tuned.AircraftQuantity);
    }

    [Fact]
    public void ResolveWithMinProfitFraction_NoReachableNeighborForAnyAppropriateAircraft_Throws()
    {
        var a = new City("a", "A", Region.Europe, 0, 0, 60, false, null);
        var b = new City("b", "B", Region.Europe, 60, 60, 60, false, null); // unreachably far
        var cities = new[] { a, b };

        var tooSmall = new AircraftType("small", "Small", Seats: 40, RangeKm: 500, ManufacturerRegion: Region.Europe);
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, a);

        Assert.Throws<InvalidOperationException>(
            () => StartingConditionsResolver.ResolveWithMinProfitFraction(seat, cities, new[] { tooSmall }, 0.5, RealGrowthSchedule));
    }

    // --- ResolveWithWeightedRouteRandomness (docs/OPEN_WORK.md's difficulty-setting future work) ---

    [Fact]
    public void ResolveWithWeightedRouteRandomness_AtLargeExponent_AlwaysMatchesMostProfitableRoute_AcrossManySeeds()
    {
        var (home, thin, rich, aircraft) = ThinVsRichFixture();
        var cities = new[] { home, thin, rich };
        var fleet = new[] { aircraft };
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, home);
        var expected = StartingConditionsResolver.MostProfitableRoute(seat, cities, fleet, RealGrowthSchedule);

        for (var seed = 0; seed < 20; seed++)
        {
            var result = StartingConditionsResolver.ResolveWithWeightedRouteRandomness(seat, cities, fleet, weightExponent: 50.0, new SimRng(seed), RealGrowthSchedule);
            Assert.Equal(expected.Aircraft, result.StartingAircraft);
            Assert.Equal(expected.Destination, result.GuaranteedRouteDestination);
        }
    }

    [Fact]
    public void ResolveWithWeightedRouteRandomness_AtZero_EveryValidCombinationIsEquallyLikely_AcrossManySeeds()
    {
        var (home, thin, rich, aircraft) = ThinVsRichFixture();
        var cities = new[] { home, thin, rich };
        var fleet = new[] { aircraft };
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, home);

        var seenDestinations = new HashSet<string>();
        for (var seed = 0; seed < 50; seed++)
        {
            var result = StartingConditionsResolver.ResolveWithWeightedRouteRandomness(seat, cities, fleet, weightExponent: 0.0, new SimRng(seed), RealGrowthSchedule);
            seenDestinations.Add(result.GuaranteedRouteDestination.Id);
        }

        Assert.Equal(new HashSet<string> { "aaa", "zzz" }, seenDestinations); // both reachable combinations show up, not just the best
    }

    [Fact]
    public void ResolveWithWeightedRouteRandomness_AtModerateExponent_BiasesTowardTheBest_ButStillSometimesPicksOthers()
    {
        var (home, thin, rich, aircraft) = ThinVsRichFixture();
        var cities = new[] { home, thin, rich };
        var fleet = new[] { aircraft };
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, home);

        var pickCounts = new Dictionary<string, int> { ["aaa"] = 0, ["zzz"] = 0 };
        for (var seed = 0; seed < 200; seed++)
        {
            var result = StartingConditionsResolver.ResolveWithWeightedRouteRandomness(seat, cities, fleet, weightExponent: 1.0, new SimRng(seed), RealGrowthSchedule);
            pickCounts[result.GuaranteedRouteDestination.Id]++;
        }

        Assert.True(pickCounts["aaa"] > 0, "the weaker combination should still show up sometimes at a moderate exponent");
        Assert.True(pickCounts["zzz"] > pickCounts["aaa"], "the best combination should still be favored");
    }

    [Fact]
    public void ResolveWithWeightedRouteRandomness_IsDeterministic_ForTheSameSeed()
    {
        var (home, thin, rich, aircraft) = ThinVsRichFixture();
        var cities = new[] { home, thin, rich };
        var fleet = new[] { aircraft };
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, home);

        var first = StartingConditionsResolver.ResolveWithWeightedRouteRandomness(seat, cities, fleet, weightExponent: 0.7, new SimRng(99), RealGrowthSchedule);
        var second = StartingConditionsResolver.ResolveWithWeightedRouteRandomness(seat, cities, fleet, weightExponent: 0.7, new SimRng(99), RealGrowthSchedule);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ResolveWithWeightedRouteRandomness_Cash_MatchesResolve()
    {
        var cities = RealCities;
        var seat = new AirlineSeat("Test", IsHuman: true, Region.EastAsia, HomeRegions.EligibleStartingCities(Region.EastAsia, cities)[0]);

        var resolved = StartingConditionsResolver.Resolve(seat, cities, RealFleet, RealGrowthSchedule);
        var tuned = StartingConditionsResolver.ResolveWithWeightedRouteRandomness(seat, cities, RealFleet, 0.5, new SimRng(1), RealGrowthSchedule);

        Assert.Equal(resolved.StartingCash, tuned.StartingCash);
        Assert.Equal(resolved.AircraftQuantity, tuned.AircraftQuantity);
    }

    [Fact]
    public void ResolveWithWeightedRouteRandomness_EveryEligibleStartingCity_ProducesAcceptedQuarter1Route_AtZeroExponent()
    {
        // The real-data version of the M18 "Done when" check, but for the
        // riskiest setting (uniform-random among every valid combination)
        // - a random pick must still be a *valid* one (reachable, positive
        // demand), never a made-up route.
        var cities = RealCities;
        var fleet = RealFleet;
        var growth = RealGrowthSchedule;
        var rng = new SimRng(7);

        foreach (var region in HomeRegions.All)
        {
            foreach (var startingCity in HomeRegions.EligibleStartingCities(region, cities))
            {
                var seat = new AirlineSeat("Test Airline", IsHuman: true, region, startingCity);
                var conditions = StartingConditionsResolver.ResolveWithWeightedRouteRandomness(seat, cities, fleet, weightExponent: 0.0, rng, growth);

                Assert.NotEqual(startingCity.Id, conditions.GuaranteedRouteDestination.Id);
                Assert.Equal(region, conditions.GuaranteedRouteDestination.Region);
                Assert.True(Timeline.IsAvailable(conditions.StartingAircraft, Timeline.StartYear));

                var order = new RouteOrder(startingCity, conditions.GuaranteedRouteDestination, conditions.StartingAircraft);
                var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, cities, DemandModel);

                Assert.Empty(result.RejectedOrders);
            }
        }
    }

    [Fact]
    public void ResolveWithWeightedRouteRandomness_NoReachableNeighborForAnyAppropriateAircraft_Throws()
    {
        var a = new City("a", "A", Region.Europe, 0, 0, 60, false, null);
        var b = new City("b", "B", Region.Europe, 60, 60, 60, false, null); // unreachably far
        var cities = new[] { a, b };

        var tooSmall = new AircraftType("small", "Small", Seats: 40, RangeKm: 500, ManufacturerRegion: Region.Europe);
        var seat = new AirlineSeat("Test", IsHuman: true, Region.Europe, a);

        Assert.Throws<InvalidOperationException>(
            () => StartingConditionsResolver.ResolveWithWeightedRouteRandomness(seat, cities, new[] { tooSmall }, 0.5, new SimRng(1), RealGrowthSchedule));
    }
}
