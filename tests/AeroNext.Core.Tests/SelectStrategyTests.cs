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
    public void SelectStrategy_LateGame_NoCurrent_PicksNetworkOptimizer()
    {
        // M58: NetworkOptimizerStrategy is the first strategy to cover
        // LateGame at all - this used to throw (no eligible strategy and
        // no current to fall back to); now it resolves for real.
        var context = MakeContext(Money.FromDollars(20_000_000));

        var chosen = BaselineAi.SelectStrategy(null, quarterNumber: 200, context);

        Assert.IsType<NetworkOptimizerStrategy>(chosen);
    }

    [Fact]
    public void SelectStrategy_MidGame_DomesticOptimizerRemainsEligible()
    {
        var context = MakeContext(Money.FromDollars(2_000_000)); // favors DomesticProfitOptimizer

        var chosen = BaselineAi.SelectStrategy(null, quarterNumber: 100, context); // MidGame

        Assert.IsType<DomesticProfitOptimizerStrategy>(chosen);
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
    public void Strategies_ContainsExactlyTheThreeBuiltStrategies()
    {
        Assert.Equal(3, BaselineAi.Strategies.Count);
        Assert.Contains(BaselineAi.Strategies, s => s is DomesticProfitOptimizerStrategy);
        Assert.Contains(BaselineAi.Strategies, s => s is RapidExpansionStrategy);
        Assert.Contains(BaselineAi.Strategies, s => s is NetworkOptimizerStrategy);
    }
}
