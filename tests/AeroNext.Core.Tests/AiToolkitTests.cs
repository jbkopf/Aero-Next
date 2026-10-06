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
        AirlineFleet? fleet = null, Money? currentDebtBalance = null, IReadOnlyList<AircraftType>? eraTypes = null,
        IReadOnlyList<AircraftType>? purchasableTypes = null, IReadOnlyList<AircraftType>? leasableTypes = null) =>
        new(routes ?? Array.Empty<Route>(), actionCap, cities, DemandModel, eraTypes ?? new[] { Aircraft }, fleet ?? AirlineFleet.Empty, homeCity.Region, homeCity,
            IdleContestableSlotsByCity: idleContestableSlotsByCity, AvailableCash: availableCash, CurrentDebtBalance: currentDebtBalance,
            RivalCountByServedRoute: rivalCountByServedRoute, LastQuarterSeatsCarriedByRoute: lastQuarterSeatsCarriedByRoute,
            QuarterNumber: quarterNumber, RouteHistoryByPair: routeHistoryByPair,
            PurchasableTypes: purchasableTypes, LeasableTypes: leasableTypes);

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
    public void ApplyManagementToolkit_MarketingTookTheLastAction_DoesNotCountAsBinding()
    {
        // 3 of 3 spent, but one of them was a marketing campaign - it only
        // used an action that was otherwise spare, so the cap wasn't really
        // the bottleneck and an executive ($300k/quarter forever) isn't
        // warranted. Control: the same spend with no campaign does hire.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var context = MakeContext(new[] { home }, home, actionCap: 3, availableCash: Money.FromDollars(10_000_000));
        var withCampaign = new System.Collections.Generic.List<QuarterOrder> { new NetworkMarketingOrder() };
        var withoutCampaign = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, withCampaign, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 3);
        AiToolkit.ApplyManagementToolkit(context, withoutCampaign, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 3);

        Assert.Empty(withCampaign.OfType<HireExecutiveOrder>());
        Assert.Single(withoutCampaign.OfType<HireExecutiveOrder>());
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
    public void ApplyManagementToolkit_EnabledClosure_StreakStartedBeforeMidGame_StillCloses()
    {
        // The streak (20) began before the mid-game (quarter 43, 3 quarters
        // in). It used to get zero credit, and since the streak and the
        // mid-game clock grow together, a route already losing at quarter
        // 41 could never close. Full credit now; pacing (one closure per
        // airline per quarter) keeps a backlog from closing all at once.
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

        Assert.Single(orders.OfType<CloseRouteOrder>());
        Assert.Empty(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledClosure_SeveralEligible_ClosesOnlyTheWorstLoserThisQuarter()
    {
        // Two eligible dead routes: only the bigger loser closes now; the
        // other just gets its fare cut and waits for a later quarter.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 50);
        var smallerLoss = new Route(a, b, BigAircraft, AircraftCount: 1);
        var biggerLoss = new Route(a, c, BigAircraft, AircraftCount: 1);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 20),
            [QuarterEngine.NormalizedPairKey(a, c)] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 20),
        };
        var context = MakeContext(new[] { a, b, c }, a, routes: new[] { smallerLoss, biggerLoss }, routeHistoryByPair: history, quarterNumber: 100);
        var loss = (Route r) => FareCalculator.EstimatedProfit(r, RouteEvaluator.Evaluate(r, context.DemandModel).PassengersCarried);
        var worse = loss(smallerLoss) < loss(biggerLoss) ? smallerLoss : biggerLoss;
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableSustainedSingleAircraftClosure: true);

        var closed = Assert.Single(orders.OfType<CloseRouteOrder>());
        Assert.Equal(worse.Destination.Id, closed.Destination.Id);
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

    // --- Geopolitical-aircraft-restrictions follow-up (docs/GEOPOLITICAL_AIRCRAFT_RESTRICTIONS_ROADMAP.md): FindMostProfitablePair's own idle-vs-acquire split ---

    [Fact]
    public void FindMostProfitablePair_NonIdleCandidate_ExcludedWhenNotInLeasableTypes_ReturnsNull()
    {
        // Nothing idle, and the only era-available type isn't in
        // LeasableTypes (an embargoed bloc) - the whole candidate pool is
        // empty, so the search finds nothing rather than proposing an
        // order QuarterEngine would reject.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var context = MakeContext(new[] { a, b }, a, eraTypes: new[] { Aircraft }, leasableTypes: Array.Empty<AircraftType>());

        var best = AiToolkit.FindMostProfitablePair(context, new System.Collections.Generic.HashSet<int>(), new System.Collections.Generic.HashSet<string> { "a" }, _ => true);

        Assert.Null(best);
    }

    [Fact]
    public void FindMostProfitablePair_IdleOwnedCandidate_StillConsideredEvenWhenNotInLeasableTypes()
    {
        // An idle unit of that exact type means no fresh lease is needed
        // at all - the embargo (LeasableTypes empty) never applies to
        // reusing an aircraft this airline already owns.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, eraTypes: new[] { Aircraft }, fleet: fleet, leasableTypes: Array.Empty<AircraftType>());

        var best = AiToolkit.FindMostProfitablePair(context, new System.Collections.Generic.HashSet<int>(), new System.Collections.Generic.HashSet<string> { "a" }, _ => true);

        Assert.NotNull(best);
        Assert.Equal(Aircraft, best!.Value.Aircraft);
        Assert.True(best.Value.UsesIdleUnit);
    }

    // --- AI-type-swap follow-up (docs/AI_AIRCRAFT_TYPE_SWAP_DESIGN.md): EvaluateTypeSwapCandidate / TryProposeTypeSwap / RecentlySwappedPairKeys ---

    // These two, against the weight-50/weight-50 pair every fixture here
    // shares (~758,269 pax/qtr demand at 157km): BigAircraft's 100,000
    // seats is grossly oversized (profit ~-$1.17B, verified numerically
    // via a real AeroNext.Core scratch run, not guessed), and
    // MediumAircraft's 200 seats is genuinely, substantially more
    // profitable than Aircraft's own 100 seats ($785,668 vs. $430,534 -
    // an 82.5% improvement, comfortably clearing
    // ProactiveSwapMinimumProfitImprovementFraction's 50% bar).
    // BelowBarAircraft's 150 seats ($608,478, a 41.4% improvement) is
    // deliberately just under that same bar.
    private static readonly AircraftType MediumAircraft = new("med", "Medium", Seats: 200, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);
    private static readonly AircraftType BelowBarAircraft = new("below", "BelowBar", Seats: 150, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);

    [Fact]
    public void RecentlySwappedPairKeys_NullRouteHistory_ReturnsEmpty()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var context = MakeContext(new[] { a, b }, a, quarterNumber: 10, routeHistoryByPair: null);

        Assert.Empty(AiToolkit.RecentlySwappedPairKeys(context, AiToolkit.TypeSwapCooldownQuarters));
    }

    [Fact]
    public void RecentlySwappedPairKeys_WithinCooldown_Excluded_JustOutside_Included()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var c = MakeCity("c", Region.NorthAmerica, 2, 2, 50);
        var d = MakeCity("d", Region.NorthAmerica, 3, 3, 50);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = new(null, null, 0, LastTypeSwapQuarter: 7), // quarter 10 - 7 = 3 < 4
            [QuarterEngine.NormalizedPairKey(c, d)] = new(null, null, 0, LastTypeSwapQuarter: 6), // quarter 10 - 6 = 4, not < 4
        };
        var context = MakeContext(new[] { a, b, c, d }, a, quarterNumber: 10, routeHistoryByPair: history);

        var result = AiToolkit.RecentlySwappedPairKeys(context, AiToolkit.TypeSwapCooldownQuarters);

        Assert.Contains(QuarterEngine.NormalizedPairKey(a, b), result);
        Assert.DoesNotContain(QuarterEngine.NormalizedPairKey(c, d), result);
    }

    [Fact]
    public void ApplyManagementToolkit_ReactiveSwapCandidate_ButNoActionLeft_FallsBackToTheFareCut()
    {
        // A losing route with a better idle type ready - but the strategy
        // already spent the whole action budget. The swap can't go out, so
        // the route still gets the free fare cut rather than nothing.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft }, fleet: fleet, actionCap: 3);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 3, enableReactiveTypeSwap: true);

        Assert.Empty(orders.OfType<AdjustRouteOrder>());
        Assert.Single(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void EvaluateTypeSwapCandidate_Reactive_GrosslyOversizedIncumbent_PicksTheBetterIdleType()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft }, fleet: fleet);
        var load = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel).Single();

        var candidate = AiToolkit.EvaluateTypeSwapCandidate(context, load, new System.Collections.Generic.HashSet<int>(), isReactive: true);

        Assert.NotNull(candidate);
        Assert.Equal(Aircraft, candidate!.Value.Type);
        Assert.True(candidate.Value.UsesIdleUnit);
    }

    [Fact]
    public void EvaluateTypeSwapCandidate_Reactive_NoAlternateTypeAtAll_ReturnsNull()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft }); // only its own current type is era-available
        var load = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel).Single();

        var candidate = AiToolkit.EvaluateTypeSwapCandidate(context, load, new System.Collections.Generic.HashSet<int>(), isReactive: true);

        Assert.Null(candidate);
    }

    [Fact]
    public void EvaluateTypeSwapCandidate_Proactive_ClearsFiftyPercentBar_ReturnsCandidate()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, MediumAircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { Aircraft, MediumAircraft }, fleet: fleet);
        var load = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel).Single();

        var candidate = AiToolkit.EvaluateTypeSwapCandidate(context, load, new System.Collections.Generic.HashSet<int>(), isReactive: false);

        Assert.NotNull(candidate);
        Assert.Equal(MediumAircraft, candidate!.Value.Type);
    }

    [Fact]
    public void EvaluateTypeSwapCandidate_Proactive_BelowFiftyPercentBar_ReturnsNull()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, BelowBarAircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { Aircraft, BelowBarAircraft }, fleet: fleet);
        var load = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel).Single();

        var candidate = AiToolkit.EvaluateTypeSwapCandidate(context, load, new System.Collections.Generic.HashSet<int>(), isReactive: false);

        Assert.Null(candidate);
    }

    [Fact]
    public void EvaluateTypeSwapCandidate_ExcludesTheRoutesOwnCurrentType()
    {
        // Even when the current type is technically "best" among what
        // AllPairs would otherwise consider, it must never be offered
        // back as its own swap candidate - AdjustRouteOrder would reject
        // a same-type request anyway.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, MediumAircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { MediumAircraft, Aircraft }, fleet: fleet);
        var load = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel).Single();

        var candidate = AiToolkit.EvaluateTypeSwapCandidate(context, load, new System.Collections.Generic.HashSet<int>(), isReactive: false);

        // Aircraft (100 seats) is a worse fit than MediumAircraft (200
        // seats) here, so nothing clears the proactive bar - but the key
        // assertion is what it does NOT return, not just null overall.
        Assert.True(candidate is null || candidate.Value.Type != MediumAircraft);
    }

    [Fact]
    public void EvaluateTypeSwapCandidate_AllowLeaseOrBuyFalse_ExcludesNonIdleTypes_EvenIfBetter()
    {
        // DistressStrategy follow-up: a strictly better type with no idle
        // unit must be excluded entirely when allowLeaseOrBuy is false -
        // not returned with UsesIdleUnit: false expecting the caller to
        // filter it out itself.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        // Aircraft is era-available (a genuinely better fit than BigAircraft) but NOT idle - no fleet passed.
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft });
        var load = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel).Single();

        var candidate = AiToolkit.EvaluateTypeSwapCandidate(context, load, new System.Collections.Generic.HashSet<int>(), isReactive: true, allowLeaseOrBuy: false);

        Assert.Null(candidate);
    }

    [Fact]
    public void EvaluateTypeSwapCandidate_AllowLeaseOrBuyFalse_StillReturnsAGenuinelyIdleCandidate()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft }, fleet: fleet);
        var load = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel).Single();

        var candidate = AiToolkit.EvaluateTypeSwapCandidate(context, load, new System.Collections.Generic.HashSet<int>(), isReactive: true, allowLeaseOrBuy: false);

        Assert.NotNull(candidate);
        Assert.Equal(Aircraft, candidate!.Value.Type);
        Assert.True(candidate.Value.UsesIdleUnit);
    }

    [Fact]
    public void EvaluateTypeSwapCandidate_NonIdleType_ExcludedWhenNotInPurchasableOrLeasableTypes()
    {
        // Geopolitical-aircraft-restrictions follow-up: a non-idle
        // candidate must be legal to at least buy or lease - here it's
        // legal for neither (both narrowed lists are empty, e.g. an
        // embargoed bloc), so it's excluded even though it's a genuinely
        // better fit and era-available.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft },
            purchasableTypes: Array.Empty<AircraftType>(), leasableTypes: Array.Empty<AircraftType>());
        var load = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel).Single();

        var candidate = AiToolkit.EvaluateTypeSwapCandidate(context, load, new System.Collections.Generic.HashSet<int>(), isReactive: true);

        Assert.Null(candidate);
    }

    [Fact]
    public void EvaluateTypeSwapCandidate_IdleType_StillConsideredEvenWhenNotInPurchasableOrLeasableTypes()
    {
        // An already-owned aircraft can always keep flying regardless of
        // embargo - only *acquiring a new one* is ever restricted.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft }, fleet: fleet,
            purchasableTypes: Array.Empty<AircraftType>(), leasableTypes: Array.Empty<AircraftType>());
        var load = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel).Single();

        var candidate = AiToolkit.EvaluateTypeSwapCandidate(context, load, new System.Collections.Generic.HashSet<int>(), isReactive: true);

        Assert.NotNull(candidate);
        Assert.Equal(Aircraft, candidate!.Value.Type);
        Assert.True(candidate.Value.UsesIdleUnit);
    }

    [Fact]
    public void TryProposeTypeSwap_IdleUnit_EmitsAdjustRouteOrderOnly_CostsOneAction()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var candidate = (Type: Aircraft, ProfitDelta: Money.FromDollars(1), UsesIdleUnit: true, CanBuyOutright: false);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var reservedUnitIds = new System.Collections.Generic.HashSet<int>();
        var spent = 0;
        var load = new RouteLoad(route, DemandPassengers: 1, SeatsPerQuarter: 1, PassengersCarried: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, actionCap: 3, fleet: fleet);

        AiToolkit.TryProposeTypeSwap(context, load, candidate, orders, reservedUnitIds, ref spent);

        var order = Assert.Single(orders);
        var adjust = Assert.IsType<AdjustRouteOrder>(order);
        Assert.Equal(Aircraft, adjust.AircraftType);
        Assert.Equal(1, spent);
    }

    [Fact]
    public void TryProposeTypeSwap_RequiresLease_EmitsLeaseAlone_CostsOneAction()
    {
        // Leasing resolves next quarter now (two-phase,
        // docs/AIRCRAFT_ACQUISITION_ROADMAP.md) - no AdjustRouteOrder yet,
        // mirroring the CanBuyOutright path below.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var candidate = (Type: Aircraft, ProfitDelta: Money.FromDollars(1), UsesIdleUnit: false, CanBuyOutright: false);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var reservedUnitIds = new System.Collections.Generic.HashSet<int>();
        var spent = 0;
        var load = new RouteLoad(route, DemandPassengers: 1, SeatsPerQuarter: 1, PassengersCarried: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, actionCap: 3);

        AiToolkit.TryProposeTypeSwap(context, load, candidate, orders, reservedUnitIds, ref spent);

        var order = Assert.Single(orders);
        Assert.IsType<LeaseAircraftOrder>(order);
        Assert.Equal(1, spent);
    }

    [Fact]
    public void TryProposeTypeSwap_CanBuyOutright_EmitsAircraftOrderOnly_NoAdjustYet_CostsOneAction()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var candidate = (Type: Aircraft, ProfitDelta: Money.FromDollars(1), UsesIdleUnit: false, CanBuyOutright: true);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var reservedUnitIds = new System.Collections.Generic.HashSet<int>();
        var spent = 0;
        var load = new RouteLoad(route, DemandPassengers: 1, SeatsPerQuarter: 1, PassengersCarried: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, actionCap: 3);

        AiToolkit.TryProposeTypeSwap(context, load, candidate, orders, reservedUnitIds, ref spent);

        var order = Assert.Single(orders);
        Assert.IsType<AircraftOrder>(order);
        Assert.Equal(1, spent);
    }

    [Fact]
    public void TryProposeTypeSwap_NullCandidate_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var reservedUnitIds = new System.Collections.Generic.HashSet<int>();
        var spent = 0;
        var load = new RouteLoad(route, DemandPassengers: 1, SeatsPerQuarter: 1, PassengersCarried: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, actionCap: 3);

        AiToolkit.TryProposeTypeSwap(context, load, null, orders, reservedUnitIds, ref spent);

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void TryProposeTypeSwap_OverBudget_IdlePath_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var candidate = (Type: Aircraft, ProfitDelta: Money.FromDollars(1), UsesIdleUnit: true, CanBuyOutright: false);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var reservedUnitIds = new System.Collections.Generic.HashSet<int>();
        var spent = 3;
        var load = new RouteLoad(route, DemandPassengers: 1, SeatsPerQuarter: 1, PassengersCarried: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, actionCap: 3);

        AiToolkit.TryProposeTypeSwap(context, load, candidate, orders, reservedUnitIds, ref spent);

        Assert.Empty(orders);
        Assert.Equal(3, spent);
    }

    [Fact]
    public void TryProposeTypeSwap_OverBudget_BuyOutrightPath_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var candidate = (Type: Aircraft, ProfitDelta: Money.FromDollars(1), UsesIdleUnit: false, CanBuyOutright: true);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var reservedUnitIds = new System.Collections.Generic.HashSet<int>();
        var spent = 3;
        var load = new RouteLoad(route, DemandPassengers: 1, SeatsPerQuarter: 1, PassengersCarried: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, actionCap: 3);

        AiToolkit.TryProposeTypeSwap(context, load, candidate, orders, reservedUnitIds, ref spent);

        Assert.Empty(orders);
        Assert.Equal(3, spent);
    }

    [Fact]
    public void TryProposeTypeSwap_OverBudget_LeasePath_NoOp()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var candidate = (Type: Aircraft, ProfitDelta: Money.FromDollars(1), UsesIdleUnit: false, CanBuyOutright: false);
        var orders = new System.Collections.Generic.List<QuarterOrder>();
        var reservedUnitIds = new System.Collections.Generic.HashSet<int>();
        var spent = 3; // 3 + 1 (lease path's own cost, now 1 not 2) > actionCap 3
        var load = new RouteLoad(route, DemandPassengers: 1, SeatsPerQuarter: 1, PassengersCarried: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, actionCap: 3);

        AiToolkit.TryProposeTypeSwap(context, load, candidate, orders, reservedUnitIds, ref spent);

        Assert.Empty(orders);
        Assert.Equal(3, spent);
    }

    // --- ApplyManagementToolkit: enableReactiveTypeSwap / enableProactiveTypeSwap / allowTypeSwapLeaseOrBuy integration ---

    [Fact]
    public void ApplyManagementToolkit_DisabledTypeSwap_ByteIdenticalToBeforeThisExisted()
    {
        // The ablation guarantee: with enableReactiveTypeSwap/
        // enableProactiveTypeSwap both left at their default false, even
        // a grossly oversized, unprofitable route with a real idle
        // better-fit type sitting right there still only gets the
        // pre-existing fare-cut response, exactly as before this
        // feature existed.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft }, fleet: fleet);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0);

        Assert.Empty(orders.OfType<AdjustRouteOrder>());
        Assert.Single(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledTypeSwap_ReactiveFires_InsteadOfCutOrShrink()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 3);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft }, fleet: fleet);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableReactiveTypeSwap: true);

        var adjust = Assert.Single(orders.OfType<AdjustRouteOrder>());
        Assert.Equal(Aircraft, adjust.AircraftType);
        Assert.Empty(orders.OfType<CloseRouteOrder>()); // the swap already collapses to 1 - shrink is redundant, skipped
        Assert.Empty(orders.OfType<SetFareOrder>()); // let the new type play out with real data next quarter
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledTypeSwap_ClosureEligibleRoute_ClosesInstead_SwapNeverAttempted()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var rivalCount = new System.Collections.Generic.Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(a, b)] = 0 };
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = new(null, null, ConsecutiveUnprofitableQuarters: 20),
        };
        // quarterNumber: 100, well past MidGameStartQuarter, so the raw
        // 20-quarter streak isn't zeroed out by the "zero credit if the
        // raw streak predates MidGame itself" rule - same convention the
        // existing SustainedSingleAircraftClosureThreshold boundary test
        // already uses.
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft }, fleet: fleet,
            rivalCountByServedRoute: rivalCount, routeHistoryByPair: history, quarterNumber: 100);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0,
            enableSustainedSingleAircraftClosure: true, enableReactiveTypeSwap: true);

        Assert.Single(orders.OfType<CloseRouteOrder>());
        Assert.Empty(orders.OfType<AdjustRouteOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledTypeSwap_RecentlySwapped_CooldownBlocksIt_FallsBackToCutFare()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(a, b)] = new(null, null, 0, LastTypeSwapQuarter: 8), // quarter 10 - 8 = 2 < TypeSwapCooldownQuarters (4)
        };
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft }, fleet: fleet,
            quarterNumber: 10, routeHistoryByPair: history);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableReactiveTypeSwap: true);

        Assert.Empty(orders.OfType<AdjustRouteOrder>());
        Assert.Single(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledTypeSwap_ProactiveFires_OnAHealthyRoute()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, MediumAircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { Aircraft, MediumAircraft }, fleet: fleet);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableProactiveTypeSwap: true);

        var adjust = Assert.Single(orders.OfType<AdjustRouteOrder>());
        Assert.Equal(MediumAircraft, adjust.AircraftType);
    }

    [Fact]
    public void ApplyManagementToolkit_EnabledTypeSwap_HealthyRouteNoBigImprovement_DoesNothing()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, BelowBarAircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { Aircraft, BelowBarAircraft }, fleet: fleet);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableProactiveTypeSwap: true);

        Assert.Empty(orders.OfType<AdjustRouteOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_ReactiveOnly_DoesNotProactivelySwapAHealthyRoute()
    {
        // The split from a single flag (M66) to independent
        // reactive/proactive toggles must actually be independent - a
        // caller passing only enableReactiveTypeSwap must never also get
        // proactive behavior for free.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, Aircraft, AircraftCount: 1); // profitable
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, MediumAircraft, quantity: 1, FleetOwnershipMode.Owned); // a clearly better idle type
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { Aircraft, MediumAircraft }, fleet: fleet);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableReactiveTypeSwap: true);

        Assert.Empty(orders.OfType<AdjustRouteOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_ProactiveOnly_DoesNotReactivelySwapAnUnprofitableRoute()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1); // grossly unprofitable
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft }, fleet: fleet);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0, enableProactiveTypeSwap: true);

        Assert.Empty(orders.OfType<AdjustRouteOrder>());
        Assert.Single(orders.OfType<SetFareOrder>()); // falls back to the ordinary cut, unaffected
    }

    [Fact]
    public void ApplyManagementToolkit_LeaseOrBuyDisallowed_SkipsALeaseRequiringCandidate_FallsBackToCutFare()
    {
        // DistressStrategy follow-up: allowTypeSwapLeaseOrBuy: false must
        // reject a candidate that would need a fresh lease/purchase, even
        // though it would otherwise clear the reactive bar easily -
        // falls back to the ordinary fare cut instead of swapping.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft }); // Aircraft is era-available but NOT idle (no fleet passed)
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0,
            enableReactiveTypeSwap: true, allowTypeSwapLeaseOrBuy: false);

        Assert.Empty(orders.OfType<AdjustRouteOrder>());
        Assert.Empty(orders.OfType<LeaseAircraftOrder>());
        Assert.Single(orders.OfType<SetFareOrder>());
    }

    [Fact]
    public void ApplyManagementToolkit_LeaseOrBuyDisallowed_StillSwapsWhenAnIdleCandidateExists()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = new Route(a, b, BigAircraft, AircraftCount: 1);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var context = MakeContext(new[] { a, b }, a, routes: new[] { route }, eraTypes: new[] { BigAircraft, Aircraft }, fleet: fleet);
        var orders = new System.Collections.Generic.List<QuarterOrder>();

        AiToolkit.ApplyManagementToolkit(context, orders, new System.Collections.Generic.HashSet<int>(), spentBeforeToolkit: 0,
            enableReactiveTypeSwap: true, allowTypeSwapLeaseOrBuy: false);

        var adjust = Assert.Single(orders.OfType<AdjustRouteOrder>());
        Assert.Equal(Aircraft, adjust.AircraftType);
    }

    // --- ApplyMarketingCampaign (value-based redesign: the campaign with the largest estimated net gain, if positive) ---

    // The weight-50/weight-50 pair every fixture here shares has ~758,269
    // pax/qtr of demand at 157km (see the type-swap fixtures above). At
    // that demand a route campaign costs ~$37.9M ($50 per passenger of
    // demand) and a network campaign $100k per route. Aircraft (100 seats,
    // ~75,400 seats/qtr there) always runs full - no spare seats, so no
    // campaign can add passengers. BigAircraft (100,000 seats) always has
    // tens of millions of spare seats, so a campaign's reach is limited
    // only by demand, and FareOverride alone decides whether it pays.
    private static readonly AircraftType BigAircraft = new("big", "Big", Seats: 100_000, RangeKm: 10_000, CruiseSpeedKmh: 900, CostPerAvailableSeatKm: 0.1);

    private static StrategyContext MakeMarketingContext(
        IReadOnlyList<Route> routes,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), int>? rivalCountByServedRoute,
        Money? availableCash = null, int actionCap = 3,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), AiRouteHistory>? routeHistoryByPair = null,
        System.Collections.Generic.IReadOnlyDictionary<(string OriginId, string DestinationId), long>? lastQuarterSeatsCarriedByRoute = null)
    {
        var home = routes.Count > 0 ? routes[0].Origin : MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var cities = routes.SelectMany(r => new[] { r.Origin, r.Destination }).Distinct().DefaultIfEmpty(home).ToList();

        return new(routes, actionCap, cities, DemandModel, new[] { Aircraft }, AirlineFleet.Empty, home.Region, home,
            AvailableCash: availableCash, RivalCountByServedRoute: rivalCountByServedRoute, RouteHistoryByPair: routeHistoryByPair,
            LastQuarterSeatsCarriedByRoute: lastQuarterSeatsCarriedByRoute);
    }

    private static System.Collections.Generic.Dictionary<(string, string), int> Rivals(params (Route Route, int Count)[] entries) =>
        entries.ToDictionary(e => QuarterEngine.NormalizedPairKey(e.Route.Origin, e.Route.Destination), e => e.Count);

    private static Route SpareSeatRoute(City from, City to, long fareDollars) =>
        new(from, to, BigAircraft, AircraftCount: 1, FareOverride: Money.FromDollars(fareDollars));

    private static (List<QuarterOrder> Orders, int Spent) Market(StrategyContext context, int spentBefore = 0)
    {
        var orders = new List<QuarterOrder>();
        var spent = spentBefore;
        AiToolkit.ApplyMarketingCampaign(context, orders, ref spent);
        return (orders, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_NullRivalCount_NoOp()
    {
        var route = SpareSeatRoute(MakeCity("a", Region.NorthAmerica, 0, 0, 50), MakeCity("b", Region.NorthAmerica, 1, 1, 50), 1_000);

        var (orders, spent) = Market(MakeMarketingContext(new[] { route }, rivalCountByServedRoute: null, availableCash: Money.FromDollars(1_000_000_000)));

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_BudgetExhausted_NoOp()
    {
        var route = SpareSeatRoute(MakeCity("a", Region.NorthAmerica, 0, 0, 50), MakeCity("b", Region.NorthAmerica, 1, 1, 50), 1_000);

        var (orders, spent) = Market(MakeMarketingContext(new[] { route }, Rivals((route, 0)), availableCash: Money.FromDollars(1_000_000_000), actionCap: 1), spentBefore: 1);

        Assert.Empty(orders);
        Assert.Equal(1, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_UncontestedRouteAlreadyFull_NoOp()
    {
        // No spare seats means no campaign can add a single passenger.
        var route = new Route(MakeCity("a", Region.NorthAmerica, 0, 0, 50), MakeCity("b", Region.NorthAmerica, 1, 1, 50), Aircraft, AircraftCount: 1);

        var (orders, _) = Market(MakeMarketingContext(new[] { route }, Rivals((route, 0)), availableCash: Money.FromDollars(1_000_000_000)));

        Assert.Empty(orders);
    }

    [Fact]
    public void ApplyMarketingCampaign_UncontestedRouteWithSpareSeats_ProposesRouteMarketingOrder()
    {
        // The case the old trigger never covered: no rival, empty seats. At
        // $1,000 a route campaign nets ~$341M (half of demand into spare
        // seats, less its ~$37.9M cost) - more than a network campaign's
        // ~$114M on a one-route network.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = SpareSeatRoute(a, b, 1_000);

        var (orders, spent) = Market(MakeMarketingContext(new[] { route }, Rivals((route, 0)), availableCash: Money.FromDollars(1_000_000_000)));

        var order = Assert.Single(orders.OfType<RouteMarketingOrder>());
        Assert.Equal((a.Id, b.Id), (order.Origin.Id, order.Destination.Id));
        Assert.Empty(orders.OfType<NetworkMarketingOrder>());
        Assert.Equal(1, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_FareTooLowForAnyCampaignToPay_NoOp()
    {
        // Plenty of spare seats, but at 1 cent a passenger neither campaign
        // earns back its cost.
        var route = new Route(MakeCity("a", Region.NorthAmerica, 0, 0, 50), MakeCity("b", Region.NorthAmerica, 1, 1, 50), BigAircraft, AircraftCount: 1, FareOverride: new Money(1));

        var (orders, _) = Market(MakeMarketingContext(new[] { route }, Rivals((route, 0)), availableCash: Money.FromDollars(1_000_000_000)));

        Assert.Empty(orders);
    }

    [Fact]
    public void ApplyMarketingCampaign_PicksTheRouteWithTheLargestNetGain()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var cheaper = SpareSeatRoute(a, MakeCity("b", Region.NorthAmerica, 1, 1, 50), 1_000);
        var pricier = SpareSeatRoute(a, MakeCity("c", Region.NorthAmerica, -1, -1, 50), 2_000); // same distance, same demand, twice the fare

        var (orders, _) = Market(MakeMarketingContext(new[] { cheaper, pricier }, Rivals((cheaper, 0), (pricier, 0)), availableCash: Money.FromDollars(1_000_000_000)));

        var order = Assert.Single(orders.OfType<RouteMarketingOrder>());
        Assert.Equal("c", order.Destination.Id);
    }

    [Fact]
    public void ApplyMarketingCampaign_ManyRoutesWithSpareSeats_NetworkCampaignBeatsAnySingleRoute()
    {
        // Four identical spare-seat routes: a network campaign's +15% on
        // all four (~$455M net) outweighs a route campaign's +50% on one
        // (~$341M net).
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var routes = new[]
        {
            SpareSeatRoute(a, MakeCity("b", Region.NorthAmerica, 1, 1, 50), 1_000),
            SpareSeatRoute(a, MakeCity("c", Region.NorthAmerica, 1, -1, 50), 1_000),
            SpareSeatRoute(a, MakeCity("d", Region.NorthAmerica, -1, 1, 50), 1_000),
            SpareSeatRoute(a, MakeCity("e", Region.NorthAmerica, -1, -1, 50), 1_000),
        };

        var (orders, spent) = Market(MakeMarketingContext(routes, Rivals(routes.Select(r => (r, 0)).ToArray()), availableCash: Money.FromDollars(1_000_000_000)));

        Assert.Single(orders.OfType<NetworkMarketingOrder>());
        Assert.Empty(orders.OfType<RouteMarketingOrder>());
        Assert.Equal(1, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_ContestedRoute_ShareGainThatPays_ProposesRouteMarketingOrder()
    {
        // One equal rival: a route campaign lifts this airline's share from
        // 1/2 to 1.5/2.5, a tenth of demand - ~$75.8M at $1,000, ~$37.9M net
        // of cost, ahead of the network campaign's smaller share gain.
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 50);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 50);
        var route = SpareSeatRoute(a, b, 1_000);

        var (orders, _) = Market(MakeMarketingContext(new[] { route }, Rivals((route, 1)), availableCash: Money.FromDollars(1_000_000_000)));

        Assert.Single(orders.OfType<RouteMarketingOrder>());
    }

    [Fact]
    public void ApplyMarketingCampaign_RealCarriedFillsTheSeats_NoOp()
    {
        // Last quarter's real carried figure (not the solo estimate) decides
        // spare seats: a full route has nothing to fill, however big the
        // aircraft.
        var route = SpareSeatRoute(MakeCity("a", Region.NorthAmerica, 0, 0, 50), MakeCity("b", Region.NorthAmerica, 1, 1, 50), 1_000);
        var carried = new System.Collections.Generic.Dictionary<(string, string), long>
        {
            [QuarterEngine.NormalizedPairKey(route.Origin, route.Destination)] = route.SeatsPerQuarter,
        };

        var (orders, _) = Market(MakeMarketingContext(new[] { route }, Rivals((route, 0)), availableCash: Money.FromDollars(1_000_000_000), lastQuarterSeatsCarriedByRoute: carried));

        Assert.Empty(orders);
    }

    [Fact]
    public void ApplyMarketingCampaign_LongUnprofitableStreak_StillMarketedWhenTheCampaignPays()
    {
        // M58's give-up rule is gone: a campaign that pays back is worth
        // running whatever the route's history, and one that doesn't never
        // clears the net-gain check in the first place.
        var route = SpareSeatRoute(MakeCity("a", Region.NorthAmerica, 0, 0, 50), MakeCity("b", Region.NorthAmerica, 1, 1, 50), 1_000);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory>
        {
            [QuarterEngine.NormalizedPairKey(route.Origin, route.Destination)] = new(LastShrunkQuarter: null, LastClosedQuarter: null, ConsecutiveUnprofitableQuarters: 10),
        };

        var (orders, _) = Market(MakeMarketingContext(new[] { route }, Rivals((route, 0)), availableCash: Money.FromDollars(1_000_000_000), routeHistoryByPair: history));

        Assert.Single(orders.OfType<RouteMarketingOrder>());
    }

    [Fact]
    public void ApplyMarketingCampaign_RouteCampaignUnaffordable_FallsBackToAffordableNetworkCampaign()
    {
        // $200k covers the $100k network campaign but not the ~$37.9M route one.
        var route = SpareSeatRoute(MakeCity("a", Region.NorthAmerica, 0, 0, 50), MakeCity("b", Region.NorthAmerica, 1, 1, 50), 1_000);

        var (orders, _) = Market(MakeMarketingContext(new[] { route }, Rivals((route, 0)), availableCash: Money.FromDollars(200_000)));

        Assert.Single(orders.OfType<NetworkMarketingOrder>());
        Assert.Empty(orders.OfType<RouteMarketingOrder>());
    }

    [Fact]
    public void ApplyMarketingCampaign_NeitherCampaignAffordable_NoOp()
    {
        var route = SpareSeatRoute(MakeCity("a", Region.NorthAmerica, 0, 0, 50), MakeCity("b", Region.NorthAmerica, 1, 1, 50), 1_000);

        var (orders, spent) = Market(MakeMarketingContext(new[] { route }, Rivals((route, 0)), availableCash: Money.FromDollars(50_000)));

        Assert.Empty(orders);
        Assert.Equal(0, spent);
    }

    [Fact]
    public void ApplyMarketingCampaign_NullAvailableCash_TreatedAsAffordable_PermissiveDefault()
    {
        var route = SpareSeatRoute(MakeCity("a", Region.NorthAmerica, 0, 0, 50), MakeCity("b", Region.NorthAmerica, 1, 1, 50), 1_000);

        var (orders, _) = Market(MakeMarketingContext(new[] { route }, Rivals((route, 0)), availableCash: null));

        Assert.Single(orders.OfType<RouteMarketingOrder>());
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

    // --- DistanceFromNetworkKm / MaxEraAircraftRangeKm (gateway-relevance follow-up) ---

    [Fact]
    public void DistanceFromNetworkKm_WithNoRoutesYet_MeasuresFromHomeCityOnly()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var candidate = MakeCity("far", Region.Europe, 10, 10, 50, isGateway: true);
        var context = MakeContext(new[] { home, candidate }, home);

        var expected = GreatCircle.DistanceKm(home, candidate);
        Assert.Equal(expected, AiToolkit.DistanceFromNetworkKm(context, candidate), precision: 3);
    }

    [Fact]
    public void DistanceFromNetworkKm_UsesTheNearestTouchedCity_NotJustHome()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var servedFarOut = MakeCity("mid", Region.NorthAmerica, 5, 5, 60);
        var candidate = MakeCity("far", Region.Europe, 6, 6, 50, isGateway: true);
        var routes = new[] { new Route(home, servedFarOut, Aircraft, AircraftCount: 1) };
        var context = MakeContext(new[] { home, servedFarOut, candidate }, home, routes: routes);

        // The airline's own route already reaches "mid", much closer to the
        // candidate than home is - the network's own footprint, not just
        // the starting city, is what should count.
        var fromHome = GreatCircle.DistanceKm(home, candidate);
        var fromMid = GreatCircle.DistanceKm(servedFarOut, candidate);
        Assert.True(fromMid < fromHome, "test fixture sanity: mid must really be closer to the candidate than home is");
        Assert.Equal(fromMid, AiToolkit.DistanceFromNetworkKm(context, candidate), precision: 3);
    }

    [Fact]
    public void MaxEraAircraftRangeKm_IsTheLongestAvailableType_NotTheOwnedFleet()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var shortRange = new AircraftType("short", "Short", Seats: 50, RangeKm: 1_000);
        var longRange = new AircraftType("long", "Long", Seats: 50, RangeKm: 9_000);
        var context = new StrategyContext(
            Array.Empty<Route>(), 3, new[] { home }, DemandModel, new[] { shortRange, longRange }, AirlineFleet.Empty, home.Region, home);

        Assert.Equal(9_000, AiToolkit.MaxEraAircraftRangeKm(context));
    }

    [Fact]
    public void MaxEraAircraftRangeKm_NoEraTypesAvailable_IsZero()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var context = new StrategyContext(
            Array.Empty<Route>(), 3, new[] { home }, DemandModel, Array.Empty<AircraftType>(), AirlineFleet.Empty, home.Region, home);

        Assert.Equal(0, AiToolkit.MaxEraAircraftRangeKm(context));
    }

    // --- TryProposeCandidate / TryOrientFromValidOrigin ---

    [Fact]
    public void TryProposeCandidate_BudgetAlreadySpent_ProposesNothing()
    {
        // Every current caller checks the budget itself before calling, so
        // this is the method's own guard for any caller that doesn't.
        var home = MakeCity("h", Region.NorthAmerica, 0, 0, 100);
        var other = MakeCity("o", Region.NorthAmerica, 1, 1, 100);
        var context = MakeContext(new[] { home, other }, home, actionCap: 3);
        (PairDemand, AircraftType, Money, bool)? best = (new PairDemand(home, other, 150, 1_000), Aircraft, Money.FromDollars(1), false);
        var validOrigins = new HashSet<string> { home.Id };

        var blockedOrders = new List<QuarterOrder>();
        var blockedSpent = 3;
        AiToolkit.TryProposeCandidate(context, best, validOrigins, blockedOrders, new HashSet<int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), ref blockedSpent);

        // Control: the identical call with one action left does propose,
        // so the empty result above really is the budget guard.
        var allowedOrders = new List<QuarterOrder>();
        var allowedSpent = 2;
        AiToolkit.TryProposeCandidate(context, best, validOrigins, allowedOrders, new HashSet<int>(), new Dictionary<string, int>(), new Dictionary<string, int>(), ref allowedSpent);

        Assert.Empty(blockedOrders);
        Assert.Equal(3, blockedSpent);
        Assert.IsType<LeaseAircraftOrder>(Assert.Single(allowedOrders));
        Assert.Equal(3, allowedSpent);
    }

    [Fact]
    public void TryOrientFromValidOrigin_NeitherEndpointValid_ReturnsFalse()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 100);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 100);

        var oriented = AiToolkit.TryOrientFromValidOrigin(new PairDemand(a, b, 150, 1_000), new HashSet<string> { "elsewhere" }, out _, out _);

        Assert.False(oriented);
    }

    [Fact]
    public void TryOrientFromValidOrigin_OnlyDestinationValid_FlipsOrientation()
    {
        var a = MakeCity("a", Region.NorthAmerica, 0, 0, 100);
        var b = MakeCity("b", Region.NorthAmerica, 1, 1, 100);

        var oriented = AiToolkit.TryOrientFromValidOrigin(new PairDemand(a, b, 150, 1_000), new HashSet<string> { b.Id }, out var origin, out var destination);

        Assert.True(oriented);
        Assert.Equal(b.Id, origin.Id);
        Assert.Equal(a.Id, destination.Id);
    }

    // --- CanOpenNow / new-route searches only consider routes that can be opened now ---

    private static StrategyContext MakeOpenabilityContext(
        IReadOnlyList<City> cities, City home, IReadOnlyDictionary<string, int>? availableSlots = null,
        IReadOnlyDictionary<string, int>? cityWideSlotsRemaining = null, AirlineFleet? fleet = null) =>
        new(Array.Empty<Route>(), 3, cities, DemandModel, new[] { Aircraft }, fleet ?? AirlineFleet.Empty, home.Region, home,
            AvailableSlotsByCity: availableSlots, CityWideSlotsRemaining: cityWideSlotsRemaining);

    private static bool CanOpen(StrategyContext context, City a, City b, Dictionary<string, int>? slotsReserved = null) =>
        AiToolkit.CanOpenNow(context, new PairDemand(a, b, 150, 1_000), new HashSet<string> { context.HomeCity.Id },
            slotsReserved ?? new Dictionary<string, int>(), new Dictionary<string, int>());

    [Fact]
    public void CanOpenNow_GatewayEndpointWithoutASlot_False()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var gateway = MakeCity("gw", Region.NorthAmerica, 1, 1, 50, isGateway: true);

        Assert.False(CanOpen(MakeOpenabilityContext(new[] { home, gateway }, home, availableSlots: new Dictionary<string, int>()), home, gateway));
    }

    [Fact]
    public void CanOpenNow_GatewayEndpointWithASlot_True()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var gateway = MakeCity("gw", Region.NorthAmerica, 1, 1, 50, isGateway: true);

        Assert.True(CanOpen(MakeOpenabilityContext(new[] { home, gateway }, home, availableSlots: new Dictionary<string, int> { ["gw"] = 1 }), home, gateway));
    }

    [Fact]
    public void CanOpenNow_OnlySlotAlreadyReservedEarlierThisQuarter_False()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var gateway = MakeCity("gw", Region.NorthAmerica, 1, 1, 50, isGateway: true);
        var context = MakeOpenabilityContext(new[] { home, gateway }, home, availableSlots: new Dictionary<string, int> { ["gw"] = 1 });

        Assert.False(CanOpen(context, home, gateway, slotsReserved: new Dictionary<string, int> { ["gw"] = 1 }));
    }

    [Fact]
    public void CanOpenNow_CityWithNoCapacityLeft_False()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var full = MakeCity("full", Region.NorthAmerica, 1, 1, 50, autoCapacity: 5);

        Assert.False(CanOpen(MakeOpenabilityContext(new[] { home, full }, home, cityWideSlotsRemaining: new Dictionary<string, int> { ["full"] = 0 }), home, full));
    }

    [Fact]
    public void CanOpenNow_NeitherEndpointAValidOrigin_False()
    {
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 50);
        var a = MakeCity("a", Region.NorthAmerica, 1, 1, 50);
        var b = MakeCity("b", Region.NorthAmerica, 2, 2, 50);

        Assert.False(CanOpen(MakeOpenabilityContext(new[] { home, a, b }, home), a, b));
    }

    [Fact]
    public void FindMostProfitablePair_TopPairHasNoGatewaySlot_ReturnsTheBestOpenableOneInstead()
    {
        // The heavier gateway pair is the most profitable, but the airline
        // holds no slot there, so the search returns the best pair it can
        // actually open. Control: holding a slot at the gateway, it wins.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var gateway = MakeCity("gw", Region.NorthAmerica, 1, 1, 100, isGateway: true);
        var plain = MakeCity("plain", Region.NorthAmerica, -1, -1, 50);
        var cities = new[] { home, gateway, plain };
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, Aircraft, quantity: 1, FleetOwnershipMode.Owned);
        var validOrigins = new HashSet<string> { home.Id };

        var blocked = AiToolkit.FindMostProfitablePair(
            MakeOpenabilityContext(cities, home, availableSlots: new Dictionary<string, int>(), fleet: fleet),
            new HashSet<int>(), validOrigins, _ => true);
        var control = AiToolkit.FindMostProfitablePair(
            MakeOpenabilityContext(cities, home, availableSlots: new Dictionary<string, int> { ["gw"] = 1 }, fleet: fleet),
            new HashSet<int>(), validOrigins, _ => true);

        Assert.NotNull(blocked);
        Assert.NotNull(control);
        Assert.Contains("plain", new[] { blocked.Value.Pair.Origin.Id, blocked.Value.Pair.Destination.Id });
        Assert.Contains("gw", new[] { control.Value.Pair.Origin.Id, control.Value.Pair.Destination.Id });
    }
}
