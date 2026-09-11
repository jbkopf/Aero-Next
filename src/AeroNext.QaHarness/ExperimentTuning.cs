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
/// Factor D (intra-region demand multiplier) started here and graduated to
/// real, unconditional gameplay behavior once the experiment confirmed it —
/// see <see cref="GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers"/>.
/// It no longer has a knob: every real per-quarter call site applies it
/// directly, not gated by this record. Factors S (this file) and A were
/// measured *not* to help once D shipped (see the plan doc's Phase 2/3
/// results) and were kept here, off by default, as harmless, already-tested
/// scaffolding rather than removed outright.
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

    /// <summary>Baseline: all knobs off.</summary>
    public static ExperimentTuning Baseline { get; } = new();

    /// <summary>
    /// The starting conditions for <paramref name="seat"/> with this
    /// tuning applied. <paramref name="baseline"/> is what
    /// <see cref="StartingConditionsResolver.Resolve"/> returned untouched;
    /// <paramref name="allCities"/> and <paramref name="fleet"/> are the
    /// same lists that resolver was handed. A no-op for any seat outside
    /// AustralAsia and for <see cref="Baseline"/>.
    /// </summary>
    public AirlineStartingConditions ApplyTo(
        AirlineSeat seat, AirlineStartingConditions baseline,
        IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet)
    {
        if (seat.HomeRegion != Region.AustralAsia)
        {
            return baseline;
        }

        var result = baseline;

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
