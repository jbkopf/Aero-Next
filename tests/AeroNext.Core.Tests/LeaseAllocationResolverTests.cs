using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// Aircraft-acquisition-limits follow-up (docs/AIRCRAFT_ACQUISITION_ROADMAP.md).
/// LeaseCapForQuarter's own formula only produces clean integers when age
/// is a multiple of LeaseCapRampYears/gcd(Max-Min, RampYears) = 15/3 = 5
/// (given Min=2, Max=20, RampYears=15) - fixtures below pick AvailableFromYear/year
/// combinations landing on age 0, 5, 10, or 15 specifically so the
/// expected cap is verified by hand, not guessed.
/// </summary>
public class LeaseAllocationResolverTests
{
    private static Airline MakeAirline(string name) => new(name, Array.Empty<Route>());

    // --- LeaseCapForQuarter ---

    [Fact]
    public void LeaseCapForQuarter_AtLeaseEligibilityStart_IsTheFloor()
    {
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);
        var leaseEligibleYear = 1970 + Timeline.LeaseEligibilityDelayYears; // 1972, age 0

        Assert.Equal(LeaseAllocationResolver.MinLeaseCapPerQuarter, LeaseAllocationResolver.LeaseCapForQuarter(type, leaseEligibleYear));
    }

    [Fact]
    public void LeaseCapForQuarter_MidRamp_InterpolatesLinearly()
    {
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);
        // Age 5: 2 + 5*(20-2)/15 = 2 + 90/15 = 2 + 6 = 8.
        var year = 1970 + Timeline.LeaseEligibilityDelayYears + 5;

        Assert.Equal(8, LeaseAllocationResolver.LeaseCapForQuarter(type, year));
    }

    [Fact]
    public void LeaseCapForQuarter_AtRampEnd_IsTheCeiling()
    {
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);
        var year = 1970 + Timeline.LeaseEligibilityDelayYears + LeaseAllocationResolver.LeaseCapRampYears; // age 15

        Assert.Equal(LeaseAllocationResolver.MaxLeaseCapPerQuarter, LeaseAllocationResolver.LeaseCapForQuarter(type, year));
    }

    [Fact]
    public void LeaseCapForQuarter_WellPastRampEnd_StaysAtTheCeiling_NeverExceedsIt()
    {
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);
        var year = 1970 + Timeline.LeaseEligibilityDelayYears + 100; // age 100, far past the 15-year ramp

        Assert.Equal(LeaseAllocationResolver.MaxLeaseCapPerQuarter, LeaseAllocationResolver.LeaseCapForQuarter(type, year));
    }

    // --- Submit / ResolveQuarter ---

    [Fact]
    public void Submit_TwoRequestsFromTheSameAirlineForTheSameType_CombineIntoOneRequest()
    {
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);
        var a = MakeAirline("A");
        var board = LeaseAllocationResolver.Submit(LeaseRequestBoard.Empty, a, new LeaseAircraftOrder(type, Quantity: 1), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, a, new LeaseAircraftOrder(type, Quantity: 2), currentQuarter: 1);
        var year = 1970 + Timeline.LeaseEligibilityDelayYears + 100; // cap = 20, well above the combined 3 requested

        var (resolvedBoard, deliveries) = LeaseAllocationResolver.ResolveQuarter(board, currentQuarter: 2, year, new SimRng(1));

        var delivery = Assert.Single(deliveries);
        Assert.Equal(3, delivery.Quantity); // 1 + 2 combined, not two independent competitors
        Assert.Empty(resolvedBoard.Queue);
    }

    [Fact]
    public void ResolveQuarter_UnderCap_EveryAirlineGetsFullRequest()
    {
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var board = LeaseAllocationResolver.Submit(LeaseRequestBoard.Empty, a, new LeaseAircraftOrder(type, Quantity: 5), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, b, new LeaseAircraftOrder(type, Quantity: 3), currentQuarter: 1);
        var year = 1970 + Timeline.LeaseEligibilityDelayYears + 100; // cap = 20, well above the combined 8 requested

        var (_, deliveries) = LeaseAllocationResolver.ResolveQuarter(board, currentQuarter: 2, year, new SimRng(1));

        Assert.Equal(2, deliveries.Count);
        Assert.Equal(5, deliveries.Single(d => d.Airline.Name == "A").Quantity);
        Assert.Equal(3, deliveries.Single(d => d.Airline.Name == "B").Quantity);
    }

    [Fact]
    public void ResolveQuarter_ExactlyAtCap_EveryAirlineGetsFullRequest_NoRandomnessNeeded()
    {
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var board = LeaseAllocationResolver.Submit(LeaseRequestBoard.Empty, a, new LeaseAircraftOrder(type, Quantity: 1), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, b, new LeaseAircraftOrder(type, Quantity: 1), currentQuarter: 1);
        var year = 1970 + Timeline.LeaseEligibilityDelayYears; // cap = 2, exactly matching the combined 2 requested

        var (_, deliveries) = LeaseAllocationResolver.ResolveQuarter(board, currentQuarter: 2, year, new SimRng(1));

        Assert.Equal(1, deliveries.Single(d => d.Airline.Name == "A").Quantity);
        Assert.Equal(1, deliveries.Single(d => d.Airline.Name == "B").Quantity);
    }

    [Fact]
    public void ResolveQuarter_OverCap_TotalAllocatedNeverExceedsCap_AndNoAirlineExceedsItsOwnRequest()
    {
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var c = MakeAirline("C");
        var board = LeaseAllocationResolver.Submit(LeaseRequestBoard.Empty, a, new LeaseAircraftOrder(type, Quantity: 3), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, b, new LeaseAircraftOrder(type, Quantity: 3), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, c, new LeaseAircraftOrder(type, Quantity: 3), currentQuarter: 1);
        var year = 1970 + Timeline.LeaseEligibilityDelayYears; // cap = 2, combined demand 9

        var (resolvedBoard, deliveries) = LeaseAllocationResolver.ResolveQuarter(board, currentQuarter: 2, year, new SimRng(1));

        Assert.True(deliveries.Sum(d => d.Quantity) <= 2);
        Assert.All(deliveries, d => Assert.True(d.Quantity <= 3));
        Assert.Empty(resolvedBoard.Queue); // no carry-forward, unlike AircraftOrderBoard
    }

    [Fact]
    public void ResolveQuarter_OverCap_SameSeed_ReproducesTheSameAllocation()
    {
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var c = MakeAirline("C");
        var board = LeaseAllocationResolver.Submit(LeaseRequestBoard.Empty, a, new LeaseAircraftOrder(type, Quantity: 3), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, b, new LeaseAircraftOrder(type, Quantity: 3), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, c, new LeaseAircraftOrder(type, Quantity: 3), currentQuarter: 1);
        var year = 1970 + Timeline.LeaseEligibilityDelayYears;

        var (_, deliveriesFirst) = LeaseAllocationResolver.ResolveQuarter(board, currentQuarter: 2, year, new SimRng(42));
        var (_, deliveriesSecond) = LeaseAllocationResolver.ResolveQuarter(board, currentQuarter: 2, year, new SimRng(42));

        Assert.Equal(
            deliveriesFirst.OrderBy(d => d.Airline.Name).Select(d => (d.Airline.Name, d.Quantity)),
            deliveriesSecond.OrderBy(d => d.Airline.Name).Select(d => (d.Airline.Name, d.Quantity)));
    }

    [Fact]
    public void ResolveQuarter_OverCap_UnderDemandingAirlineKeepsItsFullRequest_NoWaterFilling()
    {
        // A asks for less than its own even share (baseShare = 8/3 = 2) -
        // it must keep its full ask of 1, never reduced to make room for
        // B/C, and never bumped up to 2 either. B and C, both over their
        // own share, split the remaining 7 units between them - which one
        // gets how many of those 7 is randomized, but the combined total
        // they receive (cap - A's own 1 = 7) is deterministic regardless.
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var c = MakeAirline("C");
        var board = LeaseAllocationResolver.Submit(LeaseRequestBoard.Empty, a, new LeaseAircraftOrder(type, Quantity: 1), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, b, new LeaseAircraftOrder(type, Quantity: 5), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, c, new LeaseAircraftOrder(type, Quantity: 5), currentQuarter: 1);
        var year = 1970 + Timeline.LeaseEligibilityDelayYears + 5; // cap = 8

        var (_, deliveries) = LeaseAllocationResolver.ResolveQuarter(board, currentQuarter: 2, year, new SimRng(7));

        Assert.Equal(1, deliveries.Single(d => d.Airline.Name == "A").Quantity);
        var bAndCTotal = deliveries.Where(d => d.Airline.Name is "B" or "C").Sum(d => d.Quantity);
        Assert.Equal(7, bAndCTotal);
        Assert.Equal(8, deliveries.Sum(d => d.Quantity)); // the full cap is used
    }

    [Fact]
    public void ResolveQuarter_UpfrontCost_ChargedOnlyAgainstDeliveredQuantity_NotRequested()
    {
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970, Cost: Money.FromDollars(10_000_000));
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var c = MakeAirline("C");
        var board = LeaseAllocationResolver.Submit(LeaseRequestBoard.Empty, a, new LeaseAircraftOrder(type, Quantity: 1), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, b, new LeaseAircraftOrder(type, Quantity: 5), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, c, new LeaseAircraftOrder(type, Quantity: 5), currentQuarter: 1);
        var year = 1970 + Timeline.LeaseEligibilityDelayYears + 5; // cap = 8, A gets exactly 1 (see the test above)

        var (_, deliveries) = LeaseAllocationResolver.ResolveQuarter(board, currentQuarter: 2, year, new SimRng(7));

        var aDelivery = deliveries.Single(d => d.Airline.Name == "A");
        // $10,000,000 * 0.015 * 1 delivered unit = $150,000 - never priced
        // against A's own request (which happened to equal 1 here anyway,
        // so this specifically checks the multiplier/rounding, not the
        // "not billed for a shortfall" property covered by the next test).
        Assert.Equal(Money.FromDollars(150_000), aDelivery.UpfrontCost);
    }

    [Fact]
    public void ResolveQuarter_UpfrontCost_ShortfallAirlineNeverBilledForUnitsItDidNotReceive()
    {
        var type = new AircraftType("t", "Test", Seats: 100, AvailableFromYear: 1970, Cost: Money.FromDollars(10_000_000));
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var c = MakeAirline("C");
        var board = LeaseAllocationResolver.Submit(LeaseRequestBoard.Empty, a, new LeaseAircraftOrder(type, Quantity: 1), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, b, new LeaseAircraftOrder(type, Quantity: 5), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, c, new LeaseAircraftOrder(type, Quantity: 5), currentQuarter: 1);
        var year = 1970 + Timeline.LeaseEligibilityDelayYears + 5; // cap = 8

        var (_, deliveries) = LeaseAllocationResolver.ResolveQuarter(board, currentQuarter: 2, year, new SimRng(7));

        // Whichever of B/C got fewer of the contested 7 units, its own
        // upfront cost must reflect exactly that smaller delivered
        // quantity, never its original request of 5.
        foreach (var delivery in deliveries.Where(d => d.Airline.Name is "B" or "C"))
        {
            var expected = new Money((long)Math.Round(type.Cost.Cents * LeaseAllocationResolver.LeaseUpfrontCostFraction * delivery.Quantity, MidpointRounding.AwayFromZero));
            Assert.Equal(expected, delivery.UpfrontCost);
        }
    }

    [Fact]
    public void ResolveQuarter_DifferentTypesHaveIndependentPools()
    {
        var typeA = new AircraftType("ta", "TypeA", Seats: 100, AvailableFromYear: 1970);
        var typeB = new AircraftType("tb", "TypeB", Seats: 100, AvailableFromYear: 1970);
        var alpha = MakeAirline("Alpha");
        var beta = MakeAirline("Beta");
        var board = LeaseAllocationResolver.Submit(LeaseRequestBoard.Empty, alpha, new LeaseAircraftOrder(typeA, Quantity: 2), currentQuarter: 1);
        board = LeaseAllocationResolver.Submit(board, beta, new LeaseAircraftOrder(typeB, Quantity: 2), currentQuarter: 1);
        var year = 1970 + Timeline.LeaseEligibilityDelayYears; // cap = 2 for each type independently

        var (_, deliveries) = LeaseAllocationResolver.ResolveQuarter(board, currentQuarter: 2, year, new SimRng(1));

        Assert.Equal(2, deliveries.Count);
        Assert.Equal(2, deliveries.Single(d => d.Type == typeA).Quantity);
        Assert.Equal(2, deliveries.Single(d => d.Type == typeB).Quantity);
    }
}
