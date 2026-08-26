using AeroNext.Core.Airlines;
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
    /// Tries appropriate aircraft smallest-range-first — the cheapest type
    /// that can actually open a viable route, stepping up only if a
    /// smaller type can't reach any same-region eligible city with
    /// positive demand. Throws (not a per-game runtime condition, a data
    /// gap in cities.json/aircraft.json) if no appropriate aircraft can —
    /// this milestone's own tests confirm that never happens for the real
    /// dataset.
    /// </summary>
    public static AirlineStartingConditions Resolve(AirlineSeat seat, IReadOnlyList<City> allCities, IReadOnlyList<AircraftType> fleet)
    {
        var cash = Money.FromDollars((long)Math.Round(
            BaseStartingCashDollars * RegionalEconomics.Factor(seat.HomeRegion, allCities), MidpointRounding.AwayFromZero));

        var demandModel = new GravityDemandModel();
        var candidates = RegionalFleet.AppropriateAircraft(seat.HomeRegion, fleet, Timeline.StartYear)
            .OrderBy(a => a.RangeKm)
            .ThenBy(a => a.Id, StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            var destination = GuaranteedRouteDestination(seat, candidate, allCities, demandModel);
            if (destination is not null)
            {
                return new AirlineStartingConditions(cash, candidate, StartingAircraftQuantity, destination);
            }
        }

        throw new InvalidOperationException(
            $"No appropriate {Timeline.StartYear} aircraft for {seat.HomeRegion} can open a positive-demand route " +
            $"from {seat.StartingCity.Id} to any other eligible city in the region.");
    }

    private static City? GuaranteedRouteDestination(AirlineSeat seat, AircraftType aircraft, IReadOnlyList<City> allCities, GravityDemandModel demandModel) =>
        HomeRegions.EligibleStartingCities(seat.HomeRegion, allCities)
            .Where(c => c.Id != seat.StartingCity.Id)
            .Where(c => GreatCircle.DistanceKm(seat.StartingCity, c) <= aircraft.RangeKm)
            .Where(c => demandModel.ForPair(seat.StartingCity, c).PassengersPerQuarter > 0)
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
}
