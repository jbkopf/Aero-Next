using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class NetworkOptimizerStrategyTests
{
    private static City MakeCity(string id, Region region, double lat, double lon, int weight, bool isGateway = false, int? autoCapacity = null) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, isGateway, isGateway ? 3 : null, isGateway ? null : autoCapacity);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly NetworkOptimizerStrategy Strategy = new();
    private static readonly AircraftType BigAircraft = new("big", "Big", Seats: 100_000, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);

    private static StrategyContext MakeContext(
        IReadOnlyList<Route> routes, IReadOnlyList<City> cities, IReadOnlyList<AircraftType> eraTypes, Region homeRegion,
        AirlineFleet? fleet = null, int actionCap = 3, Money? cash = null, City? homeCity = null,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), int>? rivalCountByServedRoute = null,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), AiRouteHistory>? routeHistoryByPair = null,
        int quarterNumber = 1,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), long>? lastQuarterSeatsCarriedByRoute = null,
        IReadOnlySet<Region>? regionsLed = null) =>
        new(routes, actionCap, cities, DemandModel, eraTypes, fleet ?? AirlineFleet.Empty, homeRegion, HomeCity: homeCity ?? cities[0], AvailableCash: cash,
            RivalCountByServedRoute: rivalCountByServedRoute, RouteHistoryByPair: routeHistoryByPair, QuarterNumber: quarterNumber,
            LastQuarterSeatsCarriedByRoute: lastQuarterSeatsCarriedByRoute, RegionsLedLastQuarter: regionsLed);

    /// <summary>Shorthand for a RouteHistoryByPair entry with only ConsecutiveUnprofitableQuarters set - the only field this strategy's own Suitability reads.</summary>
    private static AiRouteHistory Sustained(int consecutiveUnprofitableQuarters) =>
        new(LastShrunkQuarter: null, LastClosedQuarter: null, consecutiveUnprofitableQuarters);

    [Fact]
    public void ApplicableEras_CoversMidGameAndLateGame_NeverStarting()
    {
        Assert.DoesNotContain(GameEra.Starting, Strategy.ApplicableEras);
        Assert.Contains(GameEra.MidGame, Strategy.ApplicableEras);
        Assert.Contains(GameEra.LateGame, Strategy.ApplicableEras);
    }

    [Fact]
    public void Suitability_EmptyNetwork_IsZero()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var context = MakeContext(Array.Empty<Route>(), new[] { home }, Array.Empty<AircraftType>(), Region.NorthAmerica);

        Assert.Equal(0.0, Strategy.Suitability(context));
    }

    [Fact]
    public void Suitability_OneRouteSustainedUnprofitable_OutOfThree_IsOneThird()
    {
        // MinimumTroubledRouteCountForSignal is 1 (post-M58-resume) - a
        // single genuinely-sustained-bad route is enough to register,
        // scaled by ratio. "home"-"b" has 2 consecutive unprofitable
        // quarters (>= MinimumSustainedUnprofitableQuarters); the other
        // two routes have no history at all (never served long enough to
        // build one, or always profitable) and don't count.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 50);
        var d = MakeCity("d", Region.NorthAmerica, 3, 3, 50);
        var routes = new[]
        {
            new Route(home, b, BigAircraft, AircraftCount: 1),
            new Route(home, c, BigAircraft, AircraftCount: 1),
            new Route(home, d, BigAircraft, AircraftCount: 1),
        };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(home, b)] = Sustained(2),
        };
        var context = MakeContext(routes, new[] { home, b, c, d }, new[] { BigAircraft }, Region.NorthAmerica, routeHistoryByPair: history);

        Assert.Equal(1.0 / 3.0, Strategy.Suitability(context));
    }

    [Fact]
    public void Suitability_AllRoutesSustainedUnprofitable_IsOne()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 50);
        var routes = new[]
        {
            new Route(home, b, BigAircraft, AircraftCount: 1),
            new Route(home, c, BigAircraft, AircraftCount: 1),
        };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(home, b)] = Sustained(2),
            [QuarterEngine.NormalizedPairKey(home, c)] = Sustained(5), // well past the threshold - still just counts once
        };
        var context = MakeContext(routes, new[] { home, b, c }, new[] { BigAircraft }, Region.NorthAmerica, routeHistoryByPair: history);

        Assert.Equal(1.0, Strategy.Suitability(context));
    }

    [Fact]
    public void Suitability_RouteUnprofitableOnlyThisQuarter_NoSustainedHistoryYet_DoesNotCount()
    {
        // The concrete regression-lock for the M58-resume fix itself: the
        // OLD same-quarter check (RouteEvaluator.EvaluateNetwork +
        // FareCalculator.EstimatedProfit, recomputed fresh every call)
        // would have counted this route - it's the exact same
        // huge-capacity-vs-real-demand fixture the other tests in this
        // file use to build an unprofitable route. The NEW check reads
        // ONLY RouteHistoryByPair's own ConsecutiveUnprofitableQuarters,
        // which is either absent (no history entry) or below
        // MinimumSustainedUnprofitableQuarters (2) here - a single bad
        // quarter with no sustained record yet must not trigger this
        // strategy's own selection, the literal fix for "the floor of 3
        // never fired for a real, persistently-bad ~1-route-out-of-5-8
        // pattern because it was never sustained-aware in the first
        // place."
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(home, b, BigAircraft, AircraftCount: 1); // unprofitable this quarter, if read fresh
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(home, b)] = Sustained(1), // one bad quarter so far - below the sustained threshold
        };
        var context = MakeContext(new[] { route }, new[] { home, b }, new[] { BigAircraft }, Region.NorthAmerica, routeHistoryByPair: history);

        Assert.Equal(0.0, Strategy.Suitability(context));

        // No history entry at all (e.g. this quarter is the route's first) behaves the same way.
        var contextNoHistory = MakeContext(new[] { route }, new[] { home, b }, new[] { BigAircraft }, Region.NorthAmerica);
        Assert.Equal(0.0, Strategy.Suitability(contextNoHistory));
    }

    [Fact]
    public void Suitability_ThreeOrMoreContestedRoutes_IsOne()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 50);
        var d = MakeCity("d", Region.NorthAmerica, 3, 3, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 5, RangeKm: 2000); // tiny, comfortably profitable on its own
        var routes = new[]
        {
            new Route(home, b, aircraft, AircraftCount: 1),
            new Route(home, c, aircraft, AircraftCount: 1),
            new Route(home, d, aircraft, AircraftCount: 1),
        };
        var rivalCount = routes.ToDictionary(r => QuarterEngine.NormalizedPairKey(r.Origin, r.Destination), _ => 1);
        var context = MakeContext(routes, new[] { home, b, c, d }, new[] { aircraft }, Region.NorthAmerica, rivalCountByServedRoute: rivalCount);

        Assert.Equal(1.0, Strategy.Suitability(context));
    }

    [Fact]
    public void Suitability_HealthyUncontestedNetwork_IsZero()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var a = MakeCity("a", Region.NorthAmerica, 5, 5, 50);
        var aircraft = new AircraftType("t", "Test", Seats: 5, RangeKm: 2000); // tiny, comfortably profitable
        var route = new Route(home, a, aircraft, AircraftCount: 1);
        var context = MakeContext(new[] { route }, new[] { home, a }, new[] { aircraft }, Region.NorthAmerica);

        Assert.Equal(0.0, Strategy.Suitability(context));
    }

    [Fact]
    public void DecideOrders_TightBudget_MarketingClaimsTheBudget_OverANewPair()
    {
        // Cap 1: a struggling contested route AND a great new-pair
        // candidate both exist. Marketing runs first in this strategy
        // (the inversion from both other strategies), so the one action
        // goes to fixing what's already broken, not opening something new.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var strugglingPartner = MakeCity("strug", Region.NorthAmerica, 1, 1, 50);
        var newPairTarget = MakeCity("new", Region.NorthAmerica, 5, 5, 500); // high weight - would be an attractive new pair
        var cities = new[] { home, strugglingPartner, newPairTarget };
        var aircraft = new AircraftType("t", "Test", Seats: 5, RangeKm: 2000);
        var strugglingRoute = new Route(home, strugglingPartner, BigAircraft, AircraftCount: 1); // unprofitable
        var existing = new[] { strugglingRoute };
        var key = QuarterEngine.NormalizedPairKey(home, strugglingPartner);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned); // idle - could open the new pair instead

        var context = MakeContext(existing, cities, new[] { aircraft, BigAircraft }, Region.NorthAmerica, fleet, actionCap: 1,
            cash: Money.FromDollars(50_000_000_000), rivalCountByServedRoute: rivalCount);

        var orders = Strategy.DecideOrders(context);

        Assert.Single(orders.OfType<RouteMarketingOrder>());
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_TightBudget_GrowsExistingRoute_OverOpeningANewPair()
    {
        // Cap 1, nothing struggling (so marketing has no target this
        // quarter) - the remaining real choice is grow-existing vs.
        // new-pair, and this strategy prefers the former.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var served = MakeCity("served", Region.NorthAmerica, 1, 1, 500); // already served, high real demand so it's genuinely worth growing
        var newPairTarget = MakeCity("new", Region.NorthAmerica, 5, 5, 20); // low weight so it doesn't outrank growing the existing route
        var cities = new[] { home, served, newPairTarget };
        var aircraft = new AircraftType("t", "Test", Seats: 5, RangeKm: 2000);
        var existing = new[] { new Route(home, served, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned); // one idle unit

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "served" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_GrowExisting_NothingIdle_CashComfortable_OrdersInsteadOfLeasing()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var served = MakeCity("served", Region.NorthAmerica, 1, 1, 500);
        var cities = new[] { home, served };
        var aircraft = new AircraftType("t", "Test", Seats: 10, RangeKm: 2000, Cost: Money.FromDollars(1_000_000));
        var existing = new[] { new Route(home, served, aircraft, AircraftCount: 1) };
        // No idle fleet at all - the existing unit is already flying.

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, cash: Money.FromDollars(3_000_000));

        var orders = Strategy.DecideOrders(context);

        var order = Assert.Single(orders.OfType<AircraftOrder>());
        Assert.Equal("t", order.Type.Id);
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_GrowExisting_NothingIdle_CashNotComfortable_LeasesInstead()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var served = MakeCity("served", Region.NorthAmerica, 1, 1, 500);
        var cities = new[] { home, served };
        var aircraft = new AircraftType("t", "Test", Seats: 10, RangeKm: 2000, Cost: Money.FromDollars(1_000_000));
        var existing = new[] { new Route(home, served, aircraft, AircraftCount: 1) };

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, actionCap: 2); // no cash passed - not comfortable

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<AircraftOrder>());
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>());
        Assert.Equal("t", lease.Type.Id);
        Assert.Contains(orders.OfType<RouteOrder>(), o => new HashSet<string> { "home", "served" }.SetEquals(new[] { o.Origin.Id, o.Destination.Id }));
    }

    [Fact]
    public void DecideOrders_GrowExisting_PrefersUnledRegion_OverHigherLoadFactorLedOne()
    {
        // Win-condition-awareness follow-up: home-highLf (Europe, already
        // led) reads 100% real load factor (real numbers verified via the
        // scratchpad calculator, same fixture shape as the audit test
        // below: weight-200 city, profit $430,534.00 - real-profitable).
        // home-lowLf (EastAsia, not yet led) reads only 93.34% (weight-8
        // city, profit $489,735.48 - also real-profitable). Without the
        // region tier, highLf's own better load factor would win step 2
        // outright. NorthAmerica (home) and Europe are both already led
        // (regionsLed) - only EastAsia isn't - so the region tier picks
        // lowLf specifically, not merely "any foreign region" (both
        // routes touch a foreign region; only one touches an *unled*
        // one). lowLf's own route is built origin-first (lowLf, home),
        // not home-first like highLf's - covers
        // TouchesRegionNotYetLed's own "unled region is the Origin, not
        // just the Destination" branch directly, not just by inspection.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var highLf = MakeCity("highlf", Region.Europe, 1, 1, 200);
        var lowLf = MakeCity("lowlf", Region.EastAsia, 2, 2, 8);
        var cities = new[] { home, highLf, lowLf };
        var aircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var existing = new[]
        {
            new Route(home, highLf, aircraft, AircraftCount: 1),
            new Route(lowLf, home, aircraft, AircraftCount: 1),
        };
        var regionsLed = new HashSet<Region> { Region.NorthAmerica, Region.Europe };

        var context = MakeContext(existing, cities, Array.Empty<AircraftType>(), Region.NorthAmerica, actionCap: 2, regionsLed: regionsLed);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "lowlf" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_GrowExisting_AlreadyLeadsEverywhereTouched_UnchangedFromPlainLoadFactorRanking()
    {
        // Win-condition-awareness follow-up, the degenerate/no-op case:
        // both regions already led means the region tier can never
        // distinguish these two candidates, so the plain real-load-factor
        // ranking decides - exactly today's pre-existing behavior,
        // reproduced directly rather than only argued by inspection.
        // Same fixture as the test above, just with EastAsia also marked
        // led - highLf's own better load factor (100% vs. 93.34%) should
        // now win.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var highLf = MakeCity("highlf", Region.Europe, 1, 1, 200);
        var lowLf = MakeCity("lowlf", Region.EastAsia, 2, 2, 8);
        var cities = new[] { home, highLf, lowLf };
        var aircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var existing = new[]
        {
            new Route(home, highLf, aircraft, AircraftCount: 1),
            new Route(home, lowLf, aircraft, AircraftCount: 1),
        };
        var regionsLed = new HashSet<Region> { Region.NorthAmerica, Region.Europe, Region.EastAsia };

        var context = MakeContext(existing, cities, Array.Empty<AircraftType>(), Region.NorthAmerica, actionCap: 2, regionsLed: regionsLed);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "highlf" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_GrowExisting_SoloSaturatedButReallyUnprofitable_SkipsInFavorOfRealProfitableRoute()
    {
        // Existing-route-evaluation audit follow-up: home-bad reads as
        // fully sold under RouteEvaluator's own solo estimate (100% load
        // factor, real numbers verified via the scratchpad calculator:
        // capacity 75,400 against a real gravity demand of 6,066,153) -
        // under the old, solo-only ranking this is the unambiguous growth
        // winner. But its own real, competitively-resolved carried figure
        // (LastQuarterSeatsCarriedByRoute, set here to 10% of capacity)
        // makes it a real loser (profit -$1,249,001.00). home-good has a
        // lower solo load factor (93.34%, no real-data override - real
        // equals solo) but is genuinely profitable (+$489,735.48) at that
        // real figure. actionCap 2 lets the lease+route fallback (nothing
        // idle) fully claim the budget on whichever pair step 2 picks,
        // so no NegotiationOrder or step-3 new-pair order can appear
        // afterward regardless of what candidates exist elsewhere.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var bad = MakeCity("bad", Region.NorthAmerica, 1, 1, 200);
        var good = MakeCity("good", Region.NorthAmerica, 2, 2, 8);
        var cities = new[] { home, bad, good };
        var aircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var existing = new[]
        {
            new Route(home, bad, aircraft, AircraftCount: 1),
            new Route(home, good, aircraft, AircraftCount: 1),
        };
        var badRoute = existing[0];
        var realCarried = new System.Collections.Generic.Dictionary<(string, string), long>
        {
            [QuarterEngine.NormalizedPairKey(home, bad)] = badRoute.SeatsPerQuarter / 10,
        };

        var context = MakeContext(existing, cities, Array.Empty<AircraftType>(), Region.NorthAmerica, actionCap: 2, lastQuarterSeatsCarriedByRoute: realCarried);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "good" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
        Assert.Single(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_NewPair_NothingIdle_ProfitableEvenWithLeaseFee_LeasesRegardlessOfCash()
    {
        // NetworkOptimizerStrategy follow-up: step 3 now reuses
        // AiToolkit.FindMostProfitablePair/TryProposeCandidate directly -
        // no cash-comfort multiplier and no cash gate on leasing at all
        // (leasing has no upfront cost, per CLAUDE.md), only the real
        // profitability floor. Pre-verified: this $1M aircraft's own real
        // profit including the lease fee is $67,030.60 (100% load factor,
        // distance 157km) - comfortably clears the floor, so it leases
        // even with null cash, unlike the old 6x-cash-comfort design this
        // replaced.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 10, RangeKm: 2000, Cost: Money.FromDollars(1_000_000));

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica); // no idle fleet, no cash passed

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<AircraftOrder>());
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>());
        Assert.Equal("t", lease.Type.Id);
        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "near" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_NewPair_LeaseFeeMakesItUnprofitable_NoOp()
    {
        // Real cost accounting, not a cash-comfort proxy: this $50M
        // aircraft's own real profit including the lease fee is
        // pre-verified negative (-$1,892,992.40, same 100%-load-factor
        // pair as above) - step 3 proposes nothing at all, the same
        // "genuinely too weak to serve profitably" outcome
        // BalancedGrowthStrategy's own equivalent fixture establishes.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 10, RangeKm: 2000, Cost: Money.FromDollars(50_000_000));

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, cash: Money.FromDollars(500_000_000));

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<AircraftOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_NewPair_CompetitionDerates_RejectsOnceRivalsTipItNegative()
    {
        // Real numbers: a 1000-seat aircraft on this pair is profitable
        // uncontested and even at 3 rivals (100%/90.5% load factor,
        // +$3,634,280.00/+$2,140,148.96), but 4 rivals derates assumed
        // captured demand (raw/(rivals+1)) below the ~77% breakeven
        // (72.4% load factor, -$709,741.36) - the same real, previously
        // impossible visibility BalancedGrowthStrategy's own
        // RivalPresenceByPairKey established, now reachable through
        // NetworkOptimizerStrategy's own domestic search too.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var mid = new AircraftType("mid", "Mid", Seats: 1000, RangeKm: 2000, CostPerAvailableSeatKm: 0.1);
        var rivalPresence = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, near)] = 4 };

        var context = new StrategyContext(Array.Empty<Route>(), ActionCap: 3, cities, DemandModel, new[] { mid }, AirlineFleet.Empty,
            Region.NorthAmerica, home, RivalPresenceByPairKey: rivalPresence);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<AircraftOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_BudgetRemains_StillOpensANewPair()
    {
        // Sparingly, not never: with nothing struggling, nothing to grow,
        // and real budget left over, this strategy still opens a new pair -
        // the same domestic search DomesticProfitOptimizerStrategy uses.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var target = MakeCity("target", Region.NorthAmerica, 5, 5, 100);
        var cities = new[] { home, target };
        var aircraft = new AircraftType("t", "Test", Seats: 5, RangeKm: 2000, CruiseSpeedKmh: 400, CostPerAvailableSeatKm: 0.15);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "target" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_NeverOpensACrossRegionPair()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var foreign = MakeCity("frn", Region.Europe, 2, 2, 500); // high weight - would win if cross-region were allowed
        var cities = new[] { home, foreign };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_NeverNegotiatesForACrossRegionGateway()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var foreignGateway = MakeCity("fgw", Region.Europe, 2, 2, 500, isGateway: true);
        var cities = new[] { home, foreignGateway };

        var context = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica, cash: Money.FromDollars(10_000_000));

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_AtEveryStep_StillAppliesTheSharedManagementToolkit()
    {
        // A saturated route (>= BaselineAi.SaturatedLoadFactorCeiling)
        // gets its fare raised via ApplyManagementToolkit, the same free
        // action every strategy applies regardless of what else ran.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 200);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 200);
        var tinyAircraft = new AircraftType("tiny", "Tiny", Seats: 1, RangeKm: 2000); // real demand vastly exceeds capacity - saturated
        var route = new Route(home, near, tinyAircraft, AircraftCount: 1);
        var context = MakeContext(new[] { route }, new[] { home, near }, new[] { tinyAircraft }, Region.NorthAmerica, actionCap: 0);

        var orders = Strategy.DecideOrders(context);

        var fareOrder = Assert.Single(orders.OfType<SetFareOrder>());
        Assert.Equal(new HashSet<string> { "home", "near" }, new HashSet<string> { fareOrder.Origin.Id, fareOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_SustainedUncontestedSingleAircraftRoute_ClosesIt()
    {
        // NetworkOptimizerStrategy follow-up: confirms
        // enableSustainedSingleAircraftClosure: true actually reaches
        // ApplyManagementToolkit through a real DecideOrders call, not
        // just a direct unit test of that method in isolation.
        // BigAircraft's huge capacity against tiny real demand guarantees
        // a real negative FareCalculator.EstimatedProfit reading.
        // quarterNumber: 100 - well past GameEras.MidGameStartQuarter (41)
        // + the 8-quarter threshold, so AiToolkit's own quarters-since-
        // MidGame cap (see its doc comment) doesn't defeat this case.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(home, b, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, b)] = 0 };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(home, b)] = Sustained(20),
        };
        var context = MakeContext(new[] { route }, new[] { home, b }, new[] { BigAircraft }, Region.NorthAmerica,
            actionCap: 0, rivalCountByServedRoute: rivalCount, routeHistoryByPair: history, quarterNumber: 100);

        var orders = Strategy.DecideOrders(context);

        var closeOrder = Assert.Single(orders.OfType<CloseRouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "b" }, new HashSet<string> { closeOrder.Origin.Id, closeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_GrosslyMismatchedRoute_SwapsAircraftType()
    {
        // AI-type-swap follow-up (docs/AI_AIRCRAFT_TYPE_SWAP_DESIGN.md):
        // confirms enableAircraftTypeSwap: true actually reaches
        // ApplyManagementToolkit through a real DecideOrders call, the
        // same "not just a direct unit test in isolation" bar the
        // sustained-closure test above already holds itself to.
        // ConsecutiveUnprofitableQuarters: 1 (not 20) keeps this route
        // well under SustainedSingleAircraftClosureThreshold, so closure
        // never competes with the swap for this route.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var small = new AircraftType("t", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var route = new Route(home, b, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, b)] = 0 };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(home, b)] = Sustained(1),
        };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, small, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { route }, new[] { home, b }, new[] { BigAircraft, small }, Region.NorthAmerica, fleet,
            actionCap: 3, rivalCountByServedRoute: rivalCount, routeHistoryByPair: history, quarterNumber: 100);

        var orders = Strategy.DecideOrders(context);

        var adjust = Assert.Single(orders.OfType<AdjustRouteOrder>());
        Assert.Equal(small, adjust.AircraftType);
        Assert.Empty(orders.OfType<CloseRouteOrder>());
    }
}
