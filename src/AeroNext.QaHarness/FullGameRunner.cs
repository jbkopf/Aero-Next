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
    /// <param name="difficulty">
    /// Difficulty follow-up: one value for the whole game, drives both
    /// WinConditionChecker.CheckWinner's own requiredRegionCount and every
    /// seat's own StrategyContext.TargetRegionCountOverride each quarter
    /// (see DifficultySettings.RegionCounts) - defaults to
    /// GameDifficulty.Normal, byte-identical to every pre-existing caller.
    /// </param>
    /// <param name="tradeRestrictions">
    /// Geopolitical-aircraft-restrictions follow-up
    /// (docs/GEOPOLITICAL_AIRCRAFT_RESTRICTIONS_ROADMAP.md): null (the
    /// default) means no restriction ever applies, byte-identical to
    /// before this parameter existed. A real list feeds both
    /// QuarterEngine.RunQuarter's own enforcement gate and each seat's own
    /// StrategyContext.PurchasableTypes/LeasableTypes, so the AI never
    /// proposes what the engine would reject anyway.
    /// </param>
    public static FullGameResult Run(
        int gameIndex, int configSeed, int campaignSeed, int quarters,
        IReadOnlyList<AirlineSeat> seats,
        IReadOnlyList<City> cities, IReadOnlyList<AircraftType> fleetCatalog, IReadOnlyList<DemandGrowthPoint> growthSchedule,
        ExperimentTuning? tuning = null, GameDifficulty difficulty = GameDifficulty.Normal,
        IReadOnlyList<TradeRestriction>? tradeRestrictions = null)
    {
        tuning ??= ExperimentTuning.Baseline;
        var regionCounts = DifficultySettings.RegionCounts(difficulty);
        var violations = new List<GameViolation>();
        var campaignRng = new SimRng(campaignSeed);
        // A separate stream from campaignRng (offset, not reused) so
        // ExperimentTuning.GlobalRouteWeightExponent - off by default, so
        // this never runs for the shipped baseline - never perturbs the
        // real negotiation-resolution draws campaignRng itself feeds.
        var routeWeightRng = new SimRng(unchecked(campaignSeed + 800_000_000));
        // Aircraft-acquisition-limits follow-up (docs/AIRCRAFT_ACQUISITION_ROADMAP.md):
        // another separate stream, same reasoning - LeaseAllocationResolver's
        // own even-plus-random allocation draws from this every quarter
        // any lease demand exceeds a type's own cap, and must never
        // perturb campaignRng's own negotiation-resolution draws or
        // routeWeightRng's own (off-by-default) route-selection draws.
        var leaseAllocationRng = new SimRng(unchecked(campaignSeed + 900_000_000));

        var names = seats.Select(s => s.AirlineName).ToList();

        if (seats.Count != 4 || names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
        {
            violations.Add(new GameViolation(0, "-", "Seats did not have 4 distinct airline names."));
        }

        if (seats.Select(s => s.StartingCity.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != seats.Count)
        {
            violations.Add(new GameViolation(0, "-", "Seats did not have distinct starting cities."));
        }

        // An invalid seat set isn't a game worth playing out - and duplicate
        // names would crash the per-airline dictionaries below before this
        // violation could ever be reported.
        if (violations.Count > 0)
        {
            return new FullGameResult(
                gameIndex, configSeed, campaignSeed, seats.Count(s => s.IsHuman), 0, violations,
                new Dictionary<string, Money>(), new Dictionary<string, int?>(), new Dictionary<string, Region>(),
                new Dictionary<string, string>(), Winner: null, WinQuarter: null, FinalRanking: Array.Empty<string>());
        }

        var conditionsByName = seats.ToDictionary(
            s => s.AirlineName,
            s => tuning.ApplyTo(s, StartingConditionsResolver.Resolve(s, cities, fleetCatalog, growthSchedule), cities, fleetCatalog, growthSchedule, routeWeightRng));
        var seatByName = seats.ToDictionary(s => s.AirlineName);
        var homeRegionByName = seats.ToDictionary(s => s.AirlineName, s => s.HomeRegion);
        var startingCityIdByName = seats.ToDictionary(s => s.AirlineName, s => s.StartingCity.Id);
        var routes = names.ToDictionary(n => n, _ => (IReadOnlyList<Route>)Array.Empty<Route>());
        var caps = names.ToDictionary(n => n, _ => QuarterEngine.BaseActionCap);
        var finances = names.ToDictionary(n => n, n => FinancialState.Start(conditionsByName[n].StartingCash, DefaultStartingDebt, DefaultInterestRatePerQuarter));
        var bankruptcyQuarter = names.ToDictionary(n => n, n => (int?)null);
        var currentStrategyByName = names.ToDictionary(n => n, n => (IAiStrategy?)null);
        // docs/OPEN_WORK.md's filler-seat investigation: accumulated below,
        // one quarter at a time, over each airline's first 20 quarters only.
        var negotiationSpendCentsFirst20QByName = names.ToDictionary(n => n, n => 0L);
        var negotiationBidCountFirst20QByName = names.ToDictionary(n => n, n => 0);
        var typeSwapsApplied = 0;
        var multiStopRouteOrdersApplied = 0;
        var networkOptimizerQuarters = 0;
        long routeQuarters = 0;
        long contestedRouteQuarters = 0;
        var balancedGrowthQuarters = 0;
        var routeCampaignsApplied = 0;
        var networkCampaignsApplied = 0;

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
        var leaseRequestBoard = LeaseRequestBoard.Empty; // shared across all 4 airlines - the same type's own lease pool is genuinely contested too

        var landingBoard = LandingRightsBoard.Empty;
        // M46: each city's own 1955 total demand becomes the fixed
        // reference point periodic expansion measures growth against -
        // computed once, here, before any quarter runs.
        var capacityBoard = CityCapacityBoard.Initialize(cities, new GravityDemandModel(
            schedule: growthSchedule, year: Timeline.StartYear, enableHubDemandBonus: true,
            intraRegionMultipliers: tuning.IntraRegionDemandMultipliers));
        var dominanceState = RegionalDominanceState.Empty;
        string? winner = null;
        int? winQuarter = null;
        CompetitiveMarketResult? lastMarket = null;
        IReadOnlySet<string> lastEligibleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var q = 1; q <= quarters; q++)
        {
            var year = Timeline.YearForQuarter(q);
            var availableFleet = Timeline.AvailableAircraft(fleetCatalog, year);
            var yearDemandModel = new GravityDemandModel(
                schedule: growthSchedule, year: year, enableHubDemandBonus: true,
                intraRegionMultipliers: tuning.IntraRegionDemandMultipliers); // M20: model resolves global (and any region/city-scoped) growth itself
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

            // Aircraft-acquisition-limits follow-up: lease requests
            // submitted last quarter resolve here too, the same "before
            // this quarter's own route decisions are made" timing -
            // charged against real delivered quantity, debited straight
            // into CashBalance before FinancialResolver.Resolve runs for
            // this same quarter, so a real cash-negative consequence (if
            // any) is captured by that method's own existing
            // distress/bankruptcy logic rather than needing to be
            // duplicated here.
            IReadOnlyList<LeaseDelivery> leaseDeliveries;
            (leaseRequestBoard, leaseDeliveries) = LeaseAllocationResolver.ResolveQuarter(leaseRequestBoard, q, year, leaseAllocationRng);
            foreach (var leaseDelivery in leaseDeliveries)
            {
                fleets[leaseDelivery.Airline.Name] = FleetLedger.ApplyDelivery(fleets[leaseDelivery.Airline.Name], leaseDelivery.Type, leaseDelivery.Quantity, FleetOwnershipMode.Leased);
                finances[leaseDelivery.Airline.Name] = finances[leaseDelivery.Airline.Name] with { CashBalance = finances[leaseDelivery.Airline.Name].CashBalance - leaseDelivery.UpfrontCost };
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
            // BalancedGrowthStrategy follow-up: same idea, one level up -
            // which airlines touch which region at all, computed once per
            // quarter alongside the pair-level map above (same source
            // data, routesAtQuarterStart, so this costs nothing extra to
            // build together).
            var touchingAirlineNamesByRegion = new Dictionary<Region, HashSet<string>>();
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

                    foreach (var region in new[] { route.Origin.Region, route.Destination.Region })
                    {
                        if (!touchingAirlineNamesByRegion.TryGetValue(region, out var touchingNames))
                        {
                            touchingNames = new HashSet<string>(StringComparer.Ordinal);
                            touchingAirlineNamesByRegion[region] = touchingNames;
                        }

                        touchingNames.Add(n);
                    }
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
                    routeQuarters += rivalCountByServedRoute.Count;
                    contestedRouteQuarters += rivalCountByServedRoute.Values.Count(v => v > 0);

                    // BalancedGrowthStrategy follow-up: the global
                    // counterparts - every pair/region *any* airline
                    // touches, not just this one's own served routes,
                    // each still excluding this airline itself from its
                    // own rival count.
                    var rivalPresenceByPairKey = servingAirlineNamesByPairKey.ToDictionary(
                        kv => kv.Key,
                        kv => kv.Value.Count - (kv.Value.Contains(name) ? 1 : 0));
                    var rivalPresenceByRegion = touchingAirlineNamesByRegion.ToDictionary(
                        kv => kv.Key,
                        kv => kv.Value.Count - (kv.Value.Contains(name) ? 1 : 0));

                    // Geopolitical-aircraft-restrictions follow-up: the
                    // exact same query a UI layer would use to show this
                    // airline's own home city only real options, computed
                    // once here and threaded both into the AI's own
                    // StrategyContext below and into QuarterEngine's own
                    // enforcement gate - neither can ever drift from the
                    // other, since both read the same TradeRestriction data.
                    var purchasableTypes = Timeline.AvailableForPurchase(availableFleet, seatByName[name].StartingCity, year, tradeRestrictions);
                    var leasableTypes = Timeline.AvailableForLease(availableFleet, seatByName[name].StartingCity, year, tradeRestrictions);

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
                        RouteHistoryByPair: routeHistoryByAirlineName[name],
                        InDistress: finances[name].InDistress,
                        RivalPresenceByPairKey: rivalPresenceByPairKey,
                        RivalPresenceByRegion: rivalPresenceByRegion,
                        // Win-condition-awareness follow-up (docs/OPEN_WORK.md):
                        // this real loop never actually populated this field -
                        // AeroNext.Sim's own M13 demo loop did, but this is the
                        // harness every real sweep runs on, so the mechanism
                        // it feeds (AiToolkit.RegionsNotYetLed) has been dead
                        // in every measurement that ever ran here. dominanceState
                        // still holds last quarter's fully-resolved value at
                        // this point in the loop (only advanced later, once
                        // this quarter's market resolves), giving the same
                        // one-quarter lag every other real-competitive field
                        // here already has.
                        RegionsLedLastQuarter: DominanceTrackedRegions.All.Where(r => dominanceState.StreakFor(name, r) >= 1).ToHashSet(),
                        TargetRegionCountOverride: regionCounts.TargetRegionCount,
                        PurchasableTypes: purchasableTypes,
                        LeasableTypes: leasableTypes);

                    // Bankruptcy-conditions follow-up: SelectStrategy's own
                    // year-boundary rotation keeps advancing regardless of
                    // distress - normalStrategy is what gets persisted, so
                    // the rotation resumes exactly where it would have been
                    // the moment distress clears. DistressStrategy overrides
                    // only which one actually runs *this* quarter - see its
                    // own class doc comment.
                    var normalStrategy = BaselineAi.SelectStrategy(currentStrategyByName[name], q, strategyContext);
                    currentStrategyByName[name] = normalStrategy;
                    var activeStrategy = strategyContext.InDistress ? DistressStrategy.Instance : normalStrategy;
                    orders = activeStrategy.DecideOrders(strategyContext);

                    if (activeStrategy is NetworkOptimizerStrategy)
                    {
                        networkOptimizerQuarters++;
                    }
                    else if (activeStrategy is BalancedGrowthStrategy)
                    {
                        balancedGrowthQuarters++;
                    }
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
                    pendingNegotiationCityIds: q == 1 ? null : pendingNegotiationCityIds,
                    tradeRestrictions: q == 1 ? null : tradeRestrictions);

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

                // AI-type-swap follow-up (docs/AI_AIRCRAFT_TYPE_SWAP_DESIGN.md):
                // stamp LastTypeSwapQuarter the same way, from every
                // applied AdjustRouteOrder - regardless of source (AI or,
                // once a real UI exists, a player), matching
                // LastShrunkQuarter/LastClosedQuarter's own "built fresh
                // from real applied state" principle above.
                foreach (var swapOrder in result.AppliedOrders.OfType<AdjustRouteOrder>())
                {
                    var pairKey = QuarterEngine.NormalizedPairKey(swapOrder.Origin, swapOrder.Destination);
                    var existingHistory = routeHistoryByAirlineName[name].GetValueOrDefault(pairKey) ?? new AiRouteHistory(null, null, 0);

                    routeHistoryByAirlineName[name][pairKey] = existingHistory with { LastTypeSwapQuarter = q };
                    typeSwapsApplied++;
                }

                multiStopRouteOrdersApplied += result.AppliedOrders.OfType<RouteOrder>().Count(o => !o.Stops.IsEmpty);

                routes[name] = result.Routes;
                caps[name] = result.ActionCap;
                fleets[name] = result.UpdatedFleet!;
                appliedOrdersByName[name] = result.AppliedOrders;
                routeCampaignsApplied += result.AppliedOrders.OfType<RouteMarketingOrder>().Count();
                networkCampaignsApplied += result.AppliedOrders.OfType<NetworkMarketingOrder>().Count();
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

                foreach (var leaseOrder in result.AppliedOrders.OfType<LeaseAircraftOrder>())
                {
                    leaseRequestBoard = LeaseAllocationResolver.Submit(leaseRequestBoard, airlineSnapshot, leaseOrder, q);
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

                var (nextState, statement) = FinancialResolver.Resolve(finances[name], airline, market, appliedOrdersByName[name], fleet: fleets[name], retiredUnits: retiredUnitsByName[name], currentActionCap: capsAtQuarterStart[name]);
                finances[name] = nextState;

                if (q <= 20)
                {
                    negotiationSpendCentsFirst20QByName[name] += statement.NegotiationBidCost.Cents;
                    negotiationBidCountFirst20QByName[name] += appliedOrdersByName[name].OfType<NegotiationOrder>().Count();
                }

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

            var checkedWinner = WinConditionChecker.CheckWinner(dominanceState, lastEligibleNames, regionCounts.RequiredRegionCount);
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

        var winnerRegionsDominated = winner is null ? 0 : DominanceTrackedRegions.All.Count(r =>
            dominanceState.StreakFor(winner, r) >= WinConditionChecker.RequiredStreakQuarters);

        return new FullGameResult(
            gameIndex, configSeed, campaignSeed, humanCount, winQuarter ?? quarters, violations,
            finalCash, bankruptcyQuarter, homeRegionByName, startingCityIdByName, winner, winQuarter, finalRanking,
            winnerRegionsDominated, lastEligibleNames.Count,
            negotiationSpendCentsFirst20QByName, negotiationBidCountFirst20QByName, typeSwapsApplied,
            routeCampaignsApplied, networkCampaignsApplied, multiStopRouteOrdersApplied, networkOptimizerQuarters, balancedGrowthQuarters,
            routeQuarters, contestedRouteQuarters);
    }
}
