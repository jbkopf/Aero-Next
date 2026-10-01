using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// docs/MULTI_STOP_ROUTES_DESIGN.md, Phase 3: the AI building and keeping
/// multi-stop routes.
///
/// Geometry, on the equator: the airline's home city, a refuelling stop
/// (a gateway with almost no demand of its own) 30 degrees east (~3,336 km),
/// and a big destination 60 degrees east (~6,672 km). The test aircraft
/// (4,000 km range) can reach the stop but not the destination nonstop, so
/// home-destination needs the stop, and home-stop alone is the thin "lone
/// leg" the AI used to fly. The aircraft's seat count is sized from the
/// real demand so the multi-stop route runs full - comfortably profitable.
/// </summary>
public class MultiStopAiTests
{
    private static readonly GravityDemandModel DemandModel = new();
    private static readonly City Home = new("hom", "HOME", Region.Europe, 0, 0, 100, false, null);
    private static readonly City Stop = new("stp", "STOP", Region.NorthAmerica, 0, 30, 1, true, 2);
    private static readonly City Far = new("far", "FAR", Region.NorthAmerica, 0, 60, 100, false, null);
    private static readonly City[] Cities = { Home, Stop, Far };

    /// <summary>Seats such that a single aircraft via the stop offers less than the pair's demand - it flies full.</summary>
    private static readonly AircraftType Mid = MakeSizedType("mid", rangeKm: 4_000);

    private static AircraftType MakeSizedType(string id, int rangeKm, bool viaStop = true)
    {
        var probe = new AircraftType(id, id, Seats: 1, RangeKm: rangeKm, CruiseSpeedKmh: 600, Cost: Money.FromDollars(1_000_000));
        var seatsPerSeat = new Route(Home, Far, probe, 1, Stops: viaStop ? StopList.Of(Stop) : StopList.Empty).SeatsPerQuarter;
        var demand = DemandModel.ForPair(Home, Far).PassengersPerQuarter;

        return probe with { Seats = (int)Math.Max(1, demand / seatsPerSeat * 9 / 10) };
    }

    private static AirlineFleet IdleFleet(params AircraftType[] types) =>
        types.Aggregate(AirlineFleet.Empty, (fleet, type) => FleetLedger.ApplyDelivery(fleet, type, quantity: 1, FleetOwnershipMode.Owned));

    private static StrategyContext MakeContext(
        IReadOnlyDictionary<string, int>? slots, AirlineFleet? fleet = null, IReadOnlyList<Route>? routes = null,
        IReadOnlySet<string>? pending = null, IReadOnlyList<AircraftType>? types = null, IReadOnlyList<City>? cities = null, City? home = null) =>
        new(routes ?? Array.Empty<Route>(), ActionCap: 3, cities ?? Cities, DemandModel, types ?? new[] { Mid }, fleet ?? AirlineFleet.Empty,
            Region.Europe, home ?? Home, AvailableSlotsByCity: slots, AvailableCash: Money.FromDollars(1_000_000_000),
            PendingNegotiationCityIds: pending);

    // --- Toolkit ---

    [Fact]
    public void SwapStops_NonstopWhenTheTypeCan_KeepsStopsWhenItNeedsThem_NullOtherwise()
    {
        var route = new Route(Home, Far, Mid, 1, Stops: StopList.Of(Stop));
        var longRange = new AircraftType("long", "Long", Seats: 100, RangeKm: 8_000);
        var tooShort = new AircraftType("short", "Short", Seats: 100, RangeKm: 3_000);

        Assert.Equal(StopList.Empty, AiToolkit.SwapStops(route, longRange));
        Assert.Equal(StopList.Of(Stop), AiToolkit.SwapStops(route, Mid with { Id = "mid2" }));
        Assert.Null(AiToolkit.SwapStops(route, tooShort));
    }

    [Fact]
    public void TryProposeTypeSwap_ToATypeThatFliesNonstop_DropsTheStop()
    {
        var longRange = new AircraftType("long", "Long", Seats: 100, RangeKm: 8_000);
        var route = new Route(Home, Far, Mid, 1, Stops: StopList.Of(Stop));
        var load = new RouteLoad(route, DemandPassengers: 1, SeatsPerQuarter: 1, PassengersCarried: 1);
        var context = MakeContext(new Dictionary<string, int>(), IdleFleet(longRange), new[] { route });
        var orders = new List<QuarterOrder>();
        var spent = 0;

        AiToolkit.TryProposeTypeSwap(context, load, (Type: longRange, ProfitDelta: Money.FromDollars(1), UsesIdleUnit: true, CanBuyOutright: false), orders, new HashSet<int>(), ref spent);

        var swap = Assert.IsType<AdjustRouteOrder>(Assert.Single(orders));
        Assert.Equal("long", swap.AircraftType.Id);
        Assert.True(swap.Stops.IsEmpty);
    }

    [Fact]
    public void EvaluateTypeSwapCandidate_PricesAMultiStopRouteOverItsStops_AndOffersTheNonstopUpgrade()
    {
        // The incumbent loses money via the stop (a costly, half-empty
        // type); a long-range type sized to fly the pair nonstop and full
        // is the reactive fix - it can only be one if it's priced nonstop.
        var costly = Mid with { Id = "costly", Seats = Mid.Seats * 2, CostPerAvailableSeatKm = 0.5 };
        var longRange = MakeSizedType("long", rangeKm: 8_000, viaStop: false);
        var route = new Route(Home, Far, costly, 1, Stops: StopList.Of(Stop));
        var load = RouteEvaluator.Evaluate(route, DemandModel);
        var context = MakeContext(new Dictionary<string, int>(), IdleFleet(longRange), new[] { route }, types: new[] { costly, longRange });

        var candidate = AiToolkit.EvaluateTypeSwapCandidate(context, load, new HashSet<int>(), isReactive: true);

        Assert.NotNull(candidate);
        Assert.Equal("long", candidate.Value.Type.Id);
    }

    [Fact]
    public void CanOpenNow_NeedsAFreeSlotAtEveryStop()
    {
        var pair = DemandModel.ForPair(Home, Far);
        var validOrigins = new HashSet<string> { "hom" };

        var withSlot = AiToolkit.CanOpenNow(MakeContext(new Dictionary<string, int> { ["stp"] = 1 }), pair, validOrigins, new(), new(), StopList.Of(Stop));
        var withoutSlot = AiToolkit.CanOpenNow(MakeContext(new Dictionary<string, int>()), pair, validOrigins, new(), new(), StopList.Of(Stop));

        Assert.True(withSlot);
        Assert.False(withoutSlot);
    }

    [Fact]
    public void EstimatedRealProfit_PricesTheRouteAsFlown_StopsIncluded()
    {
        // Whether a stop raises or lowers the estimate depends on demand
        // against seats; what matters is that it's priced over every leg.
        // A fast type, so the stop's extra landing costs round trips
        // (8 a week nonstop, 6 via the stop) and the two estimates differ.
        var context = MakeContext(null);
        var pair = DemandModel.ForPair(Home, Far);
        var fast = Mid with { Id = "fast", RangeKm = 8_000, CruiseSpeedKmh = 2_000 };
        var viaStop = new Route(Home, Far, fast, 1, Stops: StopList.Of(Stop));
        var expected = Finance.FareCalculator.EstimatedProfit(viaStop, Math.Min(pair.PassengersPerQuarter, viaStop.SeatsPerQuarter));

        Assert.Equal(expected, AiToolkit.EstimatedRealProfit(context, pair, fast, requiresLease: false, StopList.Of(Stop)));
        Assert.NotEqual(expected, AiToolkit.EstimatedRealProfit(context, pair, fast, requiresLease: false));
    }

    // --- Rapid Expansion ---

    [Fact]
    public void Rapid_WithTheStopSlotAndAnIdleAircraft_OpensTheMultiStopRoute_NotTheLoneLeg()
    {
        var context = MakeContext(new Dictionary<string, int> { ["stp"] = 1 }, IdleFleet(Mid));

        var orders = new RapidExpansionStrategy().DecideOrders(context);

        var open = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(("hom", "far"), (open.Origin.Id, open.Destination.Id));
        Assert.Equal(StopList.Of(Stop), open.Stops);
    }

    [Fact]
    public void Rapid_WithoutTheStopSlot_BidsForTheStopItsPlanNeeds()
    {
        var context = MakeContext(new Dictionary<string, int>(), IdleFleet(Mid));

        var orders = new RapidExpansionStrategy().DecideOrders(context);

        Assert.DoesNotContain(orders.OfType<RouteOrder>(), o => o.Destination.Id == "far" || o.Origin.Id == "far");
        Assert.Equal("stp", Assert.Single(orders.OfType<NegotiationOrder>()).TargetCity.Id);
    }

    [Fact]
    public void Rapid_StopAlreadyBeingNegotiated_IsNotBidOnAgain()
    {
        var context = MakeContext(new Dictionary<string, int>(), IdleFleet(Mid), pending: new HashSet<string> { "stp" });

        var orders = new RapidExpansionStrategy().DecideOrders(context);

        Assert.DoesNotContain(orders.OfType<NegotiationOrder>(), o => o.TargetCity.Id == "stp");
    }

    [Fact]
    public void Rapid_DoesNotBidOnAGatewayItCanOnlyFlyALosingNonstopTo()
    {
        // A long-range type flies home-destination nonstop, so no plan needs
        // the stop; on its own the stop's only route would lose money.
        var longRange = MakeSizedType("long", rangeKm: 8_000);
        var context = MakeContext(new Dictionary<string, int>(), IdleFleet(longRange), types: new[] { longRange });

        var orders = new RapidExpansionStrategy().DecideOrders(context);

        Assert.DoesNotContain(orders.OfType<NegotiationOrder>(), o => o.TargetCity.Id == "stp");
    }

    [Fact]
    public void Rapid_WithTheStopSlotButNothingIdle_AcquiresTheAircraftForTheMultiStopRoute()
    {
        var context = MakeContext(new Dictionary<string, int> { ["stp"] = 1 });

        var orders = new RapidExpansionStrategy().DecideOrders(context);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Equal("mid", Assert.Single(orders.OfType<AircraftOrder>()).Type.Id);
    }

    [Fact]
    public void Rapid_TwoStopRouteOpenedFromTheOtherEnd_FliesItsStopsInOrder()
    {
        // The airline's home ("zzz") sorts after the far end ("aaa"), so the
        // pair comes back as aaa-zzz and has to be flipped to start at home -
        // its two stops must flip with it.
        var farEnd = new City("aaa", "AAA", Region.NorthAmerica, 0, 0, 100, false, null);
        var nearFar = new City("s1", "S1", Region.NorthAmerica, 0, 20, 1, true, 2);
        var nearHome = new City("s2", "S2", Region.NorthAmerica, 0, 40, 1, true, 2);
        var home = new City("zzz", "ZZZ", Region.Europe, 0, 60, 100, false, null);
        var cities = new[] { farEnd, nearFar, nearHome, home };
        var probe = new AircraftType("hop", "Hop", Seats: 1, RangeKm: 2_300, CruiseSpeedKmh: 600);
        var seatsPerSeat = new Route(home, farEnd, probe, 1, Stops: StopList.Of(nearHome, nearFar)).SeatsPerQuarter;
        var hop = probe with { Seats = (int)Math.Max(1, DemandModel.ForPair(home, farEnd).PassengersPerQuarter / seatsPerSeat * 9 / 10) };
        var context = MakeContext(new Dictionary<string, int> { ["s1"] = 1, ["s2"] = 1 }, IdleFleet(hop), types: new[] { hop }, cities: cities, home: home);

        var orders = new RapidExpansionStrategy().DecideOrders(context);

        var open = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(("zzz", "aaa"), (open.Origin.Id, open.Destination.Id));
        Assert.Equal(new[] { "s2", "s1" }, open.Stops.Select(s => s.Id));
    }

    // --- Growing an existing multi-stop route ---

    [Fact]
    public void GrowingAMultiStopRoute_CarriesItsStops_InEveryStrategyThatGrows()
    {
        // One full, profitable multi-stop route, a spare aircraft of its type,
        // and a free slot at the stop: each growth step must ask for the
        // same stops, or the engine rejects the order.
        var route = new Route(Home, Far, Mid, 1, Stops: StopList.Of(Stop));
        var fleet = FleetLedger.Assign(IdleFleet(Mid, Mid), 1, "hom-far");
        var context = MakeContext(new Dictionary<string, int> { ["stp"] = 1 }, fleet, new[] { route }) with { QuarterNumber = 60 };

        foreach (IAiStrategy strategy in new IAiStrategy[] { new RapidExpansionStrategy(), new NetworkOptimizerStrategy(), new BalancedGrowthStrategy() })
        {
            var growth = strategy.DecideOrders(context).OfType<RouteOrder>().Where(o => o.Origin.Id == "hom" && o.Destination.Id == "far").ToList();

            Assert.True(growth.Count == 1, $"{strategy.Name} didn't grow the route");
            Assert.Equal(StopList.Of(Stop), growth[0].Stops);
        }
    }
}
