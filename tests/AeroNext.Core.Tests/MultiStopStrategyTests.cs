using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// docs/MULTI_STOP_ROUTES_DESIGN.md: multi-stop routes in the shared,
/// profit-ranked search (Network Optimizer, Balanced Growth) and in
/// Domestic Profit Optimizer. Same geometry as MultiStopAiTests: on the
/// equator, home at 0, a thin refuelling stop at 30 degrees (~3,336 km),
/// a big destination at 60 (~6,672 km); a 4,000 km-range type needs the
/// stop, sized from real demand so the route flies full.
/// </summary>
public class MultiStopStrategyTests
{
    private static readonly GravityDemandModel DemandModel = new();

    private sealed record Fixture(City Home, City Stop, City Far, AircraftType Type)
    {
        public IReadOnlyList<City> Cities => new[] { Home, Stop, Far };
        public IReadOnlySet<string> HomeOnly => new HashSet<string> { Home.Id };
    }

    private static Fixture MakeFixture(Region homeRegion, Region stopRegion, Region farRegion, string prefix)
    {
        var home = new City(prefix + "h", prefix.ToUpperInvariant() + "H", homeRegion, 0, 0, 100, false, null);
        var stop = new City(prefix + "s", prefix.ToUpperInvariant() + "S", stopRegion, 0, 30, 1, true, 2);
        var far = new City(prefix + "f", prefix.ToUpperInvariant() + "F", farRegion, 0, 60, 100, false, null);
        var probe = new AircraftType(prefix + "t", "Type", Seats: 1, RangeKm: 4_000, CruiseSpeedKmh: 600);
        var seatsPerSeat = new Route(home, far, probe, 1, Stops: StopList.Of(stop)).SeatsPerQuarter;
        var type = probe with { Seats = (int)Math.Max(1, DemandModel.ForPair(home, far).PassengersPerQuarter / seatsPerSeat * 9 / 10) };

        return new Fixture(home, stop, far, type);
    }

    /// <summary>Europe home, North America stop and destination - a new-region route.</summary>
    private static readonly Fixture Abroad = MakeFixture(Region.Europe, Region.NorthAmerica, Region.NorthAmerica, "ab");

    /// <summary>All North America - a domestic route.</summary>
    private static readonly Fixture Domestic = MakeFixture(Region.NorthAmerica, Region.NorthAmerica, Region.NorthAmerica, "do");

    /// <summary>North America home and destination, a refuelling stop in Europe.</summary>
    private static readonly Fixture DomesticViaAbroad = MakeFixture(Region.NorthAmerica, Region.Europe, Region.NorthAmerica, "dx");

    private static AirlineFleet OneIdle(AircraftType type) => FleetLedger.ApplyDelivery(AirlineFleet.Empty, type, quantity: 1, FleetOwnershipMode.Owned);

    private static StrategyContext MakeContext(Fixture f, bool stopSlotHeld, bool withSlotData = true, IReadOnlySet<string>? pending = null) =>
        new(Array.Empty<Route>(), ActionCap: 3, f.Cities, DemandModel, new[] { f.Type }, OneIdle(f.Type), f.Home.Region, f.Home,
            AvailableSlotsByCity: !withSlotData ? null : stopSlotHeld ? new Dictionary<string, int> { [f.Stop.Id] = 1 } : new Dictionary<string, int>(),
            AvailableCash: Money.FromDollars(1_000_000_000), PendingNegotiationCityIds: pending);

    private static bool TouchesFar(Fixture f, PairDemand p) => p.Origin.Id == f.Far.Id || p.Destination.Id == f.Far.Id;

    // --- Shared toolkit ---

    [Fact]
    public void FindMostProfitablePair_FindsTheMultiStopRoute_OnlyOnceTheStopSlotIsHeld()
    {
        var held = AiToolkit.FindMostProfitablePair(MakeContext(Abroad, stopSlotHeld: true), new HashSet<int>(), Abroad.HomeOnly, p => TouchesFar(Abroad, p));
        var notHeld = AiToolkit.FindMostProfitablePair(MakeContext(Abroad, stopSlotHeld: false), new HashSet<int>(), Abroad.HomeOnly, p => TouchesFar(Abroad, p));

        Assert.NotNull(held);
        Assert.Equal(Abroad.Type.Id, held.Value.Aircraft.Id);
        Assert.Null(notHeld);
    }

    [Fact]
    public void TryProposeCandidate_OpensAMultiStopCandidate_WithItsStops()
    {
        var context = MakeContext(Abroad, stopSlotHeld: true);
        var best = AiToolkit.FindMostProfitablePair(context, new HashSet<int>(), Abroad.HomeOnly, p => TouchesFar(Abroad, p));
        var orders = new List<QuarterOrder>();
        var spent = 0;

        AiToolkit.TryProposeCandidate(context, best, Abroad.HomeOnly, orders, new HashSet<int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), ref spent);

        var open = Assert.IsType<RouteOrder>(Assert.Single(orders));
        Assert.Equal(StopList.Of(Abroad.Stop), open.Stops);
    }

    [Fact]
    public void TryProposeCandidate_NoStopPathForTheAircraft_ProposesNothing()
    {
        var context = MakeContext(Abroad, stopSlotHeld: true);
        var tooShort = Abroad.Type with { Id = "short", RangeKm = 2_000 };
        var candidate = (Pair: DemandModel.ForPair(Abroad.Home, Abroad.Far), Aircraft: tooShort, Profit: Money.FromDollars(1), UsesIdleUnit: true);
        var orders = new List<QuarterOrder>();
        var spent = 0;

        AiToolkit.TryProposeCandidate(context, candidate, Abroad.HomeOnly, orders, new HashSet<int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), ref spent);

        Assert.Empty(orders);
    }

    [Fact]
    public void FindStopNeededByBestMultiStopPlan_NamesTheStopTheBestPlanNeeds()
    {
        var stop = AiToolkit.FindStopNeededByBestMultiStopPlan(MakeContext(Abroad, stopSlotHeld: false), new HashSet<int>(), Abroad.HomeOnly, _ => true, new(), new());

        Assert.Equal(Abroad.Stop.Id, stop?.Id);
    }

    [Fact]
    public void FindStopNeededByBestMultiStopPlan_NothingToBidOn_WhenHeldPendingFilteredOrPermissive()
    {
        Func<PairDemand, bool> any = _ => true;

        Assert.Null(AiToolkit.FindStopNeededByBestMultiStopPlan(MakeContext(Abroad, stopSlotHeld: true), new HashSet<int>(), Abroad.HomeOnly, any, new(), new()));
        Assert.Null(AiToolkit.FindStopNeededByBestMultiStopPlan(MakeContext(Abroad, stopSlotHeld: false, pending: new HashSet<string> { Abroad.Stop.Id }), new HashSet<int>(), Abroad.HomeOnly, any, new(), new()));
        Assert.Null(AiToolkit.FindStopNeededByBestMultiStopPlan(MakeContext(Abroad, stopSlotHeld: false), new HashSet<int>(), Abroad.HomeOnly, any, new(), new(), stopFilter: c => c.Region == Region.Europe));
        Assert.Null(AiToolkit.FindStopNeededByBestMultiStopPlan(MakeContext(Abroad, stopSlotHeld: false, withSlotData: false), new HashSet<int>(), Abroad.HomeOnly, any, new(), new()));
    }

    [Fact]
    public void HasProfitableNonstopRouteTo_TrueForABigGatewayInRange_FalseForAThinStop()
    {
        var context = MakeContext(Abroad, stopSlotHeld: false);
        var bigGateway = new City("big", "BIG", Region.NorthAmerica, 0, 20, 100, true, 3);
        var withBig = context with { AllCities = new[] { Abroad.Home, Abroad.Stop, Abroad.Far, bigGateway } };

        Assert.False(AiToolkit.HasProfitableNonstopRouteTo(context, Abroad.Stop));
        Assert.True(AiToolkit.HasProfitableNonstopRouteTo(withBig, bigGateway));
    }

    [Fact]
    public void FindBestIdleContestableGateway_SkipsAThinStop_ButTakesAUsableGateway()
    {
        // Both have an idle-expired slot up for grabs; only the big gateway
        // has a profitable nonstop route from the network.
        var bigGateway = new City("big", "BIG", Region.NorthAmerica, 0, 20, 100, true, 3);
        var context = MakeContext(Abroad, stopSlotHeld: false) with
        {
            AllCities = new[] { Abroad.Home, Abroad.Stop, Abroad.Far, bigGateway },
            IdleContestableSlotsByCity = new Dictionary<string, int> { [Abroad.Stop.Id] = 1, ["big"] = 1 },
        };
        var stopOnly = context with { IdleContestableSlotsByCity = new Dictionary<string, int> { [Abroad.Stop.Id] = 1 } };

        Assert.Equal("big", AiToolkit.FindBestIdleContestableGateway(context)?.Id);
        Assert.Null(AiToolkit.FindBestIdleContestableGateway(stopOnly));
    }

    [Fact]
    public void RegionsPursuedByPendingNegotiations_APendingStopBid_CountsTheRegionItsRouteWouldReach()
    {
        // The stop itself is too thin for any route of its own, so it only
        // counts through the multi-stop route it enables - home (Europe) to
        // the far city in North America.
        var context = MakeContext(Abroad, stopSlotHeld: false, pending: new HashSet<string> { Abroad.Stop.Id });

        var pursued = AiToolkit.RegionsPursuedByPendingNegotiations(context, new HashSet<int>(), Abroad.HomeOnly, new(), new());

        Assert.Equal(new[] { Region.NorthAmerica }, pursued);
    }

    [Fact]
    public void OrientStops_ReversesTheStopsWhenTheRouteStartsAtTheOtherEnd()
    {
        var second = new City("st2", "ST2", Region.NorthAmerica, 0, 45, 1, true, 2);
        var pair = DemandModel.ForPair(Abroad.Home, Abroad.Far);
        var stops = StopList.Of(Abroad.Stop, second);

        Assert.Equal(stops, AiToolkit.OrientStops(pair, pair.Origin, stops));
        Assert.Equal(StopList.Of(second, Abroad.Stop), AiToolkit.OrientStops(pair, pair.Destination, stops));
    }

    // --- Balanced Growth ---

    [Fact]
    public void BalancedGrowth_BidsForTheStopItsBestNewRegionRouteNeeds()
    {
        var orders = new BalancedGrowthStrategy().DecideOrders(MakeContext(Abroad, stopSlotHeld: false));

        Assert.Equal(Abroad.Stop.Id, Assert.Single(orders.OfType<NegotiationOrder>()).TargetCity.Id);
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void BalancedGrowth_WithTheStopSlotHeld_OpensTheMultiStopRoute()
    {
        var orders = new BalancedGrowthStrategy().DecideOrders(MakeContext(Abroad, stopSlotHeld: true));

        Assert.Equal(StopList.Of(Abroad.Stop), Assert.Single(orders.OfType<RouteOrder>()).Stops);
    }

    // --- Network Optimizer and Domestic Profit Optimizer: home region only ---

    public static TheoryData<string> DomesticStrategies => new() { "network", "domestic" };

    private static IAiStrategy Strategy(string which) =>
        which == "network" ? new NetworkOptimizerStrategy() : new DomesticProfitOptimizerStrategy();

    [Theory]
    [MemberData(nameof(DomesticStrategies))]
    public void DomesticStrategies_BidForAHomeRegionStop_AndOpenTheRouteOnceHeld(string which)
    {
        var bid = Strategy(which).DecideOrders(MakeContext(Domestic, stopSlotHeld: false));
        var open = Strategy(which).DecideOrders(MakeContext(Domestic, stopSlotHeld: true));

        Assert.Equal(Domestic.Stop.Id, Assert.Single(bid.OfType<NegotiationOrder>()).TargetCity.Id);
        var route = Assert.Single(open.OfType<RouteOrder>());
        Assert.Equal((Domestic.Home.Id, Domestic.Far.Id), (route.Origin.Id, route.Destination.Id));
        Assert.Equal(StopList.Of(Domestic.Stop), route.Stops);
    }

    [Theory]
    [MemberData(nameof(DomesticStrategies))]
    public void DomesticStrategies_NeverBidForAStopAbroad(string which)
    {
        var orders = Strategy(which).DecideOrders(MakeContext(DomesticViaAbroad, stopSlotHeld: false));

        Assert.DoesNotContain(orders.OfType<NegotiationOrder>(), o => o.TargetCity.Id == DomesticViaAbroad.Stop.Id);
    }
}
