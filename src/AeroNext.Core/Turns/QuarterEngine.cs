using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;
using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// Resolves one quarter: every order's cost is checked against the action
/// budget in submission order, then all orders that fit are applied
/// together and the resulting network is planned all at once. A single
/// airline can't conflict with its own orders, so batching doesn't matter
/// yet for that reason — but it's the shape M9+'s multi-airline
/// simultaneous resolution builds on, and CLAUDE.md is explicit that
/// retrofitting this later means rewriting the core.
///
/// Budget rule: orders are scanned in list order, tracking cumulative
/// spend. An order that fits within what's left of the cap is applied and
/// consumes its cost; an order that would exceed the remaining cap is
/// rejected — but scanning continues, so a free order (any
/// HireExecutiveOrder/RefinanceOrder/RetireAircraftOrder) later in the
/// list still goes through even after an earlier, costlier order was
/// rejected. Only orders that don't fit get skipped, not everything after
/// them — a hard truncate-the-rest rule would needlessly block actions
/// that cost nothing. Every RouteOrder costs 1 action now (M32) — growing
/// an already-served pair by assigning another aircraft is no longer free;
/// only genuinely private actions are.
///
/// A RouteOrder the aircraft can't physically fly (M11's range constraint)
/// is rejected before the budget check even runs — the action never gets
/// charged, since nothing was actually committed. Rejecting explicitly
/// here, rather than silently letting NetworkPlanner treat it as a dead
/// leg later, keeps the reason visible to the player (CLAUDE.md's
/// legibility pillar) instead of a route that mysteriously never carries
/// anyone. Same treatment for an aircraft the manufacturer hasn't built
/// yet this quarter (M13's era availability) — checked against the year
/// quarterNumber falls in, via Timeline.
/// </summary>
public static class QuarterEngine
{
    public const int BaseActionCap = 3;

    /// <param name="fleet">
    /// Null (the default) preserves fully permissive behavior — any
    /// era-available, in-range aircraft type can be assigned to a route
    /// regardless of ownership, exactly as before fleet-awareness existed.
    /// Passing a real AirlineFleet turns on ownership enforcement: a
    /// RouteOrder needing a new or different aircraft type is rejected
    /// without an idle unit of that type, matching CLAUDE.md's legibility
    /// pillar the same way the existing range/era rejections do. Also
    /// required (non-null) for RetireAircraftOrder to succeed at all.
    /// </param>
    /// <param name="availableCash">
    /// Null (the default) preserves fully permissive behavior — an
    /// AircraftOrder is never rejected for cost, exactly as before this
    /// parameter existed. Passing a real amount (quarter-start cash on
    /// hand, before this quarter's own revenue arrives) turns on
    /// affordability enforcement: an AircraftOrder whose Type.Cost x
    /// Quantity would exceed what's left of it — after every
    /// already-applied order's own cash cost this quarter — is rejected
    /// outright, per CLAUDE.md's affordability rule: a player is never
    /// allowed to attempt a purchase beyond cash on hand and waste the
    /// action finding out.
    /// </param>
    /// <param name="availableSlotsByCity">
    /// Null (the default) preserves fully permissive behavior — a
    /// RouteOrder is never rejected for gate scarcity, exactly as before
    /// this parameter existed (and City.GateCapacity has never had any
    /// mechanical effect on RouteOrder until now). Passing a real
    /// per-city balance (this airline's held landing-rights slots minus
    /// how many its current routes already occupy there — see
    /// LandingRightsBoard.SlotsHeldBy) turns on gate-scarcity enforcement
    /// (M33): a RouteOrder touching a city with a non-null GateCapacity is
    /// rejected if this airline has no slot left there, checked
    /// independently at both Origin and Destination — a route occupies
    /// capacity at every gateway endpoint it touches, not just one. Cities
    /// with a null GateCapacity are never gated, regardless of this
    /// dictionary's contents, matching City's own "null means gate access
    /// isn't modeled as scarce there."
    /// </param>
    public static QuarterResult RunQuarter(
        int quarterNumber,
        IReadOnlyList<Route> currentRoutes,
        int currentActionCap,
        IReadOnlyList<QuarterOrder> orders,
        IReadOnlyList<City> cities,
        GravityDemandModel demandModel,
        AirlineFleet? fleet = null,
        Money? availableCash = null,
        IReadOnlyDictionary<string, int>? availableSlotsByCity = null)
    {
        var applied = new List<QuarterOrder>();
        var rejected = new List<QuarterOrder>();
        var retiredUnits = new List<FleetUnit>();
        var spent = 0;
        var cashSpent = Money.Zero;
        var slotsSpentByCity = new Dictionary<string, int>();
        var year = Timeline.YearForQuarter(quarterNumber);
        var workingFleet = fleet;

        // Fleet-aware mode only (M32): the aircraft type a pair is
        // committed to this quarter, seeded lazily from currentRoutes and
        // updated as RouteOrders are accepted. Unlike Cost() (which is
        // deliberately evaluated against quarter-start state only, so cost
        // stays order-independent within a batch), type-consistency is a
        // real correctness constraint — Route has exactly one Aircraft
        // field — so two orders touching the same pair in the same batch
        // must be checked against each other, not just against
        // quarter-start state, or the second could consume a fleet unit
        // for a type ApplyRouteOrders then silently drops.
        var committedTypeByPair = new Dictionary<(string, string), AircraftType>();

        foreach (var order in orders)
        {
            if (order is RouteOrder ro && !ro.IsWithinRange)
            {
                rejected.Add(order);
                continue;
            }

            if (!IsAircraftAvailable(order, year))
            {
                rejected.Add(order);
                continue;
            }

            if (order is AircraftOrder aircraftOrder && availableCash is not null)
            {
                var purchaseCost = aircraftOrder.Type.Cost * aircraftOrder.Quantity;

                if (cashSpent + purchaseCost > availableCash.Value)
                {
                    rejected.Add(order);
                    continue;
                }
            }

            if (order is RouteOrder slotOrder && availableSlotsByCity is not null)
            {
                var blockedAtAGateway = GatewayEndpoints(slotOrder).Any(city =>
                    availableSlotsByCity.GetValueOrDefault(city.Id) - slotsSpentByCity.GetValueOrDefault(city.Id) <= 0);

                if (blockedAtAGateway)
                {
                    rejected.Add(order);
                    continue;
                }
            }

            FleetUnit? unitToRetire = null;

            if (order is RetireAircraftOrder retireOrder)
            {
                unitToRetire = workingFleet?.Units.FirstOrDefault(u => u.Id == retireOrder.UnitId);

                if (unitToRetire is null || unitToRetire.AssignedRouteKey is not null)
                {
                    rejected.Add(order);
                    continue;
                }
            }

            FleetUnit? unitToAssign = null;

            if (order is RouteOrder routeOrder && workingFleet is not null)
            {
                var pairKey = (routeOrder.Origin.Id, routeOrder.Destination.Id);

                if (!committedTypeByPair.TryGetValue(pairKey, out var committedType))
                {
                    var existingRoute = currentRoutes.FirstOrDefault(r => r.Origin.Id == routeOrder.Origin.Id && r.Destination.Id == routeOrder.Destination.Id);
                    committedType = existingRoute?.Aircraft;
                }

                if (committedType is not null && committedType.Id != routeOrder.Aircraft.Id)
                {
                    // Swapping a route's aircraft type isn't supported yet
                    // — there's no detach action to free the old units
                    // (that's future work). Every RouteOrder now means
                    // "assign one more of this exact type," so a mismatch
                    // is rejected outright rather than silently replacing.
                    rejected.Add(order);
                    continue;
                }

                unitToAssign = workingFleet.Idle
                    .Where(u => u.Type.Id == routeOrder.Aircraft.Id)
                    .OrderBy(u => u.Id)
                    .FirstOrDefault();

                if (unitToAssign is null)
                {
                    rejected.Add(order);
                    continue;
                }
            }

            var cost = Cost(order, currentRoutes);

            if (spent + cost > currentActionCap)
            {
                rejected.Add(order);
                continue;
            }

            // Leasing bypasses the production queue entirely and delivers
            // the same quarter it's ordered — resolved right here, inline,
            // so a RouteOrder later in this same batch can actually use
            // the newly-leased unit. AircraftOrder (the production-queue
            // path) genuinely needs cross-quarter, cross-airline board
            // state this single airline's quarter doesn't have — that
            // stays external, submitted by the caller as before.
            if (order is LeaseAircraftOrder leaseOrder && workingFleet is not null)
            {
                workingFleet = FleetLedger.ApplyDelivery(workingFleet, leaseOrder.Type, leaseOrder.Quantity, FleetOwnershipMode.Leased);
            }

            if (order is RouteOrder appliedRouteOrder && unitToAssign is not null && workingFleet is not null)
            {
                var routeKey = RouteKeyString(appliedRouteOrder.Origin, appliedRouteOrder.Destination);
                workingFleet = FleetLedger.Assign(workingFleet, unitToAssign.Id, routeKey);
                committedTypeByPair[(appliedRouteOrder.Origin.Id, appliedRouteOrder.Destination.Id)] = appliedRouteOrder.Aircraft;
            }

            if (order is RetireAircraftOrder && unitToRetire is not null && workingFleet is not null)
            {
                retiredUnits.Add(unitToRetire);
                workingFleet = FleetLedger.Retire(workingFleet, unitToRetire.Id);
            }

            if (order is AircraftOrder appliedAircraftOrder && availableCash is not null)
            {
                cashSpent += appliedAircraftOrder.Type.Cost * appliedAircraftOrder.Quantity;
            }

            if (order is RouteOrder appliedSlotOrder && availableSlotsByCity is not null)
            {
                foreach (var city in GatewayEndpoints(appliedSlotOrder))
                {
                    slotsSpentByCity[city.Id] = slotsSpentByCity.GetValueOrDefault(city.Id) + 1;
                }
            }

            applied.Add(order);
            spent += cost;
        }

        var routes = ApplyRouteOrders(currentRoutes, applied.OfType<RouteOrder>());
        var nextActionCap = currentActionCap + applied.OfType<HireExecutiveOrder>().Sum(h => h.CapIncrease);
        var plan = NetworkPlanner.Plan(routes, cities, demandModel);

        return new QuarterResult(quarterNumber, routes, plan, nextActionCap, applied, rejected, workingFleet, retiredUnits);
    }

    /// <summary>
    /// 1 action for a RouteOrder always now (M32) — opening a new pair and
    /// assigning another aircraft to a pair already served cost the same;
    /// the old free-frequency-bump variant is gone, since frequency is no
    /// longer a number the player picks. 0 for private actions.
    /// Negotiations, aircraft orders, leases, and alliance courting always
    /// cost 1 — contested actions (and leasing, which spends the same
    /// attention even though it isn't contested) have no free variant.
    /// </summary>
    public static int Cost(QuarterOrder order, IReadOnlyList<Route> currentRoutes) => order switch
    {
        RouteOrder => 1,
        HireExecutiveOrder => 0,
        RefinanceOrder => 0,
        RetireAircraftOrder => 0,
        NegotiationOrder => 1,
        AircraftOrder => 1,
        LeaseAircraftOrder => 1,
        AllianceCourtOrder => 1,
        _ => throw new NotSupportedException($"Unknown order type: {order.GetType().Name}"),
    };

    /// <summary>
    /// Each applied RouteOrder means "assign one more aircraft of this
    /// type" (M32): folded sequentially in submission order, adding 1 to
    /// AircraftCount when the type matches what's already there for that
    /// pair, or starting fresh at 1 when it doesn't (a brand-new pair, or
    /// — permissive/no-fleet mode only, since fleet-aware mode rejects
    /// this in RunQuarter before it ever reaches here — a type change).
    /// This single fold is correct in both modes without needing to know
    /// which one produced `orders`.
    /// </summary>
    private static IReadOnlyList<Route> ApplyRouteOrders(IReadOnlyList<Route> currentRoutes, IEnumerable<RouteOrder> orders)
    {
        var byKey = currentRoutes.ToDictionary(RouteKey);

        foreach (var order in orders)
        {
            var key = (order.Origin.Id, order.Destination.Id);
            var sameTypeAlreadyThere = byKey.TryGetValue(key, out var existing) && existing.Aircraft.Id == order.Aircraft.Id;
            var newCount = sameTypeAlreadyThere ? existing!.AircraftCount + 1 : 1;

            byKey[key] = new Route(order.Origin, order.Destination, order.Aircraft, newCount);
        }

        return byKey.Values
            .OrderBy(r => r.Origin.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Destination.Id, StringComparer.Ordinal)
            .ToList();
    }

    internal static (string Origin, string Destination) RouteKey(Route route) => (route.Origin.Id, route.Destination.Id);

    /// <summary>String form of RouteKey — FleetUnit.AssignedRouteKey's format, settled here.</summary>
    private static string RouteKeyString(City origin, City destination) => $"{origin.Id}-{destination.Id}";

    /// <summary>Which of a RouteOrder's two endpoints actually have gate scarcity modeled (M33) — a route occupies a slot at each one it touches.</summary>
    private static IEnumerable<City> GatewayEndpoints(RouteOrder order) =>
        new[] { order.Origin, order.Destination }.Where(c => c.GateCapacity is not null);

    private static bool IsAircraftAvailable(QuarterOrder order, int year) => order switch
    {
        RouteOrder ro => Timeline.IsAvailable(ro.Aircraft, year),
        AircraftOrder ao => Timeline.IsAvailable(ao.Type, year),
        LeaseAircraftOrder lo => Timeline.IsAvailable(lo.Type, year),
        _ => true, // orders with no aircraft (negotiations, alliances, private actions) are never era-gated
    };
}
