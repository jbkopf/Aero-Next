using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Negotiations;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class LandingRightsResolverTests
{
    private static City MakeCity(string id, int gateCapacity) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, 0, 0, 50, IsGateway: true, GateCapacity: gateCapacity);

    private static Airline MakeAirline(string name) => new(name, Array.Empty<Route>());

    [Fact]
    public void ResolveQuarter_ThreeSuitorsOneSlot_OneWinnerTwoImprovedLosers()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var c = MakeAirline("C");
        var rng = new SimRng(seed: 1);

        var board = LandingRightsBoard.Empty;
        board = LandingRightsResolver.Submit(board, a, new NegotiationOrder(city, Money.FromDollars(1_000_000)), currentQuarter: 1, rng);
        board = LandingRightsResolver.Submit(board, b, new NegotiationOrder(city, Money.FromDollars(900_000)), currentQuarter: 1, rng);
        board = LandingRightsResolver.Submit(board, c, new NegotiationOrder(city, Money.FromDollars(800_000)), currentQuarter: 1, rng);

        // Force all three to resolve on the same quarter regardless of the
        // randomly assigned delay, so this test is about resolution logic,
        // not timing.
        var forcedDueQuarter = board.Pending.Max(p => p.ResolvesQuarter);
        board = board with { Pending = board.Pending.Select(p => p with { ResolvesQuarter = forcedDueQuarter }).ToList() };

        var (_, outcomes) = LandingRightsResolver.ResolveQuarter(board, forcedDueQuarter);

        Assert.Equal(3, outcomes.Count);
        Assert.Single(outcomes, o => o.Won);
        Assert.Equal(2, outcomes.Count(o => !o.Won));
        Assert.All(outcomes.Where(o => !o.Won), o => Assert.True(o.StandingAfter > LandingRightsBoard.BaselineStanding));
        // Highest bidder (A) should win when standing is equal for all three.
        Assert.True(outcomes.Single(o => o.Won).Airline.Name == "A");
    }

    [Fact]
    public void Submit_AssignsDelayWithinTwoToFourQuartersInclusive()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var airline = MakeAirline("A");
        var rng = new SimRng(seed: 5);
        var board = LandingRightsBoard.Empty;

        for (var i = 0; i < 50; i++)
        {
            board = LandingRightsResolver.Submit(board, airline, new NegotiationOrder(city, Money.FromDollars(1_000)), currentQuarter: 1, rng);
        }

        Assert.All(board.Pending, p => Assert.InRange(p.ResolvesQuarter - p.SubmittedQuarter, 2, 4));
    }

    [Fact]
    public void Submit_SameSeed_ProducesSameDelaySequence()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var airline = MakeAirline("A");

        var boardA = LandingRightsBoard.Empty;
        var rngA = new SimRng(seed: 42);
        var boardB = LandingRightsBoard.Empty;
        var rngB = new SimRng(seed: 42);

        for (var i = 0; i < 10; i++)
        {
            boardA = LandingRightsResolver.Submit(boardA, airline, new NegotiationOrder(city, Money.FromDollars(1_000)), currentQuarter: 1, rngA);
            boardB = LandingRightsResolver.Submit(boardB, airline, new NegotiationOrder(city, Money.FromDollars(1_000)), currentQuarter: 1, rngB);
        }

        Assert.Equal(boardA.Pending.Select(p => p.ResolvesQuarter), boardB.Pending.Select(p => p.ResolvesQuarter));
    }

    [Fact]
    public void ResolveQuarter_LowerBidCanWin_IfStandingIsHigherEnough()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var richNewcomer = MakeAirline("Rich");
        var poorRegular = MakeAirline("Regular");

        // Regular has lost here before, boosting standing well above baseline.
        var standing = new Dictionary<(string, string), double>
        {
            [("Regular", "gate")] = 3.0,
        };
        var board = LandingRightsBoard.Empty with { Standing = standing };
        var rng = new SimRng(seed: 1);

        board = LandingRightsResolver.Submit(board, richNewcomer, new NegotiationOrder(city, Money.FromDollars(1_000_000)), currentQuarter: 1, rng);
        board = LandingRightsResolver.Submit(board, poorRegular, new NegotiationOrder(city, Money.FromDollars(400_000)), currentQuarter: 1, rng);

        var dueQuarter = board.Pending.Max(p => p.ResolvesQuarter);
        board = board with { Pending = board.Pending.Select(p => p with { ResolvesQuarter = dueQuarter }).ToList() };

        var (_, outcomes) = LandingRightsResolver.ResolveQuarter(board, dueQuarter);

        // Regular: 400,000 * 3.0 = 1,200,000 > Rich: 1,000,000 * 1.0
        Assert.True(outcomes.Single(o => o.Airline.Name == "Regular").Won);
        Assert.False(outcomes.Single(o => o.Airline.Name == "Rich").Won);
    }

    [Fact]
    public void ResolveQuarter_NotYetDueNegotiations_StayPending()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var airline = MakeAirline("A");
        var board = LandingRightsBoard.Empty with
        {
            Pending = new[] { new PendingNegotiation(airline, city, Money.FromDollars(1_000), SubmittedQuarter: 1, ResolvesQuarter: 5) },
        };

        var (resolvedBoard, outcomes) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 2);

        Assert.Empty(outcomes);
        Assert.Single(resolvedBoard.Pending);
    }

    [Fact]
    public void ResolveQuarter_MultipleSlots_LetsMultipleSuitorsWin()
    {
        var city = MakeCity("gate", gateCapacity: 2);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var c = MakeAirline("C");
        var rng = new SimRng(seed: 1);

        var board = LandingRightsBoard.Empty;
        board = LandingRightsResolver.Submit(board, a, new NegotiationOrder(city, Money.FromDollars(1_000_000)), 1, rng);
        board = LandingRightsResolver.Submit(board, b, new NegotiationOrder(city, Money.FromDollars(900_000)), 1, rng);
        board = LandingRightsResolver.Submit(board, c, new NegotiationOrder(city, Money.FromDollars(800_000)), 1, rng);

        var dueQuarter = board.Pending.Max(p => p.ResolvesQuarter);
        board = board with { Pending = board.Pending.Select(p => p with { ResolvesQuarter = dueQuarter }).ToList() };

        var (_, outcomes) = LandingRightsResolver.ResolveQuarter(board, dueQuarter);

        Assert.Equal(2, outcomes.Count(o => o.Won));
        Assert.Single(outcomes, o => !o.Won);
        Assert.False(outcomes.Single(o => o.Airline.Name == "C").Won); // lowest bid, loses the only contested slot
    }

    [Fact]
    public void ResolveQuarter_AlreadyGrantedSlots_CarryOverAcrossRounds()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var a = MakeAirline("A");
        var b = MakeAirline("B");

        // Round 1: A wins the only slot.
        var board = LandingRightsBoard.Empty with
        {
            Pending = new[] { new PendingNegotiation(a, city, Money.FromDollars(1_000), 1, 3) },
        };
        (board, _) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 3);
        Assert.Equal(1, board.SlotsGranted["gate"]);

        // Round 2: B bids alone on the same now-full gate. Nobody's left to
        // outbid, but there's no slot left either.
        board = board with
        {
            Pending = new[] { new PendingNegotiation(b, city, Money.FromDollars(10_000_000), 4, 6) },
        };
        (board, var outcomes) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 6);

        Assert.False(outcomes.Single().Won);
        Assert.Equal(0, board.SlotsRemaining(city));
    }

    [Fact]
    public void ResolveQuarter_IsDeterministic_RegardlessOfSubmissionOrder()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var c = MakeAirline("C");

        var pending = new[]
        {
            new PendingNegotiation(a, city, Money.FromDollars(1_000_000), 1, 3),
            new PendingNegotiation(b, city, Money.FromDollars(1_000_000), 1, 3), // tied bid with A
            new PendingNegotiation(c, city, Money.FromDollars(500_000), 1, 3),
        };

        var board1 = LandingRightsBoard.Empty with { Pending = pending };
        var board2 = LandingRightsBoard.Empty with { Pending = pending.Reverse().ToArray() };

        var (_, outcomes1) = LandingRightsResolver.ResolveQuarter(board1, 3);
        var (_, outcomes2) = LandingRightsResolver.ResolveQuarter(board2, 3);

        Assert.Equal(
            outcomes1.Select(o => (o.Airline.Name, o.Won)),
            outcomes2.Select(o => (o.Airline.Name, o.Won)));
    }

    [Fact]
    public void QuarterEngine_Cost_NegotiationOrder_AlwaysCostsOne()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var order = new NegotiationOrder(city, Money.FromDollars(1_000));

        Assert.Equal(1, QuarterEngine.Cost(order, Array.Empty<Route>()));
    }
}
