using AeroNext.Core;
using AeroNext.Core.World;

namespace AeroNext.QaHarness;

/// <summary>
/// One home region's outcome, averaged across every seat that started there
/// in the sweep — not one per game: under the unconstrained-regions QA mode
/// a single game can contribute 0-4 seats to any one region. "Wins" is a
/// final-cash comparison only, not a modeled victory condition — the sim
/// doesn't have one yet.
/// </summary>
public sealed record RegionalOutcome(
    Region Region,
    int SeatsCounted,
    int BankruptcyCount,
    double? AverageBankruptcyQuarter,
    Money AverageFinalCash,
    Money MedianFinalCash,
    int Wins);

/// <summary>
/// Groups FullGameRunner's per-airline outcomes by home region, across many
/// games, to answer a question no single game or invariant check can:
/// does one home region systematically over- or under-perform, given
/// M15-M18's real per-region starting conditions and an AI (BaselineAi) that
/// is itself region-blind? Only games with zero invariant violations feed
/// this — a broken game's final numbers aren't trustworthy data. Iterates
/// every (name, region) pair in each game's HomeRegionByName directly,
/// rather than assuming exactly one seat per region — true for
/// GameSetup-produced seats, but not for UnconstrainedSeatGenerator's.
/// </summary>
public static class RegionalOutcomeReport
{
    public static IReadOnlyList<RegionalOutcome> Aggregate(IReadOnlyList<FullGameResult> cleanResults)
    {
        var perRegion = HomeRegions.All.ToDictionary(r => r, _ => new List<(Money Cash, int? BankruptcyQuarter, bool Won)>());

        foreach (var result in cleanResults)
        {
            var bestCash = result.FinalCash.Values.Aggregate((a, b) => a > b ? a : b);

            foreach (var (name, region) in result.HomeRegionByName)
            {
                var cash = result.FinalCash[name];
                perRegion[region].Add((cash, result.BankruptcyQuarter[name], cash == bestCash));
            }
        }

        return HomeRegions.All.Select(region =>
        {
            var entries = perRegion[region];
            var bankrupt = entries.Where(e => e.BankruptcyQuarter is not null).ToList();
            var cashDollars = entries.Select(e => e.Cash.Cents / 100.0).OrderBy(c => c).ToList();
            var mid = cashDollars.Count / 2;
            var median = cashDollars.Count == 0
                ? 0.0
                : cashDollars.Count % 2 == 1
                    ? cashDollars[mid]
                    : (cashDollars[mid - 1] + cashDollars[mid]) / 2.0;

            return new RegionalOutcome(
                region,
                entries.Count,
                bankrupt.Count,
                bankrupt.Count > 0 ? bankrupt.Average(e => e.BankruptcyQuarter!.Value) : null,
                Money.FromDollars((long)Math.Round(cashDollars.Count > 0 ? cashDollars.Average() : 0.0)),
                Money.FromDollars((long)Math.Round(median)),
                entries.Count(e => e.Won));
        }).ToList();
    }
}
