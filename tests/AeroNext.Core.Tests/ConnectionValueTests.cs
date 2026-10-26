using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>docs/CONNECTING_PASSENGERS_DESIGN.md: the connecting passengers a new route would carry, counted when the AI weighs opening it.</summary>
public class ConnectionValueTests
{
    private static readonly GravityDemandModel DemandModel = new();
    private static readonly City Home = new("home", "HOME", Region.NorthAmerica, 0, 0, 100, false, null);
    private static readonly City A = new("a", "A", Region.NorthAmerica, 0, 10, 100, false, null);
    private static readonly City B = new("b", "B", Region.NorthAmerica, 10, 0, 100, false, null);
    private static readonly City New = new("new", "NEW", Region.NorthAmerica, -10, -10, 100, false, null);
    private static readonly IReadOnlyList<City> Cities = new[] { Home, A, B, New };
    private static readonly AircraftType Plane = new("p", "P", Seats: 100, RangeKm: 10_000);

    private static StrategyContext Context(IReadOnlyList<Route> routes) =>
        new(routes, 3, Cities, DemandModel, new[] { Plane }, AirlineFleet.Empty, Region.NorthAmerica, Home);

    private static PairDemand Candidate => DemandModel.ForPair(Home, New);

    private static long Connecting(StrategyContext context) =>
        AiToolkit.ConnectingPassengersOpenedBy(context, new Route(Home, New, Plane, 1), AiToolkit.ConnectionNetwork.Of(context));

    [Fact]
    public void CountsEachCityTheNewRouteWouldNewlyConnect_ThroughEitherEndpoint()
    {
        // home flies to a and b; opening home-new connects a-new and b-new.
        var routes = new[] { new Route(Home, A, Plane, 1), new Route(Home, B, Plane, 1) };
        var context = Context(routes);
        long ShareOf(City from, Route leg)
        {
            var demand = DemandModel.ForPair(from, New).PassengersPerQuarter;
            var wanted = leg.SeatsPerQuarter + demand; // no record yet: the leg counts as exactly full
            return (long)Math.Floor(demand * Math.Min(1.0, (double)leg.SeatsPerQuarter / wanted));
        }

        Assert.Equal((long)Math.Floor((ShareOf(A, routes[0]) + ShareOf(B, routes[1])) * AiToolkit.ConnectingEstimateFactor), Connecting(context));
        Assert.True(Connecting(context) > 0);
        Assert.Equal(0, Connecting(Context(Array.Empty<Route>())));
    }

    [Fact]
    public void SkipsPairsTheAirlineAlreadyServes_NonstopOrWithAConnection()
    {
        // a-new is flown nonstop, so only b-new is new; then b-new is also
        // reachable through a (b-a-new), so nothing is.
        var withANew = new[] { new Route(Home, A, Plane, 1), new Route(Home, B, Plane, 1), new Route(A, New, Plane, 1) };
        var bOnly = Connecting(Context(withANew));
        var withBA = withANew.Append(new Route(B, A, Plane, 1)).ToArray();

        Assert.True(bOnly > 0);
        Assert.True(bOnly < Connecting(Context(new[] { new Route(Home, A, Plane, 1), new Route(Home, B, Plane, 1) })));
        Assert.Equal(0, Connecting(Context(withBA)));
    }

    [Fact]
    public void ACrowdedExistingLeg_AndRivalsOnThePair_ShrinkTheShare()
    {
        var routes = new[] { new Route(Home, A, Plane, 1) };
        var legKey = QuarterEngine.NormalizedPairKey(Home, A);
        var plain = Context(routes) with
        {
            LastQuarterSeatsCarriedByRoute = new Dictionary<(string, string), long> { [legKey] = routes[0].SeatsPerQuarter },
            LastQuarterTurnedAwayByRoute = new Dictionary<(string, string), long> { [legKey] = 0 },
        };
        var crowded = plain with { LastQuarterTurnedAwayByRoute = new Dictionary<(string, string), long> { [legKey] = routes[0].SeatsPerQuarter * 10 } };
        var contested = plain with { RivalPresenceByPairKey = new Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(A, New)] = 1 } };

        Assert.True(Connecting(crowded) < Connecting(plain));
        Assert.True(Connecting(contested) < Connecting(plain));
    }

    [Fact]
    public void EstimatedRealProfit_SeatsConnectionsAlongsideThePairsOwnPassengers_UpToTheSeats()
    {
        var context = Context(Array.Empty<Route>());
        var seats = new Route(Home, New, Plane, 1).SeatsPerQuarter;
        var alone = AiToolkit.EstimatedRealProfit(context, Candidate, Plane, requiresLease: false);
        var withConnections = AiToolkit.EstimatedRealProfit(context, Candidate, Plane, requiresLease: false, connectingPassengers: 1_000);

        Assert.True(Candidate.PassengersPerQuarter < seats); // room for both
        Assert.True(withConnections > alone);
        Assert.Equal(
            AiToolkit.EstimatedRealProfit(context, Candidate, Plane, requiresLease: false, connectingPassengers: seats * 5),
            AiToolkit.EstimatedRealProfit(context, Candidate, Plane, requiresLease: false, connectingPassengers: seats * 50));
    }

    // --- Competitiveness (CompetitiveMarket.ExpectedShare) ---

    private static Competition.PairOffer Offer(long seats, long journeyDollars) => new("Rival", seats, 1.0, Money.FromDollars(journeyDollars));

    [Fact]
    public void ExpectedShare_FollowsTheContestsSeatsAndJourneyCostWeighting()
    {
        var cost = Money.FromDollars(100);

        Assert.Equal(1.0, Competition.CompetitiveMarket.ExpectedShare(100, 1.0, cost, Array.Empty<Competition.PairOffer>()));
        Assert.Equal(0.5, Competition.CompetitiveMarket.ExpectedShare(100, 1.0, cost, new[] { Offer(100, 100) }), precision: 9);
        Assert.Equal(2.0 / 3, Competition.CompetitiveMarket.ExpectedShare(200, 1.0, cost, new[] { Offer(100, 100) }), precision: 9);
        // A journey twice as dear keeps (1/2)^1.5 of its weight.
        var dear = Math.Pow(0.5, 1.5);
        Assert.Equal(dear / (dear + 1), Competition.CompetitiveMarket.ExpectedShare(100, 1.0, Money.FromDollars(200), new[] { Offer(100, 100) }), precision: 9);
    }

    [Fact]
    public void ANewRouteFacingACheaperRival_ExpectsFewerPassengers_ThanTheEvenSplit()
    {
        var key = QuarterEngine.NormalizedPairKey(Home, New);
        var evenSplit = Context(Array.Empty<Route>()) with { RivalPresenceByPairKey = new Dictionary<(string, string), int> { [key] = 1 } };
        var priced = evenSplit with
        {
            LastQuarterRivalOffersByPair = new Dictionary<(string, string), IReadOnlyList<Competition.PairOffer>> { [key] = new[] { Offer(1_000_000, 1) } },
        };
        var unopposed = evenSplit with { LastQuarterRivalOffersByPair = new Dictionary<(string, string), IReadOnlyList<Competition.PairOffer>>() };

        var even = AiToolkit.EstimatedRealProfit(evenSplit, Candidate, Plane, requiresLease: false);
        var vsCheapRival = AiToolkit.EstimatedRealProfit(priced, Candidate, Plane, requiresLease: false);
        var alone = AiToolkit.EstimatedRealProfit(unopposed, Candidate, Plane, requiresLease: false);

        Assert.True(vsCheapRival < even);
        Assert.True(alone > even); // no rival offer on record: the whole pair
    }

    [Fact]
    public void AConnectionFacingADirectRival_CountsLessThanAnUnopposedOne()
    {
        var routes = new[] { new Route(Home, A, Plane, 1) };
        var key = QuarterEngine.NormalizedPairKey(A, New);
        var unopposed = Context(routes) with { LastQuarterRivalOffersByPair = new Dictionary<(string, string), IReadOnlyList<Competition.PairOffer>>() };
        var contested = unopposed with
        {
            LastQuarterRivalOffersByPair = new Dictionary<(string, string), IReadOnlyList<Competition.PairOffer>> { [key] = new[] { Offer(100_000, 50) } },
        };

        Assert.True(Connecting(unopposed) > 0);
        Assert.True(Connecting(contested) < Connecting(unopposed));
    }

    [Fact]
    public void TheMarketRecordsEveryAirlinesOffer_IncludingOneThatWonNothing()
    {
        var b = new City("b", "B", Region.NorthAmerica, 0, 20, 100, false, null);
        var winner = new Airline("W", new[] { new Route(Home, b, new AircraftType("big", "Big", Seats: 5000), 3) });
        var loser = new Airline("L", new[] { new Route(Home, b, Plane, 1) }, Quality: 0.0);

        var result = Competition.CompetitiveMarket.Resolve(new[] { winner, loser }, new[] { Home, b }, DemandModel);

        var offers = result.Pairs.Single().Offers!;
        Assert.Equal(new[] { "L", "W" }, offers.Select(o => o.AirlineName).OrderBy(n => n));
        Assert.Equal(new Route(Home, b, Plane, 1).SeatsPerQuarter, offers.Single(o => o.AirlineName == "L").Seats);
    }
}
