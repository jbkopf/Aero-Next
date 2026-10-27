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
/// and no avoidance of a region another seat already landed on. Starting
/// cities stay distinct, as in every real game: a seat draws only from its
/// region's cities no earlier seat took, and a region with none left drops
/// out of the region draw. (This used to draw cities independently on the
/// assumption a same-city collision was rare - a real 20-game Hard sweep
/// hit it in 3 games, each failing FullGameRunner's distinct-starting-
/// cities invariant at Q0.) Exists to answer one QA question: is a
/// region's measured disadvantage intrinsic to that region's own
/// economics, or an artifact of GameSetup's own richness-based AI-fill
/// logic?
/// </summary>
public static class UnconstrainedSeatGenerator
{
    public static IReadOnlyList<AirlineSeat> Generate(SimRng rng, IReadOnlyList<City> allCities)
    {
        var seats = new List<AirlineSeat>();
        var takenCityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        List<City> FreeCities(Region region) => HomeRegions.EligibleStartingCities(region, allCities)
            .Where(c => !takenCityIds.Contains(c.Id))
            .ToList();

        for (var i = 0; i < 4; i++)
        {
            var regions = HomeRegions.All.Where(r => FreeCities(r).Count > 0).ToList();
            var region = regions[rng.Next(0, regions.Count)];
            var eligible = FreeCities(region);
            var city = eligible[rng.Next(0, eligible.Count)];
            takenCityIds.Add(city.Id);

            seats.Add(new AirlineSeat($"Random Air {i + 1}", IsHuman: false, region, city));
        }

        return seats;
    }
}
