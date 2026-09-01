using AeroNext.Core.Airlines;
using AeroNext.Core.Finance;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Ai;

/// <summary>
/// M39: the close/raise/cut/retire/pay-down-debt/hire logic shared by
/// every IAiStrategy — extracted from BaselineAi.DecideOrders's own tail
/// section (M31/M35-M37), which itself stays byte-for-byte unchanged
/// (see BaselineAi's own class doc comment) rather than being rewritten
/// to call this. A deliberate, small duplication in exchange for keeping
/// the ~490 tests and both real loops that already call
/// BaselineAi.DecideOrders directly completely unaffected by this
/// milestone.
///
/// M45: bumped from internal to public — ComputeCityWideSlotsRemaining is
/// needed directly by the orchestration layer (AeroNext.QaHarness/
/// FullGameRunner.cs, AeroNext.Sim's own M13 loop), not just by
/// IAiStrategy implementations within this assembly, the same reason
/// LandingRightsBoard's own analogous methods are public.
/// </summary>
public static class AiToolkit
{
    /// <summary>
    /// M36's negotiate-or-borrow logic, extracted so every strategy can
    /// reuse it with its own gateway-selection rule (DomesticProfitOptimizerStrategy
    /// restricts to intra-region gateways; RapidExpansionStrategy weights
    /// cross-region ones). Mirrors BaselineAi.DecideOrders's own inline
    /// version exactly - never attempts the negotiation the same quarter
    /// as a same-quarter RefinanceOrder, since QuarterEngine's cash gate
    /// doesn't see it (M29's own established point).
    /// </summary>
    /// <summary>
    /// M41: every gateway city this airline has already spent real
    /// money pursuing and shouldn't bid on again — a NegotiationOrder's
    /// full bid is deducted the instant it's submitted (M28,
    /// FinancialResolver.ComputeRevenue's own negotiationBidCost), win or
    /// lose, so re-bidding a city with a still-pending negotiation is a
    /// straight, avoidable loss, not a second chance. Unions
    /// PendingNegotiationCityIds (bids already in flight, not yet
    /// resolved) with AvailableSlotsByCity's own keys (a slot already won
    /// there but not yet turned into a route - also no reason to bid
    /// again). Both source fields are nullable/permissive - a caller that
    /// doesn't populate either gets an empty set here, reproducing the
    /// pre-M41 unfixed behavior exactly.
    /// </summary>
    public static IReadOnlySet<string> CitiesAlreadyPursuedForNegotiation(StrategyContext context)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        if (context.PendingNegotiationCityIds is not null)
        {
            ids.UnionWith(context.PendingNegotiationCityIds);
        }

        if (context.AvailableSlotsByCity is not null)
        {
            ids.UnionWith(context.AvailableSlotsByCity.Keys);
        }

        return ids;
    }

    /// <summary>
    /// M44: the network-contiguity valid-origin set - a new route may
    /// only originate from the home city or a city the airline already
    /// serves (touched by CurrentRoutes), the same rule
    /// QuarterEngine.RunQuarter's own homeCity parameter enforces
    /// authoritatively. Every new-pair search below uses this to avoid
    /// proposing an order that would predictably be rejected, mirroring
    /// range/era/cash/slot's own existing "never attempt what will fail"
    /// checks. HomeCity is always included, unconditionally - a real,
    /// persistent right to re-enter from home, not just an exemption for
    /// the very first route.
    /// </summary>
    public static IReadOnlySet<string> ValidOriginCityIds(StrategyContext context)
    {
        var ids = context.CurrentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet(StringComparer.Ordinal);
        ids.Add(context.HomeCity.Id);
        return ids;
    }

    /// <summary>
    /// M44: true, and correctly oriented, only if at least one side of
    /// candidate is a valid origin. GravityDemandModel.AllPairs's own
    /// canonical Origin/Destination assignment is alphabetical by city
    /// id, entirely unrelated to which side the airline can legally
    /// originate a new route from - every new-pair search needs to
    /// re-orient the winning candidate before building a RouteOrder from
    /// it, not just filter candidates out. When both sides already
    /// qualify (e.g. a direct pair between the home city and an
    /// already-served city with no route between them yet), keeps
    /// AllPairs's own canonical Origin as Origin - an arbitrary but
    /// deterministic tie-break.
    ///
    /// The false path (neither side qualifies) is a real safety net, not
    /// dead code - every real search site today already filters its own
    /// candidates by this exact same "either side" check before ever
    /// calling this method, so it's not exercised by any current test
    /// (confirmed via the coverage report, not assumed), but a public
    /// helper validating its own invariant rather than trusting every
    /// future caller to have pre-filtered is the correct, legible
    /// failure mode - silently returning a wrongly-oriented pair would
    /// be worse.
    /// </summary>
    public static bool TryOrientFromValidOrigin(PairDemand pair, IReadOnlySet<string> validOriginCityIds, out City origin, out City destination)
    {
        if (validOriginCityIds.Contains(pair.Origin.Id))
        {
            origin = pair.Origin;
            destination = pair.Destination;
            return true;
        }

        if (validOriginCityIds.Contains(pair.Destination.Id))
        {
            origin = pair.Destination;
            destination = pair.Origin;
            return true;
        }

        origin = pair.Origin;
        destination = pair.Destination;
        return false;
    }

    /// <summary>
    /// The best unserved pair touching a gateway city this airline already
    /// holds an unused slot at - checked ahead of a general new-pair
    /// search so a contested negotiation win actually gets exploited
    /// instead of stranding cash spent to win it while more bids keep
    /// going out for other gateways (the regional-balance investigation's
    /// own diagnosed root cause: one traced game had an airline win 6
    /// gateway negotiations across 11 quarters and open a route at none
    /// of them). "Unused" means AvailableSlotsByCity shows a positive
    /// balance there (M33: this airline's own remaining slot count) *and*
    /// no current route already touches that city - a gateway this
    /// airline is already flying through doesn't need this priority
    /// boost, the existing "grow best-performing route" step already
    /// covers that. Searches only reachable-by-idle-fleet pairs, matching
    /// every other new-pair search's own idle-first convention; callers
    /// fall back to their own general search when this returns null,
    /// exactly as before this method existed. Returns null (not "no
    /// priority") when AvailableSlotsByCity itself is null - a caller
    /// that doesn't populate real slot data has nothing to prioritize,
    /// same permissive-default convention as every other nullable
    /// StrategyContext field.
    ///
    /// M44: a won slot doesn't grant "start a route from nowhere" rights
    /// either - the returned pair still needs its *other* endpoint (or
    /// the won city itself) to be a valid origin. Callers use
    /// TryOrientFromValidOrigin on the returned Pair before building a
    /// RouteOrder from it - this method doesn't orient itself, since some
    /// callers (the M43 priority-boost's own two proof tests) inspect
    /// Pair.Origin/Destination directly against AllPairs's own canonical
    /// ordering.
    /// </summary>
    public static (PairDemand Pair, AircraftType Aircraft)? FindBestUnservedPairTouchingWonSlot(StrategyContext context, IReadOnlySet<int> reservedUnitIds)
    {
        if (context.AvailableSlotsByCity is null)
        {
            return null;
        }

        var touchedCityIds = context.CurrentRoutes
            .SelectMany(r => new[] { r.Origin.Id, r.Destination.Id })
            .ToHashSet(StringComparer.Ordinal);

        var wonUnusedCityIds = context.AvailableSlotsByCity
            .Where(kv => kv.Value > 0 && !touchedCityIds.Contains(kv.Key))
            .Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (wonUnusedCityIds.Count == 0)
        {
            return null;
        }

        var validOriginCityIds = ValidOriginCityIds(context);

        var served = context.CurrentRoutes
            .SelectMany(r => new[] { (r.Origin.Id, r.Destination.Id), (r.Destination.Id, r.Origin.Id) })
            .ToHashSet();

        var candidateTypes = context.OwnFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Select(u => u.Type)
            .Distinct()
            .ToList();

        var best = candidateTypes
            .SelectMany(type => context.DemandModel.AllPairs(context.AllCities)
                .Where(p => wonUnusedCityIds.Contains(p.Origin.Id) || wonUnusedCityIds.Contains(p.Destination.Id))
                .Where(p => validOriginCityIds.Contains(p.Origin.Id) || validOriginCityIds.Contains(p.Destination.Id))
                .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
                .Where(p => p.DistanceKm <= type.RangeKm)
                .Select(p => (Pair: p, Aircraft: type)))
            .OrderByDescending(c => c.Pair.PassengersPerQuarter)
            .ThenBy(c => c.Pair.Origin.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Pair.Destination.Id, StringComparer.Ordinal)
            .ThenBy(c => c.Aircraft.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        return best.Pair is null ? null : (best.Pair, best.Aircraft);
    }

    /// <summary>
    /// M45: the shared, ownerless capacity balance for every non-gateway
    /// city with a modeled AutoCapacity - each such city's AutoCapacity
    /// minus the AircraftCount every airline's current routes commit
    /// there, combined. Unlike LandingRightsBoard.AvailableSlotsByCity,
    /// this has no per-airline owner; it's the same number for whichever
    /// airline asks. The caller must recompute this fresh immediately
    /// before each airline's own QuarterEngine.RunQuarter call within a
    /// quarter (from routesByAirline as it stands at that point), or
    /// multiple airlines could each independently see spare room and
    /// collectively overshoot a hard cap - see QuarterEngine's own
    /// cityWideSlotsRemaining doc comment.
    /// </summary>
    /// <param name="capacityBonusByCity">
    /// M46: extra slots each city has earned via periodic, demand-driven
    /// expansion (see CityCapacityBoard) — null (the default) preserves
    /// every pre-existing caller's exact behavior.
    /// </param>
    public static IReadOnlyDictionary<string, int> ComputeCityWideSlotsRemaining(
        IReadOnlyDictionary<string, IReadOnlyList<Route>> routesByAirline, IReadOnlyList<City> cities,
        IReadOnlyDictionary<string, int>? capacityBonusByCity = null)
    {
        var used = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var routes in routesByAirline.Values)
        {
            foreach (var route in routes)
            {
                used[route.Origin.Id] = used.GetValueOrDefault(route.Origin.Id) + route.AircraftCount;
                used[route.Destination.Id] = used.GetValueOrDefault(route.Destination.Id) + route.AircraftCount;
            }
        }

        return cities
            .Where(c => c.AutoCapacity is not null)
            .ToDictionary(
                c => c.Id,
                c => Math.Max(0, c.AutoCapacity!.Value + (capacityBonusByCity?.GetValueOrDefault(c.Id) ?? 0) - used.GetValueOrDefault(c.Id)),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// M45: true when cityWideSlotsRemaining is null (permissive), or
    /// there's still room left at every AutoCapacity-modeled endpoint of
    /// origin/destination, net of what this same planning pass has
    /// already reserved. Mirrors BaselineAi.HasSlotAtEveryGateway's own
    /// shape exactly, for the ownerless non-gateway mechanic instead of
    /// the negotiated gateway one.
    /// </summary>
    public static bool HasRoomAtEveryCity(City origin, City destination, IReadOnlyDictionary<string, int>? cityWideSlotsRemaining, Dictionary<string, int> citySpendReservedThisPass)
    {
        if (cityWideSlotsRemaining is null)
        {
            return true;
        }

        return new[] { origin, destination }
            .Where(c => c.AutoCapacity is not null)
            .All(c => cityWideSlotsRemaining.GetValueOrDefault(c.Id) - citySpendReservedThisPass.GetValueOrDefault(c.Id) > 0);
    }

    /// <summary>M45: the AutoCapacity-modeled sibling of BaselineAi.ReserveSlots.</summary>
    public static void ReserveCityWideSlots(City origin, City destination, Dictionary<string, int> citySpendReservedThisPass)
    {
        foreach (var city in new[] { origin, destination }.Where(c => c.AutoCapacity is not null))
        {
            citySpendReservedThisPass[city.Id] = citySpendReservedThisPass.GetValueOrDefault(city.Id) + 1;
        }
    }

    /// <summary>
    /// M45: the best gateway with idle-expired, contestable inventory this
    /// airline hasn't already spent real money pursuing (same
    /// CitiesAlreadyPursuedForNegotiation exclusion every other
    /// negotiation-target search already applies) - lets AI-vs-AI play
    /// actually reach the idle-then-recontestable mechanic instead of it
    /// only ever being reachable by a human player submitting a
    /// NegotiationOrder by hand (CLAUDE.md: the sim never runs with fewer
    /// than 4 competing networks, so a mechanic invisible to AI is a real
    /// gap). Deliberately the same "single best candidate, no fallback
    /// search" shape as FindBestUnservedGateway - deeper tuning (weighing
    /// an idle-contest bid against a fresh-expansion bid) is a follow-on.
    /// Returns null when IdleContestableSlotsByCity itself is null - a
    /// caller that doesn't populate it has nothing to target, same
    /// permissive-default convention as every other nullable
    /// StrategyContext field.
    /// </summary>
    public static City? FindBestIdleContestableGateway(StrategyContext context)
    {
        if (context.IdleContestableSlotsByCity is null)
        {
            return null;
        }

        var alreadyPursued = CitiesAlreadyPursuedForNegotiation(context);
        var cityById = context.AllCities.ToDictionary(c => c.Id, StringComparer.Ordinal);

        return context.IdleContestableSlotsByCity
            .Where(kv => kv.Value > 0 && !alreadyPursued.Contains(kv.Key))
            .Select(kv => cityById[kv.Key])
            .OrderByDescending(c => c.DemandWeight)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    public static void NegotiateOrBorrow(StrategyContext context, City? bestGateway, List<QuarterOrder> orders, ref int spent)
    {
        if (bestGateway is null || spent + 1 > context.ActionCap)
        {
            return;
        }

        var canAffordNow = context.AvailableCash is null || FinancialState.CanAfford(context.AvailableCash.Value, BaselineAi.NegotiationBidAmount);

        if (canAffordNow)
        {
            orders.Add(new NegotiationOrder(bestGateway, BaselineAi.NegotiationBidAmount));
            spent += 1;
        }
        else if (context.AvailableCash is not null && context.CurrentDebtBalance is not null)
        {
            var shortfall = BaselineAi.NegotiationBidAmount - context.AvailableCash.Value;
            var ceilingHeadroom = Money.FromDollars(FinancialResolver.DebtCeilingDollars) - context.CurrentDebtBalance.Value;

            if (FinancialState.CanAfford(ceilingHeadroom, shortfall))
            {
                orders.Add(new RefinanceOrder(shortfall));
            }
        }
    }

    /// <summary>
    /// M44: mirrors BaselineAi.FindBestUnservedPair's own shape (plain
    /// demand-ranked, no cross-region/domestic preference) but also
    /// respects network contiguity - used by ApplyManagementToolkit's own
    /// stranded-unit check rather than BaselineAi.FindBestUnservedPair
    /// itself, which stays untouched (BaselineAi.DecideOrders is legacy,
    /// no longer called by either real loop - see its own class doc
    /// comment). A unit that could only reach a non-contiguous pair now
    /// correctly counts as stranded: winning a gateway slot doesn't grant
    /// route rights from a city the airline has no presence near.
    /// </summary>
    private static PairDemand? FindBestUnservedPairFromValidOrigin(StrategyContext context, IReadOnlySet<string> validOriginCityIds, AircraftType aircraft)
    {
        var served = context.CurrentRoutes
            .SelectMany(r => new[] { (r.Origin.Id, r.Destination.Id), (r.Destination.Id, r.Origin.Id) })
            .ToHashSet();

        return context.DemandModel.AllPairs(context.AllCities)
            .Where(p => !served.Contains((p.Origin.Id, p.Destination.Id)))
            .Where(p => p.DistanceKm <= aircraft.RangeKm)
            .Where(p => validOriginCityIds.Contains(p.Origin.Id) || validOriginCityIds.Contains(p.Destination.Id))
            .OrderByDescending(p => p.PassengersPerQuarter)
            .ThenBy(p => p.Origin.Id, StringComparer.Ordinal)
            .ThenBy(p => p.Destination.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>
    /// Applies BaselineAi's M31/M35/M36/M37 passive toolkit against
    /// whatever this strategy already holds, unconditionally and
    /// unfiltered by region — a route/unit is a route/unit regardless of
    /// how it got there. Mutates `orders` in place, matching
    /// BaselineAi.DecideOrders's own style.
    /// </summary>
    public static void ApplyManagementToolkit(StrategyContext context, List<QuarterOrder> orders, HashSet<int> reservedUnitIds)
    {
        var routeLoads = RouteEvaluator.EvaluateNetwork(context.CurrentRoutes, context.DemandModel);

        foreach (var overBuilt in routeLoads
            .Where(load => load.Route.AircraftCount > 1 && load.LoadFactor < BaselineAi.CloseRouteLoadFactorFloor)
            .Select(load => load.Route))
        {
            orders.Add(new CloseRouteOrder(overBuilt.Origin, overBuilt.Destination));
        }

        foreach (var saturated in routeLoads
            .Where(load => load.LoadFactor >= BaselineAi.SaturatedLoadFactorCeiling)
            .Select(load => load.Route))
        {
            var target = BaselineAi.RaisedFare(saturated);

            if (saturated.FarePerPassenger < target)
            {
                orders.Add(new SetFareOrder(saturated.Origin, saturated.Destination, target));
            }
        }

        foreach (var underperforming in routeLoads
            .Where(load => load.LoadFactor < BaselineAi.CloseRouteLoadFactorFloor)
            .Select(load => load.Route))
        {
            var target = BaselineAi.CutFare(underperforming);

            if (underperforming.FarePerPassenger > target)
            {
                orders.Add(new SetFareOrder(underperforming.Origin, underperforming.Destination, target));
            }
        }

        var validOriginCityIds = ValidOriginCityIds(context);

        foreach (var stranded in context.OwnFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Where(u => context.CurrentRoutes.All(r => r.Aircraft.Id != u.Type.Id))
            .Where(u => (FindBestUnservedPairFromValidOrigin(context, validOriginCityIds, u.Type)?.PassengersPerQuarter ?? 0) <= 0)
            .OrderBy(u => u.Id))
        {
            orders.Add(new RetireAircraftOrder(stranded.Id));
        }

        if (context.AvailableCash is not null && context.CurrentDebtBalance is not null
            && context.CurrentDebtBalance.Value > Money.Zero && context.AvailableCash.Value > BaselineAi.PayDownCashBuffer)
        {
            var affordablePaydown = context.AvailableCash.Value - BaselineAi.PayDownCashBuffer;
            var payDownAmount = affordablePaydown < context.CurrentDebtBalance.Value ? affordablePaydown : context.CurrentDebtBalance.Value;

            if (payDownAmount > Money.Zero)
            {
                orders.Add(new PayDownDebtOrder(payDownAmount));
            }
        }

        orders.Add(new HireExecutiveOrder());
    }
}
