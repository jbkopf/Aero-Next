using AeroNext.Core.World;

namespace AeroNext.Core.Airlines;

/// <summary>
/// Plans itineraries across a route network: nonstop where a direct route
/// exists, one-stop through a single connecting hub otherwise — chosen by
/// shortest total great-circle detour, not by any hub-specific bonus. This
/// is the milestone that tests whether hub-and-spoke value emerges from
/// itinerary search over the gravity demand, or has to be hardcoded.
///
/// Nonstop demand claims its own route's capacity first, since that market
/// is the reason the route exists. Leftover capacity on each leg is then
/// shared by one-stop itineraries, largest markets first, so two itineraries
/// through the same hub leg compete for what nonstop traffic didn't use.
/// </summary>
public static class NetworkPlanner
{
    public static NetworkPlan Plan(IReadOnlyList<Route> routes, IReadOnlyList<City> allCities, GravityDemandModel demandModel)
    {
        var directRouteByPair = new Dictionary<(string, string), Route>();
        var neighbors = new Dictionary<string, List<(City Neighbor, Route Route)>>(StringComparer.Ordinal);

        foreach (var route in routes)
        {
            directRouteByPair[Key(route.Origin.Id, route.Destination.Id)] = route;
            directRouteByPair[Key(route.Destination.Id, route.Origin.Id)] = route;
            AddNeighbor(neighbors, route.Origin.Id, route.Destination, route);
            AddNeighbor(neighbors, route.Destination.Id, route.Origin, route);
        }

        var servedCities = allCities
            .Where(c => neighbors.ContainsKey(c.Id))
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

        var itineraries = new List<Itinerary>();

        for (var i = 0; i < servedCities.Count; i++)
        {
            for (var j = i + 1; j < servedCities.Count; j++)
            {
                var origin = servedCities[i];
                var destination = servedCities[j];

                if (directRouteByPair.TryGetValue(Key(origin.Id, destination.Id), out var direct))
                {
                    itineraries.Add(new Itinerary(origin, destination, ItineraryKind.Nonstop, new[] { direct }));
                    continue;
                }

                var hubLegs = FindBestHub(origin, destination, neighbors);

                itineraries.Add(hubLegs is null
                    ? new Itinerary(origin, destination, ItineraryKind.Unserved, Array.Empty<Route>())
                    : new Itinerary(origin, destination, ItineraryKind.OneStop, new[] { hubLegs.Value.LegOne, hubLegs.Value.LegTwo }));
            }
        }

        return Allocate(routes, itineraries, demandModel);
    }

    private static (Route LegOne, Route LegTwo)? FindBestHub(
        City origin, City destination, Dictionary<string, List<(City Neighbor, Route Route)>> neighbors)
    {
        if (!neighbors.TryGetValue(origin.Id, out var originLegs))
        {
            return null;
        }

        (Route LegOne, Route LegTwo)? best = null;
        var bestDetourKm = double.MaxValue;
        string? bestHubId = null;

        foreach (var (hub, legOne) in originLegs.OrderBy(n => n.Neighbor.Id, StringComparer.Ordinal))
        {
            if (hub.Id == destination.Id || !neighbors.TryGetValue(hub.Id, out var hubLegs))
            {
                continue;
            }

            var legTwoMatch = hubLegs.FirstOrDefault(n => n.Neighbor.Id == destination.Id);
            if (legTwoMatch.Route is null)
            {
                continue;
            }

            var detourKm = GreatCircle.DistanceKm(origin, hub) + GreatCircle.DistanceKm(hub, destination);
            var better = detourKm < bestDetourKm
                || (detourKm == bestDetourKm && string.CompareOrdinal(hub.Id, bestHubId) < 0);

            if (better)
            {
                bestDetourKm = detourKm;
                bestHubId = hub.Id;
                best = (legOne, legTwoMatch.Route);
            }
        }

        return best;
    }

    private static NetworkPlan Allocate(IReadOnlyList<Route> routes, List<Itinerary> itineraries, GravityDemandModel demandModel)
    {
        var remainingCapacity = routes.ToDictionary(r => r, r => r.SeatsPerQuarter);
        var loads = new List<ItineraryLoad>();

        var nonstop = itineraries.Where(i => i.Kind == ItineraryKind.Nonstop);
        var oneStop = itineraries
            .Where(i => i.Kind == ItineraryKind.OneStop)
            .Select(i => (Itinerary: i, Demand: demandModel.ForPair(i.Origin, i.Destination).PassengersPerQuarter))
            .OrderByDescending(x => x.Demand)
            .ThenBy(x => x.Itinerary.Origin.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Itinerary.Destination.Id, StringComparer.Ordinal);
        var unserved = itineraries.Where(i => i.Kind == ItineraryKind.Unserved);

        foreach (var itinerary in nonstop)
        {
            var demand = demandModel.ForPair(itinerary.Origin, itinerary.Destination).PassengersPerQuarter;
            var leg = itinerary.Legs[0];
            var carried = Math.Min(demand, remainingCapacity[leg]);
            remainingCapacity[leg] -= carried;
            loads.Add(new ItineraryLoad(itinerary, demand, carried));
        }

        foreach (var (itinerary, demand) in oneStop)
        {
            var legOne = itinerary.Legs[0];
            var legTwo = itinerary.Legs[1];
            var carried = Math.Min(demand, Math.Min(remainingCapacity[legOne], remainingCapacity[legTwo]));
            remainingCapacity[legOne] -= carried;
            remainingCapacity[legTwo] -= carried;
            loads.Add(new ItineraryLoad(itinerary, demand, carried));
        }

        foreach (var itinerary in unserved)
        {
            var demand = demandModel.ForPair(itinerary.Origin, itinerary.Destination).PassengersPerQuarter;
            loads.Add(new ItineraryLoad(itinerary, demand, 0));
        }

        var routeUsage = routes
            .OrderBy(r => r.Origin.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Destination.Id, StringComparer.Ordinal)
            .Select(r => new RouteCapacityUsage(r, r.SeatsPerQuarter, r.SeatsPerQuarter - remainingCapacity[r]))
            .ToList();

        return new NetworkPlan(loads, routeUsage);
    }

    private static void AddNeighbor(
        Dictionary<string, List<(City Neighbor, Route Route)>> neighbors, string fromId, City to, Route route)
    {
        if (!neighbors.TryGetValue(fromId, out var list))
        {
            list = new List<(City, Route)>();
            neighbors[fromId] = list;
        }

        list.Add((to, route));
    }

    private static (string, string) Key(string a, string b) => (a, b);
}
