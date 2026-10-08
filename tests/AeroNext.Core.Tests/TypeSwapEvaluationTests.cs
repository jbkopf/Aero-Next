using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// AiToolkit.EvaluateTypeSwapCandidate's profit inputs (2026-10-04): swap
/// decisions count only rivals on the route's own pair, and a proactive
/// swap needs a positive current estimate. Traced on 40 Hard games before
/// the fix, region rivals cut a flown route's estimate 2-4x, a negative
/// estimate turned the proactive +50% bar negative, and 83% of swap-backs
/// (a route returning to the type it just left) involved that.
/// </summary>
public class TypeSwapEvaluationTests
{
    private static readonly GravityDemandModel DemandModel = new();
    private static readonly City A = new("a", "A", Region.Europe, 0, 0, 100, false, null);
    private static readonly City B = new("b", "B", Region.Europe, 0, 10, 100, false, null);
    private static readonly long Demand = DemandModel.ForPair(A, B).PassengersPerQuarter;

    /// <summary>A type whose single aircraft offers about <paramref name="fractionOfDemand"/> of the pair's quarterly demand in seats.</summary>
    private static AircraftType Sized(string id, double fractionOfDemand)
    {
        var probe = new AircraftType(id, id, Seats: 1, RangeKm: 5_000, CruiseSpeedKmh: 800);
        var seatsPerSeat = new Route(A, B, probe, 1).SeatsPerQuarter;

        return probe with { Seats = (int)Math.Max(1, Demand * fractionOfDemand / seatsPerSeat) };
    }

    private static StrategyContext MakeContext(Route route, AircraftType idleType,
        IReadOnlyDictionary<Region, int>? regionRivals = null, IReadOnlyDictionary<(string, string), int>? pairRivals = null) =>
        new(new[] { route }, ActionCap: 3, new[] { A, B }, DemandModel, new[] { route.Aircraft, idleType },
            FleetLedger.ApplyDelivery(AirlineFleet.Empty, idleType, quantity: 1, FleetOwnershipMode.Owned),
            Region.Europe, A, RivalPresenceByRegion: regionRivals, RivalPresenceByPairKey: pairRivals);

    private static RouteLoad LoadOf(Route route) => RouteEvaluator.Evaluate(route, DemandModel);

    [Fact]
    public void Proactive_NoSwapWhenTheCurrentEstimateIsNotPositive_ButReactiveStillOffersIt()
    {
        // A wildly oversized incumbent estimates at a loss; a right-sized
        // type is far better. Proactive must not use "+50% of a negative
        // number" as its bar - the reactive path, with its flat floor, is
        // the one that handles a losing route.
        var oversized = Sized("over", 20.0);
        var fit = Sized("fit", 0.5);
        var route = new Route(A, B, oversized, 1);
        var context = MakeContext(route, fit);

        Assert.Null(AiToolkit.EvaluateTypeSwapCandidate(context, LoadOf(route), new HashSet<int>(), isReactive: false));
        Assert.Equal("fit", AiToolkit.EvaluateTypeSwapCandidate(context, LoadOf(route), new HashSet<int>(), isReactive: true)?.Type.Id);
    }

    [Fact]
    public void RegionRivals_DoNotChangeTheSwapDecision()
    {
        // A full small aircraft and an idle one twice its size, both still
        // full at the pair's own demand: a genuine proactive upgrade. Rivals
        // elsewhere in the region don't fly this pair, so they must not
        // change the answer.
        var small = Sized("small", 0.2);
        var bigger = Sized("bigger", 0.45);
        var route = new Route(A, B, small, 1);

        var alone = AiToolkit.EvaluateTypeSwapCandidate(MakeContext(route, bigger), LoadOf(route), new HashSet<int>(), isReactive: false);
        var crowdedRegion = AiToolkit.EvaluateTypeSwapCandidate(MakeContext(route, bigger, regionRivals: new Dictionary<Region, int> { [Region.Europe] = 3 }),
            LoadOf(route), new HashSet<int>(), isReactive: false);

        Assert.Equal("bigger", alone?.Type.Id);
        Assert.Equal(alone, crowdedRegion);
    }

    [Fact]
    public void PairRivals_StillShareTheRoutesPassengers()
    {
        // Three rivals on the pair itself leave a quarter of the demand -
        // the bigger aircraft would fly mostly empty, so the upgrade is off.
        var small = Sized("small", 0.2);
        var bigger = Sized("bigger", 0.45);
        var route = new Route(A, B, small, 1);
        var pairRivals = new Dictionary<(string, string), int> { [QuarterEngine.NormalizedPairKey(A, B)] = 3 };

        var candidate = AiToolkit.EvaluateTypeSwapCandidate(MakeContext(route, bigger, pairRivals: pairRivals), LoadOf(route), new HashSet<int>(), isReactive: false);

        Assert.Null(candidate);
    }

    [Fact]
    public void EstimatedRealProfit_RegionRivals_NeverChangeTheEstimate()
    {
        // Only rivals on the pair share its passengers (2026-10-08) - rival
        // airlines elsewhere in the region don't.
        var type = Sized("t", 0.45);
        var route = new Route(A, B, type, 1);
        var crowded = MakeContext(route, type, regionRivals: new Dictionary<Region, int> { [Region.Europe] = 3 });
        var empty = MakeContext(route, type);
        var pair = DemandModel.ForPair(A, B);

        Assert.Equal(AiToolkit.EstimatedRealProfit(empty, pair, type, requiresLease: false),
            AiToolkit.EstimatedRealProfit(crowded, pair, type, requiresLease: false));
    }
}
