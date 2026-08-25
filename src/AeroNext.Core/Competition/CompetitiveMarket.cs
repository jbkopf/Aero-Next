using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Competition;

/// <summary>
/// Splits shared city-pair demand across airlines whose networks both reach
/// it. Each airline's own <see cref="NetworkPlanner"/> plan — computed as if
/// it had no rivals — gives its "monopoly ceiling" per pair: the most it
/// could ever carry there, given its own routes and hub connections.
///
/// On a pair reached by more than one airline, demand splits by
/// (bottleneck-leg capacity × Quality) — raw seats-per-quarter on the
/// itinerary's tightest leg, not the demand-capped monopoly ceiling. Using
/// the ceiling as the weight would make frequency stop mattering the moment
/// one airline's capacity alone already exceeds demand (the ceiling
/// saturates at demand and stops growing even as capacity keeps growing),
/// which would silently kill the "more frequency wins more share" dynamic
/// this milestone exists to demonstrate. Each airline's raw share is then
/// capped at its own monopoly ceiling, so competition can only redistribute
/// demand, never grant an airline more than its network could physically
/// carry.
///
/// This only produces a visible effect when combined industry capacity on a
/// pair exceeds that pair's demand. If every competitor is individually
/// capacity-constrained (demand alone would fill their planes), each just
/// carries its own ceiling regardless of rivals — competition has nothing
/// to redistribute. That's not a bug: it's the same "capacity- vs demand-
/// constrained" distinction M3 established, now applied per competitor.
///
/// Simplification: capacity freed up by capping one airline's share at its
/// ceiling is not redistributed to less-constrained rivals (no iterative
/// water-filling). Leftover demand in that case goes uncarried by anyone
/// this quarter, rather than exhaustively reallocated.
/// </summary>
public static class CompetitiveMarket
{
    public static CompetitiveMarketResult Resolve(IReadOnlyList<Airline> airlines, IReadOnlyList<City> cities, GravityDemandModel demandModel)
    {
        var monopolyPlans = airlines.ToDictionary(a => a, a => NetworkPlanner.Plan(a.Routes, cities, demandModel));

        var pairKeys = monopolyPlans.Values
            .SelectMany(p => p.Itineraries.Where(i => i.Itinerary.Kind != ItineraryKind.Unserved))
            .Select(i => (i.Itinerary.Origin, i.Itinerary.Destination))
            .GroupBy(t => (t.Origin.Id, t.Destination.Id))
            .Select(g => g.First())
            .OrderBy(t => t.Origin.Id, StringComparer.Ordinal)
            .ThenBy(t => t.Destination.Id, StringComparer.Ordinal);

        var pairResults = new List<CompetitivePairResult>();
        var routeSeatsUsed = new Dictionary<(Airline, Route), long>();

        foreach (var (origin, destination) in pairKeys)
        {
            var demand = demandModel.ForPair(origin, destination).PassengersPerQuarter;

            var competitors = airlines
                .Select(a => (Airline: a, Load: FindPair(monopolyPlans[a], origin.Id, destination.Id)))
                .Where(x => x.Load is { PassengersCarried: > 0 })
                .ToList();

            var carriedByAirline = new Dictionary<Airline, long>();

            if (competitors.Count == 1)
            {
                var (airline, load) = competitors[0];
                carriedByAirline[airline] = load!.PassengersCarried;
            }
            else if (competitors.Count > 1)
            {
                var weights = competitors
                    .Select(c => (
                        c.Airline,
                        Weight: c.Load!.Itinerary.Legs.Min(l => l.SeatsPerQuarter) * c.Airline.Quality,
                        Ceiling: c.Load!.PassengersCarried))
                    .ToList();
                var totalWeight = weights.Sum(w => w.Weight);

                foreach (var (airline, weight, ceiling) in weights)
                {
                    var rawShare = totalWeight <= 0 ? 0 : demand * weight / totalWeight;
                    carriedByAirline[airline] = Math.Min(ceiling, (long)Math.Round(rawShare, MidpointRounding.AwayFromZero));
                }
            }

            pairResults.Add(new CompetitivePairResult(origin, destination, demand, carriedByAirline));

            foreach (var (airline, carried) in carriedByAirline)
            {
                var itinerary = FindPair(monopolyPlans[airline], origin.Id, destination.Id)!.Itinerary;
                foreach (var leg in itinerary.Legs)
                {
                    var key = (airline, leg);
                    routeSeatsUsed[key] = routeSeatsUsed.GetValueOrDefault(key) + carried;
                }
            }
        }

        var routeUsage = airlines
            .SelectMany(a => a.Routes.Select(r => new CompetitiveRouteUsage(a, r, r.SeatsPerQuarter, routeSeatsUsed.GetValueOrDefault((a, r)))))
            .OrderBy(u => u.Airline.Name, StringComparer.Ordinal)
            .ThenBy(u => u.Route.Origin.Id, StringComparer.Ordinal)
            .ThenBy(u => u.Route.Destination.Id, StringComparer.Ordinal)
            .ToList();

        return new CompetitiveMarketResult(pairResults, routeUsage);
    }

    private static ItineraryLoad? FindPair(NetworkPlan plan, string originId, string destinationId)
    {
        foreach (var load in plan.Itineraries)
        {
            if (load.Itinerary.Origin.Id == originId && load.Itinerary.Destination.Id == destinationId)
            {
                return load;
            }
        }

        return null;
    }
}
