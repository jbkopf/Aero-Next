using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.World;

namespace AeroNext.Core.Setup;

/// <summary>
/// M18: the real starting state derived from an <see cref="AirlineSeat"/>'s
/// (region, city) — cash scaled by M16's regional economics, a
/// region-appropriate starting aircraft, and the specific same-region city
/// this milestone's own "Done when" check guarantees is reachable with
/// positive demand on quarter 1. Fleet ownership isn't enforced against
/// route capacity anywhere in the sim yet (a gap M10/M11/M12 each already
/// noted and deliberately left open) — <c>AircraftQuantity</c> is real data
/// for that future mechanic to consume, not something <c>QuarterEngine</c>
/// checks today.
///
/// Guaranteed-route profitability follow-up: the destination + aircraft
/// pairing used to be picked "smallest-range aircraft that can reach the
/// alphabetically-first eligible city with any positive demand" —
/// profitability-blind. A real analysis (temporary FullGameRunner.cs
/// instrumentation + a scratchpad calculator, both reverted after use)
/// found ~13% of eligible starting cities were routed onto a pair that
/// can never turn a profit with any 1955 aircraft — e.g. every
/// Northern-European start (Helsinki, Manchester, Oslo, Stockholm) went
/// to Athens, a ~$1M/quarter loss from turn one, purely because "ath"
/// sorts near the front of the alphabet. That seeds a real cascade a
/// human player hits just as hard: cash falls → AiSuitability.CashRatio
/// tips the year-boundary strategy comparison → the airline is reassigned
/// to DomesticProfitOptimizerStrategy (5-route cap, no cross-region
/// reach) → it can't grow out of the hole → borrows → debt overhang
/// keeps net income negative permanently → ~32% of those airlines go
/// bankrupt.
///
/// The **minimal** fix here keeps the old selection whenever it already
/// lands on a profitable pair, and only re-optimizes to the most
/// profitable (aircraft, destination) combination when the old pick would
/// have been a loss — so a game where everyone already had a decent start
/// is left byte-identical, and only the ~13% trap cities change. A real
/// 300-game sweep: Europe bankruptcy 13.7%→7.1%, AustralAsia 29.9%→19.6%
/// (the two regions with trap starting cities), every other region
/// unchanged to the seat, total bankruptcy 6.1%→4.2%. A fuller variant
/// that re-optimized *every* airline's start instead of just the broken
/// ones was measured and rejected — it fixed Europe harder (0.5%) but
/// spiked EastAsia to 24.5% and raised total bankruptcy, because a game
/// where everyone starts strong is a more brutal competitive game that
/// crushes the structurally-small home regions.
///
/// Four genuinely isolated cities (Nadi, Nouméa, Perth, Papeete) still
/// have no profitable domestic pair at 1955 tech and get the least-bad
/// option — a separate, harder problem (a data or catalog gap, not a
/// selection one) left open.
///
/// **"Always optimal" tried again, re-rejected.** The old "V1" experiment
/// (docs/AUSTRALASIA_TUNING_EXPERIMENTS.md) measured making every
/// airline's Q1 pick unconditionally optimal and rejected it — fixed
/// Europe harder but spiked EastAsia bankruptcy to 24.5%. Per direct
/// instruction this was tried again on the current codebase (after M65's
/// AustralAsia tuning, the SouthAmerica Belo Horizonte fix, and this
/// file's own sibling gateway-negotiation reachability fix in
/// <c>RapidExpansionStrategy</c>/<c>BalancedGrowthStrategy</c> had all
/// shipped, in case those changes had defused the old failure mode). They
/// hadn't: a real 300-game sweep with <see cref="Resolve"/> made
/// unconditionally optimal showed total bankruptcy 1.50%→4.42%
/// (EastAsia 3.1%→10.7%, AustralAsia 5.2%→15.5%), Europe still the one
/// clear winner (0.0%→0.5% bankrupt, win share 5.7%→12.3%). A follow-up
/// screen across <see cref="ResolveWithWeightedRouteRandomness"/>'s own
/// exponent, hoping for a safer middle ground, found the opposite of a
/// dial: total bankruptcy is monotonically *worse* toward the
/// uniform-random end (12.5% at exponent 0.5) and only improves toward
/// the optimal end, plateauing around 4.8–5.2% — never back near 1.50% at
/// any exponent. The old heuristic below isn't a "medium-random" policy;
/// it's a specific, decent pick that beats uniform-random-among-all-valid-
/// combos, because most valid combos are mediocre and only one or two per
/// city are genuinely strong. <see cref="Resolve"/> therefore stays on the
/// heuristic below, unconditionally, exactly as it always has;
/// <see cref="ResolveWithWeightedRouteRandomness"/> is kept, tested, and
/// off by default — real scaffolding for whenever a future difficulty
/// setting exists, same disposition as every other measured-and-rejected
/// knob in <see cref="ExperimentTuning"/>.
/// </summary>
public sealed record AirlineStartingConditions(
    Money StartingCash,
    AircraftType StartingAircraft,
    int AircraftQuantity,
    City GuaranteedRouteDestination);

public static class StartingConditionsResolver
{
    public const long BaseStartingCashDollars = 20_000_000;
    public const int StartingAircraftQuantity = 2;

    public static AirlineStartingConditions Resolve(AirlineSeat seat, IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet)
    {
        var cash = StartingCashFor(seat, allCities);

        var demandModel = new GravityDemandModel(enableHubDemandBonus: true);
        var aircraftOptions = RegionalFleet.AppropriateAircraft(seat.HomeRegion, fleet, Timeline.StartYear)
            .OrderBy(a => a.RangeKm)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
        var destinationOptions = HomeRegions.EligibleStartingCities(seat.HomeRegion, allCities)
            .Where(c => c.Id != seat.StartingCity.Id)
            .ToList();

        // The original selection: smallest-range aircraft that can reach
        // the alphabetically-first positive-demand eligible city.
        foreach (var aircraft in aircraftOptions)
        {
            var destination = destinationOptions
                .Where(c => GreatCircle.DistanceKm(seat.StartingCity, c) <= aircraft.RangeKm)
                .Where(c => demandModel.ForPair(seat.StartingCity, c).PassengersPerQuarter > 0)
                .OrderBy(c => c.Id, StringComparer.Ordinal)
                .FirstOrDefault();

            if (destination is null)
            {
                continue;
            }

            // Only re-optimize when the original pick would lose money.
            if (RouteProfit(seat.StartingCity, destination, aircraft, demandModel) >= 0)
            {
                return new AirlineStartingConditions(cash, aircraft, StartingAircraftQuantity, destination);
            }

            var best = MostProfitableCombination(seat, destinationOptions, aircraftOptions, demandModel);
            return new AirlineStartingConditions(cash, best.Aircraft, StartingAircraftQuantity, best.Destination);
        }

        throw new InvalidOperationException(
            $"No appropriate {Timeline.StartYear} aircraft for {seat.HomeRegion} can open a positive-demand route " +
            $"from {seat.StartingCity.Id} to any other eligible city in the region.");
    }

    /// <summary>
    /// The most profitable starting (aircraft, destination) combination
    /// outright — no smallest-aircraft / alphabetical-first heuristic, just
    /// the pick <see cref="Resolve"/> already falls back to when its
    /// heuristic pick loses money, applied unconditionally. Measured as a
    /// universal policy twice now (docs/AUSTRALASIA_TUNING_EXPERIMENTS.md's
    /// "V1", and again on the current codebase — see this file's own
    /// top-level doc comment) and rejected both times. Same option set,
    /// same tie-break, same "nothing reachable" throw as <see cref="Resolve"/>.
    /// </summary>
    public static (AircraftType Aircraft, City Destination) MostProfitableRoute(
        AirlineSeat seat, IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet)
    {
        var demandModel = new GravityDemandModel(enableHubDemandBonus: true);
        var aircraftOptions = RegionalFleet.AppropriateAircraft(seat.HomeRegion, fleet, Timeline.StartYear)
            .OrderBy(a => a.RangeKm)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
        var destinationOptions = HomeRegions.EligibleStartingCities(seat.HomeRegion, allCities)
            .Where(c => c.Id != seat.StartingCity.Id)
            .ToList();

        var anyReachable = aircraftOptions.Any(a => destinationOptions.Any(c =>
            GreatCircle.DistanceKm(seat.StartingCity, c) <= a.RangeKm &&
            demandModel.ForPair(seat.StartingCity, c).PassengersPerQuarter > 0));

        if (!anyReachable)
        {
            throw new InvalidOperationException(
                $"No appropriate {Timeline.StartYear} aircraft for {seat.HomeRegion} can open a positive-demand route " +
                $"from {seat.StartingCity.Id} to any other eligible city in the region.");
        }

        return MostProfitableCombination(seat, destinationOptions, aircraftOptions, demandModel);
    }

    /// <summary>
    /// A generalized guaranteed-route selection, bracketing the two
    /// already-measured extremes (docs/OPEN_WORK.md's filler-seat
    /// investigation): keeps the heuristic pick — the same
    /// smallest-aircraft / alphabetical-first choice <see cref="Resolve"/>
    /// itself uses — only when its profit is at least
    /// <paramref name="minProfitFraction"/> of the best available
    /// (aircraft, destination) combination's profit; otherwise uses the
    /// best combination outright. <c>minProfitFraction</c> near 0.0
    /// reduces to (but is not byte-identical with — see the degenerate
    /// both-non-positive case below) <see cref="Resolve"/>'s own shipped
    /// "keep if profit ≥ $0" rule; 1.0 reduces to
    /// <see cref="MostProfitableRoute"/> always winning (the "V1" variant
    /// the AustralAsia investigation measured and rejected as a universal
    /// policy — see docs/AUSTRALASIA_TUNING_EXPERIMENTS.md). A separate,
    /// parallel method — never touches <see cref="Resolve"/>, which stays
    /// real, shipped, unconditional behavior.
    /// </summary>
    public static AirlineStartingConditions ResolveWithMinProfitFraction(
        AirlineSeat seat, IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet, double minProfitFraction)
    {
        var cash = StartingCashFor(seat, allCities);

        var demandModel = new GravityDemandModel(enableHubDemandBonus: true);
        var (bestAircraft, bestDestination) = MostProfitableRoute(seat, allCities, fleet); // throws if nothing reachable at all

        var aircraftOptions = RegionalFleet.AppropriateAircraft(seat.HomeRegion, fleet, Timeline.StartYear)
            .OrderBy(a => a.RangeKm)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
        var destinationOptions = HomeRegions.EligibleStartingCities(seat.HomeRegion, allCities)
            .Where(c => c.Id != seat.StartingCity.Id)
            .ToList();

        // The same heuristic pick Resolve itself makes; guaranteed to find
        // something, since MostProfitableRoute above didn't throw and uses
        // the identical reachability condition.
        var (heuristicAircraft, heuristicDestination) = aircraftOptions
            .Select(aircraft => (aircraft, destination: destinationOptions
                .Where(c => GreatCircle.DistanceKm(seat.StartingCity, c) <= aircraft.RangeKm)
                .Where(c => demandModel.ForPair(seat.StartingCity, c).PassengersPerQuarter > 0)
                .OrderBy(c => c.Id, StringComparer.Ordinal)
                .FirstOrDefault()))
            .First(x => x.destination is not null);

        var heuristicProfit = RouteProfit(seat.StartingCity, heuristicDestination!, heuristicAircraft, demandModel);
        var bestProfit = RouteProfit(seat.StartingCity, bestDestination, bestAircraft, demandModel);

        var keepHeuristic = bestProfit <= 0
            ? heuristicProfit >= bestProfit
            : heuristicProfit >= minProfitFraction * bestProfit;

        return keepHeuristic
            ? new AirlineStartingConditions(cash, heuristicAircraft, StartingAircraftQuantity, heuristicDestination!)
            : new AirlineStartingConditions(cash, bestAircraft, StartingAircraftQuantity, bestDestination);
    }

    /// <summary>
    /// docs/OPEN_WORK.md's "difficulty setting" future-work note: a
    /// genuinely weighted (not binary) pick among every reachable,
    /// positive-demand (aircraft, destination) combination, biased toward
    /// the best by <paramref name="weightExponent"/> — the same
    /// rank-weighted idiom <see cref="GameSetup"/>'s own
    /// <c>fillerWeightExponent</c> applies to region wealth, applied here
    /// to route profitability rank instead. Combination rank <c>i</c>
    /// (0 = <see cref="MostProfitableRoute"/>'s own pick, richest profit
    /// first) gets weight <c>1 / (i+1)^weightExponent</c>: at
    /// <paramref name="weightExponent"/> = 0.0 every valid combination is
    /// equally likely; as it grows, the distribution concentrates on the
    /// best pick. <b>Not a safety dial</b> — a real screening sweep (this
    /// file's own top-level doc comment) found total bankruptcy gets
    /// *worse*, not better, toward the uniform-random end (12.5% at
    /// exponent 0.5) and only improves toward the fully-optimal end,
    /// plateauing around 4.8–5.2% — never back near <see cref="Resolve"/>'s
    /// own ~1.5% baseline at any exponent. Kept, tested, off by default —
    /// real scaffolding for a future difficulty setting, not a validated
    /// safety knob. Uses <paramref name="rng"/> explicitly, per CLAUDE.md's
    /// determinism rule (no ambient randomness; same seed, same pick,
    /// always). A separate, parallel method — never touches
    /// <see cref="Resolve"/>. Throws under the same "nothing reachable"
    /// condition as <see cref="Resolve"/>/<see cref="MostProfitableRoute"/>.
    /// </summary>
    public static AirlineStartingConditions ResolveWithWeightedRouteRandomness(
        AirlineSeat seat, IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet, double weightExponent, SimRng rng)
    {
        var cash = StartingCashFor(seat, allCities);

        var demandModel = new GravityDemandModel(enableHubDemandBonus: true);
        var aircraftOptions = RegionalFleet.AppropriateAircraft(seat.HomeRegion, fleet, Timeline.StartYear)
            .OrderBy(a => a.RangeKm)
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
        var destinationOptions = HomeRegions.EligibleStartingCities(seat.HomeRegion, allCities)
            .Where(c => c.Id != seat.StartingCity.Id)
            .ToList();

        var allValid = AllValidCombinationsByProfitDescending(seat, destinationOptions, aircraftOptions, demandModel);
        if (allValid.Count == 0)
        {
            throw new InvalidOperationException(
                $"No appropriate {Timeline.StartYear} aircraft for {seat.HomeRegion} can open a positive-demand route " +
                $"from {seat.StartingCity.Id} to any other eligible city in the region.");
        }

        var (aircraft, destination) = WeightedRandomCombination(allValid, weightExponent, rng);
        return new AirlineStartingConditions(cash, aircraft, StartingAircraftQuantity, destination);
    }

    /// <summary>
    /// Draws one combination with probability proportional to
    /// <c>1 / (rank+1)^weightExponent</c>, where rank is the combination's
    /// own index in <paramref name="rankedCombinations"/> (already
    /// profit-descending). Same weighted-draw shape as
    /// <see cref="GameSetup"/>'s own region sampler, minus the
    /// without-replacement removal loop (this only ever draws once).
    /// </summary>
    private static (AircraftType Aircraft, City Destination) WeightedRandomCombination(
        IReadOnlyList<(AircraftType Aircraft, City Destination)> rankedCombinations, double weightExponent, SimRng rng)
    {
        var weights = rankedCombinations.Select((_, rank) => Math.Pow(rank + 1, -weightExponent)).ToList();
        var totalWeight = weights.Sum();
        var roll = rng.NextDouble() * totalWeight;
        var cumulative = 0.0;
        var pickIndex = rankedCombinations.Count - 1; // floating-point fallback: last item absorbs any rounding slop

        for (var i = 0; i < weights.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
            {
                pickIndex = i;
                break;
            }
        }

        return rankedCombinations[pickIndex];
    }

    private static Money StartingCashFor(AirlineSeat seat, IReadOnlyList<City> allCities) =>
        Money.FromDollars((long)Math.Round(
            BaseStartingCashDollars * RegionalEconomics.Factor(seat.HomeRegion, allCities), MidpointRounding.AwayFromZero));

    private static long RouteProfit(City origin, City destination, AircraftType aircraft, GravityDemandModel demandModel)
    {
        var route = new Route(origin, destination, aircraft, AircraftCount: 1);
        var carried = Math.Min(demandModel.ForPair(origin, destination).PassengersPerQuarter, route.SeatsPerQuarter);
        return FareCalculator.EstimatedProfit(route, carried).Cents;
    }

    /// <summary>The (aircraft, destination) with the best real quarter-1 profit; ties break by shorter range, then aircraft id, then destination id.</summary>
    private static (AircraftType Aircraft, City Destination) MostProfitableCombination(
        AirlineSeat seat, IReadOnlyList<City> destinationOptions, IReadOnlyList<AircraftType> aircraftOptions, GravityDemandModel demandModel) =>
        AllValidCombinationsByProfitDescending(seat, destinationOptions, aircraftOptions, demandModel).First();

    /// <summary>Every reachable, positive-demand (aircraft, destination) combination, richest profit first; same tie-break as <see cref="MostProfitableCombination"/> always used.</summary>
    private static List<(AircraftType Aircraft, City Destination)> AllValidCombinationsByProfitDescending(
        AirlineSeat seat, IReadOnlyList<City> destinationOptions, IReadOnlyList<AircraftType> aircraftOptions, GravityDemandModel demandModel) =>
        (from aircraft in aircraftOptions
         from destination in destinationOptions
         where GreatCircle.DistanceKm(seat.StartingCity, destination) <= aircraft.RangeKm
         where demandModel.ForPair(seat.StartingCity, destination).PassengersPerQuarter > 0
         orderby RouteProfit(seat.StartingCity, destination, aircraft, demandModel) descending, aircraft.RangeKm, aircraft.Id, destination.Id
         select (aircraft, destination))
        .ToList();
}
