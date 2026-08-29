using AeroNext.Core;
using AeroNext.Core.Setup;
using AeroNext.Core.World;

namespace AeroNext.QaHarness;

/// <summary>
/// Builds 4 AirlineSeats with an independently random home region per seat —
/// duplicates allowed, same as real gameplay now permits (see GameSetup's
/// same-region change). What still makes this different from a real game:
/// GameSetup.CreateSeats' AI-fill always avoids regions a human already
/// claimed and picks the richest one first; this generator does neither —
/// every one of the 4 seats is independently random, with no richness bias
/// and no avoidance of a region (or, rarely, a city) another seat already
/// landed on. A same-city collision is possible in principle (low odds
/// given how few regions are small) and would be a genuine invariant
/// violation if it happened — FullGameRunner checks for it, not this
/// generator. Exists to answer one QA question: is a region's measured
/// disadvantage intrinsic to that region's own economics, or an artifact
/// of GameSetup's own richness-based AI-fill logic?
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
