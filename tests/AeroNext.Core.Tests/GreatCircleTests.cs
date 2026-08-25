using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class GreatCircleTests
{
    [Fact]
    public void DistanceKm_SameCity_IsZero()
    {
        var city = new City("a", "A", Region.Asia, 10, 20, 10, false, null);

        Assert.Equal(0, GreatCircle.DistanceKm(city, city), precision: 6);
    }

    [Fact]
    public void DistanceKm_IsSymmetric()
    {
        var a = new City("a", "A", Region.Asia, 40.6413, -73.7781, 10, false, null);
        var b = new City("b", "B", Region.Europe, 51.4700, -0.4543, 10, false, null);

        Assert.Equal(GreatCircle.DistanceKm(a, b), GreatCircle.DistanceKm(b, a), precision: 6);
    }

    [Fact]
    public void DistanceKm_JfkToLhr_IsPlausible()
    {
        var jfk = new City("jfk", "New York", Region.NorthAmerica, 40.6413, -73.7781, 10, false, null);
        var lhr = new City("lhr", "London", Region.Europe, 51.4700, -0.4543, 10, false, null);

        var distance = GreatCircle.DistanceKm(jfk, lhr);

        // Real-world great-circle distance is ~5,550 km.
        Assert.InRange(distance, 5_400, 5_700);
    }
}
