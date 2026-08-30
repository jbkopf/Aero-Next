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
}
