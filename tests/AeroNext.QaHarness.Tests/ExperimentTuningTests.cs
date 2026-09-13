using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Setup;
using AeroNext.Core.World;
using AeroNext.QaHarness;

namespace AeroNext.QaHarness.Tests;

public class ExperimentTuningTests
{
    private static string DataDir => Path.Combine(AppContext.BaseDirectory, "data");
    private static IReadOnlyList<City> RealCities => CityCatalog.LoadFile(Path.Combine(DataDir, "cities.json"));
    private static IReadOnlyList<AircraftType> RealFleet => AircraftCatalog.LoadFile(Path.Combine(DataDir, "aircraft.json"));

    private static readonly AircraftType AnyAircraft =
        new("x", "X", Seats: 40, RangeKm: 5000, CostPerAvailableSeatKm: 0.1, CruiseSpeedKmh: 500);

    private static City CityIn(Region region) =>
        new($"{region}1", region.ToString(), region, 0, 0, 40, false, null);

    private static AirlineStartingConditions Fake(long cashDollars) =>
        new(Money.FromDollars(cashDollars), AnyAircraft, 2, CityIn(Region.AustralAsia));

    private static AirlineSeat SeatIn(Region region) =>
        new(region.ToString(), IsHuman: true, region, CityIn(region));

    private static readonly IReadOnlyList<City> NoCities = Array.Empty<City>();
    private static readonly IReadOnlyList<AircraftType> NoFleet = Array.Empty<AircraftType>();

    // --- Baseline / no-op ---

    [Fact]
    public void Baseline_IsExactlyADefaultConstructedInstance()
    {
        Assert.Equal(new ExperimentTuning(), ExperimentTuning.Baseline);
        Assert.NotEqual(new ExperimentTuning { AustralAsiaCashFloorFactor = 1.0 }, ExperimentTuning.Baseline);
    }

    [Fact]
    public void With_ExpressionChangesOnlyTheNamedKnob()
    {
        var single = ExperimentTuning.Baseline with { AustralAsiaAircraft = AustralAsiaAircraftMode.MostProfitable };

        Assert.Equal(AustralAsiaAircraftMode.MostProfitable, single.AustralAsiaAircraft);
        Assert.Null(single.AustralAsiaCashFloorFactor);
        Assert.NotEqual(ExperimentTuning.Baseline, single);
    }

    [Fact]
    public void Baseline_ApplyTo_IsANoOp()
    {
        var conditions = Fake(15_100_000);
        Assert.Equal(conditions, ExperimentTuning.Baseline.ApplyTo(SeatIn(Region.AustralAsia), conditions, NoCities, NoFleet));
    }

    [Fact]
    public void AnyTuning_IsANoOpForNonAustralAsiaSeats()
    {
        var tuning = new ExperimentTuning
        {
            AustralAsiaCashFloorFactor = 2.0,
            AustralAsiaAircraft = AustralAsiaAircraftMode.MostProfitable,
        };
        var conditions = Fake(20_500_000);

        Assert.Equal(conditions, tuning.ApplyTo(SeatIn(Region.Europe), conditions, RealCities, RealFleet));
    }

    // --- Factor S: cash floor ---

    [Fact]
    public void CashFloor_RaisesAustralAsiaStartingCash_ToTheFloor()
    {
        var tuning = new ExperimentTuning { AustralAsiaCashFloorFactor = 1.0 };

        var result = tuning.ApplyTo(SeatIn(Region.AustralAsia), Fake(15_100_000), NoCities, NoFleet);

        Assert.Equal(Money.FromDollars(StartingConditionsResolver.BaseStartingCashDollars), result.StartingCash);
    }

    [Fact]
    public void CashFloor_BelowTheRealFactor_NeverLowersCash()
    {
        var tuning = new ExperimentTuning { AustralAsiaCashFloorFactor = 0.5 }; // $10M, below the ~$15.1M real figure

        var result = tuning.ApplyTo(SeatIn(Region.AustralAsia), Fake(15_100_000), NoCities, NoFleet);

        Assert.Equal(Money.FromDollars(15_100_000), result.StartingCash);
    }

    [Fact]
    public void CashFloor_LeavesAircraftAndDestinationUntouched()
    {
        var tuning = new ExperimentTuning { AustralAsiaCashFloorFactor = 1.117 };
        var conditions = Fake(15_100_000);

        var result = tuning.ApplyTo(SeatIn(Region.AustralAsia), conditions, NoCities, NoFleet);

        Assert.Equal(conditions.StartingAircraft, result.StartingAircraft);
        Assert.Equal(conditions.GuaranteedRouteDestination, result.GuaranteedRouteDestination);
        Assert.Equal(conditions.AircraftQuantity, result.AircraftQuantity);
    }

    // --- Factor A: aircraft mode ---

    [Fact]
    public void AircraftMode_Keep_LeavesTheResolverPickUntouched()
    {
        var cities = RealCities;
        var seat = new AirlineSeat("AA", IsHuman: true, Region.AustralAsia,
            HomeRegions.EligibleStartingCities(Region.AustralAsia, cities).First(c => c.Id == "sin"));
        var baseline = StartingConditionsResolver.Resolve(seat, cities, RealFleet);

        var result = new ExperimentTuning { AustralAsiaAircraft = AustralAsiaAircraftMode.Keep }
            .ApplyTo(seat, baseline, cities, RealFleet);

        Assert.Equal(baseline, result);
    }

    [Fact]
    public void AircraftMode_MostProfitable_ReplacesAircraftAndDestination_WithTheMostProfitableCombination()
    {
        var cities = RealCities;
        var fleet = RealFleet;
        var seat = new AirlineSeat("AA", IsHuman: true, Region.AustralAsia,
            HomeRegions.EligibleStartingCities(Region.AustralAsia, cities).First(c => c.Id == "sin"));
        var baseline = StartingConditionsResolver.Resolve(seat, cities, fleet);
        var expected = StartingConditionsResolver.MostProfitableRoute(seat, cities, fleet);

        var result = new ExperimentTuning { AustralAsiaAircraft = AustralAsiaAircraftMode.MostProfitable }
            .ApplyTo(seat, baseline, cities, fleet);

        Assert.Equal(expected.Aircraft, result.StartingAircraft);
        Assert.Equal(expected.Destination, result.GuaranteedRouteDestination);
        Assert.Equal(baseline.StartingCash, result.StartingCash); // A alone doesn't touch cash
        // Singapore's untuned start is a token Il-14; the profitable pick is a bigger aircraft.
        Assert.NotEqual(baseline.StartingAircraft.Id, result.StartingAircraft.Id);
    }

    [Fact]
    public void AircraftMode_MostProfitable_ComposesWithCashFloor()
    {
        var cities = RealCities;
        var fleet = RealFleet;
        var seat = new AirlineSeat("AA", IsHuman: true, Region.AustralAsia,
            HomeRegions.EligibleStartingCities(Region.AustralAsia, cities).First(c => c.Id == "cgk"));
        var baseline = StartingConditionsResolver.Resolve(seat, cities, fleet);
        var expected = StartingConditionsResolver.MostProfitableRoute(seat, cities, fleet);

        var result = new ExperimentTuning
        {
            AustralAsiaAircraft = AustralAsiaAircraftMode.MostProfitable,
            AustralAsiaCashFloorFactor = 1.0,
        }.ApplyTo(seat, baseline, cities, fleet);

        Assert.Equal(expected.Aircraft, result.StartingAircraft);
        Assert.Equal(expected.Destination, result.GuaranteedRouteDestination);
        Assert.Equal(Money.FromDollars(StartingConditionsResolver.BaseStartingCashDollars), result.StartingCash);
    }

    // --- SouthAmerica's own factor D: intra-region demand multiplier ---

    [Fact]
    public void IntraRegionDemandMultipliers_Baseline_IsExactlyTheShippedAustralAsiaMap()
    {
        Assert.Same(GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers, ExperimentTuning.Baseline.IntraRegionDemandMultipliers);
    }

    [Fact]
    public void IntraRegionDemandMultipliers_SouthAmericaFactor_MergesWithoutReplacingAustralAsia()
    {
        var map = new ExperimentTuning { SouthAmericaIntraRegionDemandMultiplier = 1.5 }.IntraRegionDemandMultipliers;

        Assert.Equal(1.5, map[Region.SouthAmerica]);
        Assert.Equal(GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers[Region.AustralAsia], map[Region.AustralAsia]);
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void IntraRegionDemandMultipliers_SouthAmericaFactorAtOne_IsANoOp()
    {
        var tuning = new ExperimentTuning { SouthAmericaIntraRegionDemandMultiplier = 1.0 };
        Assert.Same(GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers, tuning.IntraRegionDemandMultipliers);
    }

    // --- Global Q1 under-provisioning threshold (docs/OPEN_WORK.md) ---

    [Fact]
    public void GlobalMinProfitFraction_Null_IsANoOp_ForEveryRegion()
    {
        var cities = RealCities;
        var fleet = RealFleet;

        foreach (var region in HomeRegions.All)
        {
            var startingCity = HomeRegions.EligibleStartingCities(region, cities)[0];
            var seat = new AirlineSeat("T", IsHuman: true, region, startingCity);
            var baseline = StartingConditionsResolver.Resolve(seat, cities, fleet);

            Assert.Equal(baseline, ExperimentTuning.Baseline.ApplyTo(seat, baseline, cities, fleet));
        }
    }

    [Fact]
    public void GlobalMinProfitFraction_AppliesToNonAustralAsiaSeats_UnlikeEveryOtherKnob()
    {
        var cities = RealCities;
        var fleet = RealFleet;
        // Europe's own filler-default city (docs/OPEN_WORK.md): Paris's V2
        // pick (Athens) is a known, large under-provisioning gap.
        var seat = new AirlineSeat("T", IsHuman: true, Region.Europe,
            HomeRegions.EligibleStartingCities(Region.Europe, cities).First(c => c.Id == "cdg"));
        var baseline = StartingConditionsResolver.Resolve(seat, cities, fleet);
        var expected = StartingConditionsResolver.ResolveWithMinProfitFraction(seat, cities, fleet, 1.0);

        var result = new ExperimentTuning { GlobalMinProfitFraction = 1.0 }.ApplyTo(seat, baseline, cities, fleet);

        Assert.Equal(expected.StartingAircraft, result.StartingAircraft);
        Assert.Equal(expected.GuaranteedRouteDestination, result.GuaranteedRouteDestination);
        Assert.NotEqual(baseline.GuaranteedRouteDestination.Id, result.GuaranteedRouteDestination.Id);
    }

    [Fact]
    public void GlobalMinProfitFraction_ComposesWithAustralAsiaOnlyKnobs()
    {
        var cities = RealCities;
        var fleet = RealFleet;
        var seat = new AirlineSeat("AA", IsHuman: true, Region.AustralAsia,
            HomeRegions.EligibleStartingCities(Region.AustralAsia, cities).First(c => c.Id == "cgk"));
        var baseline = StartingConditionsResolver.Resolve(seat, cities, fleet);

        var tuning = new ExperimentTuning { GlobalMinProfitFraction = 1.0, AustralAsiaCashFloorFactor = 1.0 };
        var result = tuning.ApplyTo(seat, baseline, cities, fleet);

        // The global knob picks the route; the AustralAsia-only cash floor still applies on top.
        var expectedRoute = StartingConditionsResolver.ResolveWithMinProfitFraction(seat, cities, fleet, 1.0);
        Assert.Equal(expectedRoute.GuaranteedRouteDestination, result.GuaranteedRouteDestination);
        Assert.Equal(Money.FromDollars(StartingConditionsResolver.BaseStartingCashDollars), result.StartingCash);
    }

    // --- Global route-choice weighting exponent (docs/OPEN_WORK.md's difficulty-setting future work) ---

    [Fact]
    public void GlobalRouteWeightExponent_Null_NeverRequiresAnRng()
    {
        // Baseline never reads routeWeightRng at all - must not throw
        // even when none is supplied.
        var conditions = Fake(15_100_000);
        var result = ExperimentTuning.Baseline.ApplyTo(SeatIn(Region.Europe), conditions, RealCities, RealFleet, routeWeightRng: null);
        Assert.Equal(conditions, result);
    }

    [Fact]
    public void GlobalRouteWeightExponent_SetWithoutAnRng_Throws()
    {
        var tuning = new ExperimentTuning { GlobalRouteWeightExponent = 0.5 };
        Assert.Throws<ArgumentNullException>(() =>
            tuning.ApplyTo(SeatIn(Region.Europe), Fake(15_100_000), RealCities, RealFleet, routeWeightRng: null));
    }

    [Fact]
    public void GlobalRouteWeightExponent_AppliesToNonAustralAsiaSeats_UnlikeEveryOtherKnob()
    {
        var cities = RealCities;
        var fleet = RealFleet;
        var seat = new AirlineSeat("T", IsHuman: true, Region.Europe,
            HomeRegions.EligibleStartingCities(Region.Europe, cities).First(c => c.Id == "cdg"));
        var baseline = StartingConditionsResolver.Resolve(seat, cities, fleet);
        var expected = StartingConditionsResolver.ResolveWithWeightedRouteRandomness(seat, cities, fleet, 0.0, new SimRng(1));

        var result = new ExperimentTuning { GlobalRouteWeightExponent = 0.0 }.ApplyTo(seat, baseline, cities, fleet, new SimRng(1));

        Assert.Equal(expected.StartingAircraft, result.StartingAircraft);
        Assert.Equal(expected.GuaranteedRouteDestination, result.GuaranteedRouteDestination);
    }

    [Fact]
    public void GlobalRouteWeightExponent_ComposesWithAustralAsiaOnlyKnobs()
    {
        var cities = RealCities;
        var fleet = RealFleet;
        var seat = new AirlineSeat("AA", IsHuman: true, Region.AustralAsia,
            HomeRegions.EligibleStartingCities(Region.AustralAsia, cities).First(c => c.Id == "cgk"));
        var baseline = StartingConditionsResolver.Resolve(seat, cities, fleet);

        var tuning = new ExperimentTuning { GlobalRouteWeightExponent = 0.0, AustralAsiaCashFloorFactor = 1.0 };
        var result = tuning.ApplyTo(seat, baseline, cities, fleet, new SimRng(1));

        var expectedRoute = StartingConditionsResolver.ResolveWithWeightedRouteRandomness(seat, cities, fleet, 0.0, new SimRng(1));
        Assert.Equal(expectedRoute.GuaranteedRouteDestination, result.GuaranteedRouteDestination);
        Assert.Equal(Money.FromDollars(StartingConditionsResolver.BaseStartingCashDollars), result.StartingCash);
    }
}
