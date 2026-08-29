using AeroNext.Core;
using AeroNext.Core.Setup;
using AeroNext.Core.World;

namespace AeroNext.QaHarness;

/// <summary>
/// Builds 4 AirlineSeats with an independently random home region per seat —
/// duplicates allowed. GameSetup.CreateSeats is the only place "each home
/// region claimed by at most one airline" is enforced (its
/// humanSeatsByRegion.TryAdd throws on a repeat); AirlineSeat itself carries
/// no such invariant, so bypassing GameSetup here needs no change to
/// AeroNext.Core at all. Exists to answer one QA question: is a region's
/// measured disadvantage intrinsic to that region's own economics, or an
/// artifact of always facing exactly one rival per other region? Not a
/// configuration real gameplay (GameSetup/M15-M17) can ever produce.
/// </summary>
public static class UnconstrainedSeatGenerator
{
    public static IReadOnlyList<AirlineSeat> Generate(SimRng rng, IReadOnlyList<City> allCities)
    {
        var seats = new List<AirlineSeat>();

        for (var i = 0; i < 4; i++)
        {
            var region = HomeRegions.All[rng.Next(0, HomeRegions.All.Count)];
            var eligible = HomeRegions.EligibleStartingCities(region, allCities);
            var city = eligible[rng.Next(0, eligible.Count)];

            seats.Add(new AirlineSeat($"Random Air {i + 1}", IsHuman: false, region, city));
        }

        return seats;
    }
}
