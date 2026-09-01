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
        var airline = new Airline("A", new[] { new Route(a, b, aircraft, AircraftCount: 1) });
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
        var airline = new Airline("A", new[] { new Route(a, b, aircraft, AircraftCount: 1) });
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, b }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(1_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.True(statement.OperatingCost.Cents > 0); // paid to fly the empty capacity
        Assert.True(statement.NetIncome.Cents < 0); // cost with essentially no offsetting revenue
    }

    [Fact]
    public void Resolve_DebtService_IsInterestPlusPrincipalRepayment_BothTimesDebtBalance()
    {
        // M29: DebtService now means interest + automatic principal
        // repayment combined - PrincipalRepayment is broken out
        // separately so DebtService - PrincipalRepayment recovers
        // interest alone.
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(1_000_000), Money.FromDollars(100_000), interestRatePerQuarter: 0.05);

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.Equal(Money.FromDollars(5_000), statement.PrincipalRepayment); // 5% of 100,000
        Assert.Equal(Money.FromDollars(10_000), statement.DebtService); // 5% interest + 5% principal of 100,000
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
        // M29: the ceiling is now checked against the balance *after* this
        // same quarter's automatic 5% principal repayment, so borrowing
        // merely 1 dollar over the ceiling (the old amount here) no longer
        // triggers it on its own - borrow comfortably past ceiling/0.95
        // instead, so the post-repayment balance is still well over it.
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new RefinanceOrder(Money.FromDollars(100_000_000)) };

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
        var airline = new Airline("A", new[] { new Route(a, b, aircraft, AircraftCount: 1) });
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
    public void Resolve_RefinanceOrder_IncreasesCashAndDebt_ThenAutomaticPrincipalAppliesTheSameQuarter()
    {
        // M29: the freshly-borrowed balance isn't exempt from this same
        // quarter's automatic principal repayment - PrincipalRepaymentRatePerQuarter
        // applies regardless of InterestRatePerQuarter (even at 0% interest,
        // as here), so DebtBalance ends up below the raw Borrowed amount
        // and cash absorbs that same charge.
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(1_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new RefinanceOrder(Money.FromDollars(50_000)) };

        var (newState, statement) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.Equal(Money.FromDollars(50_000), statement.Borrowed);
        Assert.Equal(Money.FromDollars(2_500), statement.PrincipalRepayment); // 5% of the freshly-borrowed 50,000
        Assert.Equal(Money.FromDollars(47_500), newState.DebtBalance); // 50,000 - 2,500 automatic principal
        Assert.Equal(state.CashBalance + Money.FromDollars(50_000) - Money.FromDollars(2_500), newState.CashBalance);
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

        var lowAirline = new Airline("Low", new[] { new Route(a, b, lowRate, AircraftCount: 1) });
        var highAirline = new Airline("High", new[] { new Route(a, b, highRate, AircraftCount: 1) });

        var lowMarket = CompetitiveMarket.Resolve(new[] { lowAirline }, new[] { a, b }, DemandModel);
        var highMarket = CompetitiveMarket.Resolve(new[] { highAirline }, new[] { a, b }, DemandModel);

        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (_, lowStatement) = FinancialResolver.Resolve(state, lowAirline, lowMarket, Array.Empty<QuarterOrder>());
        var (_, highStatement) = FinancialResolver.Resolve(state, highAirline, highMarket, Array.Empty<QuarterOrder>());

        Assert.True(lowStatement.OperatingCost.Cents > 0);
        // 0.20 / 0.05 = 4x, within a cent — each side rounds independently
        // (Math.Round on its own seats*distance*rate product), so an exact
        // match isn't guaranteed at the cent level.
        Assert.InRange(highStatement.OperatingCost.Cents, lowStatement.OperatingCost.Cents * 4 - 1, lowStatement.OperatingCost.Cents * 4 + 1);
    }

    [Fact]
    public void Resolve_NonCarryingAirlineInCompetitiveMarket_OnlyPaysItsOwnOperatingCost()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 100);
        var bigAircraft = new AircraftType("t", "Test", Seats: 1000);
        var winner = new Airline("Winner", new[] { new Route(a, b, bigAircraft, AircraftCount: 1) });
        var loser = new Airline("Loser", new[] { new Route(a, b, bigAircraft, AircraftCount: 1) }, Quality: 0.0); // effectively no competitive weight
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

    // --- M28: NegotiationOrder bids cost real cash ---

    [Fact]
    public void Resolve_NegotiationOrder_DeductsBidCost_ReflectedInNetIncomeAndStatement()
    {
        var a = MakeCity("a", 0, 0, 1);
        var gateway = MakeCity("gw", 30, 30, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, gateway }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new NegotiationOrder(gateway, Money.FromDollars(2_000_000)) };

        var (newState, statement) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.Equal(Money.FromDollars(2_000_000), statement.NegotiationBidCost);
        Assert.Equal(state.CashBalance - Money.FromDollars(2_000_000), newState.CashBalance);
    }

    [Fact]
    public void Resolve_NoNegotiationOrders_NegotiationBidCostIsZero()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.Equal(Money.Zero, statement.NegotiationBidCost);
    }

    [Fact]
    public void Resolve_NegotiationBid_IsChargedRegardlessOfEventualWinOrLoss()
    {
        // FinancialResolver has no notion of the negotiation's outcome at
        // all - it charges purely from `appliedOrders` this quarter,
        // 2-4 quarters before LandingRightsResolver ever decides who wins.
        var a = MakeCity("a", 0, 0, 1);
        var gateway = MakeCity("gw", 30, 30, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, gateway }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new NegotiationOrder(gateway, Money.FromDollars(500_000)) };

        var (newState, _) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.Equal(state.CashBalance - Money.FromDollars(500_000), newState.CashBalance);
    }

    // --- M29: automatic principal amortization + manual paydown ---

    [Fact]
    public void Resolve_UntouchedDebtBalance_ShrinksQuarterOverQuarter_WithNoPlayerAction()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.FromDollars(100_000), interestRatePerQuarter: 0.0);

        var (afterQ1, _) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());
        var (afterQ2, _) = FinancialResolver.Resolve(afterQ1, airline, market, Array.Empty<QuarterOrder>());

        Assert.Equal(Money.FromDollars(95_000), afterQ1.DebtBalance); // 5% automatic principal, no player action
        Assert.True(afterQ2.DebtBalance < afterQ1.DebtBalance);
        Assert.True(afterQ2.DebtBalance > Money.Zero); // declining-balance - shrinks, never reaches exactly zero
    }

    [Fact]
    public void Resolve_ManualPaydown_ReducesBalanceImmediately_AndShrinksNextQuartersAutomaticCharge()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.FromDollars(100_000), interestRatePerQuarter: 0.0);
        var paydownOrders = new QuarterOrder[] { new PayDownDebtOrder(Money.FromDollars(20_000)) };

        var (withPaydown, statement) = FinancialResolver.Resolve(state, airline, market, paydownOrders);
        var (withoutPaydown, _) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.Equal(Money.FromDollars(20_000), statement.ManualDebtPaydown);
        Assert.Equal(Money.FromDollars(76_000), withPaydown.DebtBalance); // (100,000 - 20,000) * 0.95
        Assert.True(withPaydown.DebtBalance < withoutPaydown.DebtBalance); // immediately smaller this same quarter

        var (nextWithPaydown, nextStatementWithPaydown) = FinancialResolver.Resolve(withPaydown, airline, market, Array.Empty<QuarterOrder>());
        var (_, nextStatementWithoutPaydown) = FinancialResolver.Resolve(withoutPaydown, airline, market, Array.Empty<QuarterOrder>());

        Assert.True(nextStatementWithPaydown.PrincipalRepayment.Cents < nextStatementWithoutPaydown.PrincipalRepayment.Cents);
        Assert.NotNull(nextWithPaydown); // sanity: the paid-down path keeps resolving normally
    }

    [Fact]
    public void Resolve_PayDownDebtOrder_ExceedingDebtBalance_IsQuarterEnginesJob_NotClampedHereEither()
    {
        // FinancialResolver itself never rejects anything - QuarterEngine
        // is the sole place an order is accepted or rejected (M29's own
        // affordability rule). This documents that FinancialResolver just
        // applies whatever Amount it's handed, trusting QuarterEngine
        // already validated it - if it somehow received an oversized
        // amount directly (bypassing QuarterEngine), debt legitimately
        // goes negative rather than silently clamping.
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.FromDollars(10_000), interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new PayDownDebtOrder(Money.FromDollars(15_000)) };

        var (newState, _) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.True(newState.DebtBalance < Money.Zero);
    }

    [Fact]
    public void Resolve_BorrowedAndManualDebtPaydown_StayOutsideNetIncome_LikeBorrowedAlreadyDid()
    {
        // Equal-and-opposite borrow + paydown in one quarter nets to zero
        // change in the balance used for this quarter's own automatic
        // interest/principal calc - NetIncome, CashBalance, and DebtBalance
        // should all come out identical to a quarter with neither order.
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.FromDollars(100_000), interestRatePerQuarter: 0.02);
        var orders = new QuarterOrder[] { new RefinanceOrder(Money.FromDollars(50_000)), new PayDownDebtOrder(Money.FromDollars(50_000)) };

        var (withBoth, statementWithBoth) = FinancialResolver.Resolve(state, airline, market, orders);
        var (withNeither, statementWithNeither) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.Equal(statementWithNeither.NetIncome, statementWithBoth.NetIncome);
        Assert.Equal(withNeither.CashBalance, withBoth.CashBalance);
        Assert.Equal(withNeither.DebtBalance, withBoth.DebtBalance);
    }

    // --- M30: general salaries, a flat per-aircraft headcount cost ---

    [Fact]
    public void Resolve_NullFleet_ChargesZeroSalaryCost_PreservesEveryPreM30CallSite()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.Equal(Money.Zero, statement.SalaryCost);
    }

    [Fact]
    public void Resolve_EmptyFleet_ChargesZeroSalaryCost()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), fleet: AirlineFleet.Empty);

        Assert.Equal(Money.Zero, statement.SalaryCost);
    }

    /// <summary>M34 amendment: an idle unit's rate, derived the same way FinancialResolver.ComputeSalaryCost computes it.</summary>
    private static Money IdleSalaryRate() =>
        new((long)Math.Round(FinancialResolver.SalaryCostPerAircraftPerQuarter.Cents * FinancialResolver.IdleSalaryCostFraction, MidpointRounding.AwayFromZero));

    [Fact]
    public void Resolve_SalaryCost_ScalesLinearlyWithFleetSize_AssignedAndIdleMixed()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var owned = FleetLedger.ApplyDelivery(AirlineFleet.Empty, CostlyAircraft, quantity: 2, FleetOwnershipMode.Owned);
        var fleet = FleetLedger.ApplyDelivery(owned, CostlyAircraft, quantity: 3, FleetOwnershipMode.Leased); // 5 units total
        fleet = FleetLedger.Assign(fleet, fleet.Units[0].Id, "a-b");
        fleet = FleetLedger.Assign(fleet, fleet.Units[1].Id, "a-b"); // 2 assigned, 3 idle

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), fleet: fleet);

        Assert.Equal(FinancialResolver.SalaryCostPerAircraftPerQuarter * 2 + IdleSalaryRate() * 3, statement.SalaryCost);
    }

    [Fact]
    public void Resolve_SalaryCost_IdleUnit_CostsLessThanAssignedUnit()
    {
        // M34 amendment to M30: a flat rate regardless of assignment meant
        // a starting airline that leaves one aircraft unassigned bled cash
        // forever with no path back short of retiring it (see
        // docs/FINANCIAL_MODEL_ROADMAP.md's M34 section) -- an idle unit
        // now costs a fraction of a flying one, not the same amount.
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var idleFleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, CostlyAircraft, quantity: 1, FleetOwnershipMode.Owned);
        var assignedFleet = FleetLedger.Assign(idleFleet, idleFleet.Units.Single().Id, "a-b");

        var (_, idleStatement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), fleet: idleFleet);
        var (_, assignedStatement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), fleet: assignedFleet);

        Assert.Equal(IdleSalaryRate(), idleStatement.SalaryCost);
        Assert.Equal(FinancialResolver.SalaryCostPerAircraftPerQuarter, assignedStatement.SalaryCost);
        Assert.True(idleStatement.SalaryCost < assignedStatement.SalaryCost);
        Assert.True(idleStatement.SalaryCost.Cents > 0); // idle still costs something, just less
    }

    [Fact]
    public void Resolve_SalaryCost_IsFoldedIntoNetIncome_ReducesCash()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, CostlyAircraft, quantity: 2, FleetOwnershipMode.Owned);
        fleet = FleetLedger.Assign(fleet, fleet.Units[0].Id, "a-b");
        fleet = FleetLedger.Assign(fleet, fleet.Units[1].Id, "a-b"); // both assigned, full rate applies to both

        var (withFleet, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), fleet: fleet);
        var (withoutFleet, _) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.Equal(FinancialResolver.SalaryCostPerAircraftPerQuarter * 2, statement.SalaryCost);
        Assert.Equal(withoutFleet.CashBalance - statement.SalaryCost, withFleet.CashBalance);
    }

    // --- M34: fares ---

    [Fact]
    public void Resolve_Revenue_EqualsFarePerPassengerTimesSeatsUsed_NotSeatsOffered()
    {
        var a = MakeCity("a", 0, 0, 1);
        var b = MakeCity("b", 1, 1, 1);
        var aircraft = new AircraftType("t", "Test", Seats: 400); // demand at this distance won't fill it - real load factor < 100%
        var route = new Route(a, b, aircraft, AircraftCount: 1, FareOverride: Money.FromDollars(100));
        var airline = new Airline("A", new[] { route });
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, b }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var seatsUsed = market.RouteUsage.Single().SeatsUsed;

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.True(seatsUsed > 0);
        Assert.True(seatsUsed < route.SeatsPerQuarter); // genuinely load-factor-constrained, not sold out
        Assert.Equal(Money.FromDollars(100) * seatsUsed, statement.Revenue);
    }

    [Fact]
    public void Resolve_Revenue_ReflectsACustomFareOverride_NotJustTheComputedDefault()
    {
        var a = MakeCity("a", 0, 0, 100);
        var b = MakeCity("b", 1, 1, 100); // close + heavy: demand swamps capacity -> full, known load factor
        var aircraft = new AircraftType("t", "Test", Seats: 10);
        var defaultRoute = new Route(a, b, aircraft, AircraftCount: 1);
        var expensiveRoute = new Route(a, b, aircraft, AircraftCount: 1, FareOverride: defaultRoute.FarePerPassenger * 2);
        var defaultAirline = new Airline("Default", new[] { defaultRoute });
        var expensiveAirline = new Airline("Expensive", new[] { expensiveRoute });
        var defaultMarket = CompetitiveMarket.Resolve(new[] { defaultAirline }, new[] { a, b }, DemandModel);
        var expensiveMarket = CompetitiveMarket.Resolve(new[] { expensiveAirline }, new[] { a, b }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (_, defaultStatement) = FinancialResolver.Resolve(state, defaultAirline, defaultMarket, Array.Empty<QuarterOrder>());
        var (_, expensiveStatement) = FinancialResolver.Resolve(state, expensiveAirline, expensiveMarket, Array.Empty<QuarterOrder>());

        Assert.Equal(defaultStatement.Revenue * 2, expensiveStatement.Revenue); // same carried count both sides (both fully sold out), double the fare
    }

    [Fact]
    public void Resolve_StartingScenario_NoBankruptcy_FullStartingFleetAssigned_90PercentLoadFactorHeldIndefinitely()
    {
        // The user's own invariant, verified by hand in
        // docs/FINANCIAL_MODEL_ROADMAP.md's M34 section before this test
        // existed: $0 starting debt, one valid route opened turn one
        // using the real 1955 starting aircraft allotment (both units
        // assigned), 90% load factor held indefinitely, no other
        // spending -- the airline must never go bankrupt. Reproduced
        // directly against the real Ilyushin Il-14's stats (data/aircraft.json)
        // and StartingConditionsResolver.StartingAircraftQuantity (2).
        var il14 = new AircraftType("il14", "Ilyushin Il-14", Seats: 28, RangeKm: 1600, Cost: Money.FromDollars(300_000), CostPerAvailableSeatKm: 0.13, CruiseSpeedKmh: 350);
        var origin = MakeCity("origin", 0, 0, 100);
        var destination = MakeCity("destination", 8, 8, 100);
        var route = new Route(origin, destination, il14, AircraftCount: 2); // both starting aircraft on the one route
        Assert.True(route.IsWithinRange); // sanity check this is actually a valid opening route for the Il-14

        var airline = new Airline("A", new[] { route });
        var fleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, il14, quantity: 2, FleetOwnershipMode.Owned);
        fleet = FleetLedger.Assign(fleet, fleet.Units[0].Id, "origin-destination");
        fleet = FleetLedger.Assign(fleet, fleet.Units[1].Id, "origin-destination");

        var seatsUsed = (long)Math.Round(route.SeatsPerQuarter * 0.90, MidpointRounding.AwayFromZero); // 90% load factor, held every quarter below
        var pair = new CompetitivePairResult(origin, destination, seatsUsed, new Dictionary<Airline, long> { [airline] = seatsUsed });
        var routeUsage = new[] { new CompetitiveRouteUsage(airline, route, route.SeatsPerQuarter, seatsUsed) };
        var market = new CompetitiveMarketResult(new[] { pair }, routeUsage);

        var state = FinancialState.Start(Money.Zero, Money.Zero, interestRatePerQuarter: 0.02); // $0 starting cash -- the strictest possible check: net income alone must never go negative

        for (var quarter = 1; quarter <= 40; quarter++)
        {
            var (newState, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>(), fleet: fleet);

            Assert.True(statement.NetIncome.Cents >= 0, $"Quarter {quarter}: net income was {statement.NetIncome}, cash was {newState.CashBalance}");
            Assert.False(newState.IsBankrupt);

            state = newState;
        }
    }

    // --- M47: marketing campaign costs ---

    [Fact]
    public void Resolve_NetworkMarketingOrder_DeductsCostScaledByRouteCount()
    {
        var a = MakeCity("a", 0, 0, 1);
        var b = MakeCity("b", 1, 1, 1);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var airline = new Airline("A", new[] { new Route(a, b, aircraft, AircraftCount: 1) }); // 1 route
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a, b }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new NetworkMarketingOrder() };

        var (newState, statement) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.Equal(MarketingCostCalculator.NetworkCampaignCost(1), statement.MarketingCost);
        Assert.Equal(newState.CashBalance, statement.CashBalanceAfter);
    }

    [Fact]
    public void Resolve_RouteMarketingOrder_DeductsCostScaledByThatPairsOwnDemand()
    {
        var origin = MakeCity("origin", 0, 0, 1);
        var destination = MakeCity("destination", 1, 1, 1);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var route = new Route(origin, destination, aircraft, AircraftCount: 1);
        var airline = new Airline("A", new[] { route });
        const long demand = 4_000;
        var pair = new CompetitivePairResult(origin, destination, demand, new Dictionary<Airline, long> { [airline] = 100 });
        var market = new CompetitiveMarketResult(new[] { pair }, new[] { new CompetitiveRouteUsage(airline, route, route.SeatsPerQuarter, 100) });
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new RouteMarketingOrder(origin, destination) };

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.Equal(MarketingCostCalculator.RouteCampaignCost(demand), statement.MarketingCost);
    }

    [Fact]
    public void Resolve_RouteMarketingOrder_MatchesPairRegardlessOfOrientation()
    {
        // market.Pairs stores this pair as (origin, destination); the
        // applied order names it in the reverse orientation - the lookup
        // must still find the right demand figure via the normalized key.
        var origin = MakeCity("origin", 0, 0, 1);
        var destination = MakeCity("destination", 1, 1, 1);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var route = new Route(origin, destination, aircraft, AircraftCount: 1);
        var airline = new Airline("A", new[] { route });
        const long demand = 6_000;
        var pair = new CompetitivePairResult(origin, destination, demand, new Dictionary<Airline, long> { [airline] = 100 });
        var market = new CompetitiveMarketResult(new[] { pair }, new[] { new CompetitiveRouteUsage(airline, route, route.SeatsPerQuarter, 100) });
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new RouteMarketingOrder(destination, origin) }; // reversed

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.Equal(MarketingCostCalculator.RouteCampaignCost(demand), statement.MarketingCost);
    }

    [Fact]
    public void Resolve_RouteMarketingOrder_PairNotInMarketResult_CostsZero_NotAThrow()
    {
        // A real, unrelated pair is present (forcing the lookup's own
        // equality check to actually evaluate false at least once, not
        // just fall through an empty list), but never the targeted one.
        var origin = MakeCity("origin", 0, 0, 1);
        var destination = MakeCity("destination", 1, 1, 1);
        var unrelatedA = MakeCity("ua", 10, 10, 1);
        var unrelatedB = MakeCity("ub", 11, 11, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var unrelatedPair = new CompetitivePairResult(unrelatedA, unrelatedB, 500, new Dictionary<Airline, long>());
        var market = new CompetitiveMarketResult(new[] { unrelatedPair }, Array.Empty<CompetitiveRouteUsage>());
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);
        var orders = new QuarterOrder[] { new RouteMarketingOrder(origin, destination) };

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, orders);

        Assert.Equal(Money.Zero, statement.MarketingCost);
    }

    [Fact]
    public void Resolve_NoMarketingOrders_MarketingCostIsZero()
    {
        var a = MakeCity("a", 0, 0, 1);
        var airline = new Airline("A", Array.Empty<Route>());
        var market = CompetitiveMarket.Resolve(new[] { airline }, new[] { a }, DemandModel);
        var state = FinancialState.Start(Money.FromDollars(10_000_000), Money.Zero, interestRatePerQuarter: 0.0);

        var (_, statement) = FinancialResolver.Resolve(state, airline, market, Array.Empty<QuarterOrder>());

        Assert.Equal(Money.Zero, statement.MarketingCost);
    }
}
