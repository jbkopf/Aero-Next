using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// A simple heuristic opponent: greedily enters the single highest-demand
/// city pair it can actually reach and doesn't already serve, if it can
/// afford a market action; bids on the single highest-demand gateway it
/// doesn't yet touch, if it can also afford a contested action (per
/// CLAUDE.md — negotiations are "blind" even for a skilled human, so not
/// checking the target's remaining gate capacity before bidding isn't a
/// missing smarts, it's true to the mechanic); and always bumps frequency
/// on its own best-performing existing route (free, per CLAUDE.md — adding
/// frequency to a served pair costs nothing).
///
/// Not trying to play well — CLAUDE.md's game is always exactly 4 airlines,
/// so this exists because the sim can never run a representative quarter
/// without something producing orders for whichever seats aren't human, not
/// because this is meant to be a strong opponent. Deliberately narrow:
/// no hub-awareness (picks the single best pair/gateway in the whole world,
/// not the best one connected to its existing network), no executive
/// hiring or alliance courting of its own — M9's alliance mechanic has no
/// consequence that would change this AI's route decisions, so extending it
/// there would be cosmetic. Range-awareness (M11), fleet availability
/// (M13), and now real fleet *ownership* (QuarterEngine's fleet-aware
/// RouteOrder check) are different: an out-of-range, not-yet-available, or
/// unowned-aircraft order is rejected today, for real — so this AI must
/// never propose one, the same "never attempt what will predictably fail"
/// principle already settled for cash affordability. Purely a function of
/// current state — no randomness, so it's deterministic by construction
/// without needing a seeded RNG.
/// </summary>
public static class BaselineAi
{
    private const int NewRouteWeeklyRoundTrips = 3;
    private const int FrequencyIncrement = 2;
    private static readonly Money NegotiationBidAmount = Money.FromDollars(2_000_000);

    /// <param name="eraAvailableTypes">
    /// The full aircraft catalog available to lease this era (e.g.
    /// Timeline.AvailableAircraft) — not this airline's own fleet. Only
    /// consulted as a fallback, when nothing already in ownFleet is idle
    /// and able to reach anywhere worth serving.
    /// </param>
    /// <param name="ownFleet">
    /// This airline's actual holdings. A new route always prefers an idle
    /// unit already owned or leased — free, no new action needed for the
    /// aircraft itself — over acquiring something new. Only when nothing
    /// idle can reach a positive-demand unserved pair does this AI lease
    /// (never order — see the class doc comment) the best available type
    /// and, in the same order batch, propose a route using it, relying on
    /// QuarterEngine's inline lease resolution to make that same-quarter
    /// use possible.
    /// </param>
    public static IReadOnlyList<QuarterOrder> DecideOrders(
        IReadOnlyList<Route> currentRoutes,
        int actionCap,
        IReadOnlyList<City> allCities,
        GravityDemandModel demandModel,
        IReadOnlyList<AircraftType> eraAvailableTypes,
        AirlineFleet ownFleet)
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
            var idleBest = FindBestUnservedPairUsingIdleFleet(currentRoutes, allCities, demandModel, ownFleet);

            if (idleBest is not null)
            {
                var (pair, aircraft) = idleBest.Value;
                orders.Add(new RouteOrder(pair.Origin, pair.Destination, aircraft, NewRouteWeeklyRoundTrips));
                spent += 1;
            }
            else if (spent + 2 <= actionCap)
            {
                // Nothing idle can reach anywhere worth serving - lease the
                // best available type and open a route with it in the same
                // batch, rather than leasing speculatively with no route to
                // show for it this quarter.
                var bestAircraft = eraAvailableTypes
                    .OrderByDescending(a => a.RangeKm)
                    .ThenBy(a => a.Id, StringComparer.Ordinal)
                    .FirstOrDefault();

                var bestUnserved = bestAircraft is null ? null : FindBestUnservedPair(currentRoutes, allCities, demandModel, bestAircraft);

                if (bestAircraft is not null && bestUnserved is not null)
                {
                    orders.Add(new LeaseAircraftOrder(bestAircraft, Quantity: 1));
                    orders.Add(new RouteOrder(bestUnserved.Origin, bestUnserved.Destination, bestAircraft, NewRouteWeeklyRoundTrips));
                    spent += 2;
                }
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

    /// <summary>
    /// The best unserved pair reachable using any type this airline
    /// currently has an idle unit of — searched per distinct idle type,
    /// then the single best result kept, so a smaller idle jet doesn't get
    /// passed over just because a bigger one happens to sort first.
    /// </summary>
    private static (PairDemand Pair, AircraftType Aircraft)? FindBestUnservedPairUsingIdleFleet(
        IReadOnlyList<Route> currentRoutes, IReadOnlyList<City> allCities, GravityDemandModel demandModel, AirlineFleet ownFleet)
    {
        var candidates = ownFleet.Idle
            .Select(u => u.Type)
            .Distinct()
            .Select(type => (Pair: FindBestUnservedPair(currentRoutes, allCities, demandModel, type), Aircraft: type))
            .Where(c => c.Pair is not null)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .OrderByDescending(c => c.Pair!.PassengersPerQuarter)
            .ThenBy(c => c.Pair!.Origin.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Pair!.Destination.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Aircraft.Id, StringComparer.Ordinal)
            .First();

        return (best.Pair!, best.Aircraft);
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
