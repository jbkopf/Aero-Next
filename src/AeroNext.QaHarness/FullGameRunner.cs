using AeroNext.Core;
using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
using AeroNext.Core.Fleet;
using AeroNext.Core.Negotiations;
using AeroNext.Core.Setup;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.QaHarness;

/// <summary>
/// Runs one full campaign from a random-but-valid GameSetup configuration
/// through <paramref name="quarters"/> quarters of the real resolver chain,
/// checking invariants every quarter along the way. Deliberately more
/// complete than AeroNext.Sim's own M13 campaign loop: BaselineAi does
/// submit NegotiationOrders (per M9), but M13's loop never actually wires
/// them into LandingRightsResolver, so those actions are silently wasted
/// there — a real, pre-existing gap this harness doesn't repeat, since a QA
/// sweep should exercise the same resolver surface a real game would.
///
/// Quarter 1 is seeded with each seat's M18-guaranteed route explicitly,
/// not an AI pick — the guaranteed-route mechanic predates M39 and still
/// runs unconditionally on quarter 1, before any strategy is even
/// selected. From quarter 2 on, each seat is driven by an M39 IAiStrategy
/// (BaselineAi.SelectStrategy assigns one initially and re-evaluates once
/// per in-game year), which — unlike the old flat BaselineAi.DecideOrders
/// call this replaced — is genuinely home-region-aware from the moment it
/// picks its first order.
///
/// M39 note (not yet also applied to AeroNext.Sim's own M13 loop): the
/// StrategyContext this method builds needs a real HomeRegion, which every
/// seat here already carries via GameSetup.CreateSeats — this loop's own
/// prior "identical global-best-pair for all 4 seats" limitation is gone.
/// </summary>
public static class FullGameRunner
{
    private static readonly Money DefaultStartingDebt = Money.FromDollars(0);
    private const double DefaultInterestRatePerQuarter = 0.02;

    /// <summary>Anything past this is almost certainly a sign-flip/overflow bug, not a plausible airline balance.</summary>
    private static readonly Money SaneMagnitudeBound = Money.FromDollars(1_000_000_000_000);

    /// <param name="seats">
    /// The 4 finalized airline seats to run. Normally built via
    /// GameConfigGenerator + GameSetup.CreateSeats; the unconstrained-regions
    /// QA mode builds these a different way (see UnconstrainedSeatGenerator)
    /// — this method itself has no opinion on how seats were produced.
    /// Regions are not required to be distinct across seats (real games
    /// allow sharing a home region since the same-region change) — only
    /// airline names and starting cities are checked below.
    /// </param>
    public static FullGameResult Run(
        int gameIndex, int configSeed, int campaignSeed, int quarters,
        IReadOnlyList<AirlineSeat> seats,
        IReadOnlyList<City> cities, IReadOnlyList<AircraftType> fleetCatalog, IReadOnlyList<DemandGrowthPoint> growthSchedule)
    {
        var violations = new List<GameViolation>();
        var campaignRng = new SimRng(campaignSeed);

        var names = seats.Select(s => s.AirlineName).ToList();

        if (seats.Count != 4 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
        {
            violations.Add(new GameViolation(0, "-", "Seats did not have 4 distinct airline names."));
        }

        if (seats.Select(s => s.StartingCity.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != seats.Count)
        {
            violations.Add(new GameViolation(0, "-", "Seats did not have distinct starting cities."));
        }

        var conditionsByName = seats.ToDictionary(s => s.AirlineName, s => StartingConditionsResolver.Resolve(s, cities, fleetCatalog));
        var seatByName = seats.ToDictionary(s => s.AirlineName);
        var homeRegionByName = seats.ToDictionary(s => s.AirlineName, s => s.HomeRegion);
        var routes = names.ToDictionary(n => n, _ => (IReadOnlyList<Route>)Array.Empty<Route>());
        var caps = names.ToDictionary(n => n, _ => QuarterEngine.BaseActionCap);
        var finances = names.ToDictionary(n => n, n => FinancialState.Start(conditionsByName[n].StartingCash, DefaultStartingDebt, DefaultInterestRatePerQuarter));
        var bankruptcyQuarter = names.ToDictionary(n => n, n => (int?)null);
        var currentStrategyByName = names.ToDictionary(n => n, n => (IAiStrategy?)null);

        // M54: real, competitively-resolved carried seats per route, one
        // quarter lagged - built from each quarter's own real market.RouteUsage
        // (below), read by the *next* quarter's own StrategyContext. Empty
        // dict (not null) until the first quarter resolves, since every
        // airline's own quarter-1 route is the M18 guaranteed one anyway
        // (no strategy consults this that early).
        var lastQuarterSeatsCarriedByRouteByName = names.ToDictionary(n => n, n => (IReadOnlyDictionary<(string, string), long>)new Dictionary<(string, string), long>());

        // M55: each airline's own real memory of its own routes' recent
        // past - the missing piece M43 and M54 each separately diagnosed
        // and deferred (see AiRouteHistory's own doc comment). A plain
        // mutable Dictionary, updated in place across the whole game
        // (unlike lastQuarterSeatsCarriedByRouteByName just above, which
        // is replaced wholesale each quarter) - StrategyContext reads it
        // by reference at build time, before this same quarter's own two
        // update passes run later in the loop, so the one-quarter lag
        // every other real field here already has falls out naturally,
        // with no extra bookkeeping needed.
        var routeHistoryByAirlineName = names.ToDictionary(n => n, n => new Dictionary<(string, string), AiRouteHistory>());
        var humanCount = seats.Count(s => s.IsHuman);

        // M18's AircraftQuantity (2), real data never consumed until now —
        // becomes each airline's actual starting fleet, owned outright, the
        // thing that backs the guaranteed quarter-1 route.
        var fleets = names.ToDictionary(n => n, n => FleetLedger.ApplyDelivery(
            AirlineFleet.Empty, conditionsByName[n].StartingAircraft, StartingConditionsResolver.StartingAircraftQuantity, FleetOwnershipMode.Owned));
        var aircraftOrderBoard = AircraftOrderBoard.Empty; // shared across all 4 airlines - the production line is genuinely contested

        var landingBoard = LandingRightsBoard.Empty;
        // M46: each city's own 1955 total demand becomes the fixed
        // reference point periodic expansion measures growth against -
        // computed once, here, before any quarter runs.
        var capacityBoard = CityCapacityBoard.Initialize(cities, new GravityDemandModel(schedule: growthSchedule, year: Timeline.StartYear, enableHubDemandBonus: true));
        var dominanceState = RegionalDominanceState.Empty;
        string? winner = null;
        int? winQuarter = null;
        CompetitiveMarketResult? lastMarket = null;
        IReadOnlySet<string> lastEligibleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var q = 1; q <= quarters; q++)
        {
            var year = Timeline.YearForQuarter(q);
            var availableFleet = Timeline.AvailableAircraft(fleetCatalog, year);
            var yearDemandModel = new GravityDemandModel(schedule: growthSchedule, year: year, enableHubDemandBonus: true); // M20: model resolves global (and any region/city-scoped) growth itself
            var appliedOrdersByName = new Dictionary<string, IReadOnlyList<QuarterOrder>>();
            var retiredUnitsByName = new Dictionary<string, IReadOnlyList<FleetUnit>>();

            // Deliveries from orders placed in earlier quarters arrive
            // before this quarter's own route decisions are made, per
            // AircraftOrderResolver.ResolveQuarter's own eligibility rule
            // (OrderedQuarter < currentQuarter — an order can't ship the
            // same quarter it's placed).
            IReadOnlyList<AircraftDelivery> deliveries;
            (aircraftOrderBoard, deliveries) = AircraftOrderResolver.ResolveQuarter(aircraftOrderBoard, q);
            foreach (var delivery in deliveries)
            {
                fleets[delivery.Airline.Name] = FleetLedger.ApplyDelivery(fleets[delivery.Airline.Name], delivery, FleetOwnershipMode.Owned);
            }

            // Snapshotted before any order processing this quarter, for
            // every seat — not just non-bankrupt ones — so the freeze check
            // below can catch a bankrupt seat's routes/cap changing even if
            // the skip-guard that's supposed to prevent that ever regresses
            // (this is the exact shape of the real M13 bug).
            var routesAtQuarterStart = names.ToDictionary(n => n, n => routes[n]);
            var capsAtQuarterStart = names.ToDictionary(n => n, n => caps[n]);

            // M46: re-evaluated on its own fixed periodic cadence
            // (a no-op most quarters) - depends only on demand, not on
            // any order placed this quarter, so it's resolved before the
            // per-airline loop and is visible to every airline's own
            // decisions the same quarter it lands.
            capacityBoard = capacityBoard.AdvanceQuarter(q, cities, yearDemandModel);

            // M45: which gateways currently carry idle-expired, contestable
            // inventory - computed once per quarter from state entering it
            // (last quarter's own AdvanceIdleTracking call, below), so
            // every airline's strategy sees the same public-information
            // snapshot regardless of processing order within this quarter.
            var idleContestableSlotsByCity = landingBoard.IdleContestableSlotsByCity(cities, routesAtQuarterStart);

            // M52: how many distinct airlines serve each normalized pair,
            // from state entering this quarter - same "public snapshot,
            // computed once before any airline's turn this quarter"
            // convention as idleContestableSlotsByCity just above, so
            // every airline's strategy sees the same picture regardless
            // of processing order within the quarter.
            var servingAirlineNamesByPairKey = new Dictionary<(string, string), HashSet<string>>();
            foreach (var n in names)
            {
                foreach (var route in routesAtQuarterStart[n])
                {
                    var key = QuarterEngine.NormalizedPairKey(route.Origin, route.Destination);
                    if (!servingAirlineNamesByPairKey.TryGetValue(key, out var servingNames))
                    {
                        servingNames = new HashSet<string>(StringComparer.Ordinal);
                        servingAirlineNamesByPairKey[key] = servingNames;
                    }

                    servingNames.Add(n);
                }
            }

            foreach (var name in names)
            {
                var wasBankruptEnteringQuarter = finances[name].IsBankrupt;

                if (wasBankruptEnteringQuarter)
                {
                    appliedOrdersByName[name] = Array.Empty<QuarterOrder>();
                    retiredUnitsByName[name] = Array.Empty<FleetUnit>();
                    continue;
                }

                var capBefore = caps[name];
                var airlineForSlots = new Airline(name, routes[name]);
                var availableSlotsByCity = landingBoard.AvailableSlotsByCity(airlineForSlots, routes[name]);

                // M45: recomputed fresh right before each airline's own
                // turn, from routes as they stand at this point in the
                // loop - already reflecting any earlier airline's updates
                // this same quarter, so this ownerless shared balance can
                // never be independently overshot by two airlines both
                // seeing spare room (see QuarterEngine's own
                // cityWideSlotsRemaining doc comment).
                var cityWideSlotsRemaining = AiToolkit.ComputeCityWideSlotsRemaining(routes, cities, capacityBoard.CapacityBonusByCity);

                // M41: which gateways this airline already has an
                // unresolved negotiation bid at - this loop already
                // builds a real landingBoard, so it's the one real call
                // site that can populate this for real. M59: hoisted out
                // of the q != 1 branch below (was StrategyContext-only)
                // since QuarterEngine.RunQuarter now needs it too, to
                // reject a duplicate bid before any cash is spent.
                var pendingNegotiationCityIds = landingBoard.Pending
                    .Where(p => p.Airline.Name == name)
                    .Select(p => p.TargetCity.Id)
                    .ToHashSet(StringComparer.Ordinal);

                IReadOnlyList<QuarterOrder> orders;
                if (q == 1)
                {
                    orders = new QuarterOrder[]
                    {
                        new RouteOrder(
                            seatByName[name].StartingCity,
                            conditionsByName[name].GuaranteedRouteDestination,
                            conditionsByName[name].StartingAircraft),
                    };
                }
                else
                {
                    // M39: strategy-driven from here on - selected once
                    // per airline (initial assignment then re-evaluated
                    // once per in-game year) rather than BaselineAi's own
                    // single fixed heuristic. M41's own
                    // pendingNegotiationCityIds (so the strategy stops
                    // re-bidding a gateway already pending) is now
                    // computed above, before this branch.
                    //
                    // M52: scoped to only this airline's own currently-
                    // served routes - servingAirlineNamesByPairKey's own
                    // count includes this airline itself wherever it
                    // appears, so subtract 1 to get rivals, not total.
                    var rivalCountByServedRoute = routesAtQuarterStart[name].ToDictionary(
                        r => QuarterEngine.NormalizedPairKey(r.Origin, r.Destination),
                        r => servingAirlineNamesByPairKey[QuarterEngine.NormalizedPairKey(r.Origin, r.Destination)].Count - 1);

                    var strategyContext = new StrategyContext(
                        routes[name], caps[name], cities, yearDemandModel, availableFleet, fleets[name], homeRegionByName[name],
                        HomeCity: seatByName[name].StartingCity,
                        AvailableSlotsByCity: availableSlotsByCity, AvailableCash: finances[name].CashBalance, CurrentDebtBalance: finances[name].DebtBalance,
                        PendingNegotiationCityIds: pendingNegotiationCityIds,
                        CityWideSlotsRemaining: cityWideSlotsRemaining,
                        IdleContestableSlotsByCity: idleContestableSlotsByCity,
                        RivalCountByServedRoute: rivalCountByServedRoute,
                        LastQuarterSeatsCarriedByRoute: lastQuarterSeatsCarriedByRouteByName[name],
                        QuarterNumber: q,
                        RouteHistoryByPair: routeHistoryByAirlineName[name]);
                    var strategy = BaselineAi.SelectStrategy(currentStrategyByName[name], q, strategyContext);
                    currentStrategyByName[name] = strategy;
                    orders = strategy.DecideOrders(strategyContext);
                }

                // M18's guaranteed quarter-1 route is exempt from slot
                // gating — nothing in the starting-conditions design pre-
                // grants a negotiated slot at a gateway home/destination
                // city, so gating it here would break the "always
                // accepted" guarantee the very next check relies on. M44:
                // exempt from network-contiguity too, for the same reason
                // — it's always Origin=StartingCity anyway, so homeCity
                // would never reject it, but passing null on quarter 1
                // keeps the "guaranteed route is unconditionally exempt"
                // guarantee explicit rather than incidental.
                var result = QuarterEngine.RunQuarter(
                    q, routes[name], caps[name], orders, cities, yearDemandModel, fleets[name],
                    availableCash: finances[name].CashBalance,
                    availableSlotsByCity: q == 1 ? null : availableSlotsByCity,
                    currentDebtBalance: finances[name].DebtBalance,
                    homeCity: q == 1 ? null : seatByName[name].StartingCity,
                    cityWideSlotsRemaining: q == 1 ? null : cityWideSlotsRemaining,
                    pendingNegotiationCityIds: q == 1 ? null : pendingNegotiationCityIds);

                if (q == 1 && result.RejectedOrders.Count > 0)
                {
                    violations.Add(new GameViolation(q, name, "M18's guaranteed quarter-1 route was rejected — should never happen."));
                }

                // M55, pass 1 of 2: stamp LastShrunkQuarter/LastClosedQuarter
                // from what actually applied - routes[name] here is still
                // the *pre*-quarter route list (reassigned just below), so
                // this is the same technique M54 used to gather its own
                // 19,679-vs-25,722 closure count: look up the pair's own
                // AircraftCount before this quarter's own CloseRouteOrder
                // to tell a shrink from a full closure.
                foreach (var closeOrder in result.AppliedOrders.OfType<CloseRouteOrder>())
                {
                    var pairKey = QuarterEngine.NormalizedPairKey(closeOrder.Origin, closeOrder.Destination);
                    var before = routes[name].FirstOrDefault(r => r.Origin.Id == closeOrder.Origin.Id && r.Destination.Id == closeOrder.Destination.Id);
                    var wasFullClosure = before is not null && before.AircraftCount == 1;
                    var existingHistory = routeHistoryByAirlineName[name].GetValueOrDefault(pairKey) ?? new AiRouteHistory(null, null, 0);

                    routeHistoryByAirlineName[name][pairKey] = wasFullClosure
                        ? existingHistory with { LastClosedQuarter = q }
                        : existingHistory with { LastShrunkQuarter = q };
                }

                routes[name] = result.Routes;
                caps[name] = result.ActionCap;
                fleets[name] = result.UpdatedFleet!;
                appliedOrdersByName[name] = result.AppliedOrders;
                retiredUnitsByName[name] = result.RetiredUnits!;

                var airlineSnapshot = new Airline(name, routes[name]);
                foreach (var negotiation in result.AppliedOrders.OfType<NegotiationOrder>())
                {
                    landingBoard = LandingRightsResolver.Submit(landingBoard, airlineSnapshot, negotiation, q, campaignRng, capacityBoard.CapacityBonusByCity);
                }

                foreach (var relinquish in result.AppliedOrders.OfType<RelinquishSlotOrder>())
                {
                    landingBoard = LandingRightsResolver.Relinquish(landingBoard, airlineSnapshot, relinquish.TargetCity);
                }

                foreach (var aircraftOrder in result.AppliedOrders.OfType<AircraftOrder>())
                {
                    aircraftOrderBoard = AircraftOrderResolver.Submit(aircraftOrderBoard, airlineSnapshot, aircraftOrder, q);
                }

                // M54: this used to be a real invariant - "a route can
                // shrink but never disappear outright" was true for every
                // milestone through M53 (M35's own design: shrink, never
                // close, to avoid the thrash risk). M54 deliberately adds
                // real closure for a genuinely unprofitable, uncontested,
                // single-aircraft route (AiToolkit.ApplyManagementToolkit),
                // so a route count decrease with no bankruptcy is now an
                // expected, intended outcome, not a bug - removed rather
                // than weakened, since there's no longer a real invariant
                // left to check here.

                if (result.ActionCap < capBefore)
                {
                    violations.Add(new GameViolation(q, name, "Action cap decreased — it should only ever hold or rise."));
                }
            }

            // M45: advance idle-slot tracking from this quarter's own
            // final route state before resolving negotiations, so a
            // challenger due to resolve this same quarter can already see
            // inventory that just crossed IdleExpiryThreshold.
            landingBoard = landingBoard.AdvanceIdleTracking(routes);
            (landingBoard, _) = LandingRightsResolver.ResolveQuarter(landingBoard, q, routes, capacityBoard.CapacityBonusByCity);

            // M47: each airline's own accepted marketing campaign this
            // quarter (at most one of network-wide or up to
            // MaxConcurrentRouteMarketingCampaigns route-targeted -
            // QuarterEngine.RunQuarter's own mutual-exclusivity/cap
            // enforcement already guarantees this shape), built fresh
            // from this quarter's own AppliedOrders.
            var marketingByAirlineName = names.ToDictionary(
                n => n,
                n => new AirlineMarketingCampaign(
                    NetworkWide: appliedOrdersByName[n].OfType<NetworkMarketingOrder>().Any(),
                    RouteKeys: appliedOrdersByName[n].OfType<RouteMarketingOrder>()
                        .Select(o => QuarterEngine.NormalizedPairKey(o.Origin, o.Destination))
                        .ToHashSet()),
                StringComparer.Ordinal);

            var league = seats.Select(s => new Airline(s.AirlineName, routes[s.AirlineName])).ToArray();
            var market = CompetitiveMarket.Resolve(league, cities, yearDemandModel, marketingByAirlineName);

            // M54: this quarter's own real carried seats per route,
            // stored for *next* quarter's own StrategyContext to read -
            // see lastQuarterSeatsCarriedByRouteByName's own declaration.
            // M55, pass 2 of 2 (same loop, real data already in hand):
            // ConsecutiveUnprofitableQuarters - increment on a real
            // negative FareCalculator.EstimatedProfit reading, reset to 0
            // otherwise. A route closed this quarter already dropped out
            // of airline.Routes (built from routes[name] above, after
            // pass 1's own reassignment), so it naturally stops updating
            // here - its own LastClosedQuarter stamp from pass 1 is what
            // matters going forward, not a stale profit streak.
            foreach (var airline in league)
            {
                var usageByPair = market.RouteUsage
                    .Where(u => u.Airline == airline)
                    .ToDictionary(u => QuarterEngine.NormalizedPairKey(u.Route.Origin, u.Route.Destination), u => u);

                lastQuarterSeatsCarriedByRouteByName[airline.Name] = usageByPair.ToDictionary(kv => kv.Key, kv => kv.Value.SeatsUsed);

                foreach (var (pairKey, usage) in usageByPair)
                {
                    var profit = FareCalculator.EstimatedProfit(usage.Route, usage.SeatsUsed);
                    var existingHistory = routeHistoryByAirlineName[airline.Name].GetValueOrDefault(pairKey) ?? new AiRouteHistory(null, null, 0);
                    var streak = profit < Money.Zero ? existingHistory.ConsecutiveUnprofitableQuarters + 1 : 0;

                    routeHistoryByAirlineName[airline.Name][pairKey] = existingHistory with { ConsecutiveUnprofitableQuarters = streak };
                }
            }

            foreach (var airline in league)
            {
                var name = airline.Name;
                var wasBankruptEnteringQuarter = finances[name].IsBankrupt;

                var (nextState, _) = FinancialResolver.Resolve(finances[name], airline, market, appliedOrdersByName[name], fleet: fleets[name], retiredUnits: retiredUnitsByName[name], currentActionCap: capsAtQuarterStart[name]);
                finances[name] = nextState;

                if (wasBankruptEnteringQuarter)
                {
                    if (!nextState.IsBankrupt)
                    {
                        violations.Add(new GameViolation(q, name, "A bankrupt airline became solvent again — bankruptcy must be sticky."));
                    }

                    if (routes[name].Count != routesAtQuarterStart[name].Count || caps[name] != capsAtQuarterStart[name])
                    {
                        violations.Add(new GameViolation(q, name, "A bankrupt airline's routes/action cap changed — they must freeze."));
                    }
                }
                else if (nextState.IsBankrupt)
                {
                    bankruptcyQuarter[name] = q;
                }

                if (Math.Abs(nextState.CashBalance.Cents) > SaneMagnitudeBound.Cents || Math.Abs(nextState.DebtBalance.Cents) > SaneMagnitudeBound.Cents)
                {
                    violations.Add(new GameViolation(q, name, $"Cash/debt magnitude implausible (cash={nextState.CashBalance}, debt={nextState.DebtBalance}) — possible overflow."));
                }
            }

            lastMarket = market;

            lastEligibleNames = names.Where(n => !finances[n].IsBankrupt).ToHashSet(StringComparer.OrdinalIgnoreCase);
            dominanceState = RegionalDominanceTracker.Advance(dominanceState, market, lastEligibleNames);

            var checkedWinner = WinConditionChecker.CheckWinner(dominanceState, lastEligibleNames);
            if (checkedWinner is not null)
            {
                winner = checkedWinner;
                winQuarter = q;
                break;
            }

            if (lastEligibleNames.Count == 0)
            {
                // Every remaining airline went bankrupt this exact
                // quarter - a real stalemate, nobody left to declare a
                // winner from. See WinConditionChecker's own doc comment.
                break;
            }
        }

        var finalCash = names.ToDictionary(n => n, n => finances[n].CashBalance);
        var netWorth = names.ToDictionary(n => n, n => finances[n].CashBalance - finances[n].DebtBalance);

        if (winner is null && lastEligibleNames.Count > 0)
        {
            // The campaign ran its full course (or ended on mutual
            // elimination, in which case lastEligibleNames is empty and
            // this is correctly skipped) with 2+ solvent airlines and no
            // dominance/sole-survivor winner - declare one from the same
            // ranking CampaignRanking already produces, per the user's
            // own direction. See CampaignRanking.DeclareWinner.
            var ranking = CampaignRanking.Rank(dominanceState, lastMarket!, netWorth);
            winner = CampaignRanking.DeclareWinner(ranking, lastEligibleNames);
            winQuarter = winner is not null ? quarters : null;
        }

        var finalRanking = winner is null
            ? CampaignRanking.Rank(dominanceState, lastMarket!, netWorth).Select(r => r.AirlineName).ToList()
            : new List<string> { winner };

        return new FullGameResult(
            gameIndex, configSeed, campaignSeed, humanCount, winQuarter ?? quarters, violations,
            finalCash, bankruptcyQuarter, homeRegionByName, winner, winQuarter, finalRanking);
    }
}
