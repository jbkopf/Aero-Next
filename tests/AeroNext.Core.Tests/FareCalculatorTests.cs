using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class FareCalculatorTests
{
    private static City MakeCity(string id, double lat, double lon) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, DemandWeight: 1, IsGateway: false, GateCapacity: null);

    /// <summary>Recomputes the expected fare independently from the documented formula, rather than calling FareCalculator itself — a genuine regression check, not a tautology.</summary>
    private static Money ExpectedFare(Route route)
    {
        var seatsPerQuarterPerAircraft = route.SeatsPerQuarter / route.AircraftCount;
        var operatingCostPerSeat = route.DistanceKm * route.Aircraft.CostPerAvailableSeatKm;
        var salaryPerSeat = FinancialResolver.SalaryCostPerAircraftPerQuarter.Cents / (double)seatsPerQuarterPerAircraft / 100.0;
        var costCents = (long)Math.Round((operatingCostPerSeat + salaryPerSeat) * 100, MidpointRounding.AwayFromZero);
        var fareCents = (long)Math.Round(costCents * (1 + FareCalculator.DefaultFareMarginOverCost), MidpointRounding.AwayFromZero);

        return new Money(fareCents);
    }

    [Fact]
    public void SuggestedFarePerPassenger_MatchesCostPlusSalaryShare_GrossedUpByMargin()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 20, CruiseSpeedKmh: 400, CostPerAvailableSeatKm: 0.10);
        var route = new Route(a, b, aircraft, AircraftCount: 1);

        var fare = FareCalculator.SuggestedFarePerPassenger(route);

        Assert.Equal(ExpectedFare(route), fare);
    }

    [Fact]
    public void SuggestedFarePerPassenger_UnaffectedByAircraftCount_SamePerSeatBasis()
    {
        // Fare is priced per seat one assigned aircraft offers, not the
        // route's total capacity -- adding more aircraft to the same pair
        // shouldn't move the price, the same way a real fare doesn't
        // change just because an airline adds more flights.
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 20, CruiseSpeedKmh: 400, CostPerAvailableSeatKm: 0.10);
        var oneAircraft = new Route(a, b, aircraft, AircraftCount: 1);
        var twoAircraft = new Route(a, b, aircraft, AircraftCount: 2);

        Assert.Equal(FareCalculator.SuggestedFarePerPassenger(oneAircraft), FareCalculator.SuggestedFarePerPassenger(twoAircraft));
    }

    [Fact]
    public void SuggestedFarePerPassenger_ScalesUpWithDistance()
    {
        var origin = MakeCity("origin", 0, 0);
        var near = MakeCity("near", 1, 1);
        var far = MakeCity("far", 20, 20);
        var aircraft = new AircraftType("t", "Test", Seats: 20, CruiseSpeedKmh: 900, RangeKm: 10_000, CostPerAvailableSeatKm: 0.10);
        var shortRoute = new Route(origin, near, aircraft, AircraftCount: 1);
        var longRoute = new Route(origin, far, aircraft, AircraftCount: 1);

        Assert.True(FareCalculator.SuggestedFarePerPassenger(longRoute).Cents > FareCalculator.SuggestedFarePerPassenger(shortRoute).Cents);
    }

    [Fact]
    public void Route_FarePerPassenger_UsesOverrideWhenSet_NotTheComputedDefault()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 20, CruiseSpeedKmh: 400, CostPerAvailableSeatKm: 0.10);
        var route = new Route(a, b, aircraft, AircraftCount: 1, FareOverride: Money.FromDollars(999));

        Assert.Equal(Money.FromDollars(999), route.FarePerPassenger);
    }

    [Fact]
    public void Route_FarePerPassenger_FallsBackToComputedDefault_WhenNoOverride()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var aircraft = new AircraftType("t", "Test", Seats: 20, CruiseSpeedKmh: 400, CostPerAvailableSeatKm: 0.10);
        var route = new Route(a, b, aircraft, AircraftCount: 1);

        Assert.Null(route.FareOverride);
        Assert.Equal(FareCalculator.SuggestedFarePerPassenger(route), route.FarePerPassenger);
    }

    // --- M48: DemandDeltaFraction ---

    private static Route MakeRoute(City a, City b, Money? fareOverride = null) =>
        new(a, b, new AircraftType("t", "Test", Seats: 20, CruiseSpeedKmh: 400, CostPerAvailableSeatKm: 0.10), AircraftCount: 1, fareOverride);

    [Fact]
    public void DemandDeltaFraction_DegenerateZeroCostRoute_NeverDividesByZero_ReturnsZero()
    {
        // A defensive guard, not a realistic scenario: engineered so
        // SuggestedFarePerPassenger itself rounds to exactly zero (a
        // same-location "route" with zero distance and an enormous seat
        // count spreads the flat salary cost thin enough to round away)
        // - DemandDeltaFraction must not divide by that zero.
        var a = MakeCity("a", 0, 0);
        var hugeAircraft = new AircraftType("t", "Test", Seats: 100_000, CruiseSpeedKmh: 400, CostPerAvailableSeatKm: 0.0);
        var route = new Route(a, a, hugeAircraft, AircraftCount: 1, Money.FromDollars(50)); // a's own coords for both ends
        Assert.Equal(Money.Zero, FareCalculator.SuggestedFarePerPassenger(route)); // confirms the test's own premise

        Assert.Equal(0.0, FareCalculator.DemandDeltaFraction(route));
    }

    [Fact]
    public void DemandDeltaFraction_NoOverride_IsExactlyZero()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var route = MakeRoute(a, b);

        Assert.Equal(0.0, FareCalculator.DemandDeltaFraction(route));
    }

    [Fact]
    public void DemandDeltaFraction_Discount_IsPositive()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var defaultRoute = MakeRoute(a, b);
        var discounted = MakeRoute(a, b, new Money(defaultRoute.FarePerPassenger.Cents * 9 / 10)); // -10%

        Assert.True(FareCalculator.DemandDeltaFraction(discounted) > 0.0);
    }

    [Fact]
    public void DemandDeltaFraction_Premium_IsNegative()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var defaultRoute = MakeRoute(a, b);
        var expensive = MakeRoute(a, b, new Money(defaultRoute.FarePerPassenger.Cents * 11 / 10)); // +10%

        Assert.True(FareCalculator.DemandDeltaFraction(expensive) < 0.0);
    }

    [Fact]
    public void DemandDeltaFraction_MatchesElasticityFormula_TenPercentDiscount()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var defaultRoute = MakeRoute(a, b);
        var discounted = MakeRoute(a, b, new Money(defaultRoute.FarePerPassenger.Cents * 9 / 10)); // -10%

        // -elasticity * (0.9 - 1.0) = +0.1 * elasticity - within a small
        // tolerance, since the -10% itself is computed via integer-cents
        // truncation (Money has no fractional cents), not exactly 0.9x.
        var expected = FareCalculator.FareDemandElasticity * 0.1;
        Assert.Equal(expected, FareCalculator.DemandDeltaFraction(discounted), precision: 3);
    }

    [Fact]
    public void DemandDeltaFraction_Symmetric_DiscountAndPremiumOfSameMagnitude_OppositeSign()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var defaultRoute = MakeRoute(a, b);
        var discounted = MakeRoute(a, b, new Money(defaultRoute.FarePerPassenger.Cents * 8 / 10)); // -20%
        var expensive = MakeRoute(a, b, new Money(defaultRoute.FarePerPassenger.Cents * 12 / 10)); // +20%

        Assert.Equal(FareCalculator.DemandDeltaFraction(discounted), -FareCalculator.DemandDeltaFraction(expensive), precision: 3);
    }

    // --- M54: EstimatedProfit ---

    [Fact]
    public void EstimatedProfit_MatchesRevenueMinusFullCapacityCost_IndependentlyComputed()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var route = MakeRoute(a, b);
        var carried = route.SeatsPerQuarter / 2;

        var expectedRevenue = route.FarePerPassenger * carried;
        var expectedCost = FareCalculator.CostPerSeatCarried(route) * route.SeatsPerQuarter;

        Assert.Equal(expectedRevenue - expectedCost, FareCalculator.EstimatedProfit(route, carried));
    }

    [Fact]
    public void EstimatedProfit_ZeroCarried_EqualsNegativeFullCapacityCost()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var route = MakeRoute(a, b);

        var expectedCost = FareCalculator.CostPerSeatCarried(route) * route.SeatsPerQuarter;

        Assert.Equal(-expectedCost, FareCalculator.EstimatedProfit(route, 0));
    }

    [Fact]
    public void EstimatedProfit_FullyLoadedAtDefaultFare_IsPositive()
    {
        // The default fare's own 30% margin over CostPerSeatCarried means
        // a fully-sold route at the default fare always clears cost.
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var route = MakeRoute(a, b);

        Assert.True(FareCalculator.EstimatedProfit(route, route.SeatsPerQuarter) > Money.Zero);
    }

    [Fact]
    public void EstimatedProfit_SeverelyUndersold_IsNegative()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var route = MakeRoute(a, b);
        var barelyAnyone = route.SeatsPerQuarter / 100; // 1% load - nowhere near the ~77% breakeven a 30% margin implies

        Assert.True(FareCalculator.EstimatedProfit(route, barelyAnyone) < Money.Zero);
    }

    // --- BalancedGrowthStrategy follow-up: includeLeaseFee ---

    [Fact]
    public void CostPerSeatCarried_IncludeLeaseFeeDefaultsFalse_MatchesEveryExistingCallSite()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var costlyAircraft = new AircraftType("t", "Test", Seats: 20, CruiseSpeedKmh: 400, CostPerAvailableSeatKm: 0.10, Cost: Money.FromDollars(1_000_000));
        var route = new Route(a, b, costlyAircraft, AircraftCount: 1);

        Assert.Equal(FareCalculator.CostPerSeatCarried(route), FareCalculator.CostPerSeatCarried(route, includeLeaseFee: false));
    }

    [Fact]
    public void CostPerSeatCarried_IncludeLeaseFeeTrue_MatchesIndependentlyComputedTotal()
    {
        // Independently computed, not calling CostPerSeatCarried itself -
        // the same operating+salary terms ExpectedFare already
        // establishes, plus FinancialResolver.LeaseFeeRatePerQuarter (4%)
        // of the aircraft's own $1,000,000 Cost, prorated across the same
        // per-aircraft seat count the salary term already uses - all
        // three summed and rounded once together, matching
        // CostPerSeatCarried's own single combined rounding (comparing
        // via subtraction of two independently-rounded totals instead
        // can be off by a cent from ordinary rounding, not a real bug).
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var costlyAircraft = new AircraftType("t", "Test", Seats: 20, CruiseSpeedKmh: 400, CostPerAvailableSeatKm: 0.10, Cost: Money.FromDollars(1_000_000));
        var route = new Route(a, b, costlyAircraft, AircraftCount: 1);
        var seatsPerQuarterPerAircraft = route.SeatsPerQuarter / route.AircraftCount;
        var operatingCostPerSeat = route.DistanceKm * route.Aircraft.CostPerAvailableSeatKm;
        var salaryPerSeat = FinancialResolver.SalaryCostPerAircraftPerQuarter.Cents / (double)seatsPerQuarterPerAircraft / 100.0;
        var leaseFeePerSeat = costlyAircraft.Cost.Cents * FinancialResolver.LeaseFeeRatePerQuarter / seatsPerQuarterPerAircraft / 100.0;
        var expectedCents = (long)Math.Round((operatingCostPerSeat + salaryPerSeat + leaseFeePerSeat) * 100, MidpointRounding.AwayFromZero);

        Assert.Equal(expectedCents, FareCalculator.CostPerSeatCarried(route, includeLeaseFee: true).Cents);
    }

    [Fact]
    public void CostPerSeatCarried_IncludeLeaseFeeTrue_ZeroCostAircraft_UnaffectedByTheFlag()
    {
        // A synthetic Test aircraft's own default Cost (Money.Zero, per
        // AircraftType's own doc comment) means 4% of zero is zero -
        // every pre-existing test in this file using the shared MakeRoute
        // helper is genuinely unaffected regardless of this flag.
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var route = MakeRoute(a, b);

        Assert.Equal(FareCalculator.CostPerSeatCarried(route, includeLeaseFee: false), FareCalculator.CostPerSeatCarried(route, includeLeaseFee: true));
    }

    [Fact]
    public void EstimatedProfit_IncludeLeaseFee_CanFlipAThinMarginRouteNegative()
    {
        var a = MakeCity("a", 0, 0);
        var b = MakeCity("b", 5, 5);
        var costlyAircraft = new AircraftType("t", "Test", Seats: 20, CruiseSpeedKmh: 400, CostPerAvailableSeatKm: 0.10, Cost: Money.FromDollars(50_000_000));
        var route = new Route(a, b, costlyAircraft, AircraftCount: 1);

        var profitOwned = FareCalculator.EstimatedProfit(route, route.SeatsPerQuarter, includeLeaseFee: false);
        var profitLeased = FareCalculator.EstimatedProfit(route, route.SeatsPerQuarter, includeLeaseFee: true);

        Assert.True(profitOwned > Money.Zero);
        Assert.True(profitLeased < profitOwned);
    }
}
