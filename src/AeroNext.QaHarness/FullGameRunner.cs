using AeroNext.Core;
using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
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
/// not BaselineAi's own pick — because BaselineAi has no concept of "home
/// region" or "starting city" at all (it searches every city pair globally,
/// independent of where an airline is based), so on an empty route list it
/// would otherwise make the identical global-best-pair choice for all 4
/// seats regardless of M15-M19's differentiated setup, same as before this
/// roadmap existed. From quarter 2 on, BaselineAi drives every seat
/// normally (matching the real M13 pattern) — divergence from there is a
/// genuine knock-on effect of each seat's different quarter-1 history, not
/// because the AI is itself region-aware. Worth knowing: this is the
/// current, honest limit of how far M15-M19's differentiation actually
/// reaches into an AI-driven game.
/// </summary>
public static class FullGameRunner
{
    private static readonly Money DefaultStartingDebt = Money.FromDollars(10_000_000);
    private const double DefaultInterestRatePerQuarter = 0.02;
    private const int GuaranteedRouteWeeklyRoundTrips = 3;

    /// <summary>Anything past this is almost certainly a sign-flip/overflow bug, not a plausible airline balance.</summary>
    private static readonly Money SaneMagnitudeBound = Money.FromDollars(1_000_000_000_000);

    public static FullGameResult Run(
        int gameIndex, int configSeed, int campaignSeed, int quarters,
        IReadOnlyList<City> cities, IReadOnlyList<AircraftType> fleetCatalog, IReadOnlyList<DemandGrowthPoint> growthSchedule)
    {
        var violations = new List<GameViolation>();
        var configRng = new SimRng(configSeed);
        var campaignRng = new SimRng(campaignSeed);

        var humanChoices = GameConfigGenerator.Generate(configRng, cities);
        var seats = GameSetup.CreateSeats(humanChoices, cities);
        var names = seats.Select(s => s.AirlineName).ToList();

        if (seats.Count != 4
            || seats.Select(s => s.HomeRegion).Distinct().Count() != 4
            || seats.Select(s => s.StartingCity.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
        {
            violations.Add(new GameViolation(0, "-", "GameSetup did not produce 4 seats with distinct regions/cities."));
        }

        var conditionsByName = seats.ToDictionary(s => s.AirlineName, s => StartingConditionsResolver.Resolve(s, cities, fleetCatalog));
        var seatByName = seats.ToDictionary(s => s.AirlineName);
        var routes = names.ToDictionary(n => n, _ => (IReadOnlyList<Route>)Array.Empty<Route>());
        var caps = names.ToDictionary(n => n, _ => QuarterEngine.BaseActionCap);
        var finances = names.ToDictionary(n => n, n => FinancialState.Start(conditionsByName[n].StartingCash, DefaultStartingDebt, DefaultInterestRatePerQuarter));
        var bankruptcyQuarter = names.ToDictionary(n => n, n => (int?)null);

        var landingBoard = LandingRightsBoard.Empty;

        for (var q = 1; q <= quarters; q++)
        {
            var year = Timeline.YearForQuarter(q);
            var availableFleet = Timeline.AvailableAircraft(fleetCatalog, year);
            var yearDemandModel = new GravityDemandModel(globalMultiplier: Timeline.DemandMultiplierForYear(growthSchedule, year));
            var appliedOrdersByName = new Dictionary<string, IReadOnlyList<QuarterOrder>>();

            // Snapshotted before any order processing this quarter, for
            // every seat — not just non-bankrupt ones — so the freeze check
            // below can catch a bankrupt seat's routes/cap changing even if
            // the skip-guard that's supposed to prevent that ever regresses
            // (this is the exact shape of the real M13 bug).
            var routesAtQuarterStart = names.ToDictionary(n => n, n => routes[n]);
            var capsAtQuarterStart = names.ToDictionary(n => n, n => caps[n]);

            foreach (var name in names)
            {
                var wasBankruptEnteringQuarter = finances[name].IsBankrupt;

                if (wasBankruptEnteringQuarter)
                {
                    appliedOrdersByName[name] = Array.Empty<QuarterOrder>();
                    continue;
                }

                var routesBefore = routes[name];
                var capBefore = caps[name];

                var orders = q == 1
                    ? new QuarterOrder[]
                    {
                        new RouteOrder(
                            seatByName[name].StartingCity,
                            conditionsByName[name].GuaranteedRouteDestination,
                            conditionsByName[name].StartingAircraft,
                            GuaranteedRouteWeeklyRoundTrips),
                    }
                    : BaselineAi.DecideOrders(routes[name], caps[name], cities, yearDemandModel, availableFleet).ToArray();

                var result = QuarterEngine.RunQuarter(q, routes[name], caps[name], orders, cities, yearDemandModel);

                if (q == 1 && result.RejectedOrders.Count > 0)
                {
                    violations.Add(new GameViolation(q, name, "M18's guaranteed quarter-1 route was rejected — should never happen."));
                }

                routes[name] = result.Routes;
                caps[name] = result.ActionCap;
                appliedOrdersByName[name] = result.AppliedOrders;

                var airlineSnapshot = new Airline(name, routes[name]);
                foreach (var negotiation in result.AppliedOrders.OfType<NegotiationOrder>())
                {
                    landingBoard = LandingRightsResolver.Submit(landingBoard, airlineSnapshot, negotiation, q, campaignRng);
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

            (landingBoard, _) = LandingRightsResolver.ResolveQuarter(landingBoard, q);

            var league = seats.Select(s => new Airline(s.AirlineName, routes[s.AirlineName])).ToArray();
            var market = CompetitiveMarket.Resolve(league, cities, yearDemandModel);

            foreach (var airline in league)
            {
                var name = airline.Name;
                var wasBankruptEnteringQuarter = finances[name].IsBankrupt;

                var (nextState, _) = FinancialResolver.Resolve(finances[name], airline, market, appliedOrdersByName[name]);
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
        }

        var finalCash = names.ToDictionary(n => n, n => finances[n].CashBalance);

        return new FullGameResult(gameIndex, configSeed, campaignSeed, humanChoices.Count, quarters, violations, finalCash, bankruptcyQuarter);
    }
}
