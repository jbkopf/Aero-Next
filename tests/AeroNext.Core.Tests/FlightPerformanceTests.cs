using AeroNext.Core.Airlines;
using Xunit;

namespace AeroNext.Core.Tests;

public class FlightPerformanceTests
{
    [Fact]
    public void MaxWeeklyRoundTrips_ShortRoute_FastAircraft_ProducesManyRoundTrips()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, CruiseSpeedKmh: 900);

        var result = FlightPerformance.MaxWeeklyRoundTrips(aircraft, distanceKm: 150);

        Assert.True(result > 10); // short hop, turnaround dominates - many round trips fit in a week
    }

    [Fact]
    public void MaxWeeklyRoundTrips_LongRoute_NearRangeLimit_ProducesFewRoundTrips()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, CruiseSpeedKmh: 900);

        var result = FlightPerformance.MaxWeeklyRoundTrips(aircraft, distanceKm: 12_000);

        Assert.True(result < 5); // ultra-long-haul: only a couple of round trips per week per aircraft
    }

    [Fact]
    public void MaxWeeklyRoundTrips_NeverFallsBelowOne_EvenOnAnExtremelyLongRoute()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, CruiseSpeedKmh: 900);

        var result = FlightPerformance.MaxWeeklyRoundTrips(aircraft, distanceKm: 19_000);

        Assert.Equal(1, result);
    }

    [Fact]
    public void MaxWeeklyRoundTrips_FasterAircraft_SameDistance_ProducesAtLeastAsManyRoundTrips()
    {
        var slow = new AircraftType("slow", "Slow", Seats: 100, CruiseSpeedKmh: 300);
        var fast = new AircraftType("fast", "Fast", Seats: 100, CruiseSpeedKmh: 900);

        var slowResult = FlightPerformance.MaxWeeklyRoundTrips(slow, distanceKm: 3_000);
        var fastResult = FlightPerformance.MaxWeeklyRoundTrips(fast, distanceKm: 3_000);

        Assert.True(fastResult >= slowResult);
    }

    [Fact]
    public void MaxWeeklyRoundTrips_IsDeterministic()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, CruiseSpeedKmh: 850);

        var first = FlightPerformance.MaxWeeklyRoundTrips(aircraft, distanceKm: 5_500);
        var second = FlightPerformance.MaxWeeklyRoundTrips(aircraft, distanceKm: 5_500);

        Assert.Equal(first, second);
    }
}
