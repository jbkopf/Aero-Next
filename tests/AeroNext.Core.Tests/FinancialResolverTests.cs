using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class FinancialResolverTests
{
    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, false, null);

    private static readonly GravityDemandModel DemandModel = new();

    [Fact]
    public void Resolve_ProfitableRoute_IncreasesCash()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 100); // close + heavy: demand swamps capacity -> full load factor
        var aircraft = new AircraftType("t", "Test", Seats: 10);
        var airline = new Airline("A", new[] { new Route(a, b, aircraft, WeeklyRoundTrips: 1) });
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, b }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(1_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (newState, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.True(statement.NetIncome.Cents > 0); // fully sold out at yield > cost per seat-km
        Assert.True(newState.CashBalance > state.CashBalance);
        Assert.Equal(newState.CashBalance, statement.CashBalanceAfter);
        Assert.False(newState.IsBankrupt);
    }

    [Fact]
    public void QuarterEngine_Cost_RefinanceOrder_IsAlwaysFree()
    {
        Assert.Equal(0, QuarterEngine.Cost(new RefinanceOrder(Money.FromDollars(1_000)), Array.Empty<Route>()));
    }

    [Fact]
    public void Resolve_EmptyRoute_StillChargesOperatingCost_NoRevenue()
    {
        var a = MakeCity("a", 0, 0, 1);
        var b = MakeCity("b", 60, 60, 1); // far + light: negligible demand
        var aircraft = new AircraftType("t", "Test", Seats: 400);
        var airline = new Airline("A", new[] { new Route(a, b, aircraft, WeeklyRoundTrips: 10) });
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, b }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(1_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.True(statement.OperatingCost.Cents > 0); // paid to fly the empty capacity
        Assert.True(statement.NetIncome.Cents < 0); // cost with essentially no offsetting revenue
    }

    [Fact]
    public void Resolve_DebtService_IsInterestRateTimesDebtBalance()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(1_000_000), Money.FromDollars(100_000), interestRatePerQuarter: 0.05);

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.Equal(Money.FromDollars(5_000), statement.DebtService); // 5% of 100,000
    }

    [Fact]
    public void Resolve_CashGoesNegative_TriggersBankruptcy()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(100), Money.FromDollars(1_000_000), interestRatePerQuarter: 0.5);

        var (newState, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.True(statement.IsBankrupt);
        Assert.True(newState.IsBankrupt);
        Assert.True(newState.CashBalance < Money.Zero);
    }

    [Fact]
    public void Resolve_DebtExceedsCeiling_TriggersBankruptcy_EvenWithPositiveCash()
    {
        // M23: closes the infinite-refinance gap - without this trigger, an
        // airline could stay technically solvent forever by borrowing just
        // enough cash each quarter, no matter how deep its debt hole is.
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new RefinanceOrder(Money.FromDollars(FinancialResolver.DebtCeilingDollars + 1)) };

        var (newState, statement) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.True(newState.CashBalance > Money.Zero); // cash alone would not have triggered it
        Assert.True(statement.IsBankrupt);
        Assert.True(newState.IsBankrupt);
    }

    [Fact]
    public void Resolve_DebtAtOrBelowCeiling_DoesNotTriggerBankruptcy_OnDebtAlone()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new RefinanceOrder(Money.FromDollars(FinancialResolver.DebtCeilingDollars)) };

        var (newState, _) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.False(newState.IsBankrupt);
    }

    [Fact]
    public void Resolve_AlreadyBankrupt_NoFurtherActivity_CashUnchanged()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 100);
        var aircraft = new AircraftType("t", "Test", Seats: 10);
        var airline = new Airline("A", new[] { new Route(a, b, aircraft, WeeklyRoundTrips: 1) });
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, b }, DemandModel);
        var bankruptState = FinancialState.Start(Money.FromDollars(-500), Money.FromDollars(1_000_000), 0.05) with { IsBankrupt = true };

        var (newState, statement) = FinancialResolver.Resolve(bankruptState, airline, market, Array.Empty<QuarterOrder>());

        Assert.Equal(0, statement.Revenue.Cents);
        Assert.Equal(0, statement.OperatingCost.Cents);
        Assert.Equal(0, statement.DebtService.Cents);
        Assert.Equal(bankruptState.CashBalance, newState.CashBalance);
        Assert.True(newState.IsBankrupt);
    }

    [Fact]
    public void Resolve_RefinanceOrder_IncreasesCashAndDebtByTheSameAmount()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(1_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new RefinanceOrder(Money.FromDollars(50_000)) };

        var (newState, statement) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.Equal(Money.FromDollars(50_000), statement.Borrowed);
        Assert.Equal(Money.FromDollars(50_000), newState.DebtBalance);
        Assert.Equal(state.CashBalance + Money.FromDollars(50_000), newState.CashBalance);
    }

    [Fact]
    public void Resolve_Refinancing_CanAvoidBankruptcy_ThatWouldOtherwiseTrigger()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(100), Money.FromDollars(10_000), interestRatePerQuarter: 0.1); // debt service = $1,000 > cash

        var (withoutRefinance, _) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());
        var (withRefinance, statement) = FinancialResolver.Resolve(state, airline, market, new QuarterOrder[] { new RefinanceOrder(Money.FromDollars(5_000)) });

        Assert.True(withoutRefinance.IsBankrupt);
        Assert.False(withRefinance.IsBankrupt);
        Assert.False(statement.IsBankrupt);
    }

    [Fact]
    public void Resolve_OperatingCost_ScalesExactlyWithAircraftsOwnRate()
    {
        // Operating cost is per-aircraft now, not one flat global rate —
        // two airlines flying the identical route/seats/frequency, differing
        // only in CostPerAvailableSeatKm, should differ in operating cost by
        // exactly the same ratio as their rates.
        var a = MakeCity("a", 0, 0, 1);
        var b = MakeCity("b", 60, 60, 1); // far + light: negligible demand, isolates cost from revenue
        var lowRate = new AircraftType("low", "Low", Seats: 100, CostPerAvailableSeatKm: 0.05);
        var highRate = new AircraftType("high", "High", Seats: 100, CostPerAvailableSeatKm: 0.20);

        var lowAirline = new Airline("Low", new[] { new Route(a, b, lowRate, WeeklyRoundTrips: 5) });
        var highAirline = new Airline("High", new[] { new Route(a, b, highRate, WeeklyRoundTrips: 5) });

        var lowMarket = CompetitiveMarket.Resolve(new[] { lowAirline }, new[] { a, b }, DemandModel);
        var highMarket = CompetitiveMarket.Resolve(new[] { highAirline }, new[] { a, b }, DemandModel);

        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (_, lowStatement) = FinancialResolver.Resolve(state, lowAirline, lowMarket, Array.Empty<QuarterOrder>());
        var (_, highStatement) = FinancialResolver.Resolve(state, highAirline, highMarket, Array.Empty<QuarterOrder>());

        Assert.True(lowStatement.OperatingCost.Cents > 0);
        Assert.Equal(highStatement.OperatingCost.Cents, lowStatement.OperatingCost.Cents * 4); // 0.20 / 0.05 = 4x
    }

    [Fact]
    public void Resolve_NonCarryingAirlineInCompetitiveMarket_OnlyPaysItsOwnOperatingCost()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 100);
        var bigAircraft = new AircraftType("t", "Test", Seats: 1000);
        var winner = new Airline("Winner", new[] { new Route(a, b, bigAircraft, WeeklyRoundTrips: 10) });
        var loser = new Airline("Loser", new[] { new Route(a, b, bigAircraft, WeeklyRoundTrips: 1) }, Quality: 0.0); // effectively no competitive weight
        var market = CompetitiveMarket.Resolve(new[] { winner, loser }, new[] { a, b }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (_, loserStatement) = FinancialResolver.Resolve(state, loser, market, Array.Empty<QuarterOrder>());

        Assert.True(loserStatement.OperatingCost.Cents > 0); // still pays for its own capacity
    }

    // --- M26: aircraft purchase, lease fee, and retirement proceeds ---

    private static readonly AircraftType CostlyAircraft = new("costly", "Costly", Seats: 100, Cost: Money.FromDollars(1_000_000));

    [Fact]
    public void Resolve_AircraftOrder_DeductsPurchaseCost_ReflectedInNetIncomeAndStatement()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new AircraftOrder(CostlyAircraft, Quantity: 2) };

        var (newState, statement) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.Equal(Money.FromDollars(2_000_000), statement.AircraftPurchaseCost);
        Assert.Equal(state.CashBalance - Money.FromDollars(2_000_000), newState.CashBalance);
    }

    [Fact]
    public void RunQuarter_RejectsUnaffordableAircraftOrder_BeforeFinancialResolverEverSeesItApplied()
    {
        // Integration through both: QuarterEngine's own affordability gate
        // must stop an order this expensive before it ever reaches
        // FinancialResolver, not rely on FinancialResolver to catch it.
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(500_000), Money.Zero, interestRatePerQuarter: 0.0);
        var order = new AircraftOrder(CostlyAircraft, Quantity: 1);

        var quarterResult = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, new[] { a }, DemandModel, availableCash: state.CashBalance);
        Assert.Empty(quarterResult.AppliedOrders);

        var (newState, statement) = FinancialResolver.Resolve(state, airline, market, quarterResult.AppliedOrders);

        Assert.Equal(Money.Zero, statement.AircraftPurchaseCost);
        Assert.Equal(state.CashBalance, newState.CashBalance);
    }

    [Fact]
    public void Resolve_LeaseFee_ChargedPerLeasedUnit_ScalesWithUnitCount()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var oneLeased = FleetLedger.ApplyDelivery(AirlineFleet.Empty, CostlyAircraft, quantity: 1, FleetOwnershipMode.Leased);
        var threeLeased = FleetLedger.ApplyDelivery(AirlineFleet.Empty, CostlyAircraft, quantity: 3, FleetOwnershipMode.Leased);

        var (_, oneStatement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), fleet: oneLeased);
        var (_, threeStatement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), fleet: threeLeased);

        var expectedPerUnit = Money.FromDollars(40_000); // 4% of $1,000,000
        Assert.Equal(expectedPerUnit, oneStatement.LeaseFee);
        Assert.Equal(oneStatement.LeaseFee * 3, threeStatement.LeaseFee);
    }

    [Fact]
    public void Resolve_NoFleetPassed_NoLeaseFeeCharged()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.Equal(Money.Zero, statement.LeaseFee);
    }

    [Fact]
    public void Resolve_OwnedFleetUnits_DoNotCarryALeaseFee()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var owned = FleetLedger.ApplyDelivery(AirlineFleet.Empty, CostlyAircraft, quantity: 5, FleetOwnershipMode.Owned);

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), fleet: owned);

        Assert.Equal(Money.Zero, statement.LeaseFee);
    }

    [Fact]
    public void Resolve_RetiringOwnedUnit_CreditsFlatResaleFraction()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(1_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var retired = new[] { new FleetUnit(1, CostlyAircraft, FleetOwnershipMode.Owned, AssignedRouteKey: null) };

        var (newState, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), retiredUnits: retired);

        Assert.Equal(Money.FromDollars(400_000), statement.RetirementProceeds); // 40% of $1,000,000
        Assert.Equal(state.CashBalance + Money.FromDollars(400_000), newState.CashBalance);
    }

    [Fact]
    public void Resolve_RetiringLeasedUnit_CreditsNothing()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(1_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var retired = new[] { new FleetUnit(1, CostlyAircraft, FleetOwnershipMode.Leased, AssignedRouteKey: null) };

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), retiredUnits: retired);

        Assert.Equal(Money.Zero, statement.RetirementProceeds);
    }

    [Fact]
    public void Resolve_UnitRetiredThisQuarter_NotInPostQuarterFleet_NotDoubleChargedLeaseFee()
    {
        // The `fleet` parameter is the *post*-quarter fleet, per its own
        // documented contract — a unit retired this quarter is already
        // gone from it, so its lease fee doesn't accrue the same quarter
        // it stops existing.
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(1_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var postQuarterFleet = AirlineFleet.Empty; // the retired unit is already gone
        var retired = new[] { new FleetUnit(1, CostlyAircraft, FleetOwnershipMode.Leased, AssignedRouteKey: null) };

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), fleet: postQuarterFleet, retiredUnits: retired);

        Assert.Equal(Money.Zero, statement.LeaseFee);
        Assert.Equal(Money.Zero, statement.RetirementProceeds); // leased -> nothing either way
    }
}
