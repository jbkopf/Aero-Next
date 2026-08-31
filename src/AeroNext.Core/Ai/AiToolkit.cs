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
/// </summary>
internal static class AiToolkit
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

        foreach (var stranded in context.OwnFleet.Idle
            .Where(u => !reservedUnitIds.Contains(u.Id))
            .Where(u => context.CurrentRoutes.All(r => r.Aircraft.Id != u.Type.Id))
            .Where(u => (BaselineAi.FindBestUnservedPair(context.CurrentRoutes, context.AllCities, context.DemandModel, u.Type)?.PassengersPerQuarter ?? 0) <= 0)
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
