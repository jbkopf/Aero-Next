using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// REDESIGN: BalancedGrowthStrategy now ranks candidates by real,
/// cost-aware projected profit (FareCalculator.EstimatedProfit at each
/// candidate's own best-fitting aircraft type), not raw demand - every
/// fixture below uses real numbers, pre-verified with a scratchpad
/// calculator against FareCalculator itself, matching this project's own
/// established convention (see e.g. DomesticProfitOptimizerStrategyTests).
/// </summary>
public class BalancedGrowthStrategyTests
{
    private static City MakeCity(string id, Region region, double lat, double lon, int weight, bool isGateway = false) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, isGateway, isGateway ? 3 : null);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly BalancedGrowthStrategy Strategy = new();
    private static readonly AircraftType Wide = new("wide", "Wide", Seats: 200, RangeKm: 20_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
    private static readonly AircraftType Small = new("small", "Small", Seats: 20, RangeKm: 20_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);

    private static StrategyContext MakeContext(
        IReadOnlyList<City> cities, City homeCity, IReadOnlyList<Route>? routes = null, AirlineFleet? fleet = null,
        Money? availableCash = null, IReadOnlyList<AircraftType>? eraTypes = null,
        IReadOnlyDictionary<(string, string), int>? rivalPresenceByPairKey = null,
        IReadOnlyDictionary<Region, int>? rivalPresenceByRegion = null,
        IReadOnlySet<Region>? regionsLed = null,
        int actionCap = 3, int? targetRegionCountOverride = null,
        IReadOnlyList<AircraftType>? purchasableTypes = null, IReadOnlyList<AircraftType>? leasableTypes = null) =>
        new(routes ?? Array.Empty<Route>(), actionCap, cities, DemandModel, eraTypes ?? new[] { Wide, Small }, fleet ?? AirlineFleet.Empty,
            homeCity.Region, homeCity, AvailableCash: availableCash,
            RivalPresenceByPairKey: rivalPresenceByPairKey, RivalPresenceByRegion: rivalPresenceByRegion,
            RegionsLedLastQuarter: regionsLed, TargetRegionCountOverride: targetRegionCountOverride,
            PurchasableTypes: purchasableTypes, LeasableTypes: leasableTypes);

    [Fact]
    public void ApplicableEras_CoversMidGameAndLateGame_NeverStarting()
    {
        Assert.DoesNotContain(GameEra.Starting, Strategy.ApplicableEras);
        Assert.Contains(GameEra.MidGame, Strategy.ApplicableEras);
        Assert.Contains(GameEra.LateGame, Strategy.ApplicableEras);
    }

    [Fact]
    public void Suitability_HealthyNetwork_IsOne()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var context = MakeContext(new[] { home }, home);

        Assert.Equal(1.0, Strategy.Suitability(context));
    }

    [Fact]
    public void Suitability_TroubledNetwork_IsZero_NetworkOptimizerWinsInstead()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var away = MakeCity("away", Region.NorthAmerica, 1, 1, 100);
        var route = new Route(home, away, Wide, AircraftCount: 1);
        var rivalCount = new Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, away)] = 1 };
        var context = new StrategyContext(new[] { route }, ActionCap: 3, new[] { home, away }, DemandModel, new[] { Wide }, AirlineFleet.Empty,
            home.Region, home, RivalCountByServedRoute: rivalCount);

        Assert.Equal(0.0, Strategy.Suitability(context));
    }

    [Fact]
    public void DecideOrders_Case1_PicksRealProfitWinner_NotRawDemandWinner()
    {
        // Real numbers (pre-verified): candA has far higher raw demand
        // (1,365,092 vs 202,287) but is contested by 3 rivals - derated
        // to a quarter of that - while candB is uncontested and slightly
        // farther (longer routes earn more per seat under the cost-plus-margin
        // fare model at full load). Best-fit aircraft for both is Wide
        // (dominates Small in absolute profit once demand safely fills
        // either): candA's own real profit is $1,301,300.00, candB's is
        // $1,694,264.00 - candB genuinely wins on real economics despite
        // ~85% less raw demand, exactly the inversion raw-demand ranking
        // could never produce.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var candA = MakeCity("candA", Region.Europe, 2, 2, 30);
        var candB = MakeCity("candB", Region.AustralAsia, 3, 3, 10);
        var cities = new[] { home, candA, candB };
        var rivalPresence = new Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, candA)] = 3 };
        var context = MakeContext(cities, home, rivalPresenceByPairKey: rivalPresence);

        var orders = Strategy.DecideOrders(context);

        // Leasing resolves next quarter now (two-phase,
        // docs/AIRCRAFT_ACQUISITION_ROADMAP.md) - no RouteOrder yet.
        Assert.Empty(orders.OfType<AircraftOrder>());
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>());
        Assert.Equal("wide", lease.Type.Id);
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_LeaseFeeRequired_FlipsAnOtherwiseProfitableCandidateBelowTheFloor()
    {
        // Real numbers: same candA-shaped pair, a $50M aircraft. Owned/idle
        // (no lease fee): profit $1,301,300.00, clears the floor. Requiring
        // a fresh lease (no idle unit of this type exists): profit
        // -$699,400.00 - the real per-quarter lease fee
        // (FinancialResolver.LeaseFeeRatePerQuarter x $50M, prorated per
        // seat) flips it below zero. No idle fleet at all here, so this
        // candidate is correctly proposed as nothing this quarter.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var candA = MakeCity("candA", Region.Europe, 2, 2, 30);
        var costlyWide = new AircraftType("costlywide", "CostlyWide", Seats: 200, RangeKm: 20_000, CostPerAvailableSeatKm: 0.1, Cost: Money.FromDollars(50_000_000));
        var cities = new[] { home, candA };
        var rivalPresence = new Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, candA)] = 3 };
        var context = new StrategyContext(Array.Empty<Route>(), ActionCap: 3, cities, DemandModel, new[] { costlyWide }, AirlineFleet.Empty,
            home.Region, home, RivalPresenceByPairKey: rivalPresence);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_LeaseFeeNotRequired_WhenIdleUnitCoversIt_CandidateIsProposed()
    {
        // Same fixture as above, but this airline already has an idle
        // unit of that exact costly type - no lease fee applies (already
        // owned), so the same candidate's real profit is the full
        // $1,301,300.00 and clears the floor.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var candA = MakeCity("candA", Region.Europe, 2, 2, 30);
        var costlyWide = new AircraftType("costlywide", "CostlyWide", Seats: 200, RangeKm: 20_000, CostPerAvailableSeatKm: 0.1, Cost: Money.FromDollars(50_000_000));
        var cities = new[] { home, candA };
        var rivalPresence = new Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, candA)] = 3 };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, costlyWide, quantity: 1, FleetOwnershipMode.Owned);
        var context = new StrategyContext(Array.Empty<Route>(), ActionCap: 3, cities, DemandModel, new[] { costlyWide }, fleet,
            home.Region, home, RivalPresenceByPairKey: rivalPresence);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "candA" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_RegionLevelCompetition_AlsoDeratesRealProfit()
    {
        // Same candA numbers, but the derating this time comes entirely
        // from RivalPresenceByRegion (3 rivals present somewhere in
        // Europe generally) rather than RivalPresenceByPairKey on this
        // exact pair - EstimatedRealProfit sums both signals, so this
        // produces the identical $1,301,300.00 real profit, still below
        // candB's own $1,694,264.00.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var candA = MakeCity("candA", Region.Europe, 2, 2, 30);
        var candB = MakeCity("candB", Region.AustralAsia, 3, 3, 10);
        var cities = new[] { home, candA, candB };
        var rivalPresenceByRegion = new Dictionary<Region, int> { [Region.Europe] = 3 };
        var context = MakeContext(cities, home, rivalPresenceByRegion: rivalPresenceByRegion);

        var orders = Strategy.DecideOrders(context);

        // Leasing resolves next quarter now (two-phase,
        // docs/AIRCRAFT_ACQUISITION_ROADMAP.md) - no RouteOrder yet.
        Assert.Empty(orders.OfType<RouteOrder>());
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>());
        Assert.Equal("wide", lease.Type.Id);
    }

    [Fact]
    public void DecideOrders_CandidateBelowProfitabilityFloor_ProposesNothing_RegardlessOfAircraftType()
    {
        // Real numbers: a thin, far candidate where every available
        // aircraft type loses money (Wide: -$11,851,301.67 at 2.9% load
        // factor; Small: -$908,461.65 at 28.9%) - genuinely too weak to
        // serve profitably with this fleet, exactly what "sufficient
        // projected profit margins" should reject.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var weak = MakeCity("weak", Region.Europe, 25, 25, 3);
        var cities = new[] { home, weak };
        var context = MakeContext(cities, home);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<RouteOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Empty(orders.OfType<AircraftOrder>());
    }

    [Fact]
    public void DecideOrders_Case2_RestrictsToTouchedRegions_IgnoringAHigherProfitUntouchedCandidate()
    {
        // 4 regions already touched (NorthAmerica home, Europe, EastAsia,
        // SouthAmerica via existing routes) - TargetRegionCount=4, so Case
        // 1 doesn't run at all. "within" (both endpoints touched, real
        // profit $785,668.00) is a legal Case 2 candidate; candB
        // (AustralAsia, untouched, real profit $1,694,264.00 - genuinely
        // higher) is excluded outright by Case 2's own BothEndpointsTouched
        // filter, regardless of its own superior economics.
        // Europe-EastAsia is already served, and saHub's own weight (1)
        // is deliberately too weak for any new pair touching it
        // (euhub-sahub, asiahub-sahub) to outrank "within" - otherwise
        // adding SouthAmerica as a 4th touched region would open a new,
        // unserved, within-touched-region candidate of its own, winning
        // Case 2's own search for a completely different reason than the
        // one this test exists to isolate. With both neutralized,
        // "within" is the sole real Case 2 candidate. Real cash shifts
        // grow-existing to the free ($0 Wide) AircraftOrder branch (1
        // action, not a 2-action lease), leaving room for Case 2 to
        // actually run.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var europeHub = MakeCity("euhub", Region.Europe, 10, 10, 100);
        var asiaHub = MakeCity("asiahub", Region.EastAsia, 20, 20, 100);
        var saHub = MakeCity("sahub", Region.SouthAmerica, -10, -10, 1);
        var within = MakeCity("within", Region.NorthAmerica, 1, 1, 15);
        var candB = MakeCity("candB", Region.AustralAsia, 3, 3, 10);
        var cities = new[] { home, europeHub, asiaHub, saHub, within, candB };
        var existing = new[]
        {
            new Route(home, europeHub, Wide, AircraftCount: 1),
            new Route(home, asiaHub, Wide, AircraftCount: 1),
            new Route(europeHub, asiaHub, Wide, AircraftCount: 1),
            new Route(home, saHub, Wide, AircraftCount: 1),
        };
        var context = MakeContext(cities, home, routes: existing, availableCash: Money.FromDollars(50_000_000));

        var orders = Strategy.DecideOrders(context);

        // Leasing resolves next quarter now (two-phase,
        // docs/AIRCRAFT_ACQUISITION_ROADMAP.md) - no RouteOrder yet.
        Assert.Empty(orders.OfType<RouteOrder>());
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>());
        Assert.Equal("wide", lease.Type.Id);
    }

    [Fact]
    public void DecideOrders_GrowsBestExistingRouteFirst_UnchangedRealLoadFactorLogic()
    {
        // Untouched by the redesign - still BaselineAi.FindBestPerformingRoute's
        // own real load-factor ranking, confirmed still reachable through
        // a real DecideOrders call. euhub's own much higher weight
        // guarantees a real, unambiguous load-factor winner over asiahub.
        // Europe-EastAsia is a third existing route, already served - it
        // would otherwise be a real, profitable Case 2 candidate of its
        // own, adding a second RouteOrder unrelated to what this test
        // exists to isolate.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var europeHub = MakeCity("euhub", Region.Europe, 10, 10, 1000);
        var asiaHub = MakeCity("asiahub", Region.EastAsia, 20, 20, 10);
        var cities = new[] { home, europeHub, asiaHub };
        // hugeAircraft on the europeHub-asiaHub leg specifically keeps its
        // own load factor trivially low regardless of that pair's own
        // gravity demand, so it can never win the load-factor comparison
        // against home-euhub.
        var hugeAircraft = new AircraftType("huge", "Huge", Seats: 100_000, RangeKm: 20_000, CostPerAvailableSeatKm: 0.1);
        var existing = new[]
        {
            new Route(home, europeHub, Wide, AircraftCount: 1),
            new Route(home, asiaHub, Wide, AircraftCount: 1),
            new Route(europeHub, asiaHub, hugeAircraft, AircraftCount: 1),
        };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Wide, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(cities, home, routes: existing, fleet: fleet, availableCash: Money.FromDollars(50_000_000));

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "euhub" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_GrowsBestExistingRoute_PrefersUnledRegion_OverHigherLoadFactorLedOne()
    {
        // Win-condition-awareness follow-up: home-highlf reads 100% real
        // load factor (real numbers verified via the scratchpad
        // calculator: weight-200 city, profit $430,534.00 - real-profitable).
        // home-lowlf reads only 93.34% (weight-8 city, profit $489,735.48 -
        // also real-profitable). Without the region tier, highlf's own
        // better load factor would win the reinforce step outright.
        // NorthAmerica (home) and Europe are both already led (regionsLed)
        // - only EastAsia isn't - so the region tier picks lowlf
        // specifically. touchedRegions is already at TargetRegionCount
        // (3: home + both existing routes' own regions), so Case 1 is a
        // no-op; no idle fleet and no cash means the reinforce step's own
        // lease+route fallback (2 actions) leaves no budget for Case 2's
        // own 2-action fallback at the default actionCap (3), isolating
        // the reinforce step's own choice cleanly. Distinct-but-identical
        // types (rather than one shared type), so the winner stays
        // observable via the leased type now that leasing no longer
        // produces a same-quarter RouteOrder (two-phase,
        // docs/AIRCRAFT_ACQUISITION_ROADMAP.md).
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var highLf = MakeCity("highlf", Region.Europe, 1, 1, 200);
        var lowLf = MakeCity("lowlf", Region.EastAsia, 2, 2, 8);
        var cities = new[] { home, highLf, lowLf };
        var highLfAircraft = new AircraftType("t-highlf", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var lowLfAircraft = new AircraftType("t-lowlf", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var existing = new[]
        {
            new Route(home, highLf, highLfAircraft, AircraftCount: 1),
            new Route(home, lowLf, lowLfAircraft, AircraftCount: 1),
        };
        var regionsLed = new HashSet<Region> { Region.NorthAmerica, Region.Europe };
        var context = MakeContext(cities, home, routes: existing, eraTypes: Array.Empty<AircraftType>(), regionsLed: regionsLed);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<RouteOrder>());
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>());
        Assert.Equal("t-lowlf", lease.Type.Id);
    }

    [Fact]
    public void DecideOrders_Case2_PrefersUnledTouchedRegion_OverMoreProfitableLedOnlyOne()
    {
        // Win-condition-awareness follow-up: home-asia1 already served
        // (EastAsia touched but not yet led - asia1 is also a valid
        // origin for a new route, alongside home). home-dom (NorthAmerica
        // only, both ends already led) is real and profitable
        // (Wide, $785,668.00, real numbers verified via the scratchpad
        // calculator) but has *no* unled endpoint at all, so it's
        // excluded from the new preferred filter outright - under the
        // original, broad BothEndpointsTouched filter it would have been
        // evaluated (and would have lost anyway to asia1-dom below, but
        // that's incidental to what this test isolates). asia1-dom
        // (EastAsia + NorthAmerica, one real unled endpoint) is both the
        // single most profitable candidate overall ($2,937,220.00, Wide)
        // *and* legitimately qualifies for the preferred filter, so it
        // wins Case 2 outright and the broader fallback search never even
        // runs. Both candidate pairs already sit within touched regions,
        // so Case 1 - whose own job is reaching an *untouched* region -
        // finds neither and stays a no-op, isolating Case 2's own new
        // preferred-then-fallback search. No idle fleet and no cash
        // forces the lease+route fallback for both the reinforce step and
        // Case 2, so actionCap 4 covers both without either starving the
        // other.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var asia1 = MakeCity("asia1", Region.EastAsia, 10, 10, 10);
        var asia2 = MakeCity("asia2", Region.EastAsia, 11, 11, 12);
        var dom = MakeCity("dom", Region.NorthAmerica, 1, 1, 400);
        var cities = new[] { home, asia1, asia2, dom };
        var existing = new[] { new Route(home, asia1, Small, AircraftCount: 1) };
        var regionsLed = new HashSet<Region> { Region.NorthAmerica };
        var context = MakeContext(cities, home, routes: existing, regionsLed: regionsLed, actionCap: 4);

        var orders = Strategy.DecideOrders(context);

        // Leasing resolves next quarter now (two-phase,
        // docs/AIRCRAFT_ACQUISITION_ROADMAP.md), so which pair Case 2
        // actually picked is no longer visible as a same-quarter
        // RouteOrder - both home-dom and asia1-dom would lease the same
        // Wide type, so the type alone can't disambiguate either.
        // Materialize the leased unit directly via LeaseAllocationResolver
        // (QuarterEngine no longer owns lease delivery) and re-run
        // DecideOrders one quarter later: whichever pair the now-idle unit
        // gets assigned to, via the idle-unit path, reveals which
        // candidate actually won.
        var wideLease = Assert.Single(orders.OfType<LeaseAircraftOrder>().Where(o => o.Type.Id == "wide"));
        var airline = new Airline("Test", Array.Empty<Route>());
        var leaseBoard = LeaseAllocationResolver.Submit(LeaseRequestBoard.Empty, airline, wideLease, currentQuarter: 1);
        var (_, deliveries) = LeaseAllocationResolver.ResolveQuarter(leaseBoard, currentQuarter: 2, year: 2000, new SimRng(1));
        var delivery = Assert.Single(deliveries);
        var fleetWithLeasedUnit = FleetLedger.ApplyDelivery(AirlineFleet.Empty, delivery.Type, delivery.Quantity, FleetOwnershipMode.Leased);
        var nextContext = context with { OwnFleet = fleetWithLeasedUnit };

        var nextOrders = Strategy.DecideOrders(nextContext);

        Assert.Contains(nextOrders.OfType<RouteOrder>(), o => new HashSet<string> { "asia1", "dom" }.SetEquals(new[] { o.Origin.Id, o.Destination.Id }));
        Assert.DoesNotContain(nextOrders.OfType<RouteOrder>(), o => new HashSet<string> { "home", "dom" }.SetEquals(new[] { o.Origin.Id, o.Destination.Id }));
    }

    [Fact]
    public void DecideOrders_Phase1_NegotiatesForUntouchedRegionGateway_NotTouchedRegionOne()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var domesticGateway = MakeCity("domgw", Region.NorthAmerica, 1, 1, 500, isGateway: true);
        var foreignGateway = MakeCity("forgw", Region.Europe, 2, 2, 50, isGateway: true);
        var cities = new[] { home, domesticGateway, foreignGateway };
        var context = MakeContext(cities, home, availableCash: Money.FromDollars(50_000_000));

        var orders = Strategy.DecideOrders(context);

        var negotiationOrder = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("forgw", negotiationOrder.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_UnreachableHigherDemandNewRegionGateway_LosesToAReachableLowerDemandOne()
    {
        // Gateway-relevance follow-up (docs/OPEN_WORK.md's filler-seat
        // investigation): both untouched-region candidates used to be
        // ranked by demand weight alone - "far" would have won purely for
        // its higher weight, however unreachable. Same fix as
        // RapidExpansionStrategy's own equivalent test.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var near = MakeCity("near", Region.Europe, 0, 10, 20, isGateway: true); // ~1,100km - reachable
        var far = MakeCity("far", Region.SouthAmerica, 0, 100, 90, isGateway: true); // ~11,100km - not reachable
        var cities = new[] { home, near, far };
        var shortRangeAircraft = new AircraftType("t", "Test", Seats: 140, RangeKm: 3_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var context = MakeContext(cities, home, availableCash: Money.FromDollars(50_000_000), eraTypes: new[] { shortRangeAircraft });

        var orders = Strategy.DecideOrders(context);

        var negotiationOrder = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("near", negotiationOrder.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_Phase2_NegotiatesForTouchedRegionGateway_NotUntouchedRegionOne()
    {
        // withinEuropeGateway carries enough demand (weight 50) that a
        // profitable nonstop route to it exists - a gateway bid that isn't
        // for a planned refuelling stop requires one
        // (AiToolkit.HasProfitableNonstopRouteTo); an earlier version of
        // this test used weight 1 precisely so no route reached it, which
        // that rule now treats as not worth bidding on. Case 2 uses one
        // action leasing for a new route, leaving the last one for
        // negotiation - exactly what this test exists to isolate.
        // Europe-EastAsia and home-auHub are already-served routes. auHub
        // (AustralAsia) is the 4th touched region, matching
        // TargetRegionCount=4, so the negotiation ternary resolves to the
        // within-touched-region gateway search, not the reach-a-new-region
        // one - and the far bigger untouchedGateway is passed over.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var europeHub = MakeCity("euhub", Region.Europe, 10, 10, 100);
        var asiaHub = MakeCity("asiahub", Region.EastAsia, 20, 20, 100);
        var auHub = MakeCity("auhub", Region.AustralAsia, -20, -20, 1);
        var withinEuropeGateway = MakeCity("eugw", Region.Europe, 11, 11, 50, isGateway: true);
        var untouchedGateway = MakeCity("elsewheregw", Region.SouthAmerica, -30, -30, 5000, isGateway: true);
        var cities = new[] { home, europeHub, asiaHub, auHub, withinEuropeGateway, untouchedGateway };
        var existing = new[]
        {
            new Route(home, europeHub, Wide, AircraftCount: 1),
            new Route(home, asiaHub, Wide, AircraftCount: 1),
            new Route(europeHub, asiaHub, Wide, AircraftCount: 1),
            new Route(home, auHub, Wide, AircraftCount: 1),
        };
        var context = MakeContext(cities, home, routes: existing, availableCash: Money.FromDollars(50_000_000), eraTypes: new[] { Wide });

        var orders = Strategy.DecideOrders(context);

        var negotiationOrder = Assert.Single(orders.OfType<NegotiationOrder>());
        Assert.Equal("eugw", negotiationOrder.TargetCity.Id);
    }

    [Fact]
    public void DecideOrders_StillAppliesSharedToolkit_RaisesFareOnSaturatedRoute()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 200);
        var near = MakeCity("near", Region.NorthAmerica, 1, 1, 200);
        var tinyAircraft = new AircraftType("tiny", "Tiny", Seats: 1, RangeKm: 2000); // real demand vastly exceeds capacity - saturated
        var route = new Route(home, near, tinyAircraft, AircraftCount: 1);
        var context = new StrategyContext(new[] { route }, ActionCap: 0, new[] { home, near }, DemandModel, new[] { tinyAircraft }, AirlineFleet.Empty, home.Region, home);

        var orders = Strategy.DecideOrders(context);

        var fareOrder = Assert.Single(orders.OfType<SetFareOrder>());
        Assert.Equal(new HashSet<string> { "home", "near" }, new HashSet<string> { fareOrder.Origin.Id, fareOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_GrowExisting_NoIdleUnit_NullCash_LeasesWhenBudgetAllows()
    {
        // The grow-existing step's own lease fallback (no idle unit of
        // the route's own type, cash null so ordering is never assumed
        // affordable) - unchanged from before this redesign, still real
        // and reachable.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var away = MakeCity("away", Region.NorthAmerica, 1, 1, 100);
        var route = new Route(home, away, Wide, AircraftCount: 1);
        var context = MakeContext(new[] { home, away }, home, routes: new[] { route });

        var orders = Strategy.DecideOrders(context);

        // Leasing resolves next quarter now (two-phase,
        // docs/AIRCRAFT_ACQUISITION_ROADMAP.md) - no RouteOrder yet.
        Assert.Empty(orders.OfType<AircraftOrder>().Where(o => o.Type.Id == "wide"));
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>().Where(o => o.Type.Id == "wide"));
        Assert.NotNull(lease);
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_GrowExisting_NothingIdle_TypeExcludedFromLeasableTypes_ProposesNoAcquisition()
    {
        // AI-acquisition-awareness-gaps follow-up
        // (docs/AI_ACQUISITION_AWARENESS_GAPS_ROADMAP.md): the route's own
        // type is no longer legally leasable for this home city - growth
        // must propose nothing rather than an order QuarterEngine would
        // reject.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var away = MakeCity("away", Region.NorthAmerica, 1, 1, 100);
        var route = new Route(home, away, Wide, AircraftCount: 1);
        var context = MakeContext(new[] { home, away }, home, routes: new[] { route },
            leasableTypes: Array.Empty<AircraftType>());

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<AircraftOrder>().Where(o => o.Type.Id == "wide"));
        Assert.Empty(orders.OfType<LeaseAircraftOrder>().Where(o => o.Type.Id == "wide"));
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_GrowExisting_NothingIdle_TypeExcludedFromPurchasableTypes_LeasesInsteadOfBuying()
    {
        // The route's own type is no longer legally purchasable (e.g.
        // past its own EndOfManufactureYear) even though cash would
        // otherwise cover buying outright - falls through to leasing
        // (still legal, LeasableTypes left null/permissive) rather than
        // skipping growth entirely.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var away = MakeCity("away", Region.NorthAmerica, 1, 1, 100);
        var route = new Route(home, away, Wide, AircraftCount: 1);
        var context = MakeContext(new[] { home, away }, home, routes: new[] { route }, availableCash: Money.FromDollars(50_000_000),
            purchasableTypes: Array.Empty<AircraftType>());

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<AircraftOrder>().Where(o => o.Type.Id == "wide"));
        var lease = Assert.Single(orders.OfType<LeaseAircraftOrder>().Where(o => o.Type.Id == "wide"));
        Assert.NotNull(lease);
        Assert.Empty(orders.OfType<RouteOrder>());
    }

    [Fact]
    public void DecideOrders_Case1_PrioritizesAWonUnusedSlot_OverAHigherProfitGeneralCandidate()
    {
        // Same priority AiToolkit.FindBestUnservedPairTouchingWonSlot's
        // own doc comment documents for RapidExpansionStrategy: a
        // contested negotiation win already paid for gets exploited
        // before the general profit-ranked search ever runs, even though
        // candB's own profit ($1,694,264.00, pre-verified) would clearly
        // win that general search. wongw's own weight is low enough that
        // its own real profit is modest but still clears the floor - the
        // point here is priority, not profit comparison between the two.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var wonGateway = MakeCity("wongw", Region.Europe, 2, 2, 10, isGateway: true);
        var candB = MakeCity("candB", Region.AustralAsia, 3, 3, 10);
        var cities = new[] { home, wonGateway, candB };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Wide, quantity: 1, FleetOwnershipMode.Owned);
        var availableSlotsByCity = new Dictionary<string, int> { ["wongw"] = 1 };
        var context = new StrategyContext(Array.Empty<Route>(), ActionCap: 3, cities, DemandModel, new[] { Wide, Small }, fleet, home.Region, home,
            AvailableSlotsByCity: availableSlotsByCity);

        var orders = Strategy.DecideOrders(context);

        var routeOrder = Assert.Single(orders.OfType<RouteOrder>());
        Assert.Equal(new HashSet<string> { "home", "wongw" }, new HashSet<string> { routeOrder.Origin.Id, routeOrder.Destination.Id });
    }

    [Fact]
    public void DecideOrders_StillBorrows_WhenCashFallsBelowMaintainCashRunwaysWarningLine()
    {
        // Same numbers as AiToolkitTests's own proven
        // ApplyManagementToolkit_BelowWarningLine_BorrowsExactlyEnoughToReachTarget.
        // Only one city exists, so no candidate pair can possibly exist
        // either - nothing here can interfere with the borrow assertion.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Wide, quantity: 4, FleetOwnershipMode.Owned);
        var context = new StrategyContext(Array.Empty<Route>(), ActionCap: 3, new[] { home }, DemandModel, Array.Empty<AircraftType>(), fleet, home.Region, home,
            AvailableCash: Money.FromDollars(1_000_000), CurrentDebtBalance: Money.Zero);

        var orders = Strategy.DecideOrders(context);

        var borrowOrder = Assert.Single(orders.OfType<RefinanceOrder>());
        Assert.Equal(Money.FromDollars(3_000_000), borrowOrder.AdditionalBorrowing);
    }

    // --- Difficulty follow-up: StrategyContext.TargetRegionCountOverride ---

    [Fact]
    public void DecideOrders_TargetRegionCountOverride_Null_StopsChasingANewRegionAtTheDefaultOfThree()
    {
        // home (NorthAmerica) + hubA (Europe) + hubB (EastAsia) already
        // touched via existing routes - 3 regions, matching
        // BalancedGrowthStrategy.DefaultTargetRegionCount. With no
        // override (the pre-existing, every-caller-before-this-round
        // default), Case 1 must not reach for newRegion (AustralAsia),
        // even though it would otherwise be a real, profitable candidate.
        // One idle "wide" unit so the reinforce step's own 1-action
        // idle-unit branch fires (not its 2-action lease fallback),
        // leaving real budget for Case 1 to actually act on if it were
        // active - isolates the region-count gate itself, not a starved
        // action budget, as what's actually blocking newregion.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var hubA = MakeCity("hubA", Region.Europe, 10, 10, 100);
        var hubB = MakeCity("hubB", Region.EastAsia, 20, 20, 100);
        var newRegion = MakeCity("newregion", Region.AustralAsia, 3, 3, 20);
        var cities = new[] { home, hubA, hubB, newRegion };
        var existing = new[]
        {
            new Route(home, hubA, Wide, AircraftCount: 1),
            new Route(home, hubB, Wide, AircraftCount: 1),
        };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Wide, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(cities, home, routes: existing, fleet: fleet, targetRegionCountOverride: null);

        var orders = Strategy.DecideOrders(context);

        Assert.DoesNotContain(orders.OfType<RouteOrder>(), o => new[] { o.Origin.Id, o.Destination.Id }.Contains("newregion"));
    }

    [Fact]
    public void DecideOrders_TargetRegionCountOverride_Four_KeepsChasingANewRegionPastTheDefault()
    {
        // Identical fixture to the test directly above (including the
        // idle "wide" unit, for the same reason - real budget for Case 1
        // to actually act on) - only the override differs (4,
        // GameDifficulty.Hard's own value via DifficultySettings). 3
        // touched regions is no longer enough to stop Case 1, so it
        // reaches for newRegion (AustralAsia) as its real, profitable 4th.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var hubA = MakeCity("hubA", Region.Europe, 10, 10, 100);
        var hubB = MakeCity("hubB", Region.EastAsia, 20, 20, 100);
        var newRegion = MakeCity("newregion", Region.AustralAsia, 3, 3, 20);
        var cities = new[] { home, hubA, hubB, newRegion };
        var existing = new[]
        {
            new Route(home, hubA, Wide, AircraftCount: 1),
            new Route(home, hubB, Wide, AircraftCount: 1),
        };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Wide, quantity: 1, FleetOwnershipMode.Owned);
        // actionCap 2, not the default 3: reinforce (1, idle unit) + Case
        // 1's own lease (1, now that leasing is a single action instead of
        // two - two-phase, docs/AIRCRAFT_ACQUISITION_ROADMAP.md) exactly
        // exhausts it, leaving no room for Case 2 to also fire and
        // muddying which lease is actually Case 1's own.
        var context = MakeContext(cities, home, routes: existing, fleet: fleet, targetRegionCountOverride: 4, actionCap: 2);

        var orders = Strategy.DecideOrders(context);

        Assert.Empty(orders.OfType<RouteOrder>().Where(o => new[] { o.Origin.Id, o.Destination.Id }.Contains("newregion")));
        Assert.Single(orders.OfType<LeaseAircraftOrder>());
    }

    [Fact]
    public void DecideOrders_GrosslyMismatchedRoute_SwapsAircraftType()
    {
        // Follow-up to M66 (NetworkOptimizerStrategy): confirms
        // BalancedGrowthStrategy's own enableReactiveTypeSwap/
        // enableProactiveTypeSwap: true wiring actually reaches
        // ApplyManagementToolkit through a real DecideOrders call, not
        // just a direct AiToolkit unit test in isolation - same bar
        // NetworkOptimizerStrategyTests's own equivalent test holds
        // itself to.
        var huge = new AircraftType("huge", "Huge", Seats: 100_000, RangeKm: 20_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(home, b, huge, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Small, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { home, b }, home, routes: new[] { route }, fleet: fleet, eraTypes: new[] { huge, Small }, actionCap: 3);

        var orders = Strategy.DecideOrders(context);

        var adjust = Assert.Single(orders.OfType<AdjustRouteOrder>());
        Assert.Equal(Small, adjust.AircraftType);
    }
}
