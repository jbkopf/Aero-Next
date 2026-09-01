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

    // --- M33: per-airline slot holdings ---

    [Fact]
    public void ResolveQuarter_Win_PopulatesSlotsHeld_ForTheWinningAirlineSpecifically()
    {
        var city = MakeCity("gate", gateCapacity: 2);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var board = LandingRightsBoard.Empty with
        {
            Pending = new[] { new PendingNegotiation(a, city, Money.FromDollars(1_000_000), 1, 3) },
        };

        (board, var outcomes) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 3);

        Assert.True(outcomes.Single().Won);
        Assert.Equal(1, board.SlotsHeldBy(a, city));
        Assert.Equal(0, board.SlotsHeldBy(b, city)); // never bid, never holds anything
    }

    [Fact]
    public void ResolveQuarter_Loss_DoesNotAddToSlotsHeld()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var winner = MakeAirline("Winner");
        var loser = MakeAirline("Loser");
        var board = LandingRightsBoard.Empty with
        {
            Pending = new[]
            {
                new PendingNegotiation(winner, city, Money.FromDollars(1_000_000), 1, 3),
                new PendingNegotiation(loser, city, Money.FromDollars(500_000), 1, 3),
            },
        };

        (board, _) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 3);

        Assert.Equal(1, board.SlotsHeldBy(winner, city));
        Assert.Equal(0, board.SlotsHeldBy(loser, city));
    }

    [Fact]
    public void ResolveQuarter_TwoSeparateWins_SameAirlineSameCity_AccumulatesSlotsHeld()
    {
        var city = MakeCity("gate", gateCapacity: 2);
        var a = MakeAirline("A");

        var board = LandingRightsBoard.Empty with
        {
            Pending = new[] { new PendingNegotiation(a, city, Money.FromDollars(1_000_000), 1, 3) },
        };
        (board, _) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 3);
        Assert.Equal(1, board.SlotsHeldBy(a, city));

        board = board with
        {
            Pending = new[] { new PendingNegotiation(a, city, Money.FromDollars(1_000_000), 4, 6) },
        };
        (board, _) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 6);

        Assert.Equal(2, board.SlotsHeldBy(a, city)); // same airline, second win at the same city adds up
    }

    [Fact]
    public void AvailableSlotsByCity_HeldMinusInUse_ReturnsRemainingBalance()
    {
        var gate = MakeCity("gate", gateCapacity: 5);
        var elsewhere = new City("else", "Else", Region.EastAsia, 10, 10, 50, false, null);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var airline = MakeAirline("A");
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 3 },
        };
        var routes = new[] { new Route(gate, elsewhere, aircraft, AircraftCount: 2) }; // 2 of the 3 held slots in use

        var available = board.AvailableSlotsByCity(airline, routes);

        Assert.Equal(1, available["gate"]);
    }

    [Fact]
    public void AvailableSlotsByCity_CountsRouteAsInUse_WhenGatewayIsTheDestination_NotJustOrigin()
    {
        var gate = MakeCity("gate", gateCapacity: 5);
        var elsewhere = new City("else", "Else", Region.EastAsia, 10, 10, 50, false, null);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var airline = MakeAirline("A");
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 3 },
        };
        var routes = new[] { new Route(elsewhere, gate, aircraft, AircraftCount: 2) }; // gate is the destination here

        var available = board.AvailableSlotsByCity(airline, routes);

        Assert.Equal(1, available["gate"]);
    }

    [Fact]
    public void AvailableSlotsByCity_NoRoutesTouchingCity_ReturnsFullHeldAmount()
    {
        var gate = MakeCity("gate", gateCapacity: 5);
        var airline = MakeAirline("A");
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 4 },
        };

        var available = board.AvailableSlotsByCity(airline, Array.Empty<Route>());

        Assert.Equal(4, available["gate"]);
    }

    [Fact]
    public void AvailableSlotsByCity_InUseExceedsHeld_FloorsAtZero_NeverNegative()
    {
        // Shouldn't happen in practice (QuarterEngine won't let AircraftCount
        // outrun held slots going forward), but must never go negative if it did.
        var gate = MakeCity("gate", gateCapacity: 5);
        var elsewhere = new City("else", "Else", Region.EastAsia, 10, 10, 50, false, null);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var airline = MakeAirline("A");
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 1 },
        };
        var routes = new[] { new Route(gate, elsewhere, aircraft, AircraftCount: 5) };

        var available = board.AvailableSlotsByCity(airline, routes);

        Assert.Equal(0, available["gate"]);
    }

    [Fact]
    public void AvailableSlotsByCity_OnlyIncludesCitiesThisAirlineHolds_NotOtherAirlines()
    {
        var gate = MakeCity("gate", gateCapacity: 5);
        var a = MakeAirline("A");
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("B", "gate")] = 3 }, // B holds it, not A
        };

        var available = board.AvailableSlotsByCity(a, Array.Empty<Route>());

        Assert.Empty(available);
    }

    // --- M28: relinquishing a held slot ---

    [Fact]
    public void Relinquish_ReducesSlotsHeldAndSlotsGranted_ByOne()
    {
        var city = MakeCity("gate", gateCapacity: 5);
        var a = MakeAirline("A");
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 2 },
            SlotsGranted = new Dictionary<string, int> { ["gate"] = 3 }, // A holds 2 of the 3 granted city-wide
        };

        var result = LandingRightsResolver.Relinquish(board, a, city);

        Assert.Equal(1, result.SlotsHeldBy(a, city));
        Assert.Equal(2, result.SlotsGranted["gate"]);
    }

    [Fact]
    public void Relinquish_FreedSlot_IsImmediatelyVisibleToSlotsRemaining()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var a = MakeAirline("A");
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 1 },
            SlotsGranted = new Dictionary<string, int> { ["gate"] = 1 },
        };
        Assert.Equal(0, board.SlotsRemaining(city)); // fully occupied before relinquishing

        var result = LandingRightsResolver.Relinquish(board, a, city);

        Assert.Equal(1, result.SlotsRemaining(city)); // contestable again
    }

    [Fact]
    public void Relinquish_AirlineHoldsZero_IsASafeNoOp_NotAThrowOrNegative()
    {
        var city = MakeCity("gate", gateCapacity: 5);
        var a = MakeAirline("A");
        var board = LandingRightsBoard.Empty;

        var result = LandingRightsResolver.Relinquish(board, a, city);

        Assert.Equal(0, result.SlotsHeldBy(a, city));
        Assert.Same(board, result); // genuinely the same board, not just zero
    }

    [Fact]
    public void Relinquish_OnlyAffectsTheRelinquishingAirline_NotOthersHoldingTheSameCity()
    {
        var city = MakeCity("gate", gateCapacity: 5);
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 1, [("B", "gate")] = 2 },
            SlotsGranted = new Dictionary<string, int> { ["gate"] = 3 },
        };

        var result = LandingRightsResolver.Relinquish(board, a, city);

        Assert.Equal(0, result.SlotsHeldBy(a, city));
        Assert.Equal(2, result.SlotsHeldBy(b, city)); // untouched
    }

    // --- M45: idle-slot tracking ---

    private static IReadOnlyDictionary<string, IReadOnlyList<Route>> RoutesFor(string airlineName, params Route[] routes) =>
        new Dictionary<string, IReadOnlyList<Route>> { [airlineName] = routes };

    [Fact]
    public void AdvanceIdleTracking_HeldSlotFullyUnused_IncrementsStreak()
    {
        var city = MakeCity("gate", gateCapacity: 5);
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 1 },
        };

        var advanced = board.AdvanceIdleTracking(RoutesFor("A"));

        Assert.Equal(1, advanced.IdleStreak[("A", "gate")]);
    }

    [Fact]
    public void AdvanceIdleTracking_RepeatedIdleQuarters_AccumulatesStreak()
    {
        var city = MakeCity("gate", gateCapacity: 5);
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 1 },
        };

        for (var i = 0; i < 3; i++)
        {
            board = board.AdvanceIdleTracking(RoutesFor("A"));
        }

        Assert.Equal(3, board.IdleStreak[("A", "gate")]);
    }

    [Fact]
    public void AdvanceIdleTracking_SlotFullyUsed_NeverAccumulatesStreak()
    {
        var gate = MakeCity("gate", gateCapacity: 5);
        var elsewhere = new City("else", "Else", Region.EastAsia, 10, 10, 50, false, null);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 1 },
        };
        var routes = RoutesFor("A", new Route(gate, elsewhere, aircraft, AircraftCount: 1));

        var advanced = board.AdvanceIdleTracking(routes);

        Assert.False(advanced.IdleStreak.ContainsKey(("A", "gate")));
    }

    [Fact]
    public void AdvanceIdleTracking_UsageResumes_ResetsStreakToZero()
    {
        var gate = MakeCity("gate", gateCapacity: 5);
        var elsewhere = new City("else", "Else", Region.EastAsia, 10, 10, 50, false, null);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 1 },
        };

        board = board.AdvanceIdleTracking(RoutesFor("A")); // idle
        board = board.AdvanceIdleTracking(RoutesFor("A")); // still idle
        Assert.Equal(2, board.IdleStreak[("A", "gate")]);

        var routes = RoutesFor("A", new Route(gate, elsewhere, aircraft, AircraftCount: 1)); // now flying it
        board = board.AdvanceIdleTracking(routes);

        Assert.False(board.IdleStreak.ContainsKey(("A", "gate")));
    }

    [Fact]
    public void IdleExpiredHolders_BelowThreshold_ReturnsEmpty()
    {
        var city = MakeCity("gate", gateCapacity: 5);
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 1 },
            IdleStreak = new Dictionary<(string, string), int> { [("A", "gate")] = LandingRightsBoard.IdleExpiryThreshold - 1 },
        };

        Assert.Empty(board.IdleExpiredHolders(city, RoutesFor("A")));
    }

    [Fact]
    public void IdleExpiredHolders_AtThreshold_ReturnsHolderOncePerUnusedSlot()
    {
        var city = MakeCity("gate", gateCapacity: 5);
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 2 },
            IdleStreak = new Dictionary<(string, string), int> { [("A", "gate")] = LandingRightsBoard.IdleExpiryThreshold },
        };

        var holders = board.IdleExpiredHolders(city, RoutesFor("A"));

        Assert.Equal(new[] { "A", "A" }, holders);
    }

    [Fact]
    public void IdleExpiredHolders_HeldSlotAtADifferentCity_NeverIncluded()
    {
        // Board holds idle-expired inventory at "other", not "gate" - the
        // CityId filter must actually exclude it, not just pass through
        // because IdleStreak alone crossed the threshold.
        var gate = MakeCity("gate", gateCapacity: 5);
        var other = MakeCity("other", gateCapacity: 5);
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "other")] = 1 },
            IdleStreak = new Dictionary<(string, string), int> { [("A", "other")] = LandingRightsBoard.IdleExpiryThreshold },
        };

        Assert.Empty(board.IdleExpiredHolders(gate, RoutesFor("A")));
        Assert.Equal(new[] { "A" }, board.IdleExpiredHolders(other, RoutesFor("A")));
    }

    [Fact]
    public void IdleExpiredHolders_DeterministicOrder_SortedByHolderName()
    {
        var city = MakeCity("gate", gateCapacity: 5);
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("Zulu", "gate")] = 1, [("Alpha", "gate")] = 1 },
            IdleStreak = new Dictionary<(string, string), int>
            {
                [("Zulu", "gate")] = LandingRightsBoard.IdleExpiryThreshold,
                [("Alpha", "gate")] = LandingRightsBoard.IdleExpiryThreshold,
            },
        };
        var routes = new Dictionary<string, IReadOnlyList<Route>> { ["Zulu"] = Array.Empty<Route>(), ["Alpha"] = Array.Empty<Route>() };

        Assert.Equal(new[] { "Alpha", "Zulu" }, board.IdleExpiredHolders(city, routes));
    }

    [Fact]
    public void IdleContestableSlotsByCity_CountsOnlyGateways_WithIdleExpiredInventory()
    {
        var gateway = MakeCity("gate", gateCapacity: 5);
        var nonGateway = new City("plain", "Plain", Region.EastAsia, 0, 0, 50, false, null, AutoCapacity: 3);
        var cities = new[] { gateway, nonGateway };
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("A", "gate")] = 1 },
            IdleStreak = new Dictionary<(string, string), int> { [("A", "gate")] = LandingRightsBoard.IdleExpiryThreshold },
        };

        var result = board.IdleContestableSlotsByCity(cities, RoutesFor("A"));

        Assert.Equal(1, result["gate"]);
        Assert.False(result.ContainsKey("plain"));
    }

    [Fact]
    public void IdleContestableSlotsByCity_NoIdleExpiredInventoryAnywhere_ReturnsEmpty()
    {
        var gateway = MakeCity("gate", gateCapacity: 5);
        var board = LandingRightsBoard.Empty;

        var result = board.IdleContestableSlotsByCity(new[] { gateway }, RoutesFor("A"));

        Assert.Empty(result);
    }

    // --- M45: idle-expired slots become contestable via the normal auction ---

    [Fact]
    public void ResolveQuarter_NoRoutesByAirlinePassed_NeverConsidersIdleExpiredInventory()
    {
        var city = MakeCity("gate", gateCapacity: 1); // fully granted, no fresh slot left
        var challenger = MakeAirline("Challenger");
        var board = LandingRightsBoard.Empty with
        {
            SlotsGranted = new Dictionary<string, int> { ["gate"] = 1 },
            SlotsHeld = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = 1 },
            IdleStreak = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = LandingRightsBoard.IdleExpiryThreshold },
            Pending = new[] { new PendingNegotiation(challenger, city, Money.FromDollars(1_000_000), 1, 3) },
        };

        var (resolvedBoard, outcomes) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 3);

        Assert.False(outcomes.Single().Won);
        Assert.Equal(1, resolvedBoard.SlotsHeldBy(new Airline("Incumbent", Array.Empty<Route>()), city));
    }

    [Fact]
    public void ResolveQuarter_IdleExpiredSlot_ChallengerWinsATransfer_NotAFreshGrant()
    {
        var city = MakeCity("gate", gateCapacity: 1); // fully granted already
        var incumbent = MakeAirline("Incumbent");
        var challenger = MakeAirline("Challenger");
        var board = LandingRightsBoard.Empty with
        {
            SlotsGranted = new Dictionary<string, int> { ["gate"] = 1 },
            SlotsHeld = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = 1 },
            IdleStreak = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = LandingRightsBoard.IdleExpiryThreshold },
            Pending = new[] { new PendingNegotiation(challenger, city, Money.FromDollars(1_000_000), 1, 3) },
        };
        var routesByAirline = RoutesFor("Incumbent"); // Incumbent's real routes: none touch "gate" - fully idle

        var (resolvedBoard, outcomes) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 3, routesByAirline);

        Assert.True(outcomes.Single().Won);
        Assert.Equal(1, resolvedBoard.SlotsHeldBy(challenger, city));
        Assert.Equal(0, resolvedBoard.SlotsHeldBy(incumbent, city));
        Assert.Equal(1, resolvedBoard.SlotsGranted["gate"]); // ownership moved, city-wide total unchanged
    }

    [Fact]
    public void ResolveQuarter_IdleExpiredSlot_IncumbentNeverAutoEnteredAsABidder()
    {
        // No PendingNegotiation from Incumbent at all - a passive owner
        // never competes for their own slot, per the design.
        var city = MakeCity("gate", gateCapacity: 1);
        var board = LandingRightsBoard.Empty with
        {
            SlotsGranted = new Dictionary<string, int> { ["gate"] = 1 },
            SlotsHeld = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = 1 },
            IdleStreak = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = LandingRightsBoard.IdleExpiryThreshold },
            Pending = Array.Empty<PendingNegotiation>(),
        };

        var (resolvedBoard, outcomes) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 3, RoutesFor("Incumbent"));

        Assert.Empty(outcomes);
        Assert.Equal(1, resolvedBoard.SlotsHeldBy(new Airline("Incumbent", Array.Empty<Route>()), city));
    }

    [Fact]
    public void ResolveQuarter_IdleExpiredSlot_NoBidsThisQuarter_OwnerKeepsUsingItUninterrupted()
    {
        // Idle-expired but simply nobody bid - the owner's holding is
        // completely untouched (they can keep flying it).
        var city = MakeCity("gate", gateCapacity: 1);
        var board = LandingRightsBoard.Empty with
        {
            SlotsGranted = new Dictionary<string, int> { ["gate"] = 1 },
            SlotsHeld = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = 1 },
            IdleStreak = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = LandingRightsBoard.IdleExpiryThreshold },
        };

        var (resolvedBoard, _) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 99, RoutesFor("Incumbent"));

        Assert.Equal(1, resolvedBoard.SlotsHeldBy(new Airline("Incumbent", Array.Empty<Route>()), city));
    }

    [Fact]
    public void ResolveQuarter_IncumbentBidsOnOwnIdleExpiredSlot_NetsToNoChange()
    {
        // The incumbent actively defends by submitting their own
        // NegotiationOrder. If the auction draws their own idle-expired
        // unit to satisfy their own winning bid, that's a self-transfer -
        // must net to zero change, not a bug.
        var city = MakeCity("gate", gateCapacity: 1);
        var incumbent = MakeAirline("Incumbent");
        var board = LandingRightsBoard.Empty with
        {
            SlotsGranted = new Dictionary<string, int> { ["gate"] = 1 },
            SlotsHeld = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = 1 },
            IdleStreak = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = LandingRightsBoard.IdleExpiryThreshold },
            Pending = new[] { new PendingNegotiation(incumbent, city, Money.FromDollars(1_000_000), 1, 3) },
        };

        var (resolvedBoard, outcomes) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 3, RoutesFor("Incumbent"));

        Assert.True(outcomes.Single().Won);
        Assert.Equal(1, resolvedBoard.SlotsHeldBy(incumbent, city));
        Assert.Equal(1, resolvedBoard.SlotsGranted["gate"]);
    }

    [Fact]
    public void ResolveQuarter_MoreChallengersThanIdleExpiredInventory_ExcessStillLose()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var winner = MakeAirline("Winner");
        var loser = MakeAirline("Loser");
        var board = LandingRightsBoard.Empty with
        {
            SlotsGranted = new Dictionary<string, int> { ["gate"] = 1 },
            SlotsHeld = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = 1 },
            IdleStreak = new Dictionary<(string, string), int> { [("Incumbent", "gate")] = LandingRightsBoard.IdleExpiryThreshold },
            Pending = new[]
            {
                new PendingNegotiation(winner, city, Money.FromDollars(1_000_000), 1, 3),
                new PendingNegotiation(loser, city, Money.FromDollars(500_000), 1, 3),
            },
        };

        var (resolvedBoard, outcomes) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 3, RoutesFor("Incumbent"));

        Assert.True(outcomes.Single(o => o.Airline.Name == "Winner").Won);
        Assert.False(outcomes.Single(o => o.Airline.Name == "Loser").Won);
        Assert.Equal(1, resolvedBoard.SlotsHeldBy(winner, city));
    }

    // --- M46: SlotsRemaining/ResolveQuarter's capacity-bonus parameter ---

    [Fact]
    public void SlotsRemaining_NoBonusPassed_UnchangedFromBaseGateCapacity()
    {
        var city = MakeCity("gate", gateCapacity: 2);
        var board = LandingRightsBoard.Empty;

        Assert.Equal(2, board.SlotsRemaining(city));
    }

    [Fact]
    public void SlotsRemaining_WithCapacityBonus_AddsOnTopOfGateCapacity()
    {
        var city = MakeCity("gate", gateCapacity: 2);
        var board = LandingRightsBoard.Empty;

        Assert.Equal(5, board.SlotsRemaining(city, capacityBonus: 3));
    }

    [Fact]
    public void SlotsRemaining_CapacityBonus_SubtractsAlreadyGrantedTheSameAsBase()
    {
        var city = MakeCity("gate", gateCapacity: 2);
        var board = LandingRightsBoard.Empty with { SlotsGranted = new Dictionary<string, int> { ["gate"] = 2 } };

        Assert.Equal(3, board.SlotsRemaining(city, capacityBonus: 3));
    }

    [Fact]
    public void SlotsRemaining_NonGatewayCity_BonusNeverGivesItArtificialCapacity()
    {
        var city = new City("plain", "Plain", Region.EastAsia, 0, 0, 50, IsGateway: false, GateCapacity: null);
        var board = LandingRightsBoard.Empty;

        Assert.Equal(int.MaxValue, board.SlotsRemaining(city, capacityBonus: 3));
    }

    [Fact]
    public void ResolveQuarter_NullCapacityBonusByCity_PreservesPreExistingBehavior()
    {
        var city = MakeCity("gate", gateCapacity: 1);
        var a = MakeAirline("A");
        var board = LandingRightsBoard.Empty with
        {
            Pending = new[] { new PendingNegotiation(a, city, Money.FromDollars(1_000_000), 1, 3) },
        };

        var (_, outcomes) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 3, capacityBonusByCity: null);

        Assert.True(outcomes.Single().Won);
    }

    [Fact]
    public void ResolveQuarter_CapacityBonus_LetsASecondSuitorWin_WhereBaseCapacityWouldHaveRejected()
    {
        var city = MakeCity("gate", gateCapacity: 1); // base capacity 1 - only one bidder could normally win
        var a = MakeAirline("A");
        var b = MakeAirline("B");
        var board = LandingRightsBoard.Empty with
        {
            Pending = new[]
            {
                new PendingNegotiation(a, city, Money.FromDollars(1_000_000), 1, 3),
                new PendingNegotiation(b, city, Money.FromDollars(900_000), 1, 3),
            },
        };
        var capacityBonusByCity = new Dictionary<string, int> { ["gate"] = 1 }; // +1 earned via expansion

        var (_, outcomes) = LandingRightsResolver.ResolveQuarter(board, currentQuarter: 3, capacityBonusByCity: capacityBonusByCity);

        Assert.Equal(2, outcomes.Count(o => o.Won));
    }
}
