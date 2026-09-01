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
        var capacityBoard = CityCapacityBoard.Initialize(cities, new GravityDemandModel(schedule: growthSchedule, year: Timeline.StartYear));
        var dominanceState = RegionalDominanceState.Empty;
        string? winner = null;
        int? winQuarter = null;
        CompetitiveMarketResult? lastMarket = null;

        for (var q = 1; q <= quarters; q++)
        {
            var year = Timeline.YearForQuarter(q);
            var availableFleet = Timeline.AvailableAircraft(fleetCatalog, year);
            var yearDemandModel = new GravityDemandModel(schedule: growthSchedule, year: year); // M20: model resolves global (and any region/city-scoped) growth itself
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

            foreach (var name in names)
            {
                var wasBankruptEnteringQuarter = finances[name].IsBankrupt;

                if (wasBankruptEnteringQuarter)
                {
                    appliedOrdersByName[name] = Array.Empty<QuarterOrder>();
                    retiredUnitsByName[name] = Array.Empty<FleetUnit>();
                    continue;
                }

                var routesBefore = routes[name];
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
                    // single fixed heuristic.
                    //
                    // M41: also tells the strategy which gateways it
                    // already has a negotiation pending at, so it stops
                    // re-bidding the same one every quarter - this loop
                    // already builds a real landingBoard, so it's the one
                    // real call site that can populate this for real.
                    var pendingNegotiationCityIds = landingBoard.Pending
                        .Where(p => p.Airline.Name == name)
                        .Select(p => p.TargetCity.Id)
                        .ToHashSet(StringComparer.Ordinal);
                    var strategyContext = new StrategyContext(
                        routes[name], caps[name], cities, yearDemandModel, availableFleet, fleets[name], homeRegionByName[name],
                        HomeCity: seatByName[name].StartingCity,
                        AvailableSlotsByCity: availableSlotsByCity, AvailableCash: finances[name].CashBalance, CurrentDebtBalance: finances[name].DebtBalance,
                        PendingNegotiationCityIds: pendingNegotiationCityIds,
                        CityWideSlotsRemaining: cityWideSlotsRemaining,
                        IdleContestableSlotsByCity: idleContestableSlotsByCity);
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
                    cityWideSlotsRemaining: q == 1 ? null : cityWideSlotsRemaining);

                if (q == 1 && result.RejectedOrders.Count > 0)
                {
                    violations.Add(new GameViolation(q, name, "M18's guaranteed quarter-1 route was rejected — should never happen."));
                }

                routes[name] = result.Routes;
                caps[name] = result.ActionCap;
                fleets[name] = result.UpdatedFleet!;
                appliedOrdersByName[name] = result.AppliedOrders;
                retiredUnitsByName[name] = result.RetiredUnits!;

                var airlineSnapshot = new Airline(name, routes[name]);
                foreach (var negotiation in result.AppliedOrders.OfType<NegotiationOrder>())
                {
                    landingBoard = LandingRightsResolver.Submit(landingBoard, airlineSnapshot, negotiation, q, campaignRng);
                }

                foreach (var relinquish in result.AppliedOrders.OfType<RelinquishSlotOrder>())
                {
                    landingBoard = LandingRightsResolver.Relinquish(landingBoard, airlineSnapshot, relinquish.TargetCity);
                }

                foreach (var aircraftOrder in result.AppliedOrders.OfType<AircraftOrder>())
                {
                    aircraftOrderBoard = AircraftOrderResolver.Submit(aircraftOrderBoard, airlineSnapshot, aircraftOrder, q);
                }

                if (routesBefore.Count > result.Routes.Count)
                {
                    violations.Add(new GameViolation(q, name, "Route count decreased in a quarter with no bankruptcy — routes should never be removed."));
                }

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

            var eligibleNames = names.Where(n => !finances[n].IsBankrupt).ToHashSet(StringComparer.OrdinalIgnoreCase);
            dominanceState = RegionalDominanceTracker.Advance(dominanceState, market, eligibleNames);

            var checkedWinner = WinConditionChecker.CheckWinner(dominanceState);
            if (checkedWinner is not null)
            {
                winner = checkedWinner;
                winQuarter = q;
                break;
            }
        }

        var finalCash = names.ToDictionary(n => n, n => finances[n].CashBalance);
        var netWorth = names.ToDictionary(n => n, n => finances[n].CashBalance - finances[n].DebtBalance);
        var finalRanking = winner is null
            ? CampaignRanking.Rank(dominanceState, lastMarket!, netWorth).Select(r => r.AirlineName).ToList()
            : new List<string> { winner };

        return new FullGameResult(
            gameIndex, configSeed, campaignSeed, humanCount, winQuarter ?? quarters, violations,
            finalCash, bankruptcyQuarter, homeRegionByName, winner, winQuarter, finalRanking);
    }
}
