using AeroNext.Core;
using AeroNext.Core.Setup;
using AeroNext.Core.World;

namespace AeroNext.QaHarness;

/// <summary>
/// Generates a random-but-valid human player configuration — 0-4 seats,
/// each with an independently-chosen home region (regions are no longer
/// exclusive, so two seats can land in the same one — only starting city
/// has to stay distinct) — so the QA sweep exercises the real breadth of
/// what GameSetup.CreateSeats can be asked to validate, not just the three
/// hand-picked configurations AeroNext.Sim's M19 demo shows. Driven by a
/// SimRng, per CLAUDE.md's determinism rules, so a failing game is always
/// reproducible from its seed alone.
/// </summary>
public static class GameConfigGenerator
{
    public static IReadOnlyList<HumanPlayerChoice> Generate(SimRng rng, IReadOnlyList<City> allCities)
    {
        var humanCount = rng.Next(0, 5); // 0-4 inclusive

        var choices = new List<HumanPlayerChoice>();
        var claimedCityIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < humanCount; i++)
        {
            var region = HomeRegions.All[rng.Next(0, HomeRegions.All.Count)];
            var eligible = HomeRegions.EligibleStartingCities(region, allCities)
                .Where(c => !claimedCityIds.Contains(c.Id))
                .ToList();

            var city = eligible[rng.Next(0, eligible.Count)];
            claimedCityIds.Add(city.Id);

            choices.Add(new HumanPlayerChoice($"QA Human {i + 1}", city.Id));
        }

        return choices;
    }
}
