using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class GravityDemandModelTests
{
    private static City MakeCity(string id, double lat, double lon, int weight, bool isGateway = false, int? gateCapacity = null) =>
        new(id, id.ToUpperInvariant(), Region.Asia, lat, lon, weight, isGateway, gateCapacity);

    private static City MakeCity(string id, double lat, double lon, int weight, Region region) =>
        new(id, id.ToUpperInvariant(), region, lat, lon, weight, false, null);

    [Fact]
    public void ForPair_IsSymmetric()
    {
        var model = new GravityDemandModel();
        var a = MakeCity("a", 40.6413, -73.7781, 100);
        var b = MakeCity("b", 51.4700, -0.4543, 95);

        var ab = model.ForPair(a, b);
        var ba = model.ForPair(b, a);

        Assert.Equal(ab.PassengersPerQuarter, ba.PassengersPerQuarter);
    }

    [Fact]
    public void ForPair_DemandFallsOffWithDistance_HoldingWeightConstant()
    {
        var model = new GravityDemandModel();
        var origin = MakeCity("origin", 0, 0, 50);
        var near = MakeCity("near", 1, 0, 50);
        var far = MakeCity("far", 40, 0, 50);

        var nearDemand = model.ForPair(origin, near).PassengersPerQuarter;
        var farDemand = model.ForPair(origin, far).PassengersPerQuarter;

        Assert.True(nearDemand > farDemand);
    }

    [Fact]
    public void ForPair_DemandGrowsWithWeight_HoldingDistanceConstant()
    {
        var model = new GravityDemandModel();
        var origin = MakeCity("origin", 0, 0, 50);
        var smallDest = MakeCity("small", 10, 0, 5);
        var bigDest = MakeCity("big", 10, 0, 100);

        var smallDemand = model.ForPair(origin, smallDest).PassengersPerQuarter;
        var bigDemand = model.ForPair(origin, bigDest).PassengersPerQuarter;

        Assert.True(bigDemand > smallDemand);
    }

    [Fact]
    public void ForPair_AppliesCrossRegionMultiplier_OnlyAcrossRegionBoundary()
    {
        var model = new GravityDemandModel(crossRegionMultiplier: 6.0);
        var a1 = MakeCity("a1", 0, 0, 50, Region.NorthAmerica);
        var a2 = MakeCity("a2", 1, 1, 50, Region.NorthAmerica); // same region as a1
        var b = MakeCity("b", 1, 1, 50, Region.Europe); // same distance/weight as a2, different region

        var withinRegion = model.ForPair(a1, a2).PassengersPerQuarter;
        var crossRegion = model.ForPair(a1, b).PassengersPerQuarter;

        // Allow ±1 for independent rounding of each result.
        Assert.InRange(crossRegion, withinRegion * 6 - 1, withinRegion * 6 + 1);
    }

    [Fact]
    public void ForPair_GlobalMultiplier_ScalesDemandProportionally()
    {
        var a = MakeCity("a", 0, 0, 50);
        var b = MakeCity("b", 10, 10, 50);

        var baseline = new GravityDemandModel(globalMultiplier: 1.0).ForPair(a, b).PassengersPerQuarter;
        var grown = new GravityDemandModel(globalMultiplier: 4.0).ForPair(a, b).PassengersPerQuarter;

        Assert.InRange(grown, baseline * 4 - 1, baseline * 4 + 1);
    }

    [Fact]
    public void ForPair_CrossRegionMultiplier_DoesNotAffectSameRegionPairs()
    {
        var a = MakeCity("a", 0, 0, 50, Region.NorthAmerica);
        var b = MakeCity("b", 10, 10, 50, Region.NorthAmerica);

        var low = new GravityDemandModel(crossRegionMultiplier: 1.0).ForPair(a, b).PassengersPerQuarter;
        var high = new GravityDemandModel(crossRegionMultiplier: 20.0).ForPair(a, b).PassengersPerQuarter;

        Assert.Equal(low, high);
    }

    [Fact]
    public void AllPairs_ReturnsCombinationCount()
    {
        var model = new GravityDemandModel();
        var cities = new[]
        {
            MakeCity("a", 0, 0, 10),
            MakeCity("b", 1, 1, 10),
            MakeCity("c", 2, 2, 10),
            MakeCity("d", 3, 3, 10),
        };

        var pairs = model.AllPairs(cities);

        Assert.Equal(6, pairs.Count); // C(4, 2)
    }

    [Fact]
    public void AllPairs_IsDeterministic_RegardlessOfInputOrder()
    {
        var model = new GravityDemandModel();
        var cities = new[]
        {
            MakeCity("a", 0, 0, 10),
            MakeCity("b", 1, 1, 20),
            MakeCity("c", 2, 2, 30),
        };
        var shuffled = new[] { cities[2], cities[0], cities[1] };

        var fromOriginal = model.AllPairs(cities);
        var fromShuffled = model.AllPairs(shuffled);

        Assert.Equal(
            fromOriginal.Select(p => (p.Origin.Id, p.Destination.Id, p.PassengersPerQuarter)),
            fromShuffled.Select(p => (p.Origin.Id, p.Destination.Id, p.PassengersPerQuarter)));
    }

    [Fact]
    public void RealDataset_MajorMarketDwarfsChokepointPair()
    {
        var dataPath = Path.Combine(AppContext.BaseDirectory, "data", "cities.json");
        var cities = CityCatalog.LoadFile(dataPath);
        var byId = cities.ToDictionary(c => c.Id);
        var model = new GravityDemandModel();

        var jfkLhr = model.ForPair(byId["jfk"], byId["lhr"]);
        var yqxTer = model.ForPair(byId["yqx"], byId["ter"]); // Gander-Azores: both chokepoints

        Assert.True(jfkLhr.PassengersPerQuarter > yqxTer.PassengersPerQuarter * 100);
    }
}
