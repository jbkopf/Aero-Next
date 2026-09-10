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
    /// </summary>
    public double? AustralAsiaCashFloorFactor { get; init; }

    /// <summary>Factor A. How AustralAsia's starting aircraft + destination is picked.</summary>
    public AustralAsiaAircraftMode AustralAsiaAircraft { get; init; } = AustralAsiaAircraftMode.Keep;

    /// <summary>
    /// Factor D. Flat demand multiplier on pairs with both endpoints in
    /// AustralAsia. 1.0 = no change. ⚠ Pushes against the CLAUDE.md pillar
    /// that a home region cannot fund a win — see the plan's stop
    /// condition for D.
    /// </summary>
    public double AustralAsiaIntraRegionDemandMultiplier { get; init; } = 1.0;

    /// <summary>Baseline: all knobs off.</summary>
    public static ExperimentTuning Baseline { get; } = new();

    /// <summary>
    /// The factor-D map to hand <see cref="GravityDemandModel"/>, or null
    /// when D is off (multiplier 1.0) — null keeps the model byte-identical
    /// to today.
    /// </summary>
    public IReadOnlyDictionary<Region, double>? IntraRegionDemandMultipliers =>
        AustralAsiaIntraRegionDemandMultiplier == 1.0
            ? null
            : new Dictionary<Region, double> { [Region.AustralAsia] = AustralAsiaIntraRegionDemandMultiplier };

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
