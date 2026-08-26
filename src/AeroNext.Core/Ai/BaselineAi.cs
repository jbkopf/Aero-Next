using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// A simple heuristic opponent: greedily enters the single highest-demand
/// city pair its aircraft can actually reach and doesn't already serve, if
/// it can afford a market action; bids on the single highest-demand
/// gateway it doesn't yet touch, if it can also afford a contested action
/// (per CLAUDE.md — negotiations are "blind" even for a skilled human, so
/// not checking the target's remaining gate capacity before bidding isn't
/// a missing smarts, it's true to the mechanic); and always bumps
/// frequency on its own best-performing existing route (free, per
/// CLAUDE.md — adding frequency to a served pair costs nothing).
///
/// Not trying to play well — CLAUDE.md's game is always exactly 4 airlines,
/// so this exists because the sim can never run a representative quarter
/// without something producing orders for whichever seats aren't human, not
/// because this is meant to be a strong opponent. Deliberately narrow:
/// no hub-awareness (picks the single best pair/gateway in the whole world,
/// not the best one connected to its existing network), no executive
/// hiring, no aircraft ordering or alliance courting of its own — M9/M10
/// added those mechanics but neither has consequences that would change
/// this AI's route decisions, so extending it there would be cosmetic (see
/// the M9/M10 roadmap notes). Range-awareness (M11) is different: an
/// out-of-range order is rejected today, for real, so skipping candidates
/// beyond the aircraft's reach isn't speculative. Purely a function of
/// current state — no randomness, so it's deterministic by construction
/// without needing a seeded RNG.
/// </summary>
public static class BaselineAi
{
    private const int NewRouteWeeklyRoundTrips = 3;
    private const int FrequencyIncrement = 2;
    private static readonly Money NegotiationBidAmount = Money.FromDollars(2_000_000);

    public static IReadOnlyList<QuarterOrder> DecideOrders(
        IReadOnlyList<Route> currentRoutes,
        int actionCap,
        IReadOnlyList<City> allCities,
        GravityDemandModel demandModel,
        AircraftType aircraft)
    {
        var orders = new List<QuarterOrder>();
        var spent = 0;

        var bestExisting = FindBestPerformingRoute(currentRoutes, demandModel);
        if (bestExisting is not null)
        {
            orders.Add(new RouteOrder(
                bestExisting.Origin, bestExisting.Destination, bestExisting.Aircraft, bestExisting.WeeklyRoundTrips + FrequencyIncrement));
        }

        if (spent + 1 <= actionCap)
        {
            var bestUnserved = FindBestUnservedPair(currentRoutes, allCities, demandModel, aircraft);
            if (bestUnserved is not null)
            {
                orders.Add(new RouteOrder(bestUnserved.Origin, bestUnserved.Destination, aircraft, NewRouteWeeklyRoundTrips));
                spent += 1;
            }
        }

        if (spent + 1 <= actionCap)
        {
            var bestGateway = FindBestUnservedGateway(currentRoutes, allCities);
            if (bestGateway is not null)
            {
                orders.Add(new NegotiationOrder(bestGateway, NegotiationBidAmount));
                spent += 1;
            }
        }

        return orders;
    }

    private static PairDemand? FindBestUnservedPair(
        IReadOnlyList<Route> currentRoutes, IReadOnlyList<City> allCities, GravityDemandModel demandModel, AircraftType aircraft)
    {
        var served = currentRoutes
            .SelectMany(r => new[] { (r.Origin.Id, r.Destination.Id), (r.Destination.Id, r.Origin.Id) })
            .ToHashSet();

        return demandModel.AllPairs(allCities)
            .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
            .Where(p => p.DistanceKm <= aircraft.RangeKm)
            .OrderByDescending(p => p.PassengersPerQuarter)
            .ThenBy(p => p.Origin.Id, StringComparer.Ordinal)
            .ThenBy(p => p.Destination.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static Route? FindBestPerformingRoute(IReadOnlyList<Route> currentRoutes, GravityDemandModel demandModel)
    {
        return RouteEvaluator.EvaluateNetwork(currentRoutes, demandModel)
            .OrderByDescending(l => l.LoadFactor)
            .ThenBy(l => l.Route.Origin.Id, StringComparer.Ordinal)
            .ThenBy(l => l.Route.Destination.Id, StringComparer.Ordinal)
            .Select(l => l.Route)
            .FirstOrDefault();
    }

    private static City? FindBestUnservedGateway(IReadOnlyList<Route> currentRoutes, IReadOnlyList<City> allCities)
    {
        var touchedCityIds = currentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet();

        return allCities
            .Where(c => c.IsGateway && !touchedCityIds.Contains(c.Id))
            .OrderByDescending(c => c.DemandWeight)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
