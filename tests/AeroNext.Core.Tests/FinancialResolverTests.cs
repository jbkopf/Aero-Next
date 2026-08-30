using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
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
}
