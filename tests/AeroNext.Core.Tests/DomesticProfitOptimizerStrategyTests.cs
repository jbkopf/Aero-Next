using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class DomesticProfitOptimizerStrategyTests
{
    private static City MakeCity(string id, Region region, double lat, double lon, int weight, bool isGateway = false) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, isGateway, isGateway ? 3 : null);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly DomesticProfitOptimizerStrategy Strategy = new();

    // M44: homeCity defaults to cities[0] - every existing fixture in this
    // file already lists its own "home" city first, matching the pattern
    // MakeContext's own callers were already written against; explicit
    // override only where a test specifically needs a different one.
    private static StrategyContext MakeContext(
        IReadOnlyList<Route> routes, IReadOnlyList<City> cities, IReadOnlyList<AircraftType> eraTypes, Region homeRegion,
        AirlineFleet? fleet = null, int actionCap = 3, Money? cash = null, City? homeCity = null) =>
        new(routes, actionCap, cities, DemandModel, eraTypes, fleet ?? AirlineFleet.Empty, homeRegion, HomeCity: homeCity ?? cities[0], AvailableCash: cash);

    [Fact]
    public void DecideOrders_BelowCap_OpensMostProfitableIntraRegionPair_NotHighestDemandPair()
    {
        // Verified numerically before writing this test (not guessed):
        // "near" has ~160x the raw demand of "far" but is capacity-capped
        // at a much lower fare (short distance), while "far" - lower
        // demand, but capacity-capped at a much higher fare (long
        // distance) - has the higher estimated profit. A strategy
        // ranking by demand would pick "near"; ranking by profit picks
        // "far".
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 100);
        var far = MakeCity("far", Region.NorthAmerica, 8, 8, 40);
        var cities = new[] { home, near, far };
        var aircraft = new AircraftType("t", "Test", Seats: 5, RangeKm: 2000, CruiseSpeedKmh: 400, CostPerAvailableSeatKm: 0.15);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        // GravityDemandModel.AllPairs orders each pair alphabetically by
        // city id, unrelated to which city is conceptually "home" -
        // check membership, not a specific Origin/Destination order.
        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "far" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_HubConnectedCandidate_BeatsHigherNonstopProfitDisconnectedOne_OnceOneStopValueIsCounted()
    {
        // Numerically pre-verified (not guessed): with an existing
        // home-hub route, "hub-far" earns less on its own nonstop demand
        // alone ($1,525,138.86) than the fully disconnected "p2-p3"
        // ($1,612,260.00) - a nonstop-only ranking picks p2-p3. But
        // hub-far also unlocks a brand-new home<->far one-stop
        // connection through the existing hub (home is already
        // home-hub-connected) - real extra demand carried on hub-far's
        // own leg, at hub-far's own fare, exactly FinancialResolver's
        // real per-leg mechanic. Counting it correctly flips the
        // ranking: hub-far's real value ($1,826,396.60) beats p2-p3. A
        // tight 500km aircraft range excludes every other candidate pair
        // among these 5 cities (all >500km apart otherwise) so only
        // these two compete.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var hub = MakeCity("hub", Region.NorthAmerica, 5, 0, 100);
        var far = MakeCity("far", Region.NorthAmerica, 9, 0, 7);
        var p2 = MakeCity("p2", Region.NorthAmerica, -6, -6, 90);
        var p3 = MakeCity("p3", Region.NorthAmerica, -6.9, -6.9, 90);
        var cities = new[] { home, hub, far, p2, p3 };
        var wideAircraft = new AircraftType("wide", "Wide", Seats: 100, RangeKm: 2000); // flies the existing network's own leg
        var candidateAircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 500); // short enough to exclude every other candidate pair
        var existing = new[] { new Route(home, hub, wideAircraft, AircraftCount: 1) };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, candidateAircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(existing, cities, new[] { candidateAircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "hub", "far" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_HubBonus_SkipsDemandAlreadyDirectlyServedElsewhere_NotDoubleCounted()
    {
        // Numerically pre-verified: existing routes home-hub, hub-elsewhere,
        // elsewhere-far. Candidate hub-far connects through hub to TWO
        // already-served neighbors (home and elsewhere) - but elsewhere is
        // already directly connected to far via the existing elsewhere-far
        // route, so that demand is already nonstop-served and must be
        // skipped, not double-counted. Correct hub bonus = home-far demand
        // only, giving hub-far a real value of $1,826,396.60 - below the
        // home-decoy pair's $1,974,336.00, so home-decoy wins. If
        // elsewhere-far's demand were wrongly double-counted too,
        // hub-far's value would reach $2,110,988.04, wrongly beating the
        // decoy instead. decoy sits at home's own coordinates offset by
        // exactly what the original p2-p3 decoy pair (pre-M44) was offset
        // from each other, at the same weight - preserves the exact
        // dollar figures above while touching home directly, since M44's
        // network-contiguity rule now correctly excludes a pair touching
        // neither served city at all (what the original decoy pair was).
        // A tight 500km aircraft range excludes every other candidate
        // pair (all >500km apart) so only these two compete.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var hub = MakeCity("hub", Region.NorthAmerica, 5, 0, 100);
        var far = MakeCity("far", Region.NorthAmerica, 9, 0, 7);
        var elsewhere = MakeCity("elsewhere", Region.NorthAmerica, 5, -6, 60);
        var decoy = MakeCity("decoy", Region.NorthAmerica, -0.9, -1.5, 100);
        var cities = new[] { home, hub, far, elsewhere, decoy };
        var wideAircraft = new AircraftType("wide", "Wide", Seats: 100, RangeKm: 2000); // flies the existing network's own legs
        var candidateAircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 500); // short enough to exclude every other candidate pair
        var existing = new[]
        {
            new Route(home, hub, wideAircraft, AircraftCount: 1),
            new Route(hub, elsewhere, wideAircraft, AircraftCount: 1),
            new Route(elsewhere, far, wideAircraft, AircraftCount: 1),
        };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, candidateAircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(existing, cities, new[] { candidateAircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "decoy" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_NeverOpensACrossRegionPair_EvenWhenItWouldBeMoreProfitable()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var domestic = MakeCity("dom", Region.NorthAmerica, 1, 1, 20); // low-value in-region option
        var foreign = MakeCity("frn", Region.Europe, 2, 2, 500); // obviously more attractive, but cross-region
        var cities = new[] { home, domestic, foreign };
        var aircraft = new AircraftType("t", "Test", Seats: 100, RangeKm: 10_000);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "dom" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_AtCap_OpensNoNewRoutes_StillAppliesTheSharedToolkit()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var partners = Enumerable.Range(0, DomesticProfitOptimizerStrategy.MaxDomesticRoutes)
            .Select(i => MakeCity($"p{i}", Region.NorthAmerica, i + 1, i + 1, 50))
            .ToList();
        var cities = new[] { home }.Concat(partners).ToList();
        var existing = partners.Select(p => new Route(home, p, aircraft, AircraftCount: 1)).ToList();
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: existing.Count, FleetOwnershipMode.Owned);

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Empty(orders.OfType<AircraftOrder>());
        Assert.Single(orders.OfType<HireExecutiveOrder>()); // toolkit still applies
    }

    [Fact]
    public void DecideOrders_NeverNegotiatesForACrossRegionGateway()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var foreignGateway = MakeCity("fgw", Region.Europe, 30, 30, 90, isGateway: true);
        var cities = new[] { home, foreignGateway };

        var context = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica, cash: Money.FromDollars(10_000_000));

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<NegotiationOrder>());
    }

    [Fact]
    public void DecideOrders_NegotiatesForADomesticGateway()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var domesticGateway = MakeCity("dgw", Region.NorthAmerica, 30, 30, 90, isGateway: true);
        var cities = new[] { home, domesticGateway };

        var context = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica, cash: Money.FromDollars(10_000_000));

        var orders = Strategy.DecideOrders(context);

        var negotiation = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("dgw", negotiation.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_SkipsDomesticGatewayWithAPendingNegotiation_TargetsNextBestInstead()
    {
        // "best" would normally win (higher demand weight - same ranking
        // shape as DecideOrders_NegotiatesForADomesticGateway above), but
        // a negotiation is already pending there, so it's skipped in
        // favor of "next" - the same M41 fix RapidExpansionStrategy gets,
        // since both strategies share AiToolkit.NegotiateOrBorrow.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var best = MakeCity("best", Region.NorthAmerica, 2, 2, 90, isGateway: true);
        var next = MakeCity("next", Region.NorthAmerica, 3, 3, 50, isGateway: true);
        var cities = new[] { home, best, next };

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, cities, DemandModel, Array.Empty<AircraftType>(), AirlineFleet.Empty, Region.NorthAmerica, HomeCity: home,
            AvailableCash: Money.FromDollars(10_000_000),
            PendingNegotiationCityIds: new HashSet<string> { "best" });

        var orders = Strategy.DecideOrders(context);

        var negotiation = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("next", negotiation.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_SkipsDomesticGatewayWithAnAlreadyHeldUnusedSlot_TargetsNextBestInstead()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var best = MakeCity("best", Region.NorthAmerica, 2, 2, 90, isGateway: true);
        var next = MakeCity("next", Region.NorthAmerica, 3, 3, 50, isGateway: true);
        var cities = new[] { home, best, next };

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, cities, DemandModel, Array.Empty<AircraftType>(), AirlineFleet.Empty, Region.NorthAmerica, HomeCity: home,
            AvailableSlotsByCity: new Dictionary<string, int> { ["best"] = 1 },
            AvailableCash: Money.FromDollars(10_000_000));

        var orders = Strategy.DecideOrders(context);

        var negotiation = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("next", negotiation.TargetCity.Id);
    }

    [Fact]
    public void Suitability_HigherWhenCashIsLower()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var cities = new[] { home };
        var richContext = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica, cash: Money.FromDollars(20_000_000));
        var poorContext = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica, cash: Money.FromDollars(2_000_000));

        Assert.True(Strategy.Suitability(poorContext) > Strategy.Suitability(richContext));
    }

    [Fact]
    public void ApplicableEras_CoversStartingAndMidGame_NotLateGame()
    {
        Assert.Contains(GameEra.Starting, Strategy.ApplicableEras);
        Assert.Contains(GameEra.MidGame, Strategy.ApplicableEras);
        Assert.DoesNotContain(GameEra.LateGame, Strategy.ApplicableEras);
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
    public void DecideOrders_InheritedCrossRegionRoute_NotCountedTowardTheDomesticCap_AndStillOpensAFifth()
    {
        // An airline that switched from RapidExpansion could hold a
        // cross-region route already - it never counts toward
        // MaxDomesticRoutes, so a domestic strategy with 4 domestic + 1
        // foreign route still opens a 5th domestic one.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var domesticPartners = Enumerable.Range(0, 4).Select(i => MakeCity($"p{i}", Region.NorthAmerica, i + 1, i + 1, 50)).ToList();
        var newDomesticCandidate = MakeCity("p4", Region.NorthAmerica, 5, 5, 50);
        var foreign = MakeCity("frn", Region.Europe, 20, 20, 50);
        var cities = new[] { home, newDomesticCandidate, foreign }.Concat(domesticPartners).ToList();
        var existing = domesticPartners.Select(p => new Route(home, p, aircraft, AircraftCount: 1))
            .Append(new Route(home, foreign, aircraft, AircraftCount: 1))
            .ToList();
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Contains("p4", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_NothingIdle_CashComfortablyCoversPurchase_OrdersInsteadOfLeasing()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90);
        var cities = new[] { home, near };
        var aircraft = new AircraftType("t", "Test", Seats: 10, Cost: Money.FromDollars(1_000_000));

        var context = MakeContext(Array.Empty<Route>(), cities, new[] { aircraft }, Region.NorthAmerica, cash: Money.FromDollars(3_000_000));

        var orders = Strategy.DecideOrders(context);

        var order = Assert.Single(orders.OfType<AircraftOrder>());
        Assert.Equal("t", order.Type.Id);
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_NothingIdle_CashNotComfortable_LeasesInstead()
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
    public void DecideOrders_OverBuiltDomesticRoute_GetsShrunk_ViaTheSharedToolkit()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 1);
        var far = MakeCity("far", Region.NorthAmerica, 60, 60, 1); // far + light: negligible demand relative to capacity
        var bigAircraft = new AircraftType("big", "Big", Seats: 400);
        var existing = new[] { new Route(home, far, bigAircraft, AircraftCount: 2) };

        var context = MakeContext(existing, new[] { home, far }, new[] { bigAircraft }, Region.NorthAmerica, actionCap: 0);

        var orders = Strategy.DecideOrders(context);

        Assert.Single(orders.OfType<CloseRouteOrder>());
    }

    [Fact]
    public void DecideOrders_SaturatedDomesticRoute_FareGetsRaised_ViaTheSharedToolkit()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 90); // close + heavy -> full load factor
        var aircraft = new AircraftType("t", "Test", Seats: 10);
        var existing = new[] { new Route(home, near, aircraft, AircraftCount: 1) };

        var context = MakeContext(existing, new[] { home, near }, new[] { aircraft }, Region.NorthAmerica, actionCap: 0);

        var orders = Strategy.DecideOrders(context);

        Assert.Single(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void DecideOrders_StrandedIdleUnit_GetsRetired_ViaTheSharedToolkit()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100); // only city - no pair can ever exist
        var strandedType = new AircraftType("stranded", "Stranded", Seats: 50);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, strandedType, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(Array.Empty<Route>(), new[] { home }, new[] { strandedType }, Region.NorthAmerica, fleet, actionCap: 0);

        var orders = Strategy.DecideOrders(context);

        var retire = Assert.Single(orders.OfType<RetireAircraftOrder>());
        Assert.Equal(fleet.Units.Single().Id, retire.UnitId);
    }

    [Fact]
    public void DecideOrders_CashExceedsBuffer_DebtIsNonzero_PaysDownDebt_ViaTheSharedToolkit()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 1);

        var context = new StrategyContext(
            Array.Empty<Route>(), 0, new[] { home }, DemandModel, Array.Empty<AircraftType>(), AirlineFleet.Empty, Region.NorthAmerica, HomeCity: home,
            AvailableCash: Money.FromDollars(15_000_000), CurrentDebtBalance: Money.FromDollars(1_000_000));

        var orders = Strategy.DecideOrders(context);

        var payDown = Assert.Single(orders.OfType<PayDownDebtOrder>());
        Assert.Equal(Money.FromDollars(1_000_000), payDown.Amount);
    }

    [Fact]
    public void DecideOrders_NegotiatesForGateway_WhenAvailableCashIsNull_TreatsAsAffordable()
    {
        // AiToolkit.NegotiateOrBorrow's own permissive-caller branch: a
        // null AvailableCash short-circuits straight to "can afford now",
        // never falling through to the debt-ceiling check.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var domesticGateway = MakeCity("dgw", Region.NorthAmerica, 30, 30, 90, isGateway: true);
        var cities = new[] { home, domesticGateway };

        var context = MakeContext(Array.Empty<Route>(), cities, Array.Empty<AircraftType>(), Region.NorthAmerica); // cash defaults to null

        var orders = Strategy.DecideOrders(context);

        var negotiation = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("dgw", negotiation.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_CashShortOfBid_DebtBalanceUnknown_NeverBorrows()
    {
        // AiToolkit.NegotiateOrBorrow's debt-ceiling check requires a
        // known CurrentDebtBalance - a permissive caller that never
        // populates it gets neither a negotiation nor a refinance, rather
        // than a false borrow.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var domesticGateway = MakeCity("dgw", Region.NorthAmerica, 30, 30, 90, isGateway: true);
        var cities = new[] { home, domesticGateway };

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, cities, DemandModel, Array.Empty<AircraftType>(), AirlineFleet.Empty, Region.NorthAmerica, HomeCity: home,
            AvailableCash: Money.FromDollars(500_000)); // CurrentDebtBalance stays null

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<NegotiationOrder>());
        Assert.Empty(orders.OfType<RefinanceOrder>());
    }

    [Fact]
    public void DecideOrders_CashExceedsBuffer_DebtExceedsAffordableAmount_PaysDownPartially_ViaTheSharedToolkit()
    {
        // The other side of AiToolkit's paydown ternary: when the debt is
        // bigger than what's affordable after the buffer, only the
        // affordable slice gets paid, not the full balance.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 1);

        var context = new StrategyContext(
            Array.Empty<Route>(), 0, new[] { home }, DemandModel, Array.Empty<AircraftType>(), AirlineFleet.Empty, Region.NorthAmerica, HomeCity: home,
            AvailableCash: Money.FromDollars(12_000_000), CurrentDebtBalance: Money.FromDollars(5_000_000));

        var orders = Strategy.DecideOrders(context);

        var payDown = Assert.Single(orders.OfType<PayDownDebtOrder>());
        Assert.Equal(Money.FromDollars(2_000_000), payDown.Amount); // 12M - 10M buffer, less than the 5M debt
    }

    [Fact]
    public void DecideOrders_InheritedRouteWithForeignOrigin_AlsoNotCountedTowardTheDomesticCap()
    {
        // The domestic-count filter is Origin.Region == HomeRegion &&
        // Destination.Region == HomeRegion - the other inherited-route
        // test only exercises "origin matches, destination doesn't"; this
        // one covers "origin itself doesn't match" too.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var aircraft = new AircraftType("t", "Test", Seats: 140);
        var domesticPartners = Enumerable.Range(0, 4).Select(i => MakeCity($"p{i}", Region.NorthAmerica, i + 1, i + 1, 50)).ToList();
        var newDomesticCandidate = MakeCity("p4", Region.NorthAmerica, 5, 5, 50);
        var foreignA = MakeCity("frnA", Region.Europe, 20, 20, 50);
        var foreignB = MakeCity("frnB", Region.Europe, 21, 21, 50);
        var cities = new[] { home, newDomesticCandidate, foreignA, foreignB }.Concat(domesticPartners).ToList();
        var existing = domesticPartners.Select(p => new Route(home, p, aircraft, AircraftCount: 1))
            .Append(new Route(foreignA, foreignB, aircraft, AircraftCount: 1)) // origin itself is foreign
            .ToList();
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, aircraft, quantity: 1, FleetOwnershipMode.Owned);

        var context = MakeContext(existing, cities, new[] { aircraft }, Region.NorthAmerica, fleet);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Contains("p4", new[] { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_CashShortOfBid_RealDomesticTarget_CeilingHasRoom_Borrows()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var domesticGateway = MakeCity("dgw", Region.NorthAmerica, 30, 30, 90, isGateway: true);

        var context = new StrategyContext(
            Array.Empty<Route>(), 3, new[] { home, domesticGateway }, DemandModel, Array.Empty<AircraftType>(), AirlineFleet.Empty, Region.NorthAmerica, HomeCity: home,
            AvailableCash: Money.FromDollars(500_000), CurrentDebtBalance: Money.Zero);

        var orders = Strategy.DecideOrders(context);

        var refinance = Assert.Single(orders.OfType<RefinanceOrder>());
        Assert.Equal(Money.FromDollars(1_500_000), refinance.AdditionalBorrowing);
        Assert.Empty(orders.OfType<NegotiationOrder>());
    }
}
