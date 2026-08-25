using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class GravityDemandModelTests
{
    private static City MakeCity(string id, double lat, double lon, int weight, bool isGateway = false, int? gateCapacity = null) =>
        new(id, id.ToUpperInvariant(), Region.Asia, lat, lon, weight, isGateway, gateCapacity);

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
