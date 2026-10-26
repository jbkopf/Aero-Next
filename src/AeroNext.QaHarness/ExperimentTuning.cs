using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Setup;
using AeroNext.Core.World;

namespace AeroNext.QaHarness;

/// <summary>
/// How the AustralAsia starting (aircraft, destination) pair is picked —
/// factor A. Both values are a no-op relative to each other:
/// <see cref="StartingConditionsResolver.Resolve"/> itself always makes
/// the most-profitable pick (see that method's own doc comment) since it
/// shipped as the real default, so <see cref="Keep"/> and
/// <see cref="MostProfitable"/> compute the same route today. Kept,
/// unremoved, purely so the field and its own tests stay meaningful
/// history of the AustralAsia investigation
/// (docs/AUSTRALASIA_TUNING_EXPERIMENTS.md) rather than silently going
/// dead.
/// </summary>
public enum AustralAsiaAircraftMode
{
    /// <summary>The shipped <see cref="StartingConditionsResolver.Resolve"/> pick.</summary>
    Keep,

    /// <summary>The most profitable combination outright (<see cref="StartingConditionsResolver.MostProfitableRoute"/>).</summary>
    MostProfitable,
}

/// <summary>
/// The AustralAsia tuning-experiment knobs
/// (docs/AUSTRALASIA_TUNING_EXPERIMENTS.md). Every field defaults to the
/// shipped baseline, so a default-constructed instance — or passing none at
/// all — is a guaranteed no-op: <see cref="FullGameRunner.Run"/> produces a
/// byte-identical result. Each knob is deliberately AustralAsia-scoped;
/// this is scaffolding for deciding which (if any) of these become real
/// changes, not a general game-configuration surface.
///
/// AustralAsia's own factor D (intra-region demand multiplier) started here
/// and graduated to real, unconditional gameplay behavior once the
/// experiment confirmed it — see
/// <see cref="GravityDemandModel.ShippedIntraRegionMultipliers"/>.
/// It no longer has a knob for AustralAsia specifically: every real
/// per-quarter call site applies that entry directly, not gated by this
/// record. AustralAsia's factors S (this file) and A were measured *not*
/// to help once its own D shipped (see that plan doc's Phase 2/3 results)
/// and were kept here, off by default, as harmless, already-tested
/// scaffolding rather than removed outright.
///
/// A second, independent region was screened for the same
/// intra-region-multiplier mechanism — see
/// <see cref="SouthAmericaIntraRegionDemandMultiplier"/> and
/// docs/SOUTHAMERICA_TUNING_EXPERIMENTS.md — without touching AustralAsia's
/// own shipped entry. Rejected there (SouthAmerica's real problem was one
/// geographically-isolated city, not a region-wide demand shortfall);
/// the real fix landed as a data change (Belo Horizonte) instead.
/// </summary>
public sealed record ExperimentTuning
{
    /// <summary>
    /// Factor S. When set, AustralAsia's starting cash is raised to at
    /// least <c>BaseStartingCashDollars × this</c> (e.g. 1.0 → $20M,
    /// against the untuned ~$15.1M). The raw
    /// <see cref="RegionalEconomics.Factor"/> is left alone — it also
    /// drives the AI filler-region ranking in
    /// <see cref="Core.Setup.GameSetup"/>, and bumping it there silently
    /// reassigns ~100 seats to AustralAsia and invalidates the comparison.
    /// Measured: no bankruptcy benefit on top of factor D, and it
    /// measurably reduces AustralAsia's win share and the fraction of
    /// those wins that are genuine 3-region dominance rather than
    /// end-of-campaign ranking. Not adopted.
    /// </summary>
    public double? AustralAsiaCashFloorFactor { get; init; }

    /// <summary>
    /// Factor A. How AustralAsia's starting aircraft + destination is
    /// picked. Measured (against the old, heuristic-first
    /// <see cref="StartingConditionsResolver.Resolve"/>): net regression
    /// on its own (fixes the capital-starved cities but the larger
    /// starting aircraft's higher operating cost breaks several
    /// previously-safe ones — total bankruptcy rose). Not adopted at the
    /// time — though the always-optimal default that later shipped
    /// re-triggers exactly this AustralAsia failure mode at scale (see
    /// <see cref="StartingConditionsResolver"/>'s own top-level doc
    /// comment), not yet separately investigated. Now a no-op either
    /// way — see <see cref="AustralAsiaAircraftMode"/>'s own doc comment
    /// for why.
    /// </summary>
    public AustralAsiaAircraftMode AustralAsiaAircraft { get; init; } = AustralAsiaAircraftMode.Keep;

    /// <summary>
    /// SouthAmerica's own factor D (docs/SOUTHAMERICA_TUNING_EXPERIMENTS.md),
    /// screening the same intra-region demand multiplier mechanism M65
    /// shipped for AustralAsia. 1.0 = no change. Merges with, and never
    /// replaces, <see cref="GravityDemandModel.ShippedIntraRegionMultipliers"/>,
    /// which stays real and unconditional regardless of this knob.
    /// Measured (3 levels, 1.25/1.5/2.0): Rio's own win rate never moved
    /// (0.0% flat) — a flat regional multiplier scales every pair together
    /// and doesn't touch Rio's *relative* disadvantage, which was the real
    /// problem (see the real, landed fix: Belo Horizonte,
    /// data/cities.json's own "plu" entry). Not adopted; kept here, off by
    /// default, as harmless, already-tested scaffolding.
    /// </summary>
    public double SouthAmericaIntraRegionDemandMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Global Q1 under-provisioning threshold (docs/OPEN_WORK.md's
    /// filler-seat investigation) — unlike every other knob here, this
    /// applies to <em>every</em> seat, not one region. null = off,
    /// <see cref="StartingConditionsResolver.Resolve"/>'s own shipped pick,
    /// byte-identical. When set, every seat's guaranteed route comes from
    /// <see cref="StartingConditionsResolver.ResolveWithMinProfitFraction"/>
    /// at this fraction instead — see that method's own doc comment for
    /// what the value means. At 1.0 this is now a no-op (
    /// <see cref="StartingConditionsResolver.Resolve"/> already always
    /// makes the best-combination pick this fraction would force); every
    /// value below 1.0 keeps a heuristic pick this knob still computes
    /// itself, byte-for-byte the alphabetical-first-city rule
    /// <see cref="StartingConditionsResolver.Resolve"/> used to run before
    /// it shipped as always-optimal (see that class's own top-level doc
    /// comment).
    ///
    /// Measured against that old heuristic-first <c>Resolve</c> (4 levels,
    /// 0.3/0.5/0.7 vs baseline, 300 games): a cliff, not a dial. Below 0.3
    /// it's inert (most heuristic picks already clear that bar). At 0.5+
    /// total bankruptcy nearly quadruples (2.58%→9.50%), and the damage
    /// lands hardest on EastAsia specifically — previously the most
    /// bankruptcy-resistant region (4.5%) becomes the *most* (25.9%) once
    /// its filler seat gets pushed onto a bigger, pricier aircraft — the
    /// same "bigger starting aircraft, higher fixed cost" failure mode
    /// AustralAsia's factor A hit, but universal instead of scoped. Worse:
    /// even at 0.7, AustralAsia/SouthAmerica/SouthWestAsia's own filler
    /// seats stay at exactly 0.0% win rate — their problem was never
    /// really Q1 under-provisioning (their own dollar gaps were the
    /// smallest of the six regions measured); it's the deeper filler
    /// co-occurrence effect this knob was never going to reach. Not
    /// adopted at any level *at the time* — that measurement predates
    /// both the always-optimal default and the demand-model fix, so it
    /// no longer describes what 0.5/0.7 would do against today's shipped
    /// baseline.
    /// </summary>
    public double? GlobalMinProfitFraction { get; init; }

    /// <summary>
    /// Global route-choice weighting exponent, ≥ 0.0 (docs/OPEN_WORK.md's
    /// "difficulty setting" future-work note) — like
    /// <see cref="GlobalMinProfitFraction"/>, applies to every seat, not
    /// one region. null = off, <see cref="StartingConditionsResolver.Resolve"/>'s
    /// own shipped pick, byte-identical. When set, every seat's guaranteed
    /// route comes from
    /// <see cref="StartingConditionsResolver.ResolveWithWeightedRouteRandomness"/>
    /// instead, via <paramref name="routeWeightRng"/> — see that method's
    /// own doc comment for the real measurement of what each value does
    /// to bankruptcy (not a safety dial; see
    /// <see cref="StartingConditionsResolver"/>'s own top-level doc
    /// comment for the full write-up). Off by default, same disposition
    /// as every other measured-and-rejected knob in this file.
    /// </summary>
    public double? GlobalRouteWeightExponent { get; init; }

    /// <summary>Baseline: all knobs off.</summary>
    public static ExperimentTuning Baseline { get; } = new();

    /// <summary>
    /// The full <c>intraRegionMultipliers</c> map to hand a real
    /// per-quarter <see cref="GravityDemandModel"/> construction — the
    /// shipped AustralAsia entry plus this tuning's SouthAmerica entry, if
    /// any. Never null (AustralAsia's own entry is always present).
    /// </summary>
    public IReadOnlyDictionary<Region, double> IntraRegionDemandMultipliers =>
        SouthAmericaIntraRegionDemandMultiplier == 1.0
            ? GravityDemandModel.ShippedIntraRegionMultipliers
            : new Dictionary<Region, double>(GravityDemandModel.ShippedIntraRegionMultipliers)
            {
                [Region.SouthAmerica] = SouthAmericaIntraRegionDemandMultiplier,
            };

    /// <summary>
    /// The starting conditions for <paramref name="seat"/> with this
    /// tuning applied. <paramref name="baseline"/> is what
    /// <see cref="StartingConditionsResolver.Resolve"/> returned untouched;
    /// <paramref name="allCities"/>, <paramref name="fleet"/>, and
    /// <paramref name="growthSchedule"/> are the same arguments that
    /// resolver was handed. <paramref name="routeWeightRng"/>
    /// is only read when <see cref="GlobalRouteWeightExponent"/> is set,
    /// pass the caller's own seeded <see cref="SimRng"/> (never a fresh
    /// ambient one), reused across every seat's own call for the game so
    /// the whole sequence stays deterministic per campaign seed. A no-op
    /// for <see cref="Baseline"/>; a no-op for any seat outside AustralAsia
    /// unless <see cref="GlobalMinProfitFraction"/> or
    /// <see cref="GlobalRouteWeightExponent"/> is set (the two knobs here
    /// that aren't AustralAsia-scoped).
    /// </summary>
    public AirlineStartingConditions ApplyTo(
        AirlineSeat seat, AirlineStartingConditions baseline,
        IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet, IReadOnlyList<DemandGrowthPoint> growthSchedule,
        SimRng? routeWeightRng = null)
    {
        var result = baseline;

        if (GlobalMinProfitFraction is { } fraction)
        {
            var tuned = StartingConditionsResolver.ResolveWithMinProfitFraction(seat, allCities, fleet, fraction, growthSchedule);
            result = result with { StartingAircraft = tuned.StartingAircraft, GuaranteedRouteDestination = tuned.GuaranteedRouteDestination };
        }

        if (GlobalRouteWeightExponent is { } weightExponent)
        {
            if (routeWeightRng is null)
            {
                throw new ArgumentNullException(nameof(routeWeightRng), $"{nameof(GlobalRouteWeightExponent)} is set but no RNG was supplied.");
            }

            var tuned = StartingConditionsResolver.ResolveWithWeightedRouteRandomness(seat, allCities, fleet, weightExponent, routeWeightRng, growthSchedule);
            result = result with { StartingAircraft = tuned.StartingAircraft, GuaranteedRouteDestination = tuned.GuaranteedRouteDestination };
        }

        if (seat.HomeRegion != Region.AustralAsia)
        {
            return result;
        }

        if (AustralAsiaAircraft == AustralAsiaAircraftMode.MostProfitable)
        {
            var (aircraft, destination) = StartingConditionsResolver.MostProfitableRoute(seat, allCities, fleet, growthSchedule);
            result = result with { StartingAircraft = aircraft, GuaranteedRouteDestination = destination };
        }

        if (AustralAsiaCashFloorFactor is { } floor)
        {
            var flooredCash = Money.FromDollars((long)Math.Round(
                StartingConditionsResolver.BaseStartingCashDollars * floor, MidpointRounding.AwayFromZero));
            if (flooredCash.Cents > result.StartingCash.Cents)
            {
                result = result with { StartingCash = flooredCash };
            }
        }

        return result;
    }
}
