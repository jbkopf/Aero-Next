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
        var cash = Money.FromDollars((long)Math.Round(
            BaseStartingCashDollars * RegionalEconomics.Factor(seat.HomeRegion, allCities), MidpointRounding.AwayFromZero));

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

    private static long RouteProfit(City origin, City destination, AircraftType aircraft, GravityDemandModel demandModel)
    {
        var route = new Route(origin, destination, aircraft, AircraftCount: 1);
        var carried = Math.Min(demandModel.ForPair(origin, destination).PassengersPerQuarter, route.SeatsPerQuarter);
        return FareCalculator.EstimatedProfit(route, carried).Cents;
    }

    /// <summary>The (aircraft, destination) with the best real quarter-1 profit; ties break by shorter range, then aircraft id, then destination id.</summary>
    private static (AircraftType Aircraft, City Destination) MostProfitableCombination(
        AirlineSeat seat, IReadOnlyList<City> destinationOptions, IReadOnlyList<AircraftType> aircraftOptions, GravityDemandModel demandModel) =>
        (from aircraft in aircraftOptions
         from destination in destinationOptions
         where GreatCircle.DistanceKm(seat.StartingCity, destination) <= aircraft.RangeKm
         where demandModel.ForPair(seat.StartingCity, destination).PassengersPerQuarter > 0
         orderby RouteProfit(seat.StartingCity, destination, aircraft, demandModel) descending, aircraft.RangeKm, aircraft.Id, destination.Id
         select (aircraft, destination))
        .First();
}
