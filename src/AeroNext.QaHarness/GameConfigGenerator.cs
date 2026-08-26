using AeroNext.Core;
using AeroNext.Core.Setup;
using AeroNext.Core.World;

namespace AeroNext.QaHarness;

/// <summary>
/// Generates a random-but-valid human player configuration — 0-4 seats,
/// each with a distinct home region and an eligible starting city within
/// it — so the QA sweep exercises the real breadth of what
/// GameSetup.CreateSeats can be asked to validate, not just the three
/// hand-picked configurations AeroNext.Sim's M19 demo shows. Driven by a
/// SimRng, per CLAUDE.md's determinism rules, so a failing game is always
/// reproducible from its seed alone.
/// </summary>
public static class GameConfigGenerator
{
    public static IReadOnlyList<HumanPlayerChoice> Generate(SimRng rng, IReadOnlyList<City> allCities)
    {
        var humanCount = rng.Next(0, 5); // 0-4 inclusive

        var shuffledRegions = HomeRegions.All
            .Select(r => (Region: r, Key: rng.NextDouble()))
            .OrderBy(x => x.Key)
            .Select(x => x.Region)
            .Take(humanCount)
            .ToList();

        var choices = new List<HumanPlayerChoice>();

        for (var i = 0; i < shuffledRegions.Count; i++)
        {
            var eligible = HomeRegions.EligibleStartingCities(shuffledRegions[i], allCities);
            var city = eligible[rng.Next(0, eligible.Count)];

            choices.Add(new HumanPlayerChoice($"QA Human {i + 1}", city.Id));
        }

        return choices;
    }
}
