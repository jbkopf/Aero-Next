using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Negotiations;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// docs/MULTI_STOP_ROUTES_DESIGN.md, Phase 1. Geometry used throughout:
/// three cities on the equator, 20 degrees of longitude apart (~2,224 km
/// per leg, ~4,448 km end to end), so a 3,000 km-range aircraft can fly
/// each leg but not the whole pair nonstop.
/// </summary>
public class MultiStopRouteTests
{
    private static City MakeCity(string id, double lon, int weight = 50) =>
        new(id, id.ToUpperInvariant(), Region.Europe, 0, lon, weight, false, null);

    private static City MakeGateway(string id, double lon, int gateCapacity = 2, int weight = 2) =>
        new(id, id.ToUpperInvariant(), Region.NorthAmerica, 0, lon, weight, true, gateCapacity);

    private static readonly City Origin = MakeCity("aaa", 0);
    private static readonly City Stop = MakeGateway("stp", 20);
    private static readonly City Destination = MakeCity("zzz", 40);
    private static readonly AircraftType ShortRange = new("short", "Short Range", Seats: 100, RangeKm: 3_000);
    private static readonly AircraftType LongRange = new("long", "Long Range", Seats: 100, RangeKm: 6_000);
    private static readonly City[] Cities = { Origin, Stop, Destination };

    private static QuarterResult Run(IReadOnlyList<QuarterOrder> orders, IReadOnlyList<Route>? currentRoutes = null, IReadOnlyDictionary<string, int>? availableSlotsByCity = null) =>
        QuarterEngine.RunQuarter(1, currentRoutes ?? Array.Empty<Route>(), QuarterEngine.BaseActionCap, orders, Cities, new GravityDemandModel(), availableSlotsByCity: availableSlotsByCity);

    // --- StopList ---

    [Fact]
    public void StopList_Default_IsEmpty()
    {
        var stops = default(StopList);

        Assert.True(stops.IsEmpty);
        Assert.Equal(0, stops.Count);
        Assert.Empty(stops);
        Assert.Equal(StopList.Empty, stops);
        Assert.Equal("nonstop", stops.ToString());
    }

    [Fact]
    public void StopList_SameCitiesInSameOrder_AreEqual_EvenWhenBuiltSeparately()
    {
        var second = MakeGateway("snd", 30);
        var first = StopList.Of(Stop, second);
        var again = new StopList(new List<City> { Stop, second });

        Assert.Equal(first, again);
        Assert.True(first == again);
        Assert.False(first != again);
        Assert.True(first.Equals((object)again));
        Assert.Equal(first.GetHashCode(), again.GetHashCode());
        Assert.Equal("stp", first[0].Id);
        Assert.Equal("via stp, snd", first.ToString());
        Assert.Equal(2, ((System.Collections.IEnumerable)first).Cast<City>().Count());
    }

    [Fact]
    public void StopList_DifferentOrderOrCities_AreNotEqual()
    {
        var second = MakeGateway("snd", 30);

        Assert.NotEqual(StopList.Of(Stop, second), StopList.Of(second, Stop));
        Assert.NotEqual(StopList.Of(Stop), StopList.Empty);
        Assert.False(StopList.Of(Stop).Equals("not a stop list"));
    }

    [Fact]
    public void Route_WithEqualStops_IsEqualAsADictionaryKey()
    {
        // Route is a record used as a dictionary key (NetworkPlanner,
        // CompetitiveMarket) - its stop list must compare by value.
        var route = new Route(Origin, Destination, ShortRange, 1, Stops: StopList.Of(Stop));
        var sameShape = new Route(Origin, Destination, ShortRange, 1, Stops: new StopList(new[] { Stop }));
        var byRoute = new Dictionary<Route, int> { [route] = 7 };

        Assert.Equal(route, sameShape);
        Assert.Equal(7, byRoute[sameShape]);
        Assert.NotEqual(route, route with { Stops = StopList.Empty });
    }

    // --- RoutePath ---

    [Fact]
    public void RoutePath_TotalDistance_IsEveryLegAddedTogether()
    {
        var stops = StopList.Of(Stop);
        var legs = RoutePath.LegDistancesKm(Origin, stops, Destination);

        Assert.Equal(2, legs.Count);
        Assert.Equal(GreatCircle.DistanceKm(Origin, Stop), legs[0], precision: 6);
        Assert.Equal(GreatCircle.DistanceKm(Stop, Destination), legs[1], precision: 6);
        Assert.Equal(legs.Sum(), RoutePath.TotalDistanceKm(Origin, stops, Destination), precision: 6);
        Assert.Equal(GreatCircle.DistanceKm(Origin, Destination), RoutePath.TotalDistanceKm(Origin, StopList.Empty, Destination), precision: 6);
        Assert.Equal(new[] { "aaa", "stp", "zzz" }, RoutePath.Cities(Origin, stops, Destination).Select(c => c.Id));
    }

    [Fact]
    public void RoutePath_IsWithinRange_ChecksEachLeg_NotTheWholeJourney()
    {
        Assert.False(RoutePath.IsWithinRange(Origin, StopList.Empty, Destination, ShortRange));
        Assert.True(RoutePath.IsWithinRange(Origin, StopList.Of(Stop), Destination, ShortRange));
        Assert.True(RoutePath.IsWithinRange(Origin, StopList.Empty, Destination, LongRange));
    }

    [Fact]
    public void RoutePath_IsWithinRange_FalseWhenAnyOneLegIsTooLong()
    {
        var nearStop = MakeGateway("nr", 5); // leg two is ~3,900 km

        Assert.False(RoutePath.IsWithinRange(Origin, StopList.Of(nearStop), Destination, ShortRange));
    }

    [Fact]
    public void RoutePath_AreValidStops_AcceptsDistinctGatewaysAndNoStops()
    {
        Assert.True(RoutePath.AreValidStops(Origin, StopList.Empty, Destination));
        Assert.True(RoutePath.AreValidStops(Origin, StopList.Of(Stop), Destination));
    }

    [Fact]
    public void RoutePath_AreValidStops_RejectsANonGatewayStop()
    {
        Assert.False(RoutePath.AreValidStops(Origin, StopList.Of(MakeCity("plain", 20)), Destination));
    }

    [Fact]
    public void RoutePath_AreValidStops_RejectsARepeatedStopOrAnEndpointAsAStop()
    {
        var gatewayEndpoint = MakeGateway("gwe", 40);

        Assert.False(RoutePath.AreValidStops(Origin, StopList.Of(Stop, Stop), Destination));
        Assert.False(RoutePath.AreValidStops(Origin, StopList.Of(gatewayEndpoint), gatewayEndpoint));
    }

    // --- FlightPerformance / Route ---

    [Fact]
    public void FlightPerformance_EachExtraLanding_CostsGroundTime()
    {
        // 70 utilization hours a week; 2,000 km at 1,000 km/h is 4 flying
        // hours a round trip. One landing each way: 6 hours -> 11 trips.
        // Two each way: 8 hours -> 8 trips.
        var aircraft = new AircraftType("t", "Test", Seats: 100, CruiseSpeedKmh: 1_000);

        Assert.Equal(11, FlightPerformance.MaxWeeklyRoundTrips(aircraft, 2_000));
        Assert.Equal(11, FlightPerformance.MaxWeeklyRoundTrips(aircraft, 2_000, landingsPerDirection: 1));
        Assert.Equal(8, FlightPerformance.MaxWeeklyRoundTrips(aircraft, 2_000, landingsPerDirection: 2));
    }

    [Fact]
    public void Route_MultiStop_FliesEveryLeg_AndPaysForIt()
    {
        // Same type, same endpoints: the stop route flies a little further
        // (the stop is on the great circle here, so only rounding apart)
        // and spends an extra landing each way on the ground. A fast type
        // keeps flying time short enough for that hour to cost a trip:
        // ~6.4 hours a round trip nonstop (10 a week), ~8.4 via the stop (8).
        var fast = new AircraftType("fast", "Fast", Seats: 100, RangeKm: 6_000, CruiseSpeedKmh: 2_000);
        var nonstop = new Route(Origin, Destination, fast, 1);
        var viaStop = new Route(Origin, Destination, fast, 1, Stops: StopList.Of(Stop));

        Assert.Equal(RoutePath.TotalDistanceKm(Origin, StopList.Of(Stop), Destination), viaStop.DistanceKm, precision: 6);
        Assert.True(viaStop.MaxWeeklyRoundTripsPerAircraft < nonstop.MaxWeeklyRoundTripsPerAircraft);
        Assert.True(viaStop.SeatsPerQuarter < nonstop.SeatsPerQuarter);
        Assert.True(FareCalculator.SuggestedFarePerPassenger(viaStop) > FareCalculator.SuggestedFarePerPassenger(nonstop));
    }

    [Fact]
    public void Route_IsWithinRange_AndTouches_CoverTheStops()
    {
        var viaStop = new Route(Origin, Destination, ShortRange, 1, Stops: StopList.Of(Stop));

        Assert.True(viaStop.IsWithinRange);
        Assert.False(new Route(Origin, Destination, ShortRange, 1).IsWithinRange);
        Assert.True(viaStop.Touches("aaa"));
        Assert.True(viaStop.Touches("stp"));
        Assert.True(viaStop.Touches("zzz"));
        Assert.False(viaStop.Touches("elsewhere"));
    }

    // --- QuarterEngine: opening and growing ---

    [Fact]
    public void RunQuarter_StopMakesAnOutOfRangePairFlyable()
    {
        var result = Run(new[] { new RouteOrder(Origin, Destination, ShortRange, StopList.Of(Stop)) });

        var route = Assert.Single(result.Routes);
        Assert.Equal(StopList.Of(Stop), route.Stops);
        Assert.Empty(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_SamePairWithoutTheStop_IsRejectedAsOutOfRange()
    {
        var result = Run(new[] { new RouteOrder(Origin, Destination, ShortRange) });

        Assert.Empty(result.Routes);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_NonGatewayStop_IsRejected_AndTheBriefingSaysWhy()
    {
        var plainStop = MakeCity("plain", 20);
        var order = new RouteOrder(Origin, Destination, ShortRange, StopList.Of(plainStop));

        var result = Run(new[] { order });
        var briefing = BriefingGenerator.Generate(previous: null, result);

        Assert.Empty(result.Routes);
        Assert.Single(result.RejectedOrders);
        Assert.Contains(briefing, i => i.Kind == ExceptionKind.OrderRejectedInvalidStops && i.Message.Contains("aaa-zzz via plain"));
    }

    [Fact]
    public void RunQuarter_SwapWithInvalidStops_IsRejected()
    {
        var current = new[] { new Route(Origin, Destination, LongRange, 1) };
        var order = new AdjustRouteOrder(Origin, Destination, ShortRange, StopList.Of(MakeCity("plain", 20)));

        var result = Run(new[] { order }, current);

        Assert.Single(result.RejectedOrders);
        Assert.Equal("long", Assert.Single(result.Routes).Aircraft.Id);
    }

    [Fact]
    public void RunQuarter_GrowingAMultiStopRoute_WithTheSameStops_AddsAnAircraft()
    {
        var current = new[] { new Route(Origin, Destination, ShortRange, 1, Stops: StopList.Of(Stop)) };

        var result = Run(new[] { new RouteOrder(Origin, Destination, ShortRange, StopList.Of(Stop)) }, current);

        var route = Assert.Single(result.Routes);
        Assert.Equal(2, route.AircraftCount);
        Assert.Equal(StopList.Of(Stop), route.Stops);
    }

    [Fact]
    public void RunQuarter_GrowingARoute_WithDifferentStops_IsRejected()
    {
        // The long-range type could fly it nonstop, but the route already
        // flies via the stop - a different stop list is a different route.
        var current = new[] { new Route(Origin, Destination, LongRange, 1, Stops: StopList.Of(Stop)) };

        var result = Run(new[] { new RouteOrder(Origin, Destination, LongRange) }, current);

        Assert.Single(result.RejectedOrders);
        Assert.Equal(1, Assert.Single(result.Routes).AircraftCount);
    }

    [Fact]
    public void RunQuarter_SecondOrderInTheSameBatch_MustMatchTheFirstOrdersStops()
    {
        var orders = new[]
        {
            new RouteOrder(Origin, Destination, LongRange, StopList.Of(Stop)),
            new RouteOrder(Origin, Destination, LongRange),
        };

        var result = Run(orders);

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
        Assert.Equal(StopList.Of(Stop), Assert.Single(result.Routes).Stops);
    }

    [Fact]
    public void RunQuarter_ClosingAnAircraft_KeepsTheRoutesStops()
    {
        // Closing needs fleet tracking (it detaches a real unit), so both
        // aircraft are delivered and assigned to the route first.
        var current = new[] { new Route(Origin, Destination, ShortRange, 2, Stops: StopList.Of(Stop)) };
        var fleet = Fleet.FleetLedger.ApplyDelivery(Fleet.AirlineFleet.Empty, ShortRange, quantity: 2, Fleet.FleetOwnershipMode.Owned);
        foreach (var unit in fleet.Units.ToList())
        {
            fleet = Fleet.FleetLedger.Assign(fleet, unit.Id, "aaa-zzz");
        }

        var result = QuarterEngine.RunQuarter(1, current, QuarterEngine.BaseActionCap, new[] { new CloseRouteOrder(Origin, Destination) }, Cities, new GravityDemandModel(), fleet: fleet);

        var route = Assert.Single(result.Routes);
        Assert.Equal(1, route.AircraftCount);
        Assert.Equal(StopList.Of(Stop), route.Stops);
    }

    // --- QuarterEngine: slots ---

    [Fact]
    public void RunQuarter_StopWithNoFreeSlot_IsRejected()
    {
        var order = new RouteOrder(Origin, Destination, ShortRange, StopList.Of(Stop));

        var result = Run(new[] { order }, availableSlotsByCity: new Dictionary<string, int> { ["stp"] = 0 });

        Assert.Empty(result.Routes);
        Assert.Single(result.RejectedOrders);
    }

    [Fact]
    public void RunQuarter_StopUsesOneSlotPerAircraft()
    {
        var orders = new[]
        {
            new RouteOrder(Origin, Destination, ShortRange, StopList.Of(Stop)),
            new RouteOrder(Origin, Destination, ShortRange, StopList.Of(Stop)),
        };

        var result = Run(orders, availableSlotsByCity: new Dictionary<string, int> { ["stp"] = 1 });

        Assert.Single(result.AppliedOrders);
        Assert.Single(result.RejectedOrders);
        Assert.Equal(1, Assert.Single(result.Routes).AircraftCount);
    }

    // --- QuarterEngine: swaps ---

    [Fact]
    public void RunQuarter_SwapToALongerRangeType_CanDropTheStop()
    {
        var current = new[] { new Route(Origin, Destination, ShortRange, 1, Stops: StopList.Of(Stop)) };

        var result = Run(new[] { new AdjustRouteOrder(Origin, Destination, LongRange) }, current, new Dictionary<string, int> { ["stp"] = 0 });

        var route = Assert.Single(result.Routes);
        Assert.Equal("long", route.Aircraft.Id);
        Assert.True(route.Stops.IsEmpty);
    }

    [Fact]
    public void RunQuarter_SwapThatKeepsAnExistingStop_NeedsNoNewSlot()
    {
        var current = new[] { new Route(Origin, Destination, ShortRange, 2, Stops: StopList.Of(Stop)) };

        var result = Run(new[] { new AdjustRouteOrder(Origin, Destination, LongRange, StopList.Of(Stop)) }, current, new Dictionary<string, int> { ["stp"] = 0 });

        var route = Assert.Single(result.Routes);
        Assert.Equal(1, route.AircraftCount);
        Assert.Equal(StopList.Of(Stop), route.Stops);
    }

    [Fact]
    public void RunQuarter_SwapThatAddsAStop_NeedsAFreeSlotThere()
    {
        var current = new[] { new Route(Origin, Destination, LongRange, 1) };
        var order = new AdjustRouteOrder(Origin, Destination, ShortRange, StopList.Of(Stop));

        var blocked = Run(new[] { order }, current, new Dictionary<string, int> { ["stp"] = 0 });
        var allowed = Run(new[] { order }, current, new Dictionary<string, int> { ["stp"] = 1 });

        Assert.Single(blocked.RejectedOrders);
        Assert.Equal(StopList.Of(Stop), Assert.Single(allowed.Routes).Stops);
    }

    [Fact]
    public void RunQuarter_SwapThatAddsAStop_SpendsTheSlot()
    {
        // The swap takes the only free slot at the stop, so a new route
        // through it later in the same batch is rejected.
        var other = MakeCity("oth", 10);
        var current = new[] { new Route(Origin, Destination, LongRange, 1) };
        var orders = new QuarterOrder[]
        {
            new AdjustRouteOrder(Origin, Destination, ShortRange, StopList.Of(Stop)),
            new RouteOrder(other, Stop, ShortRange),
        };

        var result = QuarterEngine.RunQuarter(1, current, QuarterEngine.BaseActionCap, orders, new[] { Origin, Stop, Destination, other }, new GravityDemandModel(),
            availableSlotsByCity: new Dictionary<string, int> { ["stp"] = 1 });

        Assert.IsType<AdjustRouteOrder>(Assert.Single(result.AppliedOrders));
        Assert.IsType<RouteOrder>(Assert.Single(result.RejectedOrders));
    }

    [Fact]
    public void RouteAdjustmentOptions_ChecksRangeAgainstTheRoutesCurrentStops()
    {
        var shorter = new AircraftType("shorter", "Shorter", Seats: 100, RangeKm: 2_000);
        var fleet = Fleet.FleetLedger.ApplyDelivery(Fleet.AirlineFleet.Empty, ShortRange, quantity: 1, Fleet.FleetOwnershipMode.Owned);
        fleet = Fleet.FleetLedger.ApplyDelivery(fleet, shorter, quantity: 1, Fleet.FleetOwnershipMode.Owned);
        var route = new Route(Origin, Destination, LongRange, 1, Stops: StopList.Of(Stop));

        var eligible = RouteAdjustmentOptions.EligibleTypesForSwap(route, fleet);

        Assert.Equal(new[] { "short" }, eligible.Select(t => t.Id));
    }

    // --- Slots held, network, passengers ---

    [Fact]
    public void LandingRightsBoard_CountsAStopAsASlotInUse()
    {
        var airline = new Airline("Test Air", Array.Empty<Route>());
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("Test Air", "stp")] = 2 },
        };
        var routes = new[] { new Route(Origin, Destination, ShortRange, 1, Stops: StopList.Of(Stop)) };

        var available = board.AvailableSlotsByCity(airline, routes);

        Assert.Equal(1, available["stp"]);
    }

    [Fact]
    public void AStop_UsesOneSlot_WhereTwoSeparateRoutesThroughTheSameCityUseTwo()
    {
        // The design's core claim: Paris-Gander-Chicago is one aircraft
        // landing at Gander, so one Gander slot - not the two that
        // Paris-Gander plus Gander-Chicago as separate routes take.
        var airline = new Airline("Test Air", Array.Empty<Route>());
        var board = LandingRightsBoard.Empty with
        {
            SlotsHeld = new Dictionary<(string, string), int> { [("Test Air", "stp")] = 2 },
        };
        var multiStop = new[] { new Route(Origin, Destination, ShortRange, 1, Stops: StopList.Of(Stop)) };
        var separateRoutes = new[] { new Route(Origin, Stop, ShortRange, 1), new Route(Stop, Destination, ShortRange, 1) };

        Assert.Equal(1, board.AvailableSlotsByCity(airline, multiStop)["stp"]);
        Assert.Equal(0, board.AvailableSlotsByCity(airline, separateRoutes)["stp"]);

        // And opening them: one held slot is enough for the stop route,
        // but only lets one of the two separate routes through.
        var oneSlot = new Dictionary<string, int> { ["stp"] = 1 };
        var openMultiStop = Run(new[] { new RouteOrder(Origin, Destination, ShortRange, StopList.Of(Stop)) }, availableSlotsByCity: oneSlot);
        var openSeparate = Run(new[] { new RouteOrder(Origin, Stop, ShortRange), new RouteOrder(Stop, Destination, ShortRange) }, availableSlotsByCity: oneSlot);

        Assert.Single(openMultiStop.AppliedOrders);
        Assert.Empty(openMultiStop.RejectedOrders);
        Assert.Single(openSeparate.AppliedOrders);
        Assert.Single(openSeparate.RejectedOrders);
    }

    [Fact]
    public void NetworkPlanner_MultiStopRoute_CarriesTheEndpointPairOnly()
    {
        var demandModel = new GravityDemandModel();
        var route = new Route(Origin, Destination, ShortRange, 1, Stops: StopList.Of(Stop));

        var plan = NetworkPlanner.Plan(new[] { route }, Cities, demandModel);

        var load = Assert.Single(plan.Itineraries);
        Assert.Equal(ItineraryKind.Nonstop, load.Itinerary.Kind);
        Assert.Equal(new[] { "aaa", "zzz" }, new[] { load.Itinerary.Origin.Id, load.Itinerary.Destination.Id });
        Assert.Equal(Math.Min(demandModel.ForPair(Origin, Destination).PassengersPerQuarter, route.SeatsPerQuarter), load.PassengersCarried);
        Assert.DoesNotContain(plan.Itineraries, i => i.Itinerary.Origin.Id == "stp" || i.Itinerary.Destination.Id == "stp");
    }

    // --- Real data: the design's own worked example ---

    [Fact]
    public void RealData_ParisToChicagoOnADC7_NeedsGander_AndUsesAGanderSlot()
    {
        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        var cities = CityCatalog.LoadFile(Path.Combine(dataDir, "cities.json"));
        var dc7 = AircraftCatalog.LoadFile(Path.Combine(dataDir, "aircraft.json")).Single(a => a.Id == "dc7");
        var paris = cities.Single(c => c.Id == "cdg");
        var chicago = cities.Single(c => c.Id == "ord");
        var gander = cities.Single(c => c.Id == "yqx");
        var demandModel = new GravityDemandModel();
        QuarterResult RunReal(RouteOrder order, int ganderSlots) =>
            QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, cities, demandModel,
                availableSlotsByCity: new Dictionary<string, int> { ["yqx"] = ganderSlots });

        var nonstop = RunReal(new RouteOrder(paris, chicago, dc7), ganderSlots: 1);
        var viaGander = RunReal(new RouteOrder(paris, chicago, dc7, StopList.Of(gander)), ganderSlots: 1);
        var noGanderSlot = RunReal(new RouteOrder(paris, chicago, dc7, StopList.Of(gander)), ganderSlots: 0);

        Assert.Single(nonstop.RejectedOrders);
        var route = Assert.Single(viaGander.Routes);
        Assert.Equal(GreatCircle.DistanceKm(paris, gander) + GreatCircle.DistanceKm(gander, chicago), route.DistanceKm, precision: 6);
        Assert.Equal(demandModel.ForPair(paris, chicago).PassengersPerQuarter, Assert.Single(viaGander.Plan.Itineraries).DemandPassengers);
        Assert.Single(noGanderSlot.RejectedOrders);
    }
}
