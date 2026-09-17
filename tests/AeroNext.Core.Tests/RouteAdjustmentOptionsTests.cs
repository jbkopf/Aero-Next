using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class RouteAdjustmentOptionsTests
{
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, false, null);

    private static readonly AircraftType CurrentType = new("cur", "Current", Seats: 100);
    private static readonly AircraftType InRangeIdle = new("ir", "InRangeIdle", Seats: 100, RangeKm: 20_000);
    private static readonly AircraftType OutOfRangeIdle = new("oor", "OutOfRangeIdle", Seats: 100, RangeKm: 100);

    private static readonly City A = MakeCity("a", 0, 0, 50);
    private static readonly City B = MakeCity("b", 50, 50, 50); // ~7,800km from A

    [Fact]
    public void EligibleTypesForSwap_ExcludesTheRoutesOwnCurrentType()
    {
        var route = new Route(A, B, CurrentType, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, CurrentType, quantity: 1, FleetOwnershipMode.Owned);

        var eligible = RouteAdjustmentOptions.EligibleTypesForSwap(route, fleet);

        Assert.DoesNotContain(CurrentType, eligible);
    }

    [Fact]
    public void EligibleTypesForSwap_ExcludesTypesWithNoIdleUnit()
    {
        var route = new Route(A, B, CurrentType, AircraftCount: 1);
        var withOne = FleetLedger.ApplyDelivery(AirlineFleet.Empty, InRangeIdle, quantity: 1, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.Assign(withOne, withOne.Units.Single().Id, "elsewhere"); // the only unit is assigned, not idle

        var eligible = RouteAdjustmentOptions.EligibleTypesForSwap(route, fleet);

        Assert.Empty(eligible);
    }

    [Fact]
    public void EligibleTypesForSwap_ExcludesOutOfRangeTypes()
    {
        var route = new Route(A, B, CurrentType, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, OutOfRangeIdle, quantity: 1, FleetOwnershipMode.Owned);

        var eligible = RouteAdjustmentOptions.EligibleTypesForSwap(route, fleet);

        Assert.Empty(eligible);
    }

    [Fact]
    public void EligibleTypesForSwap_IncludesAnIdleInRangeDifferentType()
    {
        var route = new Route(A, B, CurrentType, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, InRangeIdle, quantity: 1, FleetOwnershipMode.Owned);

        var eligible = RouteAdjustmentOptions.EligibleTypesForSwap(route, fleet);

        Assert.Equal(new[] { InRangeIdle }, eligible);
    }

    [Fact]
    public void EligibleTypesForSwap_MultipleIdleUnitsOfTheSameType_ListedOnceNotPerUnit()
    {
        var route = new Route(A, B, CurrentType, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, InRangeIdle, quantity: 3, FleetOwnershipMode.Owned);

        var eligible = RouteAdjustmentOptions.EligibleTypesForSwap(route, fleet);

        Assert.Single(eligible);
    }

    [Fact]
    public void EligibleTypesForSwap_OrderedByTypeId()
    {
        var route = new Route(A, B, CurrentType, AircraftCount: 1);
        var zType = new AircraftType("z", "Z", Seats: 100);
        var aType = new AircraftType("a2", "A2", Seats: 100);
        var withZ = FleetLedger.ApplyDelivery(AirlineFleet.Empty, zType, quantity: 1, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.ApplyDelivery(withZ, aType, quantity: 1, FleetOwnershipMode.Owned);

        var eligible = RouteAdjustmentOptions.EligibleTypesForSwap(route, fleet);

        Assert.Equal(new[] { aType, zType }, eligible);
    }

    [Fact]
    public void EligibleTypesForSwap_EmptyFleet_ReturnsEmpty()
    {
        var route = new Route(A, B, CurrentType, AircraftCount: 1);

        var eligible = RouteAdjustmentOptions.EligibleTypesForSwap(route, AirlineFleet.Empty);

        Assert.Empty(eligible);
    }
}
