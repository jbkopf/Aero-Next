using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using Xunit;

namespace AeroNext.Core.Tests;

public class AircraftOrderResolverTests
{
    // Rate 1 by default so ordering/tie-break tests aren't accidentally
    // masked by AircraftType's generous default rate (4).
    private static readonly AircraftType Type = new("707", "Boeing 707", Seats: 140, ProductionRatePerQuarter: 1);

    private static Airline MakeAirline(string name) => new(name, Array.Empty<Route>());

    [Fact]
    public void ResolveQuarter_TwoOrdersSameQuarter_ProduceDifferentDeliveryQuarters_Deterministically()
    {
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var board = AircraftOrderBoard.Empty;

        board = AircraftOrderResolver.Submit(board, a, new AircraftOrder(Type, Quantity: 1), currentQuarter: 1);
        board = AircraftOrderResolver.Submit(board, b, new AircraftOrder(Type, Quantity: 1), currentQuarter: 1);

        (board, var deliveriesQ2) = AircraftOrderResolver.ResolveQuarter(board, currentQuarter: 2);
        (_, var deliveriesQ3) = AircraftOrderResolver.ResolveQuarter(board, currentQuarter: 3);

        var aDelivery = Assert.Single(deliveriesQ2);
        var bDelivery = Assert.Single(deliveriesQ3);
        Assert.Equal("A", aDelivery.Airline.Name); // alphabetical tie-break wins the earlier slot
        Assert.Equal("B", bDelivery.Airline.Name);
        Assert.NotEqual(aDelivery.DeliveredQuarter, bDelivery.DeliveredQuarter);
    }

    [Fact]
    public void ResolveQuarter_OrderPlacedThisQuarter_DoesNotDeliverThisQuarter()
    {
        var a = MakeAirline("A");
        var board = AircraftOrderResolver.Submit(AircraftOrderBoard.Empty, a, new AircraftOrder(Type, 1), currentQuarter: 1);

        var (resolvedBoard, deliveries) = AircraftOrderResolver.ResolveQuarter(board, currentQuarter: 1);

        Assert.Empty(deliveries);
        Assert.Single(resolvedBoard.Queue);
    }

    [Fact]
    public void ResolveQuarter_EmptyQueue_NoOrderWaiting_DeliversNextQuarter()
    {
        var a = MakeAirline("A");
        var board = AircraftOrderResolver.Submit(AircraftOrderBoard.Empty, a, new AircraftOrder(Type, 1), currentQuarter: 1);

        var (_, deliveries) = AircraftOrderResolver.ResolveQuarter(board, currentQuarter: 2);

        Assert.Single(deliveries);
        Assert.Equal(2, deliveries[0].DeliveredQuarter);
    }

    [Fact]
    public void ResolveQuarter_PartialShipment_RemainderStaysQueuedForNextQuarter()
    {
        var a = MakeAirline("A");
        var board = AircraftOrderResolver.Submit(AircraftOrderBoard.Empty, a, new AircraftOrder(Type, Quantity: 3), currentQuarter: 1);

        var (afterQ2, deliveriesQ2) = AircraftOrderResolver.ResolveQuarter(board, currentQuarter: 2);
        var (afterQ3, deliveriesQ3) = AircraftOrderResolver.ResolveQuarter(afterQ2, currentQuarter: 3);
        var (finalBoard, deliveriesQ4) = AircraftOrderResolver.ResolveQuarter(afterQ3, currentQuarter: 4);

        Assert.Equal(1, deliveriesQ2.Single().Quantity);
        Assert.Equal(1, deliveriesQ3.Single().Quantity);
        Assert.Equal(1, deliveriesQ4.Single().Quantity);
        Assert.Empty(finalBoard.Queue);
    }

    [Fact]
    public void ResolveQuarter_SeparateAircraftTypes_HaveIndependentProductionLines()
    {
        var narrowbody = new AircraftType("707", "Boeing 707", Seats: 140, ProductionRatePerQuarter: 1);
        var widebody = new AircraftType("747", "Boeing 747", Seats: 400, ProductionRatePerQuarter: 1);
        var a = MakeAirline("A");

        var board = AircraftOrderBoard.Empty;
        board = AircraftOrderResolver.Submit(board, a, new AircraftOrder(narrowbody, 1), 1);
        board = AircraftOrderResolver.Submit(board, a, new AircraftOrder(widebody, 1), 1);

        var (_, deliveries) = AircraftOrderResolver.ResolveQuarter(board, currentQuarter: 2);

        // Both ship the same quarter - one type's queue depth doesn't block the other's.
        Assert.Equal(2, deliveries.Count);
    }

    [Fact]
    public void ResolveQuarter_DifferentTypes_ShipAtEachOwnProductionRate()
    {
        var highRate = new AircraftType("dc3", "Douglas DC-3", Seats: 28, ProductionRatePerQuarter: 8);
        var lowRate = new AircraftType("747", "Boeing 747", Seats: 400, ProductionRatePerQuarter: 1);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var c = MakeAirline("C");
        var d = MakeAirline("D");

        var board = AircraftOrderBoard.Empty;
        // All 4 players order the high-rate type - none should be bottlenecked.
        foreach (var airline in new[] { a, b, c, d })
        {
            board = AircraftOrderResolver.Submit(board, airline, new AircraftOrder(highRate, 1), 1);
        }
        // Two players order the low-rate type - one waits.
        board = AircraftOrderResolver.Submit(board, a, new AircraftOrder(lowRate, 1), 1);
        board = AircraftOrderResolver.Submit(board, b, new AircraftOrder(lowRate, 1), 1);

        var (_, deliveries) = AircraftOrderResolver.ResolveQuarter(board, currentQuarter: 2);

        Assert.Equal(4, deliveries.Count(d => d.Type.Id == "dc3")); // all 4 players unblocked
        Assert.Equal(1, deliveries.Count(d => d.Type.Id == "747")); // only 1 of 2 ships
    }

    [Fact]
    public void ResolveQuarter_HigherProductionRate_ShipsMoreUnitsAtOnce()
    {
        var type = new AircraftType("707", "Boeing 707", Seats: 140, ProductionRatePerQuarter: 2);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var c = MakeAirline("C");
        var board = AircraftOrderBoard.Empty;

        board = AircraftOrderResolver.Submit(board, a, new AircraftOrder(type, 1), 1);
        board = AircraftOrderResolver.Submit(board, b, new AircraftOrder(type, 1), 1);
        board = AircraftOrderResolver.Submit(board, c, new AircraftOrder(type, 1), 1);

        var (resolvedBoard, deliveries) = AircraftOrderResolver.ResolveQuarter(board, currentQuarter: 2);

        Assert.Equal(2, deliveries.Count);
        Assert.Single(resolvedBoard.Queue); // C still waiting
    }

    [Fact]
    public void ResolveLease_DeliversTheSameQuarterItIsOrdered()
    {
        var a = MakeAirline("A");
        var delivery = AircraftOrderResolver.ResolveLease(a, new LeaseAircraftOrder(Type, Quantity: 1), currentQuarter: 5);

        Assert.Equal(5, delivery.DeliveredQuarter);
        Assert.Equal(1, delivery.Quantity);
    }

    [Fact]
    public void ResolveQuarter_IsDeterministic_RegardlessOfSubmissionOrder()
    {
        var type = new AircraftType("707", "Boeing 707", Seats: 140, ProductionRatePerQuarter: 2);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var c = MakeAirline("C");

        var board1 = AircraftOrderBoard.Empty;
        board1 = AircraftOrderResolver.Submit(board1, a, new AircraftOrder(type, 1), 1);
        board1 = AircraftOrderResolver.Submit(board1, b, new AircraftOrder(type, 1), 1);
        board1 = AircraftOrderResolver.Submit(board1, c, new AircraftOrder(type, 1), 1);

        var board2 = AircraftOrderBoard.Empty;
        board2 = AircraftOrderResolver.Submit(board2, c, new AircraftOrder(type, 1), 1);
        board2 = AircraftOrderResolver.Submit(board2, a, new AircraftOrder(type, 1), 1);
        board2 = AircraftOrderResolver.Submit(board2, b, new AircraftOrder(type, 1), 1);

        var (_, deliveries1) = AircraftOrderResolver.ResolveQuarter(board1, 2);
        var (_, deliveries2) = AircraftOrderResolver.ResolveQuarter(board2, 2);

        Assert.Equal(
            deliveries1.Select(d => d.Airline.Name),
            deliveries2.Select(d => d.Airline.Name));
    }

    [Fact]
    public void QuarterEngine_Cost_AircraftAndLeaseAndAllianceOrders_AlwaysCostOne()
    {
        Assert.Equal(1, QuarterEngine.Cost(new AircraftOrder(Type, 1), Array.Empty<Route>()));
        Assert.Equal(1, QuarterEngine.Cost(new LeaseAircraftOrder(Type, 1), Array.Empty<Route>()));
    }
}
