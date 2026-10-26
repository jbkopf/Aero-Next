using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// docs/MULTI_STOP_ROUTES_DESIGN.md, Phase 2: passengers on a contested pair
/// choose by fare plus journey time, compared across rivals.
/// </summary>
public class JourneyCostTests
{
    private static City MakeCity(string id, double lon, int weight = 100) =>
        new(id, id.ToUpperInvariant(), Region.Europe, 0, lon, weight, false, null);

    private static City MakeGateway(string id, double lon) =>
        new(id, id.ToUpperInvariant(), Region.NorthAmerica, 0, lon, 1, true, 2);

    private static readonly City A = MakeCity("a", 0);
    private static readonly City Hub = MakeGateway("hub", 10);
    private static readonly City B = MakeCity("b", 20);

    private static Itinerary Nonstop(Route route) => new(route.Origin, route.Destination, ItineraryKind.Nonstop, new[] { route });

    // --- JourneyCost arithmetic ---

    [Fact]
    public void JourneyHours_Nonstop_IsFlyingTimeOnly()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, CruiseSpeedKmh: 500);
        var route = new Route(A, B, aircraft, 1);

        Assert.Equal(route.DistanceKm / 500, JourneyCost.JourneyHours(Nonstop(route)), precision: 9);
    }

    [Fact]
    public void JourneyHours_MultiStop_AddsTheExtraDistanceAndAnHourOnTheGroundPerStop()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, CruiseSpeedKmh: 500);
        var route = new Route(A, B, aircraft, 1, Stops: StopList.Of(Hub));

        var expected = route.DistanceKm / 500 + FlightPerformance.TurnaroundHoursPerLanding;

        Assert.Equal(expected, JourneyCost.JourneyHours(Nonstop(route)), precision: 9);
    }

    [Fact]
    public void OneStopConnection_PaysBothFares_AndWaitsAtTheHub()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, CruiseSpeedKmh: 500);
        var legOne = new Route(A, Hub, aircraft, 1);
        var legTwo = new Route(Hub, B, aircraft, 1);
        var connection = new Itinerary(A, B, ItineraryKind.OneStop, new[] { legOne, legTwo });

        Assert.Equal(legOne.FarePerPassenger + legTwo.FarePerPassenger, JourneyCost.FarePaid(connection));
        Assert.Equal(legOne.DistanceKm / 500 + legTwo.DistanceKm / 500 + JourneyCost.ConnectionHours, JourneyCost.JourneyHours(connection), precision: 9);
    }

    [Fact]
    public void PerPassenger_IsFarePlusTimeAtTheValueOfTime()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 100, CruiseSpeedKmh: 500);
        var route = new Route(A, B, aircraft, 1, FareOverride: Money.FromDollars(300));
        var itinerary = Nonstop(route);
        var hours = JourneyCost.JourneyHours(itinerary);

        var expected = Money.FromDollars(300) + new Money((long)Math.Round(JourneyCost.ValueOfTimePerHour.Cents * hours, MidpointRounding.AwayFromZero));

        Assert.Equal(expected, JourneyCost.PerPassenger(itinerary));
    }

    // --- CompetitiveMarket: share follows price and time ---

    /// <summary>Carried by each of two airlines on the a-b pair, both with capacity far above demand so shares aren't capped.</summary>
    private static (long First, long Second) ResolvePair(Route first, Route second, IReadOnlyList<City>? cities = null)
    {
        var one = new Airline("One", new[] { first });
        var two = new Airline("Two", new[] { second });
        var result = CompetitiveMarket.Resolve(new[] { one, two }, cities ?? new[] { A, Hub, B }, new GravityDemandModel());

        return (result.TotalCarried(one), result.TotalCarried(two));
    }

    private static double Multiplier(Money own, Money cheapest) =>
        Math.Pow((double)cheapest.Cents / own.Cents, FareCalculator.FareDemandElasticity);

    [Fact]
    public void Contested_DearerFare_LosesShare_ByTheJourneyCostRatio()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 1_000);
        var cheap = new Route(A, B, aircraft, 10, FareOverride: Money.FromDollars(200));
        var dear = new Route(A, B, aircraft, 10, FareOverride: Money.FromDollars(260));

        var (cheapCarried, dearCarried) = ResolvePair(cheap, dear);

        var expectedRatio = Multiplier(JourneyCost.PerPassenger(Nonstop(dear)), JourneyCost.PerPassenger(Nonstop(cheap)));
        Assert.True(dearCarried < cheapCarried);
        Assert.InRange((double)dearCarried / cheapCarried, expectedRatio * 0.99, expectedRatio * 1.01);
    }

    [Fact]
    public void Contested_SameFare_SlowerAircraft_LosesShareToTime()
    {
        // Same fare, same seats per quarter: only journey time differs. The
        // slow type flies fewer round trips, so it gets more aircraft to
        // offer the same capacity.
        var fast = new AircraftType("fast", "Fast", Seats: 1_000, CruiseSpeedKmh: 900);
        var slow = new AircraftType("slow", "Slow", Seats: 1_000, CruiseSpeedKmh: 300);
        var fare = Money.FromDollars(200);
        var fastRoute = new Route(A, B, fast, 10, FareOverride: fare);
        var slowCount = Enumerable.Range(1, 100).First(n => new Route(A, B, slow, n).SeatsPerQuarter >= fastRoute.SeatsPerQuarter);
        var slowRoute = new Route(A, B, slow, slowCount, FareOverride: fare);

        var (fastCarried, slowCarried) = ResolvePair(fastRoute, slowRoute);

        var capacityRatio = (double)slowRoute.SeatsPerQuarter / fastRoute.SeatsPerQuarter;
        var expectedRatio = capacityRatio * Multiplier(JourneyCost.PerPassenger(Nonstop(slowRoute)), JourneyCost.PerPassenger(Nonstop(fastRoute)));
        Assert.True(slowCarried < fastCarried);
        Assert.InRange((double)slowCarried / fastCarried, expectedRatio * 0.99, expectedRatio * 1.01);
    }

    [Fact]
    public void Contested_MultiStopRoute_LosesShareToANonstop_ThroughItsOwnCostAndTime()
    {
        // Same type and aircraft count: the stop route offers fewer seats
        // (ground time), charges a higher default fare (more distance, salary
        // spread over fewer seats) and takes longer - all of it, and nothing
        // else, shows up in its share.
        var aircraft = new AircraftType("t", "Test", Seats: 1_000, RangeKm: 5_000, CruiseSpeedKmh: 600);
        var nonstop = new Route(A, B, aircraft, 10);
        var viaHub = new Route(A, B, aircraft, 10, Stops: StopList.Of(Hub));

        var (nonstopCarried, viaHubCarried) = ResolvePair(nonstop, viaHub);

        var capacityRatio = (double)viaHub.SeatsPerQuarter / nonstop.SeatsPerQuarter;
        var expectedRatio = capacityRatio * Multiplier(JourneyCost.PerPassenger(Nonstop(viaHub)), JourneyCost.PerPassenger(Nonstop(nonstop)));
        Assert.True(viaHubCarried < nonstopCarried);
        Assert.InRange((double)viaHubCarried / nonstopCarried, expectedRatio * 0.99, expectedRatio * 1.01);
    }

    [Fact]
    public void Contested_EqualOffers_StillSplitEvenly()
    {
        var aircraft = new AircraftType("t", "Test", Seats: 1_000);

        var (first, second) = ResolvePair(new Route(A, B, aircraft, 10), new Route(A, B, aircraft, 10));

        Assert.True(first > 0);
        Assert.InRange((double)first / second, 0.99, 1.01);
    }

    [Fact]
    public void Contested_ZeroOrNegativeFare_DoesNotBreakTheSplit()
    {
        // A free (or paid-to-fly) fare is the cheapest offer by far - it
        // must still produce a finite, sensible split, not NaN.
        var aircraft = new AircraftType("t", "Test", Seats: 1_000, CruiseSpeedKmh: 100_000);
        var negative = new Route(A, B, aircraft, 10, FareOverride: Money.FromDollars(-1_000));
        var normal = new Route(A, B, aircraft, 10);

        var (negativeCarried, normalCarried) = ResolvePair(negative, normal);

        Assert.True(negativeCarried > normalCarried);
        Assert.True(normalCarried >= 0);
    }
}
