using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// AiToolkit.ContestAppetite (2026-10-08): among profitable new-route
/// candidates, a pair a rival already flies ranks as if its estimated
/// profit were (1 + ContestAppetite) times higher. It never makes an
/// unprofitable candidate eligible.
/// </summary>
public class ContestAppetiteTests
{
    private static readonly GravityDemandModel DemandModel = new();

    private static City MakeCity(string id, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.Europe, 0, lon, weight, false, null);

    private static StrategyContext MakeContext(IReadOnlyList<City> cities, AircraftType type, IReadOnlyDictionary<(string, string), int>? pairRivals = null) =>
        new(Array.Empty<Route>(), ActionCap: 3, cities, DemandModel, new[] { type },
            FleetLedger.ApplyDelivery(AirlineFleet.Empty, type, quantity: 1, FleetOwnershipMode.Owned),
            Region.Europe, cities[0], RivalPresenceByPairKey: pairRivals);

    [Fact]
    public void RankingScore_RaisesOnlyAContestedPair()
    {
        var home = MakeCity("home", 0, 100);
        var other = MakeCity("other", 5, 100);
        var pair = DemandModel.ForPair(home, other);
        var type = new AircraftType("t", "T", Seats: 10);
        var profit = Money.FromDollars(1_000_000);
        var contested = MakeContext(new[] { home, other }, type, new Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, other)] = 1 });
        var uncontested = MakeContext(new[] { home, other }, type);

        Assert.Equal(profit.Cents, AiToolkit.RankingScore(uncontested, pair, profit));
        Assert.Equal(profit.Cents * (1 + AiToolkit.ContestAppetite), AiToolkit.RankingScore(contested, pair, profit));
    }

    [Fact]
    public void FindMostProfitablePair_PrefersTheContestedPair_WhenItsProfitIsWithinTheAppetite()
    {
        // Two destinations, both flown full by a one-seat aircraft even with
        // a rival halving the estimated passengers. The uncontested one is a
        // little farther, so it earns a little more at full load - and would
        // win on profit alone; the appetite tips the choice to the contested
        // one. (Checked: with ContestAppetite at 0 this test fails.)
        var home = MakeCity("home", 0, 100);
        var quiet = MakeCity("quiet", 6, 100);
        var busy = MakeCity("busy", -5, 100);
        var tiny = new AircraftType("tiny", "Tiny", Seats: 1);
        var rivalOnBusy = new Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, busy)] = 1 };
        var context = MakeContext(new[] { home, quiet, busy }, tiny, rivalOnBusy);

        var best = AiToolkit.FindMostProfitablePair(context, new HashSet<int>(), new HashSet<string> { "home" }, _ => true);

        Assert.NotNull(best);
        Assert.Equal("busy", best.Value.Pair.Origin.Id == "home" ? best.Value.Pair.Destination.Id : best.Value.Pair.Origin.Id);
    }

    [Fact]
    public void FindMostProfitablePair_AppetiteNeverMakesAnUnprofitableContestedPairEligible()
    {
        // The only candidate is contested and loses money: still nothing.
        var home = MakeCity("home", 0, 100);
        var busy = MakeCity("busy", 5, 100);
        var oversized = new AircraftType("big", "Big", Seats: 1_000_000);
        var rivalOnBusy = new Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(home, busy)] = 1 };
        var context = MakeContext(new[] { home, busy }, oversized, rivalOnBusy);

        Assert.Null(AiToolkit.FindMostProfitablePair(context, new HashSet<int>(), new HashSet<string> { "home" }, _ => true));
    }
}
