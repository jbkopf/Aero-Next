using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class RapidExpansionStrategyTests
{
    private static City MakeCity(string id, Region region, double lat, double lon, int weight, bool isGateway = false, int? autoCapacity = null) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, isGateway, isGateway ? 3 : null, isGateway ? null : autoCapacity);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly RapidExpansionStrategy Strategy = new();

    // M44: homeCity defaults to cities[0] - every existing fixture in this
    // file already lists its own "home" city first, matching the pattern
    // MakeContext's own callers were already written against; explicit
    // override only where a test specifically needs a different one.
    private static StrategyContext MakeContext(
        IReadOnlyList<Route> routes, IReadOnlyList<City> cities, IReadOnlyList<AircraftType> eraTypes, Region homeRegion,
        AirlineFleet? fleet = null, int actionCap = 3, Money? cash = null, City? homeCity = null,
        int quarterNumber = 1, System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), AiRouteHistory>? routeHistoryByPair = null,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), long>? lastQuarterSeatsCarriedByRoute = null,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), int>? rivalCountByServedRoute = null) =>
        new(routes, actionCap, cities, DemandModel, eraTypes, fleet ?? AirlineFleet.Empty, homeRegion, HomeCity: homeCity ?? cities[0], AvailableCash: cash,
            QuarterNumber: quarterNumber, RouteHistoryByPair: routeHistoryByPair, LastQuarterSeatsCarriedByRoute: lastQuarterSeatsCarriedByRoute,
            RivalCountByServedRoute: rivalCountByServedRoute);

    [Fact]
    public void DecideOrders_TightBudget_PrefersOpeningANewPair_OverGrowingAnExistingRoute()
    {
        // Cap 1: only one of the two candidate actions fits. If new-pair
        // genuinely runs before grow-existing (the inversion from
        // BaselineAi's own order), the one RouteOrder targets the new
        // pair, not the already-served one.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 5); // already served - low weight so it doesn't make an attractive *second* leg for other candidates
        var far = MakeCity("far", Region.NorthAmerica, 5, 5, 30); // new-pair candidate - weight clears MinLoadFactorForNewPair's own floor (~56% LF at this weight, comfortably above 40%)
        var cities = new[] { home, near, far };
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned); // one idle unit - enough for exactly one growth action

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        // The key claim: it opened something new, not grown the existing
        // (home, near) route - proving new-pair ran first and claimed
        // the cap-1 budget, exactly the inversion from BaselineAi's own
        // order.
        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.NotEqual(new HashSet<string> { "home", "near" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
        Assert.Contains("far", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_NewPair_RecentlyClosedPair_NeverReopened()
    {
        // M55: this strategy's own demand-only new-pair ranking (no
        // profit awareness, unlike DomesticProfitOptimizerStrategy) was
        // M54's own named "known, accepted risk" - without the cooldown,
        // it could immediately re-propose a pair the shared toolkit just
        // closed for real, structural reasons. The only candidate pair
        // here is recently closed, so step 1 should produce nothing.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var far = MakeCity("far", Region.NorthAmerica, 5, 5, 20);
        var cities = new[] { home, far };
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var key = QuarterEngine.NormalizedPairKey(home, far);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory> { [key] = new(LastShrunkQuarter: null, LastClosedQuarter: 9, ConsecutiveUnprofitableQuarters: 0) };

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, fleet, quarterNumber: 10, routeHistoryByPair: history);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<AircraftOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_PrefersACrossRegionCandidate_OverAHigherDemandIntraRegionOne()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var domestic = MakeCity("dom", Region.NorthAmerica, 1, 1, 500); // higher raw demand
        var foreign = MakeCity("frn", Region.Europe, 2, 2, 20); // lower raw demand, but cross-region
        var cities = new[] { home, domestic, foreign };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        // "dom" and "frn" also form a valid (still cross-region) pair, so
        // the exact winner isn't pinned down to (home, frn) specifically
        // - the real claim is that whichever pair wins, it touches the
        // foreign city, never the higher-weight purely-domestic option.
        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Contains("frn", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    // --- M64: route-opening consolidation bonus ---

    [Fact]
    public void DecideOrders_ConsolidationBonus_PrefersPairTouchingExistingNetwork_WhenDemandGapIsModest()
    {
        // Numerically pre-verified: existing home-hub, hub-away gives hub
        // a route-count-share of 1.0 (touches both), home only 0.5. candA
        // (touches hub) has real, modestly *lower* raw demand than candB
        // (touches home) - 5,460,924 vs 6,065,845 - but hub's bigger
        // consolidation multiplier (1.5x vs home's 1.25x) flips the
        // ranking: candA's boosted score (8,191,386) beats candB's
        // (7,582,306). Both candidates are intra-region, so IsCrossRegion
        // can't be the tiebreak here - this isolates the consolidation
        // bonus itself.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var hub = MakeCity("hub", Region.NorthAmerica, 1, 0, 100);
        var away = MakeCity("away", Region.NorthAmerica, 2, 0, 60);
        var candA = MakeCity("canda", Region.NorthAmerica, 1, 1, 90); // touches hub - share 1.0
        var candB = MakeCity("candb", Region.NorthAmerica, 0, 1, 100); // touches home - share 0.5, modestly higher raw demand
        var cities = new[] { home, hub, away, candA, candB };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var existing = new[] { new Route(home, hub, aircraft, AircraftCount: 1), new Route(hub, away, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "hub", "canda" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_ConsolidationBonus_DoesNotOverrideMuchLargerUnrelatedPair()
    {
        // Same fixture as the modest-gap test above, except candB's own
        // weight is pushed much higher (400, not 100) - its raw demand
        // (24,263,381) is now so far ahead of candA's (5,460,924) that
        // even candA's full 1.5x consolidation multiplier
        // (8,191,386) can't come close to candB's own 1.25x-boosted
        // figure (30,329,226). The bonus tilts a genuinely close choice;
        // it doesn't make a small pair look bigger than a dramatically
        // larger, unrelated one.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var hub = MakeCity("hub", Region.NorthAmerica, 1, 0, 100);
        var away = MakeCity("away", Region.NorthAmerica, 2, 0, 60);
        var candA = MakeCity("canda", Region.NorthAmerica, 1, 1, 90);
        var candB = MakeCity("candb", Region.NorthAmerica, 0, 1, 400);
        var cities = new[] { home, hub, away, candA, candB };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var existing = new[] { new Route(home, hub, aircraft, AircraftCount: 1), new Route(hub, away, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "candb" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_CrossRegionTier_StillBeatsConsolidationBonus()
    {
        // The consolidation bonus only ever tilts *within* the
        // cross-region-first tier - it can't cross it. "domestic" touches
        // the existing hub (maximal 1.5x consolidation multiplier) but is
        // intra-region; "frn" is a fresh, unconnected, cross-region
        // candidate with much lower raw demand. Cross-region still wins,
        // matching DecideOrders_PrefersACrossRegionCandidate_OverAHigherDemandIntraRegionOne's
        // own established pattern - now proven robust to the new bonus too.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var hub = MakeCity("hub", Region.NorthAmerica, 1, 0, 100);
        var domestic = MakeCity("domestic", Region.NorthAmerica, 1, 1, 500); // touches hub, huge raw demand, maximal consolidation - still intra-region
        var foreign = MakeCity("frn", Region.Europe, 2, 2, 20); // cross-region, no consolidation, much lower raw demand
        var cities = new[] { home, hub, domestic, foreign };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var existing = new[] { new Route(home, hub, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Contains("frn", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_NegotiatesForACrossRegionGateway_OverAHigherDemandDomesticOne()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var domesticGateway = MakeCity("dgw", Region.NorthAmerica, 1, 1, 500, isGateway: true);
        var foreignGateway = MakeCity("fgw", Region.Europe, 2, 2, 20, isGateway: true);
        var cities = new[] { home, domesticGateway, foreignGateway };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000); // gateway-relevance follow-up: era aircraft must be able to reach the candidates

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, cash: Money.FromDollars(10_000_000));

        var orders = Strategy.DecideOrders(context);

        var negotiation = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("fgw", negotiation.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_UnreachableHigherDemandGateway_LosesToAReachableLowerDemandOne()
    {
        // Gateway-relevance follow-up (docs/OPEN_WORK.md's filler-seat
        // investigation): both are cross-region, so the old ranking
        // (cross-region first, then demand weight) would have picked the
        // far one purely for its higher weight - exactly the "bid on
        // literally any gateway on Earth" behavior that was traced
        // directly to a real bankruptcy. Now it must be reachable first.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.Europe, 0, 10, 20, isGateway: true); // ~1,100km - reachable
        var far = MakeCity("far", Region.Europe, 0, 100, 90, isGateway: true); // ~11,100km - not reachable
        var cities = new[] { home, near, far };
        var shortRangeAircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 3_000);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { shortRangeAircraft }, Region.NorthAmerica, cash: Money.FromDollars(10_000_000));

        var orders = Strategy.DecideOrders(context);

        var negotiation = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("near", negotiation.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_SkipsGatewayWithAPendingNegotiation_TargetsNextBestInstead()
    {
        // "best" would normally win (cross-region, higher demand weight -
        // same ranking shape as DecideOrders_NegotiatesForACrossRegionGateway...
        // above), but a negotiation is already pending there, so it's
        // skipped in favor of "next" - proving the AI stops re-bidding a
        // city it's already spent real money pursuing (M41).
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var best = MakeCity("best", Region.Europe, 2, 2, 90, isGateway: true);
        var next = MakeCity("next", Region.Europe, 3, 3, 50, isGateway: true);
        var cities = new[] { home, best, next };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000); // gateway-relevance follow-up: era aircraft must be able to reach the candidates

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, cities, DemandModel, new[] { aircraft }, AirlineFleet.Empty, Region.NorthAmerica, HomeCity: home,
            AvailableCash: Money.FromDollars(10_000_000),
            PendingNegotiationCityIds: new HashSet<string> { "best" });

        var orders = Strategy.DecideOrders(context);

        var negotiation = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("next", negotiation.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_SkipsGatewayWithAnAlreadyHeldUnusedSlot_TargetsNextBestInstead()
    {
        // Same shape, but the exclusion signal is an already-won,
        // not-yet-used slot (AvailableSlotsByCity) rather than a pending
        // bid - no reason to spend another $2M bidding for more of what's
        // already held and unused.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var best = MakeCity("best", Region.Europe, 2, 2, 90, isGateway: true);
        var next = MakeCity("next", Region.Europe, 3, 3, 50, isGateway: true);
        var cities = new[] { home, best, next };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000); // gateway-relevance follow-up: era aircraft must be able to reach the candidates

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, cities, DemandModel, new[] { aircraft }, AirlineFleet.Empty, Region.NorthAmerica, HomeCity: home,
            AvailableSlotsByCity: new Dictionary<string, int> { ["best"] = 1 },
            AvailableCash: Money.FromDollars(10_000_000));

        var orders = Strategy.DecideOrders(context);

        var negotiation = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("next", negotiation.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_NoRouteCap_KeepsGrowingWellBeyondFiveRoutes()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var partners = Enumerable.Range(0, 6)
            .Select(i => MakeCity($"p{i}", Region.NorthAmerica, i + 1, i + 1, 50))
            .ToList();
        var cities = new[] { home }.Concat(partners).ToList();
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var existing = partners.Take(5).Select(p => new Route(home, p, aircraft, AircraftCount: 1)).ToList(); // already at DomesticProfitOptimizer's own cap
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        // The 6th partner (p5) is still a legal target - no cap here.
        // (The grow-existing step also fires, via a fresh lease, once
        // new-pair has claimed the one idle unit - correct "no
        // double-booking" behavior, not asserted on here directly; see
        // the dedicated QuarterEngine integration test below for that.)
        Assert.Contains(orders.OfType<RouteOrder>(), o => new[] { o.Origin.Id, o.Destination.Id }.Contains("p5"));
    }

    [Fact]
    public void Suitability_HigherWhenCashIsHigher()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var cities = new[] { home };
        var richContext = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica, cash: Money.FromDollars(20_000_000));
        var poorContext = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica, cash: Money.FromDollars(2_000_000));

        Assert.True(Strategy.Suitability(richContext) > Strategy.Suitability(poorContext));
    }

    [Fact]
    public void ApplicableEras_CoversOnlyStarting()
    {
        Assert.Equal(new[] { GameEra.Starting }, Strategy.ApplicableEras);
    }

    [Fact]
    public void DecideOrders_NewPairAndGrowExisting_BothFireTheSameQuarter_NeitherDoubleBooksTheIdleUnit()
    {
        // The real risk the inverted order creates (see the class doc
        // comment): with exactly one idle unit, both steps could pick
        // the same one if reservedUnitIds isn't threaded through
        // correctly. Proven end to end through QuarterEngine, not just
        // by inspecting the order list - zero rejections is the real
        // guarantee.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var far = MakeCity("far", Region.NorthAmerica, 5, 5, 20);
        var cities = new[] { home, near, far };
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };
        var withUnits = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 2, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.Assign(withUnits, withUnits.Units[0].Id, "home-near"); // 1 assigned to the existing route, 1 genuinely idle

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet, actionCap: QuarterEngine.BaseActionCap);

        var orders = Strategy.DecideOrders(context);
        var result = QuarterEngine.RunQuarter(1, existing, context.ActionCap, orders, cities, DemandModel, fleet);

        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void DecideOrders_ProducesLegalOrders_WhenRunThroughQuarterEngine()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, fleet, cash: Money.FromDollars(10_000_000));

        var orders = Strategy.DecideOrders(context);
        var result = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), context.ActionCap, orders, cities, DemandModel, fleet, availableCash: Money.FromDollars(10_000_000));

        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void DecideOrders_NewPair_NothingIdle_CashComfortable_OrdersInsteadOfLeasing()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 10, Cost: Money.FromDollars(1_000_000));

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, cash: Money.FromDollars(2_000_000)); // 2x RapidExpansion's own comfort multiplier

        var orders = Strategy.DecideOrders(context);

        var order = Assert.Single(orders.OfType<AircraftOrder>());
        Assert.Equal("t", order.Type.Id);
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_NewPair_NothingIdle_CashNotComfortable_LeasesInstead()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 10, Cost: Money.FromDollars(1_000_000));

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica); // no idle fleet, no cash passed

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<AircraftOrder>());
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>());
        Assert.Equal("t", lease.Type.Id);
        Assert.Single(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_GrowExisting_NothingIdleAndNoNewPairAvailable_CashComfortable_OrdersInsteadOfLeasing()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near }; // no third city - new-pair step finds nothing, falls through to grow-existing
        var aircraft = new AircraftType("t", "Test", Seats: 10, Cost: Money.FromDollars(1_000_000));
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, cash: Money.FromDollars(2_000_000));

        var orders = Strategy.DecideOrders(context);

        var order = Assert.Single(orders.OfType<AircraftOrder>());
        Assert.Equal("t", order.Type.Id);
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_GrowExisting_NothingIdleAndNoNewPairAvailable_CashNotComfortable_LeasesInstead()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 10, Cost: Money.FromDollars(1_000_000));
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<AircraftOrder>());
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>());
        Assert.Equal("t", lease.Type.Id);
        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "near" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_GrowExisting_IdleUnitAvailable_UsesItDirectly()
    {
        // Step 2's own success path (idleUnit is not null) was never
        // exercised - every prior grow-existing test had nothing idle.
        // Only two cities, so step 1 finds no unserved pair and leaves
        // reservedUnitIds empty, letting step 2's idle unit through.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned); // idle, not assigned

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "near" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
        Assert.Empty(orders.OfType<AircraftOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_GrowExisting_PicksHigherDemandRoute_NotHigherLoadFactorOne()
    {
        // Network-profitability follow-up: home-near is capacity-matched
        // to smallAircraft (100% load factor, real numbers verified via
        // the scratchpad calculator: demand 151,654 against a 7,540-seat
        // capacity) but has small absolute demand. home-far pairs a much
        // higher-weight city with a bigger aircraft whose capacity
        // outstrips even its own much larger demand (69.21% load factor,
        // demand 971,770 - over 6x home-near's) - the old real-load-factor
        // ranking would have picked home-near; the new demand-based one
        // picks home-far instead. EraAvailableTypes empty and no idle
        // fleet neutralize step 1 entirely (no idle unit, no fallback
        // aircraft to lease/order with), and no cash forces the lease+route
        // fallback so the winning pair is directly visible as a RouteOrder.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 5);
        var far = MakeCity("far", Region.NorthAmerica, 5, 5, 800);
        var cities = new[] { home, near, far };
        var smallAircraft = new AircraftType("small", "Small", Seats: 10, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var bigAircraft = new AircraftType("big", "Big", Seats: 3000, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var existing = new[]
        {
            new Route(home, near, smallAircraft, AircraftCount: 1),
            new Route(home, far, bigAircraft, AircraftCount: 1),
        };

        var context = MakeContext(existing, cities, Array.Empty<AircraftType>(), Region.NorthAmerica, actionCap: 2);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "far" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
        Assert.Single(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_GrowExisting_NetworkInRealTrouble_SkipsGrowthButStillOpensNewPair()
    {
        // Network-profitability follow-up: a served, contested route
        // makes AiToolkit.NetworkTroubleRatio read real trouble (> 0.0),
        // which should suppress step 2 (grow existing) entirely - but
        // leave step 1 (new pair, this strategy's actual cross-region
        // mission) running at full strength. "far" is a fresh, reachable,
        // cross-region candidate step 1 should still open even though
        // step 2's own target ("near") is left alone.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var far = MakeCity("far", Region.Europe, 5, 5, 20);
        var cities = new[] { home, near, far };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned); // one idle unit - step 1 claims it
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, near)] = 1 };

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet, rivalCountByServedRoute: rivalCount);

        var orders = Strategy.DecideOrders(context);

        // Contains, not an exact pair - "near" also validly originates a
        // route to "far" (both network-connected), so the real claim is
        // just that step 1 reached the new region; which of home/near
        // supplies the other end isn't what this test exists to isolate.
        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Contains("far", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_GrowExisting_RecentlyShrunkRoute_NeverPickedAgain()
    {
        // M55: same fixture as DecideOrders_GrowExisting_IdleUnitAvailable_UsesItDirectly,
        // but the only existing route was shrunk last quarter - the real
        // fix for the oscillation M43 traced (shrinking a route
        // mechanically raises its own load factor, so without this it
        // could rank "best performing" again the very next quarter and
        // get grown right back). No other candidate exists, so growth
        // step 2 should produce nothing at all.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var key = QuarterEngine.NormalizedPairKey(home, near);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory> { [key] = new(LastShrunkQuarter: 9, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 0) };

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet, quarterNumber: 10, routeHistoryByPair: history);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<AircraftOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_MultipleIdleAircraftTypes_PicksTheOneThatCanReachTheBestPair()
    {
        // Step 1's idle-unit lookup filters `u.Type.Id == aircraft.Id`
        // across every idle unit, not just ones of the winning type -
        // needs a genuinely mixed idle fleet to exercise both outcomes of
        // that equality check.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var far = MakeCity("far", Region.NorthAmerica, 40, 40, 400); // long distance; weight clears MinLoadFactorForNewPair's own floor (~57% LF for "long" at this weight, comfortably above 40%)
        var cities = new[] { home, far };
        var shortRange = new AircraftType("short", "Short", Seats: 140, RangeKm: 100); // can't reach "far"
        var longRange = new AircraftType("long", "Long", Seats: 140, RangeKm: 10_000);
        var withShort = FleetLedger.ApplyDelivery(AirlineFleet.Empty, shortRange, quantity: 1, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.ApplyDelivery(withShort, longRange, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { shortRange, longRange }, Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal("long", routeOrder.Aircraft.Id);
    }

    // --- MinLoadFactorForNewPair (docs/OPEN_WORK.md's capacity-mismatch follow-up) ---

    [Fact]
    public void DecideOrders_IdleAircraftGrosslyOversizedForTheOnlyReachablePair_SkipsRatherThanCommitting()
    {
        // Real, traced failure mode: an idle aircraft reused for whatever
        // pair has the highest reachable raw demand, even when that
        // aircraft's own capacity dwarfs it (26% load factor here) - a
        // structural loss from the quarter it opens, since operating cost
        // is charged on capacity offered, not seats filled. The fix: skip
        // opening anything this quarter rather than commit to it. No
        // era-available types either, so the buy-new fallback can't paper
        // over the same gap.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var dest = MakeCity("dest", Region.NorthAmerica, 5, 5, 30); // 26.0% load factor for "big"
        var cities = new[] { home, dest };
        var big = new AircraftType("big", "Big", Seats: 300);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, big, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<AircraftOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_IdleFleetHasAnOversizedAndAWellFittedType_PicksTheWellFittedOne()
    {
        // The positive case: with both an oversized idle unit (26% load
        // factor, filtered) and a well-fitted one (100% load factor, the
        // same pair fully saturates its own smaller capacity) idle at
        // once, the strategy picks the one that can actually fill the
        // route - not whichever happens to be bigger.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var dest = MakeCity("dest", Region.NorthAmerica, 5, 5, 30); // big: 26.0% LF (filtered); small: 100.0% LF
        var cities = new[] { home, dest };
        var big = new AircraftType("big", "Big", Seats: 300);
        var small = new AircraftType("small", "Small", Seats: 40);
        var withBig = FleetLedger.ApplyDelivery(AirlineFleet.Empty, big, quantity: 1, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.ApplyDelivery(withBig, small, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { big, small }, Region.NorthAmerica, fleet, actionCap: 1);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal("small", routeOrder.Aircraft.Id);
    }

    [Fact]
    public void DecideOrders_GrowExisting_MixedIdleFleet_SkipsUnitsOfTheWrongType()
    {
        // Step 2's own idle-unit lookup filters `u.Type.Id ==
        // bestExisting.Aircraft.Id` the same way step 1's does - needs a
        // genuinely mixed idle fleet (not just the single matching-type
        // unit every other grow-existing test uses) to exercise the
        // "wrong type, skip it" outcome, not just "matching type, take it".
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near }; // no unserved pair - step 1 reserves nothing
        var routeType = new AircraftType("t", "Test", Seats: 140);
        var otherType = new AircraftType("other", "Other", Seats: 140);
        var existing = new[] { new Route(home, near, routeType, AircraftCount: 1) };
        var withOther = FleetLedger.ApplyDelivery(AirlineFleet.Empty, otherType, quantity: 1, FleetOwnershipMode.Owned); // wrong type - must be skipped
        var fleet = FleetLedger.ApplyDelivery(withOther, routeType, quantity: 1, FleetOwnershipMode.Owned); // matching type - must be taken

        var context = MakeContext(existing, cities, new[] { routeType, otherType }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal("t", routeOrder.Aircraft.Id);
        Assert.Empty(orders.OfType<AircraftOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_CashShortOfBid_CrossRegionTarget_CeilingHasRoom_Borrows()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var foreignGateway = MakeCity("fgw", Region.Europe, 30, 30, 90, isGateway: true);
        var cities = new[] { home, foreignGateway };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000); // gateway-relevance follow-up: era aircraft must be able to reach the candidates

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, cities, DemandModel, new[] { aircraft }, AirlineFleet.Empty, Region.NorthAmerica, HomeCity: home,
            AvailableCash: Money.FromDollars(500_000), CurrentDebtBalance: Money.Zero);

        var orders = Strategy.DecideOrders(context);

        var refinance = Assert.Single(orders.OfType<RefinanceOrder>());
        Assert.Equal(Money.FromDollars(1_500_000), refinance.AdditionalBorrowing);
        Assert.Empty(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_HoldsAnUnusedWonSlot_UsesItInsteadOfChasingAHigherDemandCandidate()
    {
        // M43 (regional balance): a gateway this airline already holds a
        // slot at - won by a past negotiation, real cash already spent -
        // takes priority over a fresh, even higher-raw-demand candidate.
        // Diagnosed directly from a traced real game: an airline won 6
        // gateway negotiations across 11 quarters and used none of them,
        // burning cash on repeat bids while a route it could open for
        // free (the slot's already paid for) sat unused.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var wonGateway = MakeCity("won", Region.Europe, 1, 1, 20, isGateway: true); // lower raw demand, but a slot is already held here
        var distractor = MakeCity("hi", Region.Europe, 2, 2, 500); // far higher raw demand, but no slot held
        var cities = new[] { home, wonGateway, distractor };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, cities, DemandModel, new[] { aircraft }, fleet, Region.NorthAmerica, HomeCity: home,
            AvailableSlotsByCity: new Dictionary<string, int> { ["won"] = 1 });

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Contains("won", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
        Assert.DoesNotContain("hi", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_WonSlotAlreadyServed_FallsBackToNormalSearch()
    {
        // The priority only applies to a slot not yet built out - a
        // gateway already touched by an existing route doesn't need
        // this boost (the existing "grow best-performing route" step
        // already covers it), so the normal cross-region search runs
        // unaffected.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var wonGateway = MakeCity("won", Region.Europe, 1, 1, 20, isGateway: true);
        var target = MakeCity("hi", Region.Europe, 2, 2, 500);
        var cities = new[] { home, wonGateway, target };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var existing = new[] { new Route(home, wonGateway, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = new StrategyContext(
            existing, 3, cities, DemandModel, new[] { aircraft }, fleet, Region.NorthAmerica, HomeCity: home,
            AvailableSlotsByCity: new Dictionary<string, int> { ["won"] = 1 });

        var orders = Strategy.DecideOrders(context);

        // The won-but-already-served gateway doesn't block the normal
        // new-pair search from picking "hi" - whether the existing
        // home-won route also gets grown this same quarter is a
        // separate, unrelated decision (BaselineAi.FindBestPerformingRoute's
        // own floor), not what this test is checking.
        var routeOrders = orders.OfType<RouteOrder>().ToList();
        Assert.Contains(routeOrders, o => new[] { o.Origin.Id, o.Destination.Id }.Contains("hi"));
    }

    [Fact]
    public void DecideOrders_UnusedWonSlot_ExistingUnrelatedRoute_StillPrioritizesTheWonSlot()
    {
        // Same priority as DecideOrders_HoldsAnUnusedWonSlot..., but with
        // a real, already-served route elsewhere in the network - proves
        // the won-slot search correctly excludes only pairs that are
        // themselves already served, not every pair touching any served
        // city, when the network isn't empty.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var domestic = MakeCity("dom", Region.NorthAmerica, 1, 1, 30); // already served from home - unrelated to the won slot
        var wonGateway = MakeCity("won", Region.Europe, 10, 10, 20, isGateway: true);
        var distractor = MakeCity("hi", Region.Europe, 11, 11, 500);
        var cities = new[] { home, domestic, wonGateway, distractor };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var existing = new[] { new Route(home, domestic, aircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 2, FleetOwnershipMode.Owned); // one already flying home-dom, one idle

        var context = new StrategyContext(
            existing, 3, cities, DemandModel, new[] { aircraft }, fleet, Region.NorthAmerica, HomeCity: home,
            AvailableSlotsByCity: new Dictionary<string, int> { ["won"] = 1 });

        var orders = Strategy.DecideOrders(context);

        // The won slot gets used - via whichever reachable pair touching
        // it has the strongest demand (here, hi-won beats home-won), not
        // necessarily starting from home. That's correct: the priority
        // is "use the slot," not "use it specifically from home."
        var routeOrders = orders.OfType<RouteOrder>().ToList();
        Assert.Contains(routeOrders, o => new[] { o.Origin.Id, o.Destination.Id }.Contains("won"));
    }

    [Fact]
    public void DecideOrders_WonSlotWithZeroBalance_NotTreatedAsUsable()
    {
        // A city can appear in AvailableSlotsByCity with a zero balance
        // (every slot already spent on other routes) - that's a real,
        // distinct state from "never held one at all," and shouldn't be
        // treated as a usable won-but-unused slot. "bbb" sorts before
        // "home" (unlike the other won-slot tests above, where the won
        // city always sorted after its partner), so this also exercises
        // the origin-side half of the "touches a won city" OR match.
        var aaa = MakeCity("aaa", Region.Europe, 1, 1, 20, isGateway: true); // zero balance - not usable
        var bbb = MakeCity("bbb", Region.Europe, 2, 2, 15, isGateway: true); // real, unused slot - sorts before "home"
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var cities = new[] { aaa, bbb, home };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, cities, DemandModel, new[] { aircraft }, fleet, Region.NorthAmerica, HomeCity: home,
            AvailableSlotsByCity: new Dictionary<string, int> { ["aaa"] = 0, ["bbb"] = 1 });

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Contains("bbb", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
        Assert.DoesNotContain("aaa", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    // --- M45: non-gateway automatic capacity ---

    [Fact]
    public void DecideOrders_NewPairTargetHasZeroCityWideCapacity_NeverProposed()
    {
        // "far" is the only reachable candidate (short range excludes
        // everything else), but its own AutoCapacity balance is spent -
        // the strategy must never propose what QuarterEngine would reject.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var far = MakeCity("far", Region.NorthAmerica, 1, 1, 90, autoCapacity: 5);
        var cities = new[] { home, far };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 5_000);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, cities, DemandModel, new[] { aircraft }, fleet, Region.NorthAmerica, HomeCity: home,
            CityWideSlotsRemaining: new Dictionary<string, int> { ["far"] = 0 });

        var orders = Strategy.DecideOrders(context);

        Assert.DoesNotContain(orders.OfType<RouteOrder>(), o => new[] { o.Origin.Id, o.Destination.Id }.Contains("far"));
    }

    [Fact]
    public void DecideOrders_NewPairTargetHasPositiveCityWideCapacity_StillProposed()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var far = MakeCity("far", Region.NorthAmerica, 1, 1, 90, autoCapacity: 5);
        var cities = new[] { home, far };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 5_000);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, cities, DemandModel, new[] { aircraft }, fleet, Region.NorthAmerica, HomeCity: home,
            CityWideSlotsRemaining: new Dictionary<string, int> { ["far"] = 1 });

        var orders = Strategy.DecideOrders(context);

        Assert.Contains(orders.OfType<RouteOrder>(), o => new[] { o.Origin.Id, o.Destination.Id }.Contains("far"));
    }

    [Fact]
    public void DecideOrders_NoFullyUnservedGateway_FallsBackToIdleContestableOne()
    {
        // The only gateway in the world is already touched by an existing
        // route, so FindBestUnservedGatewayCrossRegionFirst returns null -
        // but it carries idle-expired contestable inventory, which the
        // fallback should still reach.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var gateway = MakeCity("gw", Region.NorthAmerica, 1, 1, 90, isGateway: true);
        var cities = new[] { home, gateway };
        var aircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 10_000);
        var existing = new[] { new Route(home, gateway, aircraft, AircraftCount: 1) };

        var context = new StrategyContext(
            existing, 3, cities, DemandModel, new[] { aircraft }, AirlineFleet.Empty, Region.NorthAmerica, HomeCity: home,
            AvailableCash: Money.FromDollars(10_000_000),
            IdleContestableSlotsByCity: new Dictionary<string, int> { ["gw"] = 1 });

        var orders = Strategy.DecideOrders(context);

        Assert.Contains(orders.OfType<NegotiationOrder>(), o => o.TargetCity.Id == "gw");
    }
}
