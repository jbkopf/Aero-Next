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
/// **"Always optimal" tried, rejected, root-caused, and now shipped.**
/// The old "V1" experiment (docs/AUSTRALASIA_TUNING_EXPERIMENTS.md)
/// measured making every airline's Q1 pick unconditionally optimal and
/// rejected it — fixed Europe harder but spiked EastAsia bankruptcy to
/// 24.5%. Tried again on the current codebase (after M65's AustralAsia
/// tuning, the SouthAmerica Belo Horizonte fix, and this file's own
/// sibling gateway-negotiation reachability fix had all shipped): still
/// bad (total bankruptcy 1.50%→4.42%, EastAsia 3.1%→10.7%, AustralAsia
/// 5.2%→15.5%). Traced to a real bug rather than an inherent tradeoff:
/// every method here built its demand model with no <c>schedule</c>/
/// <c>year</c> — invisible to <c>historical_events.json</c>'s authored,
/// city/region-scoped 1955 demand suppression (e.g. Shanghai's Maoist-era
/// "closed economy" entry, a real 0.3x multiplier the actual per-quarter
/// market always applies). "Most profitable" chased the biggest *mature*
/// demand weight it could see — disproportionately the same handful of
/// cities carrying the largest 1955 suppression — sizing an aircraft for
/// demand that was never really there on quarter 1 (fuk→pvg's static
/// estimate saw 27,939 passengers/quarter; the real quarter-1 market
/// delivers exactly 8,382, i.e. 27,939 × 0.3). <see cref="Quarter1DemandModel"/>
/// now builds the exact same schedule/year-aware model every real
/// per-quarter call site already uses. Re-measured with the fix in place:
/// total bankruptcy 1.75%→2.33%, but EastAsia — the region the bug hurt
/// worst before — now *improves* under always-optimal (4.5%→2.1%, safer
/// than the heuristic itself) once it can see the real, suppressed 1955
/// demand instead of a phantom mature-market number. Every region's own
/// change nets to +7 bankruptcies total, and AustralAsia alone accounts
/// for all seven (5.2%→12.4%) — a separate capital-intensity problem
/// (a bigger, pricier aircraft against the game's most capital-poor
/// starting region), not a demand mismatch, not yet investigated.
///
/// Per direct instruction, <see cref="Resolve"/> now ships as always
/// optimal — <see cref="MostProfitableRoute"/>, unconditionally — given
/// this net-positive, root-caused re-measurement. The old heuristic
/// (smallest-range aircraft that can reach the alphabetically-first
/// positive-demand eligible city, falling back to the best combination
/// only when that loses money) is gone from <see cref="Resolve"/> itself;
/// <see cref="ResolveWithMinProfitFraction"/> still has its own copy of
/// it for the AustralAsia/SouthAmerica <see cref="ExperimentTuning"/>
/// knobs that call it by name.
///
/// **The future-difficulty-setting hook.**
/// <see cref="ResolveWithWeightedRouteRandomness"/> is the adjustable
/// mechanism this always-optimal default plugs into: rank-weighted by
/// <c>weightExponent</c>, converging on (large exponent) or diverging
/// from (small exponent) exactly what <see cref="Resolve"/> now always
/// picks. No real difficulty setting exists yet to drive it — this is
/// the load-bearing structural guarantee that one can, later, without
/// re-plumbing <see cref="StartingConditionsResolver"/> itself: a future
/// setting only has to pick an exponent (and pass its own seeded
/// <see cref="SimRng"/>) instead of calling <see cref="Resolve"/>
/// directly. <see cref="ExperimentTuning.GlobalRouteWeightExponent"/> is
/// today's only real caller, off by default, QA-harness-scoped. Its own
/// exponent screen (pre-fix numbers, not yet re-verified against this
/// demand-model fix) found the low-exponent end is *not* a "safer, more
/// random" endpoint — total bankruptcy got worse toward uniform-random,
/// not better, because most valid combinations are mediocre and only one
/// or two per city are genuinely strong; treat that shape as a caveat for
/// whoever designs the real setting, not a guarantee at every level.
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

    /// <summary>
    /// The real, shipped default: always the single most profitable
    /// (aircraft, destination) combination — see this file's own doc
    /// comment above for the full history of why. Byte-identical to
    /// <see cref="MostProfitableRoute"/>, wrapped with starting cash.
    /// </summary>
    public static AirlineStartingConditions Resolve(
        AirlineSeat seat, IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet, IReadOnlyList<DemandGrowthPoint> growthSchedule)
    {
        var cash = StartingCashFor(seat, allCities);
        var (aircraft, destination) = MostProfitableRoute(seat, allCities, fleet, growthSchedule);
        return new AirlineStartingConditions(cash, aircraft, StartingAircraftQuantity, destination);
    }

    /// <summary>
    /// The most profitable starting (aircraft, destination) combination
    /// outright — no smallest-aircraft / alphabetical-first heuristic. This
    /// is exactly what <see cref="Resolve"/> itself now always returns; it
    /// stays a separate public method because <see cref="ExperimentTuning"/>'s
    /// AustralAsia-only "factor A" knob and
    /// <see cref="ResolveWithMinProfitFraction"/>'s upper bracket both still
    /// call it by name. Same option set, same tie-break, same "nothing
    /// reachable" throw as <see cref="Resolve"/>.
    /// </summary>
    public static (AircraftType Aircraft, City Destination) MostProfitableRoute(
        AirlineSeat seat, IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet, IReadOnlyList<DemandGrowthPoint> growthSchedule)
    {
        var demandModel = Quarter1DemandModel(growthSchedule);
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
    /// investigation): keeps its own copy of the heuristic pick — the
    /// same smallest-aircraft / alphabetical-first choice
    /// <see cref="Resolve"/> used before it was promoted to always-optimal
    /// (see this file's own top-level doc comment) — only when its profit
    /// is at least <paramref name="minProfitFraction"/> of the best
    /// available (aircraft, destination) combination's profit; otherwise
    /// uses the best combination outright. <c>minProfitFraction</c> near
    /// 0.0 reduces to (but is not byte-identical with — see the degenerate
    /// both-non-positive case below) <see cref="Resolve"/>'s old
    /// "keep if profit ≥ $0" rule; 1.0 reduces to
    /// <see cref="MostProfitableRoute"/> always winning, which
    /// <see cref="Resolve"/> itself now already does unconditionally. A
    /// separate, parallel method, kept for the AustralAsia/SouthAmerica
    /// <see cref="ExperimentTuning"/> tuning knobs that still call it by
    /// name; never touches <see cref="Resolve"/> itself.
    /// </summary>
    public static AirlineStartingConditions ResolveWithMinProfitFraction(
        AirlineSeat seat, IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet, double minProfitFraction, IReadOnlyList<DemandGrowthPoint> growthSchedule)
    {
        var cash = StartingCashFor(seat, allCities);

        var demandModel = Quarter1DemandModel(growthSchedule);
        var (bestAircraft, bestDestination) = MostProfitableRoute(seat, allCities, fleet, growthSchedule); // throws if nothing reachable at all

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
    /// docs/OPEN_WORK.md's "difficulty setting" future-work note, and
    /// <see cref="Resolve"/>'s own future-difficulty-setting hook (see this
    /// file's own top-level doc comment): a genuinely weighted (not binary)
    /// pick among every reachable, positive-demand (aircraft, destination)
    /// combination, biased toward the best by
    /// <paramref name="weightExponent"/> — the same rank-weighted idiom
    /// <see cref="GameSetup"/>'s own <c>fillerWeightExponent</c> applies to
    /// region wealth, applied here to route profitability rank instead.
    /// Combination rank <c>i</c> (0 = <see cref="MostProfitableRoute"/>'s
    /// own pick, richest profit first) gets weight
    /// <c>1 / (i+1)^weightExponent</c>: a large exponent converges on
    /// exactly what <see cref="Resolve"/> now always picks; at
    /// <paramref name="weightExponent"/> = 0.0 every valid combination is
    /// equally likely. <b>The low end is not a validated "easier/safer"
    /// endpoint</b> — a screening sweep taken before the demand-model fix
    /// above found total bankruptcy got *worse*, not better, toward
    /// uniform-random (12.5% at exponent 0.5), because most valid
    /// combinations are mediocre and only one or two per city are
    /// genuinely strong; not yet re-verified against the fix. Kept,
    /// tested, off by default — real scaffolding for whoever designs the
    /// real setting. Uses <paramref name="rng"/> explicitly, per
    /// CLAUDE.md's determinism rule (no ambient randomness; same seed,
    /// same pick, always). A separate, parallel method — never touches
    /// <see cref="Resolve"/>. Throws under the same "nothing reachable"
    /// condition as <see cref="Resolve"/>/<see cref="MostProfitableRoute"/>.
    /// </summary>
    public static AirlineStartingConditions ResolveWithWeightedRouteRandomness(
        AirlineSeat seat, IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet, double weightExponent, SimRng rng, IReadOnlyList<DemandGrowthPoint> growthSchedule)
    {
        var cash = StartingCashFor(seat, allCities);

        var demandModel = Quarter1DemandModel(growthSchedule);
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

    /// <summary>
    /// The same demand model every real per-quarter call site
    /// (<c>FullGameRunner</c>, <c>AeroNext.Sim</c>) builds for quarter 1 —
    /// <paramref name="growthSchedule"/> and <see cref="Timeline.StartYear"/>
    /// resolve <c>historical_events.json</c>'s authored, city/region-scoped
    /// 1955 demand suppression (e.g. Shanghai's Maoist-era "closed economy"
    /// entry, a real 0.3x multiplier), and
    /// <see cref="GravityDemandModel.ShippedIntraRegionMultipliers"/>
    /// is the real shipped baseline every quarter applies regardless of any
    /// QA-harness tuning. Before this, every method here built a bare
    /// <c>new GravityDemandModel(enableHubDemandBonus: true)</c> with no
    /// schedule at all — invisible to those suppression entries, so a
    /// profitability ranking could chase a city's *mature* demand weight
    /// while the real quarter-1 market only delivers a fraction of it. A
    /// real trace (docs/OPEN_WORK.md) found this exact gap driving most of
    /// the "always optimal" regression: fuk→pvg's static estimate saw
    /// 27,939 passengers/quarter; the real quarter-1 market, with Shanghai's
    /// 0.3x multiplier applied, delivers exactly 8,382 — an oversized
    /// aircraft chasing demand that was never really there.
    /// </summary>
    private static GravityDemandModel Quarter1DemandModel(IReadOnlyList<DemandGrowthPoint> growthSchedule) =>
        new(schedule: growthSchedule, year: Timeline.StartYear, enableHubDemandBonus: true,
            intraRegionMultipliers: GravityDemandModel.ShippedIntraRegionMultipliers,
            groundCompetitionDistanceKm: GravityDemandModel.ShippedGroundCompetitionDistanceKm);

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
