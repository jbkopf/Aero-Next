using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Negotiations;
using AeroNext.Core.Setup;
using AeroNext.Core.World;
using AeroNext.QaHarness;

// A QA sweep, not a unit test: runs many full games (setup through a
// multi-decade campaign) from randomized-but-valid starting configurations
// and checks a checklist of correctness invariants across all of them.
// Deliberately separate from `dotnet test` (CLAUDE.md's sub-second target)
// - this is the slower, broader QA tier for "does the whole system hold
// together across the real space of games," not fast per-commit feedback.
//
// Usage: dotnet run --project src/AeroNext.QaHarness [--games N] [--quarters N] [--seed N] [--unconstrained-regions] [--difficulty normal|hard]
//
// Regions aren't exclusive in GameSetup.CreateSeats any more (a player can
// share a home region with a rival), but its AI-fill logic still always
// avoids regions already claimed - so the normal sweep never puts an AI
// seat in a shared region, only humans can create that.
//
// EastAsia-dominance follow-up (docs/OPEN_WORK.md): filler region draw is
// now uniform-random among unclaimed regions (fillerWeightExponent: 0.0),
// not the old richest-unclaimed-first cut - a real 300-game sweep found
// the deterministic cut structurally excluded AustralAsia/SouthWestAsia
// from filler in most games, handing EastAsia a walkover (0% win rate
// once both were actually seated by a real airline, vs 61.3% when
// neither was) rather than a real fight. Measured on this actual wiring
// across two independent 300-game seed ranges: EastAsia's own win share
// collapsed from 47.0% to 11.3%, Europe became the new leading region at
// 31-34% (its own real fundamentals - most cities of any region, highest
// total demand weight), bankruptcy essentially unchanged. NorthAmerica is
// actually the second-weakest region (10-13%) despite its dense local
// network - an earlier pass at this measurement wrongly named it the new
// leader, from a since-fixed bug in RegionalOutcomeReport.cs that read
// "who ended richest" instead of the real win condition (see
// docs/OPEN_WORK.md's own correction). Reuses configRng sequentially
// (already consumed by GameConfigGenerator.Generate just above) rather
// than a second seed - one continuous seeded stream per game, per
// CLAUDE.md's determinism rule.
//
// --unconstrained-regions swaps GameSetup.CreateSeats entirely for
// UnconstrainedSeatGenerator (every one of the 4 seats independently
// random, no richness bias, no avoiding claimed regions at all) - isolates
// "is a region's disadvantage intrinsic, or an artifact of GameSetup's own
// richness-based AI-fill logic," a QA-only question, not a real game
// configuration.
var games = 100;
var quarters = 244; // the full 1955-2015 campaign, matching M13
var baseSeed = 1;
var unconstrainedRegions = false;
var difficulty = GameDifficulty.Normal;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--games": games = int.Parse(args[++i]); break;
        case "--quarters": quarters = int.Parse(args[++i]); break;
        case "--seed": baseSeed = int.Parse(args[++i]); break;
        case "--unconstrained-regions": unconstrainedRegions = true; break;
        case "--difficulty": difficulty = Enum.Parse<GameDifficulty>(args[++i], ignoreCase: true); break;
    }
}

var cities = CityCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "cities.json"));
var fleetCatalog = AircraftCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "aircraft.json"));
var growthSchedule = DemandGrowthCatalog.LoadCampaignSchedule(Path.Combine(AppContext.BaseDirectory, "data")); // M21: baseline curve + authored historical_events.json, merged
var knownCityIds = cities.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
var hubProfiles = HubTrafficHistoryCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "hub_traffic_history.json"), knownCityIds); // docs/HUB_CAPACITY_GROWTH_DESIGN.md
var tradeRestrictions = TradeRestrictionCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "trade-restrictions.json"), knownCityIds); // geopolitical-aircraft-restrictions follow-up

Console.WriteLine($"AeroNext QA harness: {games} games x {quarters} quarters, base seed {baseSeed}, difficulty {difficulty}{(unconstrainedRegions ? " [unconstrained regions: duplicates allowed]" : "")}");
Console.WriteLine();

var results = new List<FullGameResult>();
var hardFailures = new List<(int GameIndex, int ConfigSeed, int CampaignSeed, Exception Error)>();

for (var i = 0; i < games; i++)
{
    var configSeed = baseSeed + i;
    var campaignSeed = baseSeed + i + 500_000; // offset well clear of any --games count in practical use

    try
    {
        var configRng = new SimRng(configSeed);
        IReadOnlyList<AirlineSeat> seats = unconstrainedRegions
            ? UnconstrainedSeatGenerator.Generate(configRng, cities)
            : GameSetup.CreateSeats(GameConfigGenerator.Generate(configRng, cities), cities, configRng, fillerWeightExponent: 0.0);

        var result = FullGameRunner.Run(i, configSeed, campaignSeed, quarters, seats, cities, fleetCatalog, growthSchedule, difficulty: difficulty, tradeRestrictions: tradeRestrictions, hubProfiles: hubProfiles);
        results.Add(result);

        if (!result.Success)
        {
            Console.WriteLine($"  [FAIL] game {i} (configSeed={configSeed}, campaignSeed={campaignSeed}, humans={result.HumanCount}): {result.Violations.Count} violation(s)");
            foreach (var violation in result.Violations)
            {
                Console.WriteLine($"      Q{violation.Quarter} {violation.AirlineName}: {violation.Description}");
            }
        }
    }
    catch (Exception ex)
    {
        hardFailures.Add((i, configSeed, campaignSeed, ex));
        Console.WriteLine($"  [CRASH] game {i} (configSeed={configSeed}, campaignSeed={campaignSeed}): {ex.GetType().Name}: {ex.Message}");
    }
}

var passed = results.Count(r => r.Success);
var failed = results.Count(r => !r.Success);
var everBankrupt = results.Count(r => r.BankruptcyQuarter.Values.Any(q => q is not null));
var cleanResults = results.Where(r => r.Success).ToList();
var wonOutright = cleanResults.Where(r => r.Winner is not null).ToList();

Console.WriteLine();
Console.WriteLine("=== Summary ===");
Console.WriteLine($"  {passed}/{games} passed cleanly");
Console.WriteLine($"  {failed} failed an invariant check");
Console.WriteLine($"  {hardFailures.Count} crashed with an unhandled exception");
Console.WriteLine($"  {everBankrupt} games had at least one airline go bankrupt before quarter {quarters}");
Console.WriteLine($"  {wonOutright.Count}/{cleanResults.Count} clean games had a win-condition winner before the timeline ended" +
    (wonOutright.Count > 0 ? $" (avg quarter {wonOutright.Average(r => r.WinQuarter!.Value):N1})" : ""));
Console.WriteLine($"  AI activity: {results.Sum(r => r.TypeSwapsApplied)} type swaps, " +
    $"{results.Sum(r => r.RouteCampaignsApplied)} route marketing campaigns, {results.Sum(r => r.NetworkCampaignsApplied)} network marketing campaigns, " +
    $"{results.Sum(r => r.MultiStopRouteOrdersApplied)} multi-stop route orders");
var allRouteQuarters = results.Sum(r => r.RouteQuarters);
Console.WriteLine($"  Contested route-quarters: {results.Sum(r => r.ContestedRouteQuarters)} of {allRouteQuarters}" +
    (allRouteQuarters > 0 ? $" ({100.0 * results.Sum(r => r.ContestedRouteQuarters) / allRouteQuarters:N2}%)" : ""));
var midLateQuarters = results.Sum(r => r.NetworkOptimizerQuarters + r.BalancedGrowthQuarters);
Console.WriteLine($"  Mid/late-game strategy mix: Network Optimizer {results.Sum(r => r.NetworkOptimizerQuarters)} airline-quarters, " +
    $"Balanced Growth {results.Sum(r => r.BalancedGrowthQuarters)}" +
    (midLateQuarters > 0 ? $" ({100.0 * results.Sum(r => r.NetworkOptimizerQuarters) / midLateQuarters:N1}% Network Optimizer)" : ""));

if (hardFailures.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Crashes (reproduce with the listed seeds):");
    foreach (var (gameIndex, configSeed, campaignSeed, ex) in hardFailures)
    {
        Console.WriteLine($"  game {gameIndex}: configSeed={configSeed} campaignSeed={campaignSeed}");
        Console.WriteLine($"    {ex}");
    }
}

var regionalOutcomes = RegionalOutcomeReport.Aggregate(results.Where(r => r.Success).ToList());

Console.WriteLine();
Console.WriteLine($"=== Regional Outcome Breakdown (from {passed} clean games) ===");
Console.WriteLine($"{"Region",-14}{"Seats",7}{"Bankrupt",10}{"BnkrptQ",9}{"AvgCash",18}{"MedianCash",18}{"Wins",7}");
foreach (var o in regionalOutcomes)
{
    var bankruptQ = o.AverageBankruptcyQuarter is { } q ? q.ToString("N1") : "n/a";
    Console.WriteLine($"{o.Region,-14}{o.SeatsCounted,7}{o.BankruptcyCount,10}{bankruptQ,9}{o.AverageFinalCash,18}{o.MedianFinalCash,18}{o.Wins,7}");
}

var exitCode = failed == 0 && hardFailures.Count == 0 ? 0 : 1;
Console.WriteLine();
Console.WriteLine(exitCode == 0 ? "PASS" : "FAIL");
return exitCode;
