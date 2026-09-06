using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// Bankruptcy-conditions follow-up: DistressStrategy's own DecideOrders
/// never reads StrategyContext.InDistress itself (that flag only decides
/// *whether* this strategy runs at all - see FullGameRunner.cs and this
/// class's own doc comment) - every fixture below simply calls
/// DistressStrategy.Instance.DecideOrders directly, the same convention
/// every other real strategy's own test file already uses.
/// </summary>
public class DistressStrategyTests
{
    private static City MakeCity(string id, Region region, double lat, double lon, int weight, bool isGateway = false) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, isGateway, isGateway ? 3 : null);

    private static readonly GravityDemandModel DemandModel = new();

    private static StrategyContext MakeContext(
        IReadOnlyList<City> cities, City homeCity, IReadOnlyList<Route>? routes = null, AirlineFleet? fleet = null,
        Money? availableCash = null, Money? currentDebtBalance = null) =>
        new(routes ?? Array.Empty<Route>(), ActionCap: 3, cities, DemandModel, Array.Empty<AircraftType>(), fleet ?? AirlineFleet.Empty,
            homeCity.Region, homeCity, AvailableCash: availableCash, CurrentDebtBalance: currentDebtBalance);

    [Fact]
    public void Name_And_Suitability_AreRealButNeverConsultedThroughTheNormalRoster()
    {
        // Both exist only to satisfy IAiStrategy's contract - Suitability
        // in particular must never be able to win a real comparison
        // against a normal strategy's own score, since this class is
        // selected by direct dispatch on InDistress, not through
        // BaselineAi.SelectStrategy's roster scan (see this class's own
        // doc comment).
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 1);
        var context = MakeContext(new[] { home }, home);

        Assert.Equal("Distress", DistressStrategy.Instance.Name);
        Assert.Equal(double.NegativeInfinity, DistressStrategy.Instance.Suitability(context));
    }

    [Fact]
    public void DecideOrders_NeverProposesGrowthOrders_EvenWithGreatUnservedPairAndGateway()
    {
        // An obviously great unserved pair (huge demand, well within
        // range, an idle unit ready to fly it) and an unserved gateway,
        // with plenty of cash to negotiate - a normal strategy would
        // reach for all of this. DistressStrategy proposes none of it.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var far = MakeCity("far", Region.NorthAmerica, 1, 1, 500);
        var gateway = MakeCity("gw", Region.NorthAmerica, 5, 5, 50, isGateway: true);
        var cities = new[] { home, far, gateway };
        var aircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(cities, home, fleet: fleet, availableCash: Money.FromDollars(50_000_000));

        var orders = DistressStrategy.Instance.DecideOrders(context);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Empty(orders.OfType<AircraftOrder>());
        Assert.Empty(orders.OfType<NegotiationOrder>());
        Assert.Empty(orders.OfType<AllianceCourtOrder>());
    }

    [Fact]
    public void DecideOrders_DoesNotRetireIdleUnit_WhenItsTypeCouldStillServeAGreatUnservedPair()
    {
        // Investigation finding: an earlier design retired every idle
        // unit unconditionally, regardless of whether ApplyManagementToolkit's
        // own narrower "genuinely stranded" check (idle, no served route
        // uses its type, no unserved pair worth opening needs it either)
        // would have retired it - measured to cost real value in Europe
        // (some of those units really were useful again once solvent).
        // This unit's type could clearly still serve a great unserved
        // pair (home-far), so it is NOT stranded, and DistressStrategy no
        // longer retires it - purely ApplyManagementToolkit's own
        // narrower behavior now, reused unmodified.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var far = MakeCity("far", Region.NorthAmerica, 1, 1, 500);
        var cities = new[] { home, far };
        var aircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(cities, home, fleet: fleet, availableCash: Money.FromDollars(50_000_000));

        var orders = DistressStrategy.Instance.DecideOrders(context);

        Assert.Empty(orders.OfType<RetireAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_ClosesASustainedUncontestedSingleAircraftRoute()
    {
        // Investigation finding, the real fix: DistressStrategy's own
        // call passes enableSustainedSingleAircraftClosure: true, matching
        // NetworkOptimizerStrategy's own choice - that strategy is the
        // assigned one for 72% of all real distress events, so passing
        // false here (every other real strategy's own default) silently
        // downgraded away this capability for exactly the quarter an
        // airline is most vulnerable, measured as EastAsia's entire share
        // of the original regression. Same fixture shape as
        // NetworkOptimizerStrategyTests's own proof of this same wiring.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var bigAircraft = new AircraftType("big", "Big", Seats: 100_000, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var route = new Route(home, b, bigAircraft, AircraftCount: 1);
        var rivalCount = new Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, b)] = 0 };
        var history = new Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(home, b)] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 20),
        };
        var context = new StrategyContext(new[] { route }, ActionCap: 3, new[] { home, b }, DemandModel, new[] { bigAircraft }, AirlineFleet.Empty,
            home.Region, home, RivalCountByServedRoute: rivalCount, QuarterNumber: 100, RouteHistoryByPair: history);

        var orders = DistressStrategy.Instance.DecideOrders(context);

        var closeOrder = Assert.Single(orders.OfType<CloseRouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "b" }, new HashSet<string> { closeOrder.Origin.Id, closeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_StillAppliesSharedToolkit_RaisesFareOnSaturatedRoute()
    {
        // Confirms AiToolkit.ApplyManagementToolkit is genuinely reused,
        // not accidentally skipped - same saturated-route-raises-fare
        // fixture shape every other real strategy's own test file already
        // uses. No fleet passed (defaults to empty) - the route exists
        // purely as CurrentRoutes data, so nothing here is idle and this
        // test's own fare assertion can't be muddied by a retirement order.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 200);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 200);
        var tinyAircraft = new AircraftType("tiny", "Tiny", Seats: 1, RangeKm: 2000); // real demand vastly exceeds capacity - saturated
        var route = new Route(home, near, tinyAircraft, AircraftCount: 1);
        var context = MakeContext(new[] { home, near }, home, routes: new[] { route });

        var orders = DistressStrategy.Instance.DecideOrders(context);

        var fareOrder = Assert.Single(orders.OfType<SetFareOrder>());
        Assert.Equal(new HashSet<string> { "home", "near" }, new HashSet<string> { fareOrder.Origin.Id, fareOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_StillBorrows_WhenCashFallsBelowMaintainCashRunwaysWarningLine()
    {
        // Same numbers as AiToolkitTests's own proven
        // ApplyManagementToolkit_BelowWarningLine_BorrowsExactlyEnoughToReachTarget:
        // 4 units x $250,000/quarter = $1,000,000/quarter burn;
        // MinimumCashRunwayQuarters (2) x that = $2,000,000 warning line,
        // $1,000,000 cash is below it; TargetCashRunwayQuarters (4) x
        // burn = $4,000,000 target, so the borrow is exactly the
        // $3,000,000 shortfall. Confirms MaintainCashRunway is genuinely
        // reused, not accidentally skipped. Only one city exists in this
        // fixture, so no unserved pair can possibly exist either - these 4
        // idle units are genuinely stranded under ApplyManagementToolkit's
        // own narrower check, and get retired for that real reason, not a
        // DistressStrategy-specific override (see the test above).
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 4, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { home }, home, fleet: fleet, availableCash: Money.FromDollars(1_000_000), currentDebtBalance: Money.Zero);

        var orders = DistressStrategy.Instance.DecideOrders(context);

        var borrowOrder = Assert.Single(orders.OfType<RefinanceOrder>());
        Assert.Equal(Money.FromDollars(3_000_000), borrowOrder.AdditionalBorrowing);

        var retireOrders = orders.OfType<RetireAircraftOrder>().ToList();
        Assert.Equal(4, retireOrders.Count);
        Assert.Equal(fleet.Units.Select(u => u.Id).OrderBy(id => id), retireOrders.Select(o => o.UnitId).OrderBy(id => id));
    }
}
