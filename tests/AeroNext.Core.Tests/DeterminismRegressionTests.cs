using System.Text;
using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Alliances;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
using AeroNext.Core.Fleet;
using AeroNext.Core.Negotiations;
using AeroNext.Core.Turns;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

/// <summary>
/// M14: formalizes what every earlier milestone assumed informally. Every
/// resolver in the sim (QuarterEngine, CompetitiveMarket, FinancialResolver,
/// LandingRightsResolver, AircraftOrderResolver, AllianceResolver,
/// BaselineAi) is already a pure function over its inputs — this doesn't
/// add a new capability, it proves the existing ones actually hold the
/// determinism rules together under one realistic, multi-system scenario,
/// not just in each resolver's own isolated tests.
///
/// The scenario deliberately exercises the one place randomness enters the
/// sim at all: LandingRightsResolver.Submit's SimRng-driven negotiation
/// delay. Everything else here is already deterministic by construction
/// (no RNG involved), so the meaningful claim isn't "nothing here is
/// random" — it's "the one random thing reproduces identically given the
/// same seed," which is the actual safety net multiplayer/online play
/// needs.
/// </summary>
public class DeterminismRegressionTests
{
    private const int Seed = 12345;
    private const int Quarters = 20;

    [Fact]
    public void FullScenario_SameSeed_ProducesByteIdenticalOutput()
    {
        var first = RunScenario(Seed);
        var second = RunScenario(Seed);

        Assert.Equal(first, second);
    }

    [Fact]
    public void FullScenario_DifferentSeed_CanProduceDifferentOutput()
    {
        // A control: if this ever fails, the scenario below stopped
        // exercising SimRng-sensitive behavior at all, which would make
        // the test above trivially true and no longer a meaningful check.
        var seedA = RunScenario(1);
        var seedB = RunScenario(2);

        Assert.NotEqual(seedA, seedB);
    }

    private static string RunScenario(int seed)
    {
        var cities = new[]
        {
            MakeCity("a", 0, 0, 100),
            MakeGateway("gw1", 10, 10, 90, gateCapacity: 1),
            MakeGateway("gw2", -10, -10, 70, gateCapacity: 1),
            MakeCity("c", 20, 20, 60),
            MakeCity("d", -20, 20, 40),
        };
        var fleet = new[]
        {
            new AircraftType("early", "Early", Seats: 60, RangeKm: 4000, ProductionRatePerQuarter: 3),
            new AircraftType("late", "Late", Seats: 200, RangeKm: 12000, ProductionRatePerQuarter: 1, AvailableFromYear: 1960),
        };
        var regionalCarrier = new RegionalCarrier("regional", "Regional Carrier");
        var airlineNames = new[] { "A", "B", "C", "D" };

        var rng = new SimRng(seed);
        var demandModel = new GravityDemandModel();
        var routes = airlineNames.ToDictionary(n => n, _ => (IReadOnlyList<Route>)Array.Empty<Route>());
        var caps = airlineNames.ToDictionary(n => n, _ => QuarterEngine.BaseActionCap);
        var finances = airlineNames.ToDictionary(n => n, _ => FinancialState.Start(Money.FromDollars(20_000_000), Money.FromDollars(5_000_000), 0.02));
        var landingBoard = LandingRightsBoard.Empty;
        var fleetBoard = AircraftOrderBoard.Empty;
        var allianceBoard = AllianceBoard.Empty;
        var currentStrategyByName = airlineNames.ToDictionary(n => n, n => (IAiStrategy?)null);

        var log = new StringBuilder();

        for (var q = 1; q <= Quarters; q++)
        {
            var year = Timeline.YearForQuarter(q);
            var availableFleet = Timeline.AvailableAircraft(fleet, year);
            var appliedByAirline = new Dictionary<string, IReadOnlyList<QuarterOrder>>();
            var courtOrdersThisQuarter = new List<(Airline Airline, AllianceCourtOrder Order)>();

            foreach (var name in airlineNames)
            {
                if (finances[name].IsBankrupt)
                {
                    appliedByAirline[name] = Array.Empty<QuarterOrder>();
                    continue;
                }

                // AirlineFleet.Empty every quarter, deliberately - this test
                // proves reproducibility of the resolver chain, not fleet
                // economics, so the AI always taking its lease-fallback path
                // is fine as long as it's identical across both runs. Routed
                // through the real StrategyContext/IAiStrategy path (the only
                // one any live game seat actually uses, post-M39) rather than
                // the now-deleted flat BaselineAi.DecideOrders - a single
                // shared HomeRegion/HomeCity ("a", Region.EastAsia) is fine
                // here since this scenario never checks per-airline home-city
                // legality, only that the whole resolver chain reproduces
                // identically given the same seed.
                var context = new StrategyContext(
                    routes[name], caps[name], cities, demandModel, availableFleet, AirlineFleet.Empty,
                    HomeRegion: Region.EastAsia, HomeCity: cities[0]);
                var strategy = BaselineAi.SelectStrategy(currentStrategyByName[name], q, context);
                currentStrategyByName[name] = strategy;
                var orders = new List<QuarterOrder>(strategy.DecideOrders(context));

                // Hand-scripted injections in early quarters, exercising the
                // two resolvers BaselineAi never drives on its own.
                if (q == 1 && name == "A")
                {
                    orders.Add(new AircraftOrder(fleet[0], Quantity: 1));
                }
                if (q == 2 && (name == "B" || name == "C"))
                {
                    orders.Add(new AllianceCourtOrder(regionalCarrier));
                }

                var result = QuarterEngine.RunQuarter(q, routes[name], caps[name], orders, cities, demandModel);
                routes[name] = result.Routes;
                caps[name] = result.ActionCap;
                appliedByAirline[name] = result.AppliedOrders;

                var airlineSnapshot = new Airline(name, routes[name]);

                foreach (var negotiation in result.AppliedOrders.OfType<NegotiationOrder>())
                {
                    landingBoard = LandingRightsResolver.Submit(landingBoard, airlineSnapshot, negotiation, q, rng);
                }

                foreach (var aircraftOrder in result.AppliedOrders.OfType<AircraftOrder>())
                {
                    fleetBoard = AircraftOrderResolver.Submit(fleetBoard, airlineSnapshot, aircraftOrder, q);
                }

                foreach (var courtOrder in result.AppliedOrders.OfType<AllianceCourtOrder>())
                {
                    courtOrdersThisQuarter.Add((airlineSnapshot, courtOrder));
                }
            }

            var league = airlineNames.Select(n => new Airline(n, routes[n])).ToArray();
            var market = CompetitiveMarket.Resolve(league, cities, demandModel);

            IReadOnlyList<NegotiationOutcome> negotiationOutcomes;
            (landingBoard, negotiationOutcomes) = LandingRightsResolver.ResolveQuarter(landingBoard, q);

            IReadOnlyList<AircraftDelivery> deliveries;
            (fleetBoard, deliveries) = AircraftOrderResolver.ResolveQuarter(fleetBoard, q);

            IReadOnlyList<AllianceOutcome> allianceOutcomes;
            (allianceBoard, allianceOutcomes) = AllianceResolver.ResolveQuarter(allianceBoard, courtOrdersThisQuarter);

            foreach (var seat in league)
            {
                var (nextState, statement) = FinancialResolver.Resolve(finances[seat.Name], seat, market, appliedByAirline[seat.Name]);
                finances[seat.Name] = nextState;

                log.AppendLine(
                    $"Q{q} {seat.Name}: routes={routes[seat.Name].Count} cap={caps[seat.Name]} " +
                    $"carried={market.TotalCarried(seat)} cash={statement.CashBalanceAfter.Cents} bankrupt={statement.IsBankrupt}");
            }

            foreach (var outcome in negotiationOutcomes)
            {
                log.AppendLine($"Q{q} negotiation: {outcome.Airline.Name}->{outcome.TargetCity.Id} won={outcome.Won} standing={outcome.StandingAfter:F4}");
            }

            foreach (var delivery in deliveries)
            {
                log.AppendLine($"Q{q} delivery: {delivery.Airline.Name} {delivery.Type.Id} x{delivery.Quantity}");
            }

            foreach (var outcome in allianceOutcomes)
            {
                log.AppendLine($"Q{q} alliance: {outcome.Airline.Name}->{outcome.Target.Id} won={outcome.Won}");
            }
        }

        return log.ToString();
    }

    private static City MakeCity(string id, double lat, double lon, int weight) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, false, null);

    private static City MakeGateway(string id, double lat, double lon, int weight, int gateCapacity) =>
        new(id, id.ToUpperInvariant(), Region.EastAsia, lat, lon, weight, true, gateCapacity);
}
