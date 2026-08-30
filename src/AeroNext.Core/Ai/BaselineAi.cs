using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// A simple heuristic opponent: greedily enters the single highest-demand
/// city pair it can actually reach and doesn't already serve, if it can
/// afford a market action; bids on the single highest-demand gateway it
/// doesn't yet touch, if it can also afford a contested action *and* the
/// bid itself (M28 — negotiations cost real cash at submission now, not
/// just an action) (per CLAUDE.md — negotiations are "blind" even for a
/// skilled human, so not checking the target's remaining gate capacity
/// before bidding isn't a missing smarts, it's true to the mechanic); and
/// tries to grow its own best-performing existing route by assigning it
/// another aircraft (M32 — no longer free; competes for the same action
/// budget as everything else).
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
/// (M13), and real fleet *ownership* (QuarterEngine's fleet-aware
/// RouteOrder check) are different: an out-of-range, not-yet-available, or
/// unowned-aircraft order is rejected today, for real — so this AI must
/// never propose one, the same "never attempt what will predictably fail"
/// principle already settled for cash affordability. Purely a function of
/// current state — no randomness, so it's deterministic by construction
/// without needing a seeded RNG.
/// </summary>
public static class BaselineAi
{
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
    /// <param name="availableSlotsByCity">
    /// M33: null (the default) never blocks a candidate on gate scarcity,
    /// matching QuarterEngine's own permissive default. Passing this
    /// airline's real per-city balance (see QuarterEngine.RunQuarter's own
    /// parameter of the same name) makes this AI skip — not search for an
    /// alternative to — a growth or new-pair candidate that would touch a
    /// gateway city it has no slot left at, the same "never attempt what
    /// will predictably fail" principle already applied to cash and fleet.
    /// </param>
    /// <param name="availableCash">
    /// M28: null (the default) never blocks the negotiation step, matching
    /// QuarterEngine's own permissive default. Passing quarter-start cash
    /// on hand skips proposing a NegotiationOrder this AI can't afford —
    /// negotiations now cost real cash at submission (M28), and this is
    /// the only cash-consuming action this AI ever proposes (growth/new-pair
    /// never cost cash upfront; leasing is a recurring cost, not a
    /// submission-time one), so no running "spent so far" tracker is
    /// needed here the way fleet/slot reservations already need one.
    /// </param>
    public static IReadOnlyList<QuarterOrder> DecideOrders(
        IReadOnlyList<Route> currentRoutes,
        int actionCap,
        IReadOnlyList<City> allCities,
        GravityDemandModel demandModel,
        IReadOnlyList<AircraftType> eraAvailableTypes,
        AirlineFleet ownFleet,
        IReadOnlyDictionary<string, int>? availableSlotsByCity = null,
        Money? availableCash = null)
    {
        var orders = new List<QuarterOrder>();
        var spent = 0;

        // Reserved this planning pass but not yet actually assigned in
        // ownFleet (QuarterEngine hasn't run yet) - tracked locally so the
        // new-pair search below doesn't also propose the same idle unit
        // this step already claimed, which QuarterEngine would then
        // reject as a wasted action.
        var reservedUnitIds = new HashSet<int>();

        // Same idea for gate slots (M33): a city touched by both the
        // growth and new-pair steps in one pass must not be treated as
        // having two separate slots available when it only has one.
        var slotsReservedByCity = new Dictionary<string, int>();

        var bestExisting = FindBestPerformingRoute(currentRoutes, demandModel);
        if (bestExisting is not null && spent + 1 <= actionCap
            && HasSlotAtEveryGateway(bestExisting.Origin, bestExisting.Destination, availableSlotsByCity, slotsReservedByCity))
        {
            var idleUnit = ownFleet.Idle
                .Where(u => u.Type.Id == bestExisting.Aircraft.Id)
                .OrderBy(u => u.Id)
                .FirstOrDefault();

            if (idleUnit is not null)
            {
                orders.Add(new RouteOrder(bestExisting.Origin, bestExisting.Destination, bestExisting.Aircraft));
                reservedUnitIds.Add(idleUnit.Id);
                ReserveSlots(bestExisting.Origin, bestExisting.Destination, slotsReservedByCity);
                spent += 1;
            }
            else if (spent + 2 <= actionCap)
            {
                // Nothing idle of this route's own type - lease one more
                // and add it in the same batch, mirroring the new-pair
                // fallback below. No "best available type" search needed
                // here: growing an existing route requires the exact type
                // already flying it - QuarterEngine rejects a mismatch.
                orders.Add(new LeaseAircraftOrder(bestExisting.Aircraft, Quantity: 1));
                orders.Add(new RouteOrder(bestExisting.Origin, bestExisting.Destination, bestExisting.Aircraft));
                ReserveSlots(bestExisting.Origin, bestExisting.Destination, slotsReservedByCity);
                spent += 2;
            }
        }

        if (spent + 1 <= actionCap)
        {
            var idleBest = FindBestUnservedPairUsingIdleFleet(currentRoutes, allCities, demandModel, ownFleet, reservedUnitIds);

            if (idleBest is not null && HasSlotAtEveryGateway(idleBest.Value.Pair.Origin, idleBest.Value.Pair.Destination, availableSlotsByCity, slotsReservedByCity))
            {
                var (pair, aircraft) = idleBest.Value;
                orders.Add(new RouteOrder(pair.Origin, pair.Destination, aircraft));
                ReserveSlots(pair.Origin, pair.Destination, slotsReservedByCity);
                spent += 1;
            }
            else if (idleBest is null && spent + 2 <= actionCap)
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

                if (bestAircraft is not null && bestUnserved is not null
                    && HasSlotAtEveryGateway(bestUnserved.Origin, bestUnserved.Destination, availableSlotsByCity, slotsReservedByCity))
                {
                    orders.Add(new LeaseAircraftOrder(bestAircraft, Quantity: 1));
                    orders.Add(new RouteOrder(bestUnserved.Origin, bestUnserved.Destination, bestAircraft));
                    ReserveSlots(bestUnserved.Origin, bestUnserved.Destination, slotsReservedByCity);
                    spent += 2;
                }
            }
        }

        if (spent + 1 <= actionCap && (availableCash is null || availableCash.Value >= NegotiationBidAmount))
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
    /// reservedUnitIds excludes units another step in this same planning
    /// pass already claimed (see DecideOrders).
    /// </summary>
    private static (PairDemand Pair, AircraftType Aircraft)? FindBestUnservedPairUsingIdleFleet(
        IReadOnlyList<Route> currentRoutes, IReadOnlyList<City> allCities, GravityDemandModel demandModel, AirlineFleet ownFleet, IReadOnlySet<int> reservedUnitIds)
    {
        var candidates = ownFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
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

    /// <summary>M33: true when availableSlotsByCity is null (permissive), or this airline has a slot left at every gateway origin/destination touches.</summary>
    private static bool HasSlotAtEveryGateway(City origin, City destination, IReadOnlyDictionary<string, int>? availableSlotsByCity, Dictionary<string, int> slotsReservedByCity)
    {
        if (availableSlotsByCity is null)
        {
            return true;
        }

        return new[] { origin, destination }
            .Where(c => c.GateCapacity is not null)
            .All(c => availableSlotsByCity.GetValueOrDefault(c.Id) - slotsReservedByCity.GetValueOrDefault(c.Id) > 0);
    }

    private static void ReserveSlots(City origin, City destination, Dictionary<string, int> slotsReservedByCity)
    {
        foreach (var city in new[] { origin, destination }.Where(c => c.GateCapacity is not null))
        {
            slotsReservedByCity[city.Id] = slotsReservedByCity.GetValueOrDefault(city.Id) + 1;
        }
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
