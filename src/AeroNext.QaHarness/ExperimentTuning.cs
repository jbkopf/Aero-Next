using AeroNext.Core;
using AeroNext.Core.Airlines;
using AeroNext.Core.Setup;
using AeroNext.Core.World;

namespace AeroNext.QaHarness;

/// <summary>How the AustralAsia starting (aircraft, destination) pair is picked — factor A.</summary>
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
/// <see cref="GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers"/>.
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
    /// picked. Measured: net regression on its own (fixes the
    /// capital-starved cities but the larger starting aircraft's higher
    /// operating cost breaks several previously-safe ones — total
    /// bankruptcy rose). Not adopted.
    /// </summary>
    public AustralAsiaAircraftMode AustralAsiaAircraft { get; init; } = AustralAsiaAircraftMode.Keep;

    /// <summary>
    /// SouthAmerica's own factor D (docs/SOUTHAMERICA_TUNING_EXPERIMENTS.md),
    /// screening the same intra-region demand multiplier mechanism M65
    /// shipped for AustralAsia. 1.0 = no change. Merges with, and never
    /// replaces, <see cref="GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers"/>,
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
    /// what the value means (0.0 ≈ shipped Resolve; 1.0 = always the best
    /// available combination, the already-rejected "V1" policy).
    ///
    /// Measured (4 levels, 0.3/0.5/0.7 vs baseline, 300 games): a cliff, not
    /// a dial. Below 0.3 it's inert (most heuristic picks already clear
    /// that bar). At 0.5+ total bankruptcy nearly quadruples (2.58%→9.50%),
    /// and the damage lands hardest on EastAsia specifically — previously
    /// the most bankruptcy-resistant region (4.5%) becomes the *most*
    /// (25.9%) once its filler seat gets pushed onto a bigger, pricier
    /// aircraft — the same "bigger starting aircraft, higher fixed cost"
    /// failure mode AustralAsia's factor A hit, but universal instead of
    /// scoped. Worse: even at 0.7, AustralAsia/SouthAmerica/SouthWestAsia's
    /// own filler seats stay at exactly 0.0% win rate — their problem was
    /// never really Q1 under-provisioning (their own dollar gaps were the
    /// smallest of the six regions measured); it's the deeper filler
    /// co-occurrence effect this knob was never going to reach. Not
    /// adopted at any level.
    /// </summary>
    public double? GlobalMinProfitFraction { get; init; }

    /// <summary>
    /// Global route-choice randomness, [0.0, 1.0] (docs/OPEN_WORK.md's
    /// "difficulty setting" future-work note) — like
    /// <see cref="GlobalMinProfitFraction"/>, applies to every seat, not
    /// one region. null = off, <see cref="StartingConditionsResolver.Resolve"/>'s
    /// own shipped pick, byte-identical. When set, every seat's guaranteed
    /// route comes from
    /// <see cref="StartingConditionsResolver.ResolveWithRouteRandomness"/>
    /// instead, via <paramref name="routeRandomnessRng"/> — see that
    /// method's own doc comment for what the value means. The intended
    /// eventual use: a "how well does the AI choose its own opening move"
    /// dial for a future difficulty setting (0.0 = hardest/optimal, 1.0 =
    /// easiest/uniform-random).
    /// </summary>
    public double? GlobalRouteRandomness { get; init; }

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
            ? GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers
            : new Dictionary<Region, double>(GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers)
            {
                [Region.SouthAmerica] = SouthAmericaIntraRegionDemandMultiplier,
            };

    /// <summary>
    /// The starting conditions for <paramref name="seat"/> with this
    /// tuning applied. <paramref name="baseline"/> is what
    /// <see cref="StartingConditionsResolver.Resolve"/> returned untouched;
    /// <paramref name="allCities"/> and <paramref name="fleet"/> are the
    /// same lists that resolver was handed. <paramref name="routeRandomnessRng"/>
    /// is only read when <see cref="GlobalRouteRandomness"/> is set — pass
    /// the caller's own seeded <see cref="SimRng"/> (never a fresh ambient
    /// one), reused across every seat's own call for the game so the whole
    /// sequence stays deterministic per campaign seed. A no-op for
    /// <see cref="Baseline"/>; a no-op for any seat outside AustralAsia
    /// unless <see cref="GlobalMinProfitFraction"/> or
    /// <see cref="GlobalRouteRandomness"/> is set (the two knobs here that
    /// aren't AustralAsia-scoped).
    /// </summary>
    public AirlineStartingConditions ApplyTo(
        AirlineSeat seat, AirlineStartingConditions baseline,
        IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet,
        SimRng? routeRandomnessRng = null)
    {
        var result = baseline;

        if (GlobalMinProfitFraction is { } fraction)
        {
            var tuned = StartingConditionsResolver.ResolveWithMinProfitFraction(seat, allCities, fleet, fraction);
            result = result with { StartingAircraft = tuned.StartingAircraft, GuaranteedRouteDestination = tuned.GuaranteedRouteDestination };
        }

        if (GlobalRouteRandomness is { } randomness)
        {
            if (routeRandomnessRng is null)
            {
                throw new ArgumentNullException(nameof(routeRandomnessRng), $"{nameof(GlobalRouteRandomness)} is set but no RNG was supplied.");
            }

            var tuned = StartingConditionsResolver.ResolveWithRouteRandomness(seat, allCities, fleet, randomness, routeRandomnessRng);
            result = result with { StartingAircraft = tuned.StartingAircraft, GuaranteedRouteDestination = tuned.GuaranteedRouteDestination };
        }

        if (seat.HomeRegion != Region.AustralAsia)
        {
            return result;
        }

        if (AustralAsiaAircraft == AustralAsiaAircraftMode.MostProfitable)
        {
            var (aircraft, destination) = StartingConditionsResolver.MostProfitableRoute(seat, allCities, fleet);
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
