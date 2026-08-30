using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using Xunit;

namespace AeroNext.Core.Tests;

public class FleetLedgerTests
{
    private static readonly AircraftType Widebody = new("wb", "Widebody", Seats: 300);
    private static readonly AircraftType Narrowbody = new("nb", "Narrowbody", Seats: 150);
    private static readonly Airline SomeAirline = new("Atlas Air", Array.Empty<Route>());

    [Fact]
    public void ApplyDelivery_AddsCorrectQuantityOfIdleUnits_TaggedWithGivenOwnershipMode()
    {
        var delivery = new AircraftDelivery(SomeAirline, Widebody, Quantity: 3, DeliveredQuarter: 1);

        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, delivery, FleetOwnershipMode.Owned);

        Assert.Equal(3, fleet.Units.Count);
        Assert.All(fleet.Units, u =>
        {
            Assert.Equal(Widebody, u.Type);
            Assert.Equal(FleetOwnershipMode.Owned, u.Mode);
            Assert.Null(u.AssignedRouteKey);
        });
    }

    [Fact]
    public void ApplyDelivery_LeasedDelivery_TagsUnitsLeased()
    {
        var delivery = new AircraftDelivery(SomeAirline, Widebody, Quantity: 1, DeliveredQuarter: 1);

        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, delivery, FleetOwnershipMode.Leased);

        Assert.Equal(FleetOwnershipMode.Leased, fleet.Units.Single().Mode);
    }

    [Fact]
    public void ApplyDelivery_AssignsSequentialIds_AcrossMultipleDeliveries_NeverCollide()
    {
        var first = new AircraftDelivery(SomeAirline, Widebody, Quantity: 2, DeliveredQuarter: 1);
        var second = new AircraftDelivery(SomeAirline, Narrowbody, Quantity: 3, DeliveredQuarter: 5);

        var afterFirst = FleetLedger.ApplyDelivery(AirlineFleet.Empty, first, FleetOwnershipMode.Owned);
        var afterSecond = FleetLedger.ApplyDelivery(afterFirst, second, FleetOwnershipMode.Leased);

        var ids = afterSecond.Units.Select(u => u.Id).ToList();
        Assert.Equal(5, ids.Count);
        Assert.Equal(ids.Distinct().Count(), ids.Count); // every id unique
    }

    [Fact]
    public void Assign_IdleUnit_Succeeds_SetsAssignedRouteKey()
    {
        var delivery = new AircraftDelivery(SomeAirline, Widebody, Quantity: 1, DeliveredQuarter: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, delivery, FleetOwnershipMode.Owned);
        var unitId = fleet.Units.Single().Id;

        var updated = FleetLedger.Assign(fleet, unitId, "jfk-lhr");

        Assert.Equal("jfk-lhr", updated.Units.Single().AssignedRouteKey);
    }

    [Fact]
    public void Assign_AlreadyAssignedUnit_Throws()
    {
        var delivery = new AircraftDelivery(SomeAirline, Widebody, Quantity: 1, DeliveredQuarter: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, delivery, FleetOwnershipMode.Owned);
        var unitId = fleet.Units.Single().Id;
        var assigned = FleetLedger.Assign(fleet, unitId, "jfk-lhr");

        Assert.Throws<InvalidOperationException>(() => FleetLedger.Assign(assigned, unitId, "jfk-cdg"));
    }

    [Fact]
    public void Assign_UnknownUnitId_Throws()
    {
        Assert.Throws<ArgumentException>(() => FleetLedger.Assign(AirlineFleet.Empty, 999, "jfk-lhr"));
    }

    [Fact]
    public void Unassign_AssignedUnit_ClearsAssignedRouteKey_UnitBecomesIdleAgain()
    {
        var delivery = new AircraftDelivery(SomeAirline, Widebody, Quantity: 1, DeliveredQuarter: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, delivery, FleetOwnershipMode.Owned);
        var unitId = fleet.Units.Single().Id;
        var assigned = FleetLedger.Assign(fleet, unitId, "jfk-lhr");

        var unassigned = FleetLedger.Unassign(assigned, unitId);

        Assert.Null(unassigned.Units.Single().AssignedRouteKey);
        Assert.Single(unassigned.Idle);
    }

    [Fact]
    public void Unassign_UnknownUnitId_Throws()
    {
        Assert.Throws<ArgumentException>(() => FleetLedger.Unassign(AirlineFleet.Empty, 999));
    }

    [Fact]
    public void Retire_RemovesUnit_ReconciledCountDrops()
    {
        var delivery = new AircraftDelivery(SomeAirline, Widebody, Quantity: 2, DeliveredQuarter: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, delivery, FleetOwnershipMode.Owned);
        var toRetire = fleet.Units.First().Id;

        var afterRetirement = FleetLedger.Retire(fleet, toRetire);

        Assert.Single(afterRetirement.Units);
        Assert.DoesNotContain(afterRetirement.Units, u => u.Id == toRetire);
    }

    [Fact]
    public void Retire_UnknownUnitId_Throws()
    {
        Assert.Throws<ArgumentException>(() => FleetLedger.Retire(AirlineFleet.Empty, 999));
    }

    [Fact]
    public void AirlineFleet_Idle_ReturnsOnlyUnassignedUnits()
    {
        var delivery = new AircraftDelivery(SomeAirline, Widebody, Quantity: 2, DeliveredQuarter: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, delivery, FleetOwnershipMode.Owned);
        var assignedId = fleet.Units.First().Id;
        var withOneAssigned = FleetLedger.Assign(fleet, assignedId, "jfk-lhr");

        var idle = withOneAssigned.Idle.ToList();

        Assert.Single(idle);
        Assert.DoesNotContain(idle, u => u.Id == assignedId);
    }

    [Fact]
    public void Empty_HasNoUnits_NextUnitIdStartsAtOne()
    {
        Assert.Empty(AirlineFleet.Empty.Units);
        Assert.Equal(1, AirlineFleet.Empty.NextUnitId);
    }
}
