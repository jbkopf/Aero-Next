using AeroNext.Core.World;

namespace AeroNext.Core.Airlines;

/// <summary>
/// Plans itineraries across a route network: nonstop where a direct route
/// exists, one-stop through a single connecting hub otherwise. This is the
/// milestone that tests whether hub-and-spoke value emerges from itinerary
/// search over the gravity demand, or has to be hardcoded.
///
/// Nonstop demand claims its own route's capacity first, since that market
/// is the reason the route exists. Leftover capacity on each leg is then
/// shared by one-stop itineraries, largest markets first, so two itineraries
/// through the same hub leg compete for what nonstop traffic didn't use.
///
/// M62: hub selection (FindBestHub) was pure shortest-total-great-circle-
/// detour until this milestone — now ranks by an "effective detour" that
/// also factors in a candidate hub's own HubQuality, this airline's own
/// existing loyalty to it (route-count-share proxy), and a substitution
/// penalty for sitting close to a different hub the airline already uses.
/// See docs/HUB_DEMAND_DESIGN.md and docs/HUB_DEMAND_ROADMAP.md's M62
/// section. Deliberately bounded well below realistic intercontinental
/// detour deltas — see FindBestHub's own doc comment.
/// </summary>
public static class NetworkPlanner
{
    /// <summary>
    /// M62: km-equivalent bonus per HubQuality point, subtracted from a
    /// candidate hub's detour when ranking hub choices in
    /// <see cref="FindBestHub"/>. At the authored ceiling
    /// (<see cref="World.CityCatalog.MaxAuthoredHubQuality"/> = 40) this
    /// caps at 4000km — bounded, together with
    /// <see cref="LoyaltyBonusMaxKm"/> and
    /// <see cref="SubstitutionPenaltyMaxKm"/>, well below realistic
    /// intercontinental detour deltas, so quality/loyalty/substitution can
    /// tilt among genuinely close candidates but never make a
    /// transcontinental detour look cheaper than a nearby direct hub. Set
    /// all three of these constants to 0.0 to reproduce pre-M62
    /// pure-detour hub selection exactly — the sanctioned ablation lever
    /// for isolating a regression to this mechanism specifically, matching
    /// this project's own established ablation-instrument discipline
    /// (docs/ROUTE_CAPACITY_ROADMAP.md's M45).
    ///
    /// Retuned upward from an initial 25.0: a real diagnostic sweep found
    /// the original, more conservative values (25/800/800) never once
    /// changed a real hub choice across 529,165 real decisions in a
    /// 50-game sample, even though ~40% of the rare (0.3%) multi-candidate
    /// decisions had a real HubQuality city in the running. Root cause:
    /// whichever hub wins on pure geometry also tends to already hold the
    /// airline's strongest route presence there (network growth is
    /// self-reinforcing), so LoyaltyBonusMaxKm reinforces the incumbent
    /// rather than ever challenging it — HubQualityKmPerPoint, a flat
    /// per-city constant untouched by that feedback loop, is the one term
    /// that can realistically overcome it, hence the disproportionate
    /// increase here relative to the other two. See
    /// docs/HUB_DEMAND_ROADMAP.md's M62 section for the full measurement.
    /// </summary>
    public const double HubQualityKmPerPoint = 100.0;

    /// <summary>
    /// M62: max km-equivalent bonus (at loyaltyShare = 1.0) toward a
    /// candidate hub this airline's own network already routes a
    /// meaningful share of its routes through — the route-count-share
    /// proxy for "already a hub" decided in docs/HUB_DEMAND_DESIGN.md
    /// (a full explicit hub-designation system is deferred, see
    /// docs/OPEN_WORK.md). See <see cref="HubQualityKmPerPoint"/>'s own
    /// doc comment for the shared bound/ablation-lever convention, and for
    /// why this term was retuned only modestly (800 → 1000) relative to
    /// HubQualityKmPerPoint's own much larger increase.
    /// </summary>
    public const double LoyaltyBonusMaxKm = 1000.0;

    /// <summary>
    /// M62: max km-equivalent penalty applied to a candidate hub that sits
    /// within <see cref="SubstitutionRadiusKm"/> of a *different* hub this
    /// airline already treats as loyal — falls off linearly to 0 at the
    /// radius (the New York/Boston case: once one is established, the
    /// other loses appeal as a second hub, not because it got worse, but
    /// because it isn't adding new catchment). Only the single worst-case
    /// (closest) already-loyal hub counts, not a sum across every nearby
    /// one, so this stays bounded regardless of how many loyal hubs exist.
    /// See <see cref="HubQualityKmPerPoint"/>'s own doc comment for the
    /// shared bound/ablation-lever convention.
    /// </summary>
    public const double SubstitutionPenaltyMaxKm = 1000.0;

    /// <summary>M62: radius within which a candidate hub is considered a potential substitute for an already-loyal hub.</summary>
    public const double SubstitutionRadiusKm = 800.0;

    /// <summary>
    /// M62: the route-count-share proxy threshold above which this
    /// airline is considered to already treat a city as one of its hubs,
    /// for both the loyalty bonus and the substitution penalty.
    /// Deliberately not 0 — a city touched by a single route out of a
    /// sprawling network shouldn't count as "already a hub."
    /// </summary>
    public const double SubstitutionLoyaltyThreshold = 0.15;

    public static NetworkPlan Plan(IReadOnlyList<Route> routes, IReadOnlyList<City> allCities, GravityDemandModel demandModel)
    {
        // A route the aircraft can't physically fly isn't usable as a leg at
        // all — not nonstop, not as part of a one-stop connection. It's
        // still passed to Allocate() below so it shows up in RouteUsage
        // with 0 seats used, instead of silently disappearing from output.
        var withinRangeRoutes = routes.Where(r => r.IsWithinRange).ToList();

        var directRouteByPair = new Dictionary<(string, string), Route>();
        var neighbors = new Dictionary<string, List<(City Neighbor, Route Route)>>(StringComparer.Ordinal);

        foreach (var route in withinRangeRoutes)
        {
            directRouteByPair[Key(route.Origin.Id, route.Destination.Id)] = route;
            directRouteByPair[Key(route.Destination.Id, route.Origin.Id)] = route;
            AddNeighbor(neighbors, route.Origin.Id, route.Destination, route);
            AddNeighbor(neighbors, route.Destination.Id, route.Origin, route);
        }

        // M62: loyalty/substitution inputs, precomputed once per Plan()
        // call rather than once per FindBestHub call inside the O(cities^2)
        // double loop below — the same class of accidental redundant
        // recomputation GravityDemandModel's own M21 already fixed once.
        var routeCountByCityId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var route in withinRangeRoutes)
        {
            routeCountByCityId[route.Origin.Id] = routeCountByCityId.GetValueOrDefault(route.Origin.Id) + 1;
            routeCountByCityId[route.Destination.Id] = routeCountByCityId.GetValueOrDefault(route.Destination.Id) + 1;
        }

        // totalRoutes is always > 0 here: routeCountByCityId only ever gets
        // entries from the same withinRangeRoutes loop above, so it's empty
        // exactly when totalRoutes is 0 - no need for a defensive branch
        // that can never actually run.
        var totalRoutes = withinRangeRoutes.Count;
        var loyaltyShareByCityId = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (cityId, count) in routeCountByCityId)
        {
            loyaltyShareByCityId[cityId] = count / (double)totalRoutes;
        }

        var cityById = allCities.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var loyalHubCities = loyaltyShareByCityId
            .Where(kv => kv.Value >= SubstitutionLoyaltyThreshold)
            .Select(kv => cityById[kv.Key])
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

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

                var hubLegs = FindBestHub(origin, destination, neighbors, loyaltyShareByCityId, loyalHubCities);

                itineraries.Add(hubLegs is null
                    ? new Itinerary(origin, destination, ItineraryKind.Unserved, Array.Empty<Route>())
                    : new Itinerary(origin, destination, ItineraryKind.OneStop, new[] { hubLegs.Value.LegOne, hubLegs.Value.LegTwo }));
            }
        }

        return Allocate(routes, itineraries, demandModel);
    }

    /// <summary>
    /// Called only from Plan() with an origin drawn from servedCities (so it
    /// always has a neighbors entry) and a destination already confirmed to
    /// have no direct route to origin (Plan() checks directRouteByPair
    /// first). So origin.Id is always a valid key, and no candidate hub can
    /// equal destination — a route from origin straight to destination
    /// would already have satisfied the nonstop check. Every neighbor also
    /// always has its own neighbors entry, since AddNeighbor populates both
    /// directions of a route together. Indexing directly (rather than
    /// TryGetValue-and-bail) makes those invariants explicit: a violation
    /// throws immediately instead of silently reporting a reachable pair as
    /// unserved.
    ///
    /// M62: ranks candidates by an "effective detour" score, not pure
    /// detourKm — see HubQualityKmPerPoint/LoyaltyBonusMaxKm/
    /// SubstitutionPenaltyMaxKm's own doc comments for the three terms and
    /// their shared bound. Tie-break by hub.Id ordinal is unchanged.
    /// </summary>
    private static (Route LegOne, Route LegTwo)? FindBestHub(
        City origin,
        City destination,
        Dictionary<string, List<(City Neighbor, Route Route)>> neighbors,
        IReadOnlyDictionary<string, double> loyaltyShareByCityId,
        IReadOnlyList<City> loyalHubCities)
    {
        var originLegs = neighbors[origin.Id];

        (Route LegOne, Route LegTwo)? best = null;
        var bestScore = double.MaxValue;
        string? bestHubId = null;

        foreach (var (hub, legOne) in originLegs.OrderBy(n => n.Neighbor.Id, StringComparer.Ordinal))
        {
            var legTwoMatch = neighbors[hub.Id].FirstOrDefault(n => n.Neighbor.Id == destination.Id);
            if (legTwoMatch.Route is null)
            {
                continue; // this candidate hub doesn't connect onward to destination
            }

            var detourKm = GreatCircle.DistanceKm(origin, hub) + GreatCircle.DistanceKm(hub, destination);

            var qualityBonusKm = HubQualityKmPerPoint * hub.HubQuality;
            var loyaltyBonusKm = LoyaltyBonusMaxKm * loyaltyShareByCityId.GetValueOrDefault(hub.Id, 0.0);

            // Only the single worst-case (closest) already-loyal hub other
            // than the candidate itself counts, not a sum across every
            // nearby one — see SubstitutionPenaltyMaxKm's own doc comment.
            var substitutionPenaltyKm = 0.0;
            foreach (var loyalHub in loyalHubCities)
            {
                if (string.Equals(loyalHub.Id, hub.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                var distanceToLoyalHub = GreatCircle.DistanceKm(hub, loyalHub);
                if (distanceToLoyalHub < SubstitutionRadiusKm)
                {
                    var penalty = SubstitutionPenaltyMaxKm * (1.0 - distanceToLoyalHub / SubstitutionRadiusKm);
                    substitutionPenaltyKm = Math.Max(substitutionPenaltyKm, penalty);
                }
            }

            var score = detourKm - qualityBonusKm - loyaltyBonusKm + substitutionPenaltyKm;
            var better = score < bestScore
                || (score == bestScore && string.CompareOrdinal(hub.Id, bestHubId) < 0);

            if (better)
            {
                bestScore = score;
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
