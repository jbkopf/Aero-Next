using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using Xunit;

namespace AeroNext.Core.Tests;

public class IdleStreaksTests
{
    private static readonly AircraftType Aircraft = new("t", "Test", Seats: 100);

    [Fact]
    public void Advance_NewIdleUnit_StartsAtOne()
    {
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var streaks = IdleStreaks.Advance(IdleStreaks.Empty, fleet);

        Assert.Equal(1, streaks[1]);
    }

    [Fact]
    public void Advance_StillIdleUnit_Increments()
    {
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Leased);

        var streaks = IdleStreaks.Advance(IdleStreaks.Advance(IdleStreaks.Advance(IdleStreaks.Empty, fleet), fleet), fleet);

        Assert.Equal(3, streaks[1]);
    }

    [Fact]
    public void Advance_AssignedUnit_DropsOutAndRestartsWhenIdleAgain()
    {
        var idleFleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var assignedFleet = FleetLedger.Assign(idleFleet, 1, "a-b");

        var streaks = IdleStreaks.Advance(IdleStreaks.Advance(IdleStreaks.Empty, idleFleet), idleFleet);
        streaks = IdleStreaks.Advance(streaks, assignedFleet);
        Assert.False(streaks.ContainsKey(1));

        streaks = IdleStreaks.Advance(streaks, idleFleet);
        Assert.Equal(1, streaks[1]);
    }

    [Fact]
    public void Advance_RetiredUnit_DropsOut()
    {
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 2, FleetOwnershipMode.Owned);
        var streaks = IdleStreaks.Advance(IdleStreaks.Empty, fleet);

        streaks = IdleStreaks.Advance(streaks, FleetLedger.Retire(fleet, 1));

        Assert.Equal(new[] { 2 }, streaks.Keys);
        Assert.Equal(2, streaks[2]);
    }
}
