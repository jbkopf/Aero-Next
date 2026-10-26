using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class SelectStrategyTests
{
    private static City MakeCity(string id, Region region, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, false, null);

    private static readonly GravityDemandModel DemandModel = new();

    private static readonly City HomeCity = MakeCity("home", Region.NorthAmerica, 0, 0, 100);

    private static StrategyContext MakeContext(Money? cash) =>
        new(Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { HomeCity },
            DemandModel, Array.Empty<AircraftType>(), AirlineFleet.Empty, Region.NorthAmerica, HomeCity, AvailableCash: cash);

    [Fact]
    public void SelectStrategy_InitialAssignment_RichAirline_PicksRapidExpansion()
    {
        var context = MakeContext(Money.FromDollars(20_000_000)); // cashRatio ~1.0

        var chosen = BaselineAi.SelectStrategy(null, quarterNumber: 1, context);

        Assert.IsType<RapidExpansionStrategy>(chosen);
    }

    [Fact]
    public void SelectStrategy_InitialAssignment_PoorAirline_PicksDomesticProfitOptimizer()
    {
        var context = MakeContext(Money.FromDollars(2_000_000)); // cashRatio ~0.1

        var chosen = BaselineAi.SelectStrategy(null, quarterNumber: 1, context);

        Assert.IsType<DomesticProfitOptimizerStrategy>(chosen);
    }

    [Fact]
    public void SelectStrategy_ExactCashTie_BreaksDeterministicallyByName()
    {
        var context = MakeContext(Money.FromDollars(10_000_000)); // cashRatio exactly 0.5 for both strategies

        var chosen = BaselineAi.SelectStrategy(null, quarterNumber: 1, context);

        Assert.Equal("Domestic Profit Optimizer", chosen.Name); // "D" < "R" ordinally
    }

    [Fact]
    public void SelectStrategy_MidCycle_KeepsCurrentStrategy_RegardlessOfWhatSuitabilityWouldOtherwisePick()
    {
        var richContext = MakeContext(Money.FromDollars(20_000_000));
        var current = new DomesticProfitOptimizerStrategy(); // would NOT be picked fresh for this rich a context

        var chosen2 = BaselineAi.SelectStrategy(current, quarterNumber: 2, richContext);
        var chosen3 = BaselineAi.SelectStrategy(current, quarterNumber: 3, richContext);
        var chosen4 = BaselineAi.SelectStrategy(current, quarterNumber: 4, richContext);

        Assert.Same(current, chosen2);
        Assert.Same(current, chosen3);
        Assert.Same(current, chosen4);
    }

    [Fact]
    public void SelectStrategy_YearBoundary_ReEvaluatesAndCanSwitch()
    {
        var current = new DomesticProfitOptimizerStrategy();
        var richContext = MakeContext(Money.FromDollars(20_000_000));

        var chosen = BaselineAi.SelectStrategy(current, quarterNumber: 5, richContext); // start of year 2

        Assert.IsType<RapidExpansionStrategy>(chosen);
    }

    [Fact]
    public void SelectStrategy_YearBoundary_CanAlsoReconfirmTheSameStrategy()
    {
        var current = new RapidExpansionStrategy();
        var richContext = MakeContext(Money.FromDollars(20_000_000));

        var chosen = BaselineAi.SelectStrategy(current, quarterNumber: 5, richContext);

        Assert.IsType<RapidExpansionStrategy>(chosen);
    }

    [Fact]
    public void SelectStrategy_LateGame_MidCycle_KeepsCurrentRegardlessOfEraEligibility()
    {
        // Quarter 200 isn't a year boundary ((200-1)%4 != 0), so this
        // never even reaches the era/eligibility check - current is kept
        // unconditionally, same as SelectStrategy_MidCycle_... above,
        // just deeper into the campaign (LateGame) where, since M58,
        // RapidExpansionStrategy (Starting-only) would no longer be
        // era-eligible if this WERE a re-evaluation point.
        var current = new RapidExpansionStrategy();
        var context = MakeContext(Money.FromDollars(20_000_000));

        var chosen = BaselineAi.SelectStrategy(current, quarterNumber: 200, context);

        Assert.Same(current, chosen);
    }

    [Fact]
    public void SelectStrategy_LateGame_NoCurrent_HealthyNetwork_PicksBalancedGrowth()
    {
        // M58: NetworkOptimizerStrategy was the first strategy to cover
        // LateGame at all - this used to throw (no eligible strategy and
        // no current to fall back to). BalancedGrowthStrategy follow-up:
        // MakeContext's own empty-route fixture means
        // AiToolkit.NetworkTroubleRatio is exactly 0.0 (nothing to
        // optimize) - BalancedGrowthStrategy's own Suitability (the
        // direct complement, see its own class doc comment) is 1.0 and
        // wins. See the test below for the complementary, troubled-network
        // case.
        var context = MakeContext(Money.FromDollars(20_000_000));

        var chosen = BaselineAi.SelectStrategy(null, quarterNumber: 200, context);

        Assert.IsType<BalancedGrowthStrategy>(chosen);
    }

    [Fact]
    public void SelectStrategy_MidGame_TroubledNetwork_PicksNetworkOptimizer_RegardlessOfCash()
    {
        // M58 resume (round 3): DomesticProfitOptimizerStrategy is no
        // longer MidGame-eligible at all (see its own class doc comment
        // for why - its cash signal was structurally disqualified for
        // 90.3% of real MidGame/LateGame comparisons). BalancedGrowthStrategy
        // follow-up: a route unprofitable for 2 quarters running gives
        // AiToolkit.NetworkTroubleRatio a nonzero reading, so
        // NetworkOptimizerStrategy's own Suitability wins the
        // now-three-way MidGame/LateGame comparison regardless of cash -
        // the guaranteed partition (see NetworkOptimizerStrategy's own
        // class doc comment) rather than a cash-driven pick.
        var home = MakeCity("home", Region.NorthAmerica, 0, 0, 100);
        var away = MakeCity("away", Region.NorthAmerica, 1, 1, 100);
        var aircraft = new AircraftType("t", "Test", Seats: 100);
        var route = new Route(home, away, aircraft, AircraftCount: 1);
        var history = new System.Collections.Generic.Dictionary<(string, string), AiRouteHistory> { [QuarterEngine.NormalizedPairKey(home, away)] = new AiRouteHistory(null, null, ConsecutiveUnprofitableQuarters: 2) };

        StrategyContext MakeTroubledContext(Money cash) =>
            new(new[] { route }, QuarterEngine.BaseActionCap, new[] { home, away }, DemandModel, Array.Empty<AircraftType>(), AirlineFleet.Empty,
                Region.NorthAmerica, home, AvailableCash: cash, RouteHistoryByPair: history);

        var chosenPoor = BaselineAi.SelectStrategy(null, quarterNumber: 100, MakeTroubledContext(Money.FromDollars(2_000_000)));
        var chosenRich = BaselineAi.SelectStrategy(null, quarterNumber: 100, MakeTroubledContext(Money.FromDollars(20_000_000)));

        Assert.IsType<NetworkOptimizerStrategy>(chosenPoor);
        Assert.IsType<NetworkOptimizerStrategy>(chosenRich);
    }

    [Fact]
    public void Suitability_NullAvailableCash_IsTreatedAsNeutral()
    {
        // A permissive caller that never populates AvailableCash - the
        // shared AiSuitability.CashRatio's own null branch, exercised
        // through both strategies since it's internal.
        var context = MakeContext(null);

        Assert.Equal(0.5, new RapidExpansionStrategy().Suitability(context));
        Assert.Equal(0.5, new DomesticProfitOptimizerStrategy().Suitability(context));
    }

    [Fact]
    public void Strategies_ContainsExactlyTheFourBuiltStrategies()
    {
        Assert.Equal(4, BaselineAi.Strategies.Count);
        Assert.Contains(BaselineAi.Strategies, s => s is DomesticProfitOptimizerStrategy);
        Assert.Contains(BaselineAi.Strategies, s => s is RapidExpansionStrategy);
        Assert.Contains(BaselineAi.Strategies, s => s is NetworkOptimizerStrategy);
        Assert.Contains(BaselineAi.Strategies, s => s is BalancedGrowthStrategy);
    }

    /// <summary>
    /// Guards the precondition SelectStrategy's own "no strategy is
    /// eligible for this era and no current strategy exists to fall back
    /// to" InvalidOperationException relies on never occurring - every
    /// GameEra must have at least one covering strategy in the roster, or
    /// a fresh (no-current) SelectStrategy call for that era throws
    /// instead of returning one. Written as a roster-coverage invariant
    /// rather than a test that tries to force the throw itself: this
    /// catches the actual risk (a future edit narrows some strategy's
    /// ApplicableEras and leaves a gap) directly, at the point a change
    /// would introduce it, rather than mocking up an artificially
    /// incomplete roster to exercise a branch that should never be
    /// reachable with the real one.
    /// </summary>
    [Theory]
    [InlineData(GameEra.Starting)]
    [InlineData(GameEra.MidGame)]
    [InlineData(GameEra.LateGame)]
    public void Strategies_EveryGameEra_HasAtLeastOneCoveringStrategy(GameEra era)
    {
        Assert.Contains(BaselineAi.Strategies, s => s.ApplicableEras.Contains(era));
    }
}
