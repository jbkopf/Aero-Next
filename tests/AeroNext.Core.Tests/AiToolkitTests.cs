using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>M45: direct tests for the new AiToolkit statics the two real strategies wire alongside their existing gateway-slot checks.</summary>
public class AiToolkitTests
{
    private static City MakeCity(string id, Region region, double lat, double lon, int weight, bool isGateway = false, int? autoCapacity = null) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, isGateway, isGateway ? 3 : null, isGateway ? null : autoCapacity);

    private static readonly GravityDemandModel DemandModel = new();
    private static readonly AircraftType Aircraft = new("t", "Test", Seats: 100, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);

    private static StrategyContext MakeContext(
        IReadOnlyList<City> cities, City homeCity,
        System.Collections.Generic.IReadOnlyDictionary<string, int>? idleContestableSlotsByCity = null,
        int actionCap = 3, Money? availableCash = null, IReadOnlyList<Route>? routes = null,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), int>? rivalCountByServedRoute = null,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), long>? lastQuarterSeatsCarriedByRoute = null,
        int quarterNumber = 1,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), AiRouteHistory>? routeHistoryByPair = null,
        AirlineFleet? fleet = null, Money? currentDebtBalance = null) =>
        new(routes ?? Array.Empty<Route>(), actionCap, cities, DemandModel, new[] { Aircraft }, fleet ?? AirlineFleet.Empty, homeCity.Region, homeCity,
            IdleContestableSlotsByCity: idleContestableSlotsByCity, AvailableCash: availableCash, CurrentDebtBalance: currentDebtBalance,
            RivalCountByServedRoute: rivalCountByServedRoute, LastQuarterSeatsCarriedByRoute: lastQuarterSeatsCarriedByRoute,
            QuarterNumber: quarterNumber, RouteHistoryByPair: routeHistoryByPair);

    // --- ComputeCityWideSlotsRemaining ---

    [Fact]
    public void ComputeCityWideSlotsRemaining_NoRoutesAnywhere_ReturnsFullCapacity()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>>
        {
            ["A"] = Array.Empty<Route>(),
        };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy });

        Assert.Equal(3, result["bz"]);
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_SumsAircraftCountAcrossEveryAirline_NotJustOne()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 5);
        var elsewhere = MakeCity("el", Region.NorthAmerica, 1, 1, 50);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>>
        {
            ["A"] = new[] { new Route(busy, elsewhere, Aircraft, AircraftCount: 2) },
            ["B"] = new[] { new Route(elsewhere, busy, Aircraft, AircraftCount: 1) }, // busy is the destination here
        };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy, elsewhere });

        Assert.Equal(2, result["bz"]); // 5 - (2 + 1)
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_UsageExceedsCapacity_FloorsAtZero_NeverNegative()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var elsewhere = MakeCity("el", Region.NorthAmerica, 1, 1, 50);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>>
        {
            ["A"] = new[] { new Route(busy, elsewhere, Aircraft, AircraftCount: 5) },
        };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy });

        Assert.Equal(0, result["bz"]);
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_CityWithNoAutoCapacity_NeverAppearsInResult()
    {
        var plain = MakeCity("pl", Region.NorthAmerica, 0, 0, 50); // AutoCapacity: null
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>> { ["A"] = Array.Empty<Route>() };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { plain });

        Assert.False(result.ContainsKey("pl"));
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_NullCapacityBonus_PreservesPreExistingBehavior()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>> { ["A"] = Array.Empty<Route>() };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy }, capacityBonusByCity: null);

        Assert.Equal(3, result["bz"]);
    }

    [Fact]
    public void ComputeCityWideSlotsRemaining_CapacityBonus_AddsOnTopOfAutoCapacity()
    {
        var busy = MakeCity("bz", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var routesByAirline = new System.Collections.Generic.Dictionary<string, IReadOnlyList<Route>> { ["A"] = Array.Empty<Route>() };
        var bonus = new System.Collections.Generic.Dictionary<string, int> { ["bz"] = 2 };

        var result = AiToolkit.ComputeCityWideSlotsRemaining(routesByAirline, new[] { busy }, capacityBonusByCity: bonus);

        Assert.Equal(5, result["bz"]);
    }

    // --- HasRoomAtEveryCity ---

    [Fact]
    public void HasRoomAtEveryCity_NullBalance_AlwaysTrue()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50, autoCapacity: 1);

        Assert.True(AiToolkit.HasRoomAtEveryCity(a, b, null, new System.Collections.Generic.Dictionary<string, int>()));
    }

    [Fact]
    public void HasRoomAtEveryCity_PositiveRemainingBalance_True()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 1 };

        Assert.True(AiToolkit.HasRoomAtEveryCity(a, b, remaining, new System.Collections.Generic.Dictionary<string, int>()));
    }

    [Fact]
    public void HasRoomAtEveryCity_ZeroRemainingBalance_False()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 0 };

        Assert.False(AiToolkit.HasRoomAtEveryCity(a, b, remaining, new System.Collections.Generic.Dictionary<string, int>()));
    }

    [Fact]
    public void HasRoomAtEveryCity_AlreadyReservedThisPass_ConsumesTheBalance()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 1);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 1 };
        var reserved = new System.Collections.Generic.Dictionary<string, int> { ["a"] = 1 };

        Assert.False(AiToolkit.HasRoomAtEveryCity(a, b, remaining, reserved));
    }

    [Fact]
    public void HasRoomAtEveryCity_CityWithNoAutoCapacity_NeverGated()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50); // AutoCapacity: null
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var remaining = new System.Collections.Generic.Dictionary<string, int>(); // "a" absent - would read as 0 if checked

        Assert.True(AiToolkit.HasRoomAtEveryCity(a, b, remaining, new System.Collections.Generic.Dictionary<string, int>()));
    }

    // --- ReserveCityWideSlots ---

    [Fact]
    public void ReserveCityWideSlots_IncrementsBothEndpoints_WhenBothAreAutoCapacityCities()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50, autoCapacity: 3);
        var reserved = new System.Collections.Generic.Dictionary<string, int>();

        AiToolkit.ReserveCityWideSlots(a, b, reserved);

        Assert.Equal(1, reserved["a"]);
        Assert.Equal(1, reserved["b"]);
    }

    [Fact]
    public void ReserveCityWideSlots_SkipsEndpointWithNoAutoCapacity()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50, autoCapacity: 3);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50); // AutoCapacity: null
        var reserved = new System.Collections.Generic.Dictionary<string, int>();

        AiToolkit.ReserveCityWideSlots(a, b, reserved);

        Assert.Equal(1, reserved["a"]);
        Assert.False(reserved.ContainsKey("b"));
    }

    // --- FindBestIdleContestableGateway ---

    [Fact]
    public void FindBestIdleContestableGateway_NullField_ReturnsNull()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, idleContestableSlotsByCity: null);

        Assert.Null(AiToolkit.FindBestIdleContestableGateway(context));
    }

    [Fact]
    public void FindBestIdleContestableGateway_PicksHighestDemandWeight()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var small = MakeCity("small", Region.NorthAmerica, 1, 1, 40, isGateway: true);
        var big = MakeCity("big", Region.NorthAmerica, 2, 2, 90, isGateway: true);
        var idle = new System.Collections.Generic.Dictionary<string, int> { ["small"] = 1, ["big"] = 1 };
        var context = MakeContext(new[] { home, small, big }, home, idleContestableSlotsByCity: idle);

        var result = AiToolkit.FindBestIdleContestableGateway(context);

        Assert.Equal("big", result!.Id);
    }

    [Fact]
    public void FindBestIdleContestableGateway_ZeroCountCity_NeverPicked()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var zero = MakeCity("zero", Region.NorthAmerica, 1, 1, 90, isGateway: true);
        var idle = new System.Collections.Generic.Dictionary<string, int> { ["zero"] = 0 };
        var context = MakeContext(new[] { home, zero }, home, idleContestableSlotsByCity: idle);

        Assert.Null(AiToolkit.FindBestIdleContestableGateway(context));
    }

    [Fact]
    public void FindBestIdleContestableGateway_AlreadyPursued_Excluded()
    {
        // "target" has idle-expired inventory but this airline already
        // holds an available (won-but-unused) slot there - same exclusion
        // every other negotiation-target search already applies via
        // AiToolkit.CitiesAlreadyPursuedForNegotiation.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var target = MakeCity("target", Region.NorthAmerica, 1, 1, 90, isGateway: true);
        var idle = new System.Collections.Generic.Dictionary<string, int> { ["target"] = 1 };
        var context = MakeContext(new[] { home, target }, home, idleContestableSlotsByCity: idle) with
        {
            AvailableSlotsByCity = new System.Collections.Generic.Dictionary<string, int> { ["target"] = 1 },
        };

        Assert.Null(AiToolkit.FindBestIdleContestableGateway(context));
    }

    // --- ApplyManagementToolkit: MaintainCashRunway (bankruptcy-conditions investigation) ---

    [Fact]
    public void ApplyManagementToolkit_ComfortablyAboveWarningLine_DoesNotBorrow()
    {
        // 4 units x $250,000/quarter = $1,000,000/quarter burn.
        // MinimumCashRunwayQuarters (2) x that = $2,000,000 warning line -
        // $5,000,000 clears it comfortably.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 4, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { home }, home, availableCash: Money.FromDollars(5_000_000), currentDebtBalance: Money.Zero, fleet: fleet);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        Assert.Empty(orders.OfType<RefinanceOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_BelowWarningLine_BorrowsExactlyEnoughToReachTarget()
    {
        // Same $1,000,000/quarter burn. $1,000,000 cash is below the
        // $2,000,000 warning line. TargetCashRunwayQuarters (4) x burn =
        // $4,000,000 target - shortfall is exactly $3,000,000, well under
        // the $80M ceiling headroom (zero debt), so the full shortfall is
        // borrowed.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 4, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { home }, home, availableCash: Money.FromDollars(1_000_000), currentDebtBalance: Money.Zero, fleet: fleet);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        var order = Assert.Single(orders.OfType<RefinanceOrder>());
        Assert.Equal(Money.FromDollars(3_000_000), order.AdditionalBorrowing);
    }

    [Fact]
    public void ApplyManagementToolkit_NearDebtCeiling_CapsTheBorrowToRealHeadroom()
    {
        // Same $3,000,000 shortfall as above, but only $1,000,000 of real
        // headroom remains under the $80M ceiling ($79M already owed) -
        // the borrow is capped to that headroom, not the full shortfall.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 4, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { home }, home, availableCash: Money.FromDollars(1_000_000), currentDebtBalance: Money.FromDollars(79_000_000), fleet: fleet);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        var order = Assert.Single(orders.OfType<RefinanceOrder>());
        Assert.Equal(Money.FromDollars(1_000_000), order.AdditionalBorrowing);
    }

    [Fact]
    public void ApplyManagementToolkit_EmptyFleet_ZeroBurnRate_DoesNotBorrow()
    {
        // No fleet at all - nothing to maintain runway against, even
        // with cash far below any plausible warning line.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, availableCash: Money.Zero, currentDebtBalance: Money.Zero);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        Assert.Empty(orders.OfType<RefinanceOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_NullAvailableCash_DoesNotBorrow_PermissiveDefault()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 4, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { home }, home, currentDebtBalance: Money.Zero, fleet: fleet); // no cash passed

        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        Assert.Empty(orders.OfType<RefinanceOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_NullCurrentDebtBalance_DoesNotBorrow_PermissiveDefault()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 4, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { home }, home, availableCash: Money.FromDollars(1_000_000), fleet: fleet); // no debt balance passed

        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        Assert.Empty(orders.OfType<RefinanceOrder>());
    }

    // --- ApplyManagementToolkit: hiring (M50) ---

    [Fact]
    public void ApplyManagementToolkit_ActionCapWasBinding_AffordableAndUnderHeadcountCap_Hires()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, actionCap: 3, availableCash: Money.FromDollars(10_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 3);

        Assert.Single(orders.OfType<HireExecutiveOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_ActionCapWasNotBinding_DoesNotHire()
    {
        // 1 of 3 actions spent - the cap was never the bottleneck this
        // quarter, so an extra one wouldn't have helped.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, actionCap: 3, availableCash: Money.FromDollars(10_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 1);

        Assert.Empty(orders.OfType<HireExecutiveOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_Unaffordable_DoesNotHireEvenWhenBinding()
    {
        // Binding (3 of 3 spent), but cash falls short of ExecutiveHiringCost
        // ($600,000) x the 3x comfort multiplier ($1,800,000).
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, actionCap: 3, availableCash: Money.FromDollars(1_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 3);

        Assert.Empty(orders.OfType<HireExecutiveOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_AtHeadcountCap_DoesNotHireEvenWhenBindingAndAffordable()
    {
        // ActionCap already at QuarterEngine.MaxActionCap - 4 executives
        // already on staff (the derivation this method itself uses),
        // MaxExecutives reached.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, actionCap: QuarterEngine.MaxActionCap, availableCash: Money.FromDollars(10_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: QuarterEngine.MaxActionCap);

        Assert.Empty(orders.OfType<HireExecutiveOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_NullAvailableCash_TreatedAsAffordable_PermissiveDefault()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, actionCap: 3, availableCash: null);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 3);

        Assert.Single(orders.OfType<HireExecutiveOrder>());
    }

    // --- RecentlyShrunkPairKeys / RecentlyClosedPairKeys (M55) ---

    [Fact]
    public void RecentlyShrunkPairKeys_NullHistory_ReturnsEmpty()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, quarterNumber: 10);

        Assert.Empty(AiToolkit.RecentlyShrunkPairKeys(context, cooldownQuarters: 2));
    }

    [Fact]
    public void RecentlyShrunkPairKeys_WithinCooldown_Included()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory> { [key] = new(LastShrunkQuarter: 9, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 0) };
        var context = MakeContext(new[] { a, b }, a, quarterNumber: 10, routeHistoryByPair: history);

        Assert.Contains(key, AiToolkit.RecentlyShrunkPairKeys(context, cooldownQuarters: 2));
    }

    [Fact]
    public void RecentlyShrunkPairKeys_OutsideCooldown_Excluded()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory> { [key] = new(LastShrunkQuarter: 5, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 0) };
        var context = MakeContext(new[] { a, b }, a, quarterNumber: 10, routeHistoryByPair: history);

        Assert.DoesNotContain(key, AiToolkit.RecentlyShrunkPairKeys(context, cooldownQuarters: 2));
    }

    [Fact]
    public void RecentlyShrunkPairKeys_NeverShrunk_Excluded()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory> { [key] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 3) };
        var context = MakeContext(new[] { a, b }, a, quarterNumber: 10, routeHistoryByPair: history);

        Assert.Empty(AiToolkit.RecentlyShrunkPairKeys(context, cooldownQuarters: 2));
    }

    [Fact]
    public void RecentlyClosedPairKeys_NullHistory_ReturnsEmpty()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, quarterNumber: 10);

        Assert.Empty(AiToolkit.RecentlyClosedPairKeys(context, cooldownQuarters: 2));
    }

    [Fact]
    public void RecentlyClosedPairKeys_WithinCooldown_Included()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory> { [key] = new(LastShrunkQuarter: null, LastClosedQuarter: 9, ConsecutiveUnprofitableQuarters: 0) };
        var context = MakeContext(new[] { a, b }, a, quarterNumber: 10, routeHistoryByPair: history);

        Assert.Contains(key, AiToolkit.RecentlyClosedPairKeys(context, cooldownQuarters: 2));
    }

    [Fact]
    public void RecentlyClosedPairKeys_OutsideCooldown_Excluded()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory> { [key] = new(LastShrunkQuarter: null, LastClosedQuarter: 5, ConsecutiveUnprofitableQuarters: 0) };
        var context = MakeContext(new[] { a, b }, a, quarterNumber: 10, routeHistoryByPair: history);

        Assert.DoesNotContain(key, AiToolkit.RecentlyClosedPairKeys(context, cooldownQuarters: 2));
    }

    // --- ApplyManagementToolkit: unprofitable-route response (M37 fare cut, M35 shrink, M54 real profit + closure) ---

    [Fact]
    public void ApplyManagementToolkit_UnprofitableRoute_CutsFare()
    {
        // BigAircraft's huge capacity against real demand for these
        // close, weight-50 cities means operating cost (charged on
        // capacity OFFERED) dwarfs revenue (capped by tiny real demand)
        // - genuinely unprofitable under FareCalculator.EstimatedProfit,
        // not just low load factor.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route });
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        var order = Assert.Single(orders.OfType<SetFareOrder>());
        Assert.Equal(new HashSet<string> { "a", "b" }, new HashSet<string> { order.Origin.Id, order.Destination.Id });
        Assert.True(order.FarePerPassenger < route.FarePerPassenger); // sanity: this is a cut, not a raise
    }

    [Fact]
    public void ApplyManagementToolkit_ProfitableRoute_DoesNotCutFare()
    {
        // Aircraft's small capacity against the same demand gives a high
        // (likely oversold/saturated) load factor - genuinely profitable
        // at the default fare (revenue from a fully-sold route comfortably
        // clears cost, per FareCalculator's own 30% margin). Asserts
        // specifically that no cut happened (rather than "no SetFareOrder
        // at all") since a saturated route legitimately still gets a
        // raise from the separate RaisedFare branch above.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route });
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        // No SetFareOrder below the route's own current (suggested) fare
        // - a raise (if the route also happens to be saturated) is fine,
        // a cut is not.
        Assert.DoesNotContain(orders.OfType<SetFareOrder>(), o => o.FarePerPassenger < route.FarePerPassenger);
    }

    [Fact]
    public void ApplyManagementToolkit_UnprofitableMultiAircraftRoute_ShrinksByOne()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 3);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route });
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        var order = Assert.Single(orders.OfType<CloseRouteOrder>());
        Assert.Equal(new HashSet<string> { "a", "b" }, new HashSet<string> { order.Origin.Id, order.Destination.Id });
    }

    [Fact]
    public void ApplyManagementToolkit_SustainedlyUnprofitableSingleAircraftRoute_StillNeverClosesEntirely()
    {
        // M54 tried full closure gated on a same-quarter profit read
        // alone and pulled it (19,679 closures against 25,722 opens). M55
        // tried again with real cross-quarter memory - requiring
        // ConsecutiveUnprofitableQuarters sustained for multiple quarters
        // in a row, not just this one - on the reasonable hypothesis that
        // this was a timing/thrash problem memory could fix. It measured
        // worse than doing nothing at both a 2-quarter window (12.0% win
        // rate vs. 41.3% with closure disabled) and a much more patient
        // 6-quarter one (24.7%, still well below 41.3%): the real
        // driver is this AI's own route-opening heuristics sending a
        // large, persistent fraction of new routes into genuine,
        // sustained unprofitability - not a timing artifact, a
        // route-selection quality problem punishing with closure only
        // makes worse. See docs/BASELINE_AI_ROADMAP.md's M55 for the
        // full numbers. This test locks in the shipped behavior: even a
        // route this airline has now read as unprofitable for many
        // consecutive quarters in a row still only gets a fare cut.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(a, b)] = 0 };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 20),
        };
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, rivalCountByServedRoute: rivalCount, routeHistoryByPair: history);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        Assert.Empty(orders.OfType<CloseRouteOrder>());
        Assert.Single(orders.OfType<SetFareOrder>()); // the cut still applies
    }

    // --- NetworkOptimizerStrategy follow-up: enableSustainedSingleAircraftClosure (default false, opt-in) ---

    [Fact]
    public void ApplyManagementToolkit_EnabledClosure_NoRouteHistoryAtAll_TreatedAsZeroQuarters_StillOnlyCuts()
    {
        // RouteHistoryByPair null (permissive-default convention, same as
        // every other nullable StrategyContext field) - sustainedQuarters
        // defaults to 0, well below the threshold, so this is really just
        // exercising the null-RouteHistoryByPair branch specifically, not
        // a new behavior.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(a, b)] = 0 };
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, rivalCountByServedRoute: rivalCount, routeHistoryByPair: null);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableSustainedSingleAircraftClosure: true);

        Assert.Empty(orders.OfType<CloseRouteOrder>());
        Assert.Single(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledClosure_RouteHistoryPresentButNoEntryForThisPair_TreatedAsZeroQuarters_StillOnlyCuts()
    {
        // Distinct from the null-dictionary case above: RouteHistoryByPair
        // itself is real and non-null, just has no entry for this specific
        // pair (a route this airline only just started flying, say) -
        // GetValueOrDefault's own null result, not the dictionary's.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(a, b)] = 0 };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>();
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, rivalCountByServedRoute: rivalCount, routeHistoryByPair: history);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableSustainedSingleAircraftClosure: true);

        Assert.Empty(orders.OfType<CloseRouteOrder>());
        Assert.Single(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledClosure_NullRivalCountByServedRoute_TreatedAsUncontested_Closes()
    {
        // RivalCountByServedRoute null (permissive-default convention) -
        // treated the same as zero rivals (uncontested), so a sustained
        // single-aircraft route still closes even when this specific
        // field is entirely absent, not just present-and-zero.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 20),
        };
        // quarterNumber: 100 - well past GameEras.MidGameStartQuarter (41)
        // + the 8-quarter threshold, so the real quarters-since-MidGame
        // cap (see AiToolkit's own doc comment) doesn't defeat this case.
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, rivalCountByServedRoute: null, routeHistoryByPair: history, quarterNumber: 100);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableSustainedSingleAircraftClosure: true);

        Assert.Single(orders.OfType<CloseRouteOrder>());
        Assert.Empty(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledClosure_SustainedUncontestedSingleAircraft_Closes()
    {
        // Same exact fixture as the test above (20 consecutive unprofitable
        // quarters, well past the 8-quarter threshold, uncontested) - only
        // difference is enableSustainedSingleAircraftClosure: true. This is
        // the real, new protective behavior. quarterNumber: 100 for the
        // same reason as the test above - past the quarters-since-MidGame cap.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(a, b)] = 0 };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 20),
        };
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, rivalCountByServedRoute: rivalCount, routeHistoryByPair: history, quarterNumber: 100);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableSustainedSingleAircraftClosure: true);

        Assert.Single(orders.OfType<CloseRouteOrder>());
        Assert.Empty(orders.OfType<SetFareOrder>()); // close instead of a wasted cut, not alongside one
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledClosure_RawStreakOlderThanMidGame_GetsZeroCredit_StillOnlyCuts()
    {
        // The real bug this fixes: a route's raw ConsecutiveUnprofitableQuarters
        // (20) clears the threshold on its own, but MidGame only just
        // began 3 quarters ago (quarterNumber 43, 2 past GameEras.MidGameStartQuarter
        // 41) - the streak necessarily includes pre-MidGame quarters, so
        // it gets zero credit (not partial/capped credit - an earlier
        // attempt at this exact fix used Math.Min-style capping instead,
        // measured to still synchronize every long-backlog route onto the
        // exact same calendar quarter, just delayed - see the class doc
        // comment's own evidence chain) until it actually improves and
        // goes bad again for real, entirely within MidGame.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(a, b)] = 0 };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 20),
        };
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, rivalCountByServedRoute: rivalCount, routeHistoryByPair: history, quarterNumber: 43);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableSustainedSingleAircraftClosure: true);

        Assert.Empty(orders.OfType<CloseRouteOrder>());
        Assert.Single(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledClosure_BelowThreshold_StillOnlyCuts()
    {
        // 7 consecutive quarters - one short of SustainedSingleAircraftClosureThreshold
        // (8) - locks in the exact boundary. quarterNumber: 100, well past
        // the quarters-since-MidGame cap, so this genuinely tests the
        // threshold itself, not an incidental cap-to-zero.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(a, b)] = 0 };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 7),
        };
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, rivalCountByServedRoute: rivalCount, routeHistoryByPair: history, quarterNumber: 100);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableSustainedSingleAircraftClosure: true);

        Assert.Empty(orders.OfType<CloseRouteOrder>());
        Assert.Single(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledClosure_Contested_StillOnlyCutsRegardlessOfHowLongSustained()
    {
        // Same 20-quarter streak as the "closes" test above, but contested
        // (a real rival present) - uncontested-only is a deliberate scope
        // limit (M54's own original reasoning, kept: a contested route's
        // unprofitability reading carries real volatility a rival exiting
        // could resolve on its own). quarterNumber: 100, well past the
        // quarters-since-MidGame cap, so contestedness is genuinely what's
        // under test here.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(a, b)] = 1 };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 20),
        };
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, rivalCountByServedRoute: rivalCount, routeHistoryByPair: history, quarterNumber: 100);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableSustainedSingleAircraftClosure: true);

        Assert.Empty(orders.OfType<CloseRouteOrder>());
        Assert.Single(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_RealCarriedData_OverridesSoloEstimate()
    {
        // Aircraft's small capacity against real demand is profitable
        // under the solo estimate alone (see ProfitableRoute_DoesNotCutFare)
        // - but LastQuarterSeatsCarriedByRoute says this airline is
        // really only carrying a tiny fraction of that (losing a
        // competitive fight the solo estimate can't see), which should
        // now drive a cut the solo-only view never would have.
        //
        // Existing-route-evaluation audit follow-up: the raise loop used
        // to read the naive solo estimate directly, so this same route
        // (solo-saturated) would ALSO get a same-quarter raise alongside
        // the real-data-driven cut - a real, self-contradictory
        // fare-raise-and-cut-together outcome. The raise loop now uses
        // the same real carried figure, so with real carried this tiny,
        // no raise fires at all - just the one, correct cut.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var realCarried = new System.Collections.Generic.Dictionary<(string, string), long> { [QuarterEngine.NormalizedPairKey(a, b)] = 1 };
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, lastQuarterSeatsCarriedByRoute: realCarried);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        var order = Assert.Single(orders.OfType<SetFareOrder>());
        Assert.True(order.FarePerPassenger < route.FarePerPassenger);
    }

    [Fact]
    public void ApplyManagementToolkit_ContestedRouteSoloSaturatedButRealShareLow_DoesNotRaise()
    {
        // Existing-route-evaluation audit finding: a route that reads as
        // sold out under the fare-agnostic solo estimate (RouteEvaluator's
        // own min(demand, capacity)/capacity) but where this airline's own
        // real, competitively-resolved carried volume is far below that
        // (a rival winning most of the real share) must not get a fare
        // raise - raising would only suppress demand further via
        // FareCalculator.DemandDeltaFraction, on a route already losing
        // the fight for it. Real carried set low enough that the real
        // load factor is comfortably under SaturatedLoadFactorCeiling even
        // though the solo estimate (Aircraft's own small capacity against
        // this pair's real demand, per ProfitableRoute_DoesNotCutFare) is
        // fully saturated, and high enough that the route stays real-profitable
        // (no unwanted cut either) - isolates the raise-loop fix alone.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        // 85% of capacity - comfortably above FareCalculator's own ~77%
        // breakeven load factor at the default 30% margin (so no cut
        // fires either), comfortably below SaturatedLoadFactorCeiling
        // (0.95) (so this test isolates the raise-loop fix specifically).
        var realCarried = new System.Collections.Generic.Dictionary<(string, string), long> { [QuarterEngine.NormalizedPairKey(a, b)] = (long)(route.SeatsPerQuarter * 0.85) };
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, lastQuarterSeatsCarriedByRoute: realCarried);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        Assert.Empty(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_LastQuarterDataMissingThisPair_FallsBackToSoloEstimate()
    {
        // The dict itself is non-null (real data exists for other
        // routes) but has no entry for THIS pair - falls back to the
        // solo estimate exactly like a fully-null dict would, rather
        // than misreading a missing key as "carried zero passengers."
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var elsewhere = MakeCity("el", Region.NorthAmerica, 5, 5, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var realCarried = new System.Collections.Generic.Dictionary<(string, string), long> { [QuarterEngine.NormalizedPairKey(a, elsewhere)] = 1 };
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, lastQuarterSeatsCarriedByRoute: realCarried);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        Assert.DoesNotContain(orders.OfType<SetFareOrder>(), o => o.FarePerPassenger < route.FarePerPassenger);
    }

    // --- ApplyMarketingCampaign (M52, retargeted at load factor by M53, then real profit by M54) ---

    // Aircraft (100 seats) against these tests' own close, weight-50 city
    // pairs produces demand well above capacity - carried is pinned at
    // 100% of a small capacity, which is also genuinely PROFITABLE at the
    // default fare's own margin (a "healthy" route, in both the old
    // load-factor sense and the current real-profit sense). BigAircraft's
    // huge capacity against the same demand means operating cost (charged
    // on capacity offered) swamps the same real revenue - genuinely
    // UNPROFITABLE (a "struggling" route, in both senses too) - the two
    // together let a test build both kinds from the same cities/demand.
    // Test names below still say "struggling"; read it as "unprofitable."
    private static readonly AircraftType BigAircraft = new("big", "Big", Seats: 100_000, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);

    private static StrategyContext MakeMarketingContext(
        IReadOnlyList<Route> routes,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), int>? rivalCountByServedRoute,
        Money? availableCash = null, int actionCap = 3,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), AiRouteHistory>? routeHistoryByPair = null)
    {
        var home = routes.Count > 0 ? routes[0].Origin : MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var cities = routes.SelectMany(r => new[] { r.Origin, r.Destination }).Distinct().DefaultIfEmpty(home).ToList();

        return new(routes, actionCap, cities, DemandModel, new[] { Aircraft }, AirlineFleet.Empty, home.Region, home,
            AvailableCash: availableCash, RivalCountByServedRoute: rivalCountByServedRoute, RouteHistoryByPair: routeHistoryByPair);
    }

    [Fact]
    public void ApplyMarketingCampaign_NullRivalCount_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var context = MakeMarketingContext(new[] { route }, rivalCountByServedRoute: null, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_NoContestedRoutes_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 0 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_OneStrugglingContestedRoute_ProposesRouteMarketingOrder()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1); // huge capacity against real demand - unprofitable
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        var order = Assert.Single(orders.OfType<RouteMarketingOrder>());
        Assert.Equal(new HashSet<string> { "a", "b" }, new HashSet<string> { order.Origin.Id, order.Destination.Id });
        Assert.Empty(orders.OfType<NetworkMarketingOrder>());
        Assert.Equal(1, spent);
    }

    // --- M58 resume: give up on marketing a route that's been unprofitable too long ---

    [Fact]
    public void ApplyMarketingCampaign_RouteSustainedUnprofitableTooLong_NoOp()
    {
        // M58 resume: real 300-game evidence found this method repeatedly
        // re-spending cash on a route whose real problem (a permanent
        // demand shock) marketing can't fix, since the triggering
        // condition never clears. Same fixture as the "proposes an
        // order" test above, except the route's own history already
        // shows MaximumConsecutiveUnprofitableQuartersForMarketing (4)
        // consecutive bad quarters - genuinely persistent, not merely
        // newly-struggling.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [key] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 4),
        };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(50_000_000), routeHistoryByPair: history);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_RouteNewlyStruggling_BelowGiveUpThreshold_StillProposesOrder()
    {
        // One quarter short of the give-up threshold - still a real,
        // worthwhile target, proving this isn't an overcorrection to
        // "never market anything."
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [key] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 3),
        };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(50_000_000), routeHistoryByPair: history);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Single(orders.OfType<RouteMarketingOrder>());
        Assert.Equal(1, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_ContestedRouteNotStruggling_NoOp()
    {
        // M54: a contested route that's genuinely PROFITABLE (demand
        // exceeds this small aircraft's capacity, comfortably clearing
        // cost at the default fare) no longer draws a campaign - M52's
        // own original demand-ranking would have picked it unconditionally
        // regardless of health; the retargeted version requires it be
        // genuinely unprofitable first.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1); // small capacity against real demand - profitable
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_StrugglingContestedRoute_PickedOverHealthierHigherDemandOne()
    {
        // M53: two contested routes, only one struggling - the healthier
        // one (route to c, high raw demand, high load factor) must NOT
        // be picked even though M52's own original demand-ranking would
        // have preferred it.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 200); // much bigger demand weight
        var strugglingRoute = new Route(a, b, BigAircraft, AircraftCount: 1);
        var healthyRoute = new Route(a, c, Aircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = 1,
            [QuarterEngine.NormalizedPairKey(a, c)] = 1,
        };
        var context = MakeMarketingContext(new[] { strugglingRoute, healthyRoute }, rivalCount, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        var order = Assert.Single(orders.OfType<RouteMarketingOrder>());
        Assert.Equal(new HashSet<string> { "a", "b" }, new HashSet<string> { order.Origin.Id, order.Destination.Id });
    }

    [Fact]
    public void ApplyMarketingCampaign_TwoStrugglingContestedRoutes_ProposesNetworkMarketingOrder()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 50);
        var routeAb = new Route(a, b, BigAircraft, AircraftCount: 1);
        var routeAc = new Route(a, c, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = 1,
            [QuarterEngine.NormalizedPairKey(a, c)] = 2,
        };
        var context = MakeMarketingContext(new[] { routeAb, routeAc }, rivalCount, availableCash: Money.FromDollars(50_000_000));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Single(orders.OfType<NetworkMarketingOrder>());
        Assert.Empty(orders.OfType<RouteMarketingOrder>());
        Assert.Equal(1, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_TwoStrugglingContestedRoutes_Unaffordable_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 50);
        var routeAb = new Route(a, b, BigAircraft, AircraftCount: 1);
        var routeAc = new Route(a, c, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = 1,
            [QuarterEngine.NormalizedPairKey(a, c)] = 2,
        };
        var context = MakeMarketingContext(new[] { routeAb, routeAc }, rivalCount, availableCash: Money.FromDollars(1));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_Unaffordable_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(1));
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_BudgetExhausted_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: Money.FromDollars(50_000_000), actionCap: 3);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 3; // already at cap

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Empty(orders);
        Assert.Equal(3, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_NullAvailableCash_TreatedAsAffordable_PermissiveDefault()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var key = QuarterEngine.NormalizedPairKey(a, b);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [key] = 1 };
        var context = MakeMarketingContext(new[] { route }, rivalCount, availableCash: null);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Single(orders.OfType<RouteMarketingOrder>());
        Assert.Equal(1, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_TwoStrugglingContestedRoutes_NullAvailableCash_TreatedAsAffordable()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 50);
        var routeAb = new Route(a, b, BigAircraft, AircraftCount: 1);
        var routeAc = new Route(a, c, BigAircraft, AircraftCount: 1);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = 1,
            [QuarterEngine.NormalizedPairKey(a, c)] = 2,
        };
        var context = MakeMarketingContext(new[] { routeAb, routeAc }, rivalCount, availableCash: null);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var spent = 0;

        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);

        Assert.Single(orders.OfType<NetworkMarketingOrder>());
        Assert.Equal(1, spent);
    }

    // --- M64: route-opening consolidation bonus ---
    //
    // Direct unit tests for the shared multiplier both real strategies'
    // route-opening rankings wire in (RapidExpansionStrategy's own
    // DecideOrders_ConsolidationBonus_* tests prove the real end-to-end
    // wiring works in at least one strategy; these test the shared,
    // pure-function mechanism itself, deliberately independent of any one
    // strategy's own fare/capacity-cap complexity).

    [Fact]
    public void ConsolidationMultiplier_ZeroShareOnBothSides_IsExactlyOne()
    {
        var a = MakeCity("a", Region.EastAsia, 0, 0, 50);
        var b = MakeCity("b", Region.EastAsia, 0, 10, 50);

        var multiplier = AiToolkit.ConsolidationMultiplier(a, b, new System.Collections.Generic.Dictionary<string, double>());

        Assert.Equal(1.0, multiplier);
    }

    [Fact]
    public void ConsolidationMultiplier_UsesTheHigherOfTheTwoSidesShares_NotTheSum()
    {
        var a = MakeCity("a", Region.EastAsia, 0, 0, 50);
        var b = MakeCity("b", Region.EastAsia, 0, 10, 50);
        var shareByCityId = new System.Collections.Generic.Dictionary<string, double> { ["a"] = 0.2, ["b"] = 0.8 };

        var multiplier = AiToolkit.ConsolidationMultiplier(a, b, shareByCityId);

        // Math.Max(0.2, 0.8) = 0.8, not 0.2+0.8=1.0 - touching an
        // already-used city on either end benefits once, not twice.
        Assert.Equal(1.0 + AiToolkit.ConsolidationBonusFraction * 0.8, multiplier, precision: 9);
    }

    [Fact]
    public void ConsolidationMultiplier_AtFullShare_EqualsOnePlusTheBoundedFraction()
    {
        var a = MakeCity("a", Region.EastAsia, 0, 0, 50);
        var b = MakeCity("b", Region.EastAsia, 0, 10, 50);
        var shareByCityId = new System.Collections.Generic.Dictionary<string, double> { ["a"] = 1.0 };

        var multiplier = AiToolkit.ConsolidationMultiplier(a, b, shareByCityId);

        Assert.Equal(1.0 + AiToolkit.ConsolidationBonusFraction, multiplier, precision: 9);
    }

    [Fact]
    public void EffectiveDemandScore_MultipliesRawPassengersPerQuarterByTheConsolidationMultiplier()
    {
        var a = MakeCity("a", Region.EastAsia, 0, 0, 50);
        var b = MakeCity("b", Region.EastAsia, 0, 10, 50);
        var pair = new PairDemand(a, b, DistanceKm: 1000, PassengersPerQuarter: 10_000);
        var shareByCityId = new System.Collections.Generic.Dictionary<string, double> { ["a"] = 1.0 };

        var score = AiToolkit.EffectiveDemandScore(pair, shareByCityId);

        Assert.Equal(10_000 * (1.0 + AiToolkit.ConsolidationBonusFraction), score, precision: 6);
    }
}
