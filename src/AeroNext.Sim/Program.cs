using AeroNext.Core;
using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Alliances;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
using AeroNext.Core.Fleet;
using AeroNext.Core.Negotiations;
using AeroNext.Core.Setup;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

// Headless sim entry point. Per CLAUDE.md's current phase: load cities, run
// scripted quarters, print results as text. No rendering, no input, no UI.
var dataPath = Path.Combine(AppContext.BaseDirectory, "data", "cities.json");
var cities = CityCatalog.LoadFile(dataPath);
var byId = cities.ToDictionary(c => c.Id);

Console.WriteLine($"Loaded {cities.Count} cities from {dataPath}");
Console.WriteLine();
Console.WriteLine($"{"Id",-5} {"Name",-16} {"Region",-13} {"Demand",6} {"Gateway",-7} {"Cap",3}");

foreach (var city in cities)
{
    var gateway = city.IsGateway ? "yes" : "no";
    var cap = city.GateCapacity?.ToString() ?? "-";
    Console.WriteLine($"{city.Id,-5} {city.Name,-16} {city.Region,-13} {city.DemandWeight,6} {gateway,-7} {cap,3}");
}

var demandModel = new GravityDemandModel();
var allPairs = demandModel.AllPairs(cities);

Console.WriteLine();
Console.WriteLine("Top 15 city-pair markets by quarterly demand:");
Console.WriteLine($"{"Origin",-6} {"Destination",-16} {"Dist (km)",10} {"Pax/qtr",12}");

foreach (var pair in allPairs.OrderByDescending(p => p.PassengersPerQuarter).Take(15))
{
    Console.WriteLine($"{pair.Origin.Id,-6} {pair.Destination.Id,-16} {pair.DistanceKm,10:N0} {pair.PassengersPerQuarter,12:N0}");
}

Console.WriteLine();
Console.WriteLine("Bottom 5 city-pair markets by quarterly demand (chokepoints, mostly):");

foreach (var pair in allPairs.OrderBy(p => p.PassengersPerQuarter).Take(5))
{
    Console.WriteLine($"{pair.Origin.Id,-6} {pair.Destination.Id,-16} {pair.DistanceKm,10:N0} {pair.PassengersPerQuarter,12:N0}");
}

// --- M3: single airline, nonstop routes ---
// M11 range/cost/rate: chosen generously enough that every route already
// flown by these two types in the M3-M8 demos below (including JFK-HND at
// ~10,800 km) stays flyable — M11 adds the range constraint without
// retroactively invalidating milestones that already recorded specific
// numbers. Boeing 747's rate is pinned at 1 specifically to preserve M10's
// documented "Alpha wins quarter 2, Beta wins quarter 3" outcome.
var narrowbody = new AircraftType("707", "Boeing 707", Seats: 140, RangeKm: 12_000, Cost: Money.FromDollars(5_000_000), ProductionRatePerQuarter: 6);
var regional = new AircraftType("dc3", "Douglas DC-3", Seats: 28, RangeKm: 2_400, Cost: Money.FromDollars(150_000), ProductionRatePerQuarter: 8);

var airline = new Airline("Trans-Aero", new[]
{
    new Route(byId["jfk"], byId["ord"], narrowbody, AircraftCount: 1),  // dense short-haul
    new Route(byId["jfk"], byId["lax"], narrowbody, AircraftCount: 1),  // transcon
    new Route(byId["jfk"], byId["lhr"], narrowbody, AircraftCount: 1),  // transatlantic gateway
    new Route(byId["jfk"], byId["yqx"], regional, AircraftCount: 1),    // Gander refueling stop
});

var network = RouteEvaluator.EvaluateNetwork(airline.Routes, demandModel);

Console.WriteLine();
Console.WriteLine($"--- {airline.Name}: nonstop network ---");
Console.WriteLine($"{"Route",-14} {"Aircraft",-14} {"Seats/qtr",10} {"Demand/qtr",11} {"Carried",10} {"LF",6}");

foreach (var load in network)
{
    var routeLabel = $"{load.Route.Origin.Id}-{load.Route.Destination.Id}";
    Console.WriteLine(
        $"{routeLabel,-14} {load.Route.Aircraft.Name,-14} {load.SeatsPerQuarter,10:N0} {load.DemandPassengers,11:N0} " +
        $"{load.PassengersCarried,10:N0} {load.LoadFactor,6:P0}");
}

// --- M4: connecting itineraries — the go/no-go gate ---
// Same route budget (5 routes, same aircraft, same frequency) spent two
// ways: a hub reaching every home region vs. the strongest same-size
// network obtainable within one home region (NYC-LA-Chicago is the densest
// triangle in the dataset — a deliberately hard opponent, not an average
// one, since "cannot fund a win" should hold even for the best regional
// play). Tests two things:
//   1. Does one-stop itinerary search capture demand a nonstop-only
//      reading of the same routes can't? (the milestone's core question)
//   2. Does the hub's total captured demand approach/beat the regional
//      network's? (the M2 open question about intercontinental pools)
const int spokeAircraftCount = 1;

Route Spoke(string originId, string destId) => new(byId[originId], byId[destId], narrowbody, spokeAircraftCount);

var hubRoutes = new[]
{
    Spoke("jfk", "ord"), // North America
    Spoke("jfk", "lax"), // North America
    Spoke("jfk", "lhr"), // Europe
    Spoke("jfk", "hnd"), // Asia
    Spoke("jfk", "gru"), // South America
};

var regionalRoutes = new[]
{
    Spoke("jfk", "ord"),
    Spoke("jfk", "lax"),
    Spoke("jfk", "mia"),
    Spoke("ord", "yyz"),
    Spoke("lax", "sfo"),
};

var hubNonstopOnly = RouteEvaluator.EvaluateNetwork(hubRoutes, demandModel).Sum(l => l.PassengersCarried);
var hubPlan = NetworkPlanner.Plan(hubRoutes, cities, demandModel);
var regionalPlan = NetworkPlanner.Plan(regionalRoutes, cities, demandModel);

Console.WriteLine();
Console.WriteLine("--- M4: hub-and-spoke vs. regional-only (same 5-route, same-aircraft budget) ---");
Console.WriteLine($"Hub network, nonstop-only reading:     {hubNonstopOnly,12:N0} pax/qtr");
Console.WriteLine($"Hub network, nonstop + one-stop:        {hubPlan.TotalCarried,12:N0} pax/qtr" +
    $"  ({hubPlan.NonstopCarried:N0} nonstop + {hubPlan.OneStopCarried:N0} one-stop)");
Console.WriteLine($"Regional-only network, nonstop + one-stop: {regionalPlan.TotalCarried,9:N0} pax/qtr" +
    $"  ({regionalPlan.NonstopCarried:N0} nonstop + {regionalPlan.OneStopCarried:N0} one-stop)");

Console.WriteLine();
Console.WriteLine("Top one-stop itineraries captured by the hub network:");
Console.WriteLine($"{"Route",-18} {"Via",5} {"Demand/qtr",11} {"Carried",10}");

foreach (var load in hubPlan.Itineraries
    .Where(l => l.Itinerary.Kind == ItineraryKind.OneStop)
    .OrderByDescending(l => l.PassengersCarried)
    .Take(5))
{
    var hubId = load.Itinerary.Legs[0].Origin.Id == load.Itinerary.Origin.Id
        ? load.Itinerary.Legs[0].Destination.Id
        : load.Itinerary.Legs[0].Origin.Id;
    var label = $"{load.Itinerary.Origin.Id}-{load.Itinerary.Destination.Id}";
    Console.WriteLine($"{label,-18} {hubId,5} {load.DemandPassengers,11:N0} {load.PassengersCarried,10:N0}");
}

Console.WriteLine();
Console.WriteLine($"Hub reaches {hubPlan.TotalCarried * 100.0 / regionalPlan.TotalCarried:F0}% of the strongest regional network's " +
    "captured demand, in single-airline isolation.");
Console.WriteLine("Full closure expects M6's multi-airline competition: no single carrier monopolizes NYC-LA-Chicago in the real game.");

// --- M5: turn loop and simultaneous resolution skeleton ---
// --- M7: action economy — 3-action budget, raised by hiring executives ---
// Briefing -> Decisions -> Resolution. Each quarter's orders are collected
// as a batch and handed to QuarterEngine all at once — even with a single
// airline, this is the "collect all orders, then resolve" shape M9+'s
// multi-airline simultaneity builds on, not something to retrofit later.
var quarterlyOrders = new List<IReadOnlyList<QuarterOrder>>
{
    new QuarterOrder[] // Q1: found the hub (2 of 3 actions — entering a new pair costs 1)
    {
        new RouteOrder(byId["jfk"], byId["ord"], narrowbody),
        new RouteOrder(byId["jfk"], byId["lhr"], narrowbody),
    },
    new QuarterOrder[] // Q2: extend to Asia, and hire an executive (free — raises next quarter's cap)
    {
        new RouteOrder(byId["jfk"], byId["hnd"], narrowbody),
        new HireExecutiveOrder(),
    },
    Array.Empty<QuarterOrder>(), // Q3: quiet quarter, nothing ordered
    new QuarterOrder[] // Q4: cap is now 4 — 4 new routes fit, a 5th gets rejected over budget
    {
        new RouteOrder(byId["jfk"], byId["gru"], narrowbody),
        new RouteOrder(byId["jfk"], byId["cai"], narrowbody),
        new RouteOrder(byId["jfk"], byId["mia"], narrowbody),
        new RouteOrder(byId["jfk"], byId["lax"], narrowbody),
        new RouteOrder(byId["jfk"], byId["sfo"], narrowbody), // over budget
    },
    new QuarterOrder[] // Q5: assigning a 2nd aircraft to an existing pair (M32 — costs an action same as a new pair, no longer free)
    {
        new RouteOrder(byId["jfk"], byId["ord"], narrowbody),
    },
};

static string DescribeOrder(QuarterOrder order) => order switch
{
    RouteOrder ro => $"{ro.Origin.Id}-{ro.Destination.Id} ({ro.Aircraft.Name})",
    HireExecutiveOrder => "hire executive",
    NegotiationOrder no => $"bid {no.Bid} @ {no.TargetCity.Id}",
    AircraftOrder ao => $"order {ao.Quantity}x {ao.Type.Name}",
    LeaseAircraftOrder lo => $"lease {lo.Quantity}x {lo.Type.Name}",
    AllianceCourtOrder co => $"court {co.Target.Name}",
    _ => order.GetType().Name,
};

Console.WriteLine();
Console.WriteLine("--- M5/M7: scripted quarter loop with action budget ---");

QuarterResult? previousQuarter = null;
IReadOnlyList<Route> routes = Array.Empty<Route>();
var actionCap = QuarterEngine.BaseActionCap;

for (var q = 0; q < quarterlyOrders.Count; q++)
{
    var orders = quarterlyOrders[q];
    var quarter = QuarterEngine.RunQuarter(q + 1, routes, actionCap, orders, cities, demandModel);
    var briefing = BriefingGenerator.Generate(previousQuarter, quarter);

    Console.WriteLine();
    Console.WriteLine($"=== Quarter {quarter.QuarterNumber} (cap: {actionCap}) ===");
    Console.WriteLine(orders.Count == 0
        ? "Orders: none"
        : $"Orders: {string.Join(", ", orders.Select(DescribeOrder))}");

    if (briefing.Count == 0)
    {
        Console.WriteLine("Briefing: no exceptions. Nothing spent, nothing changed — skip ahead?");
    }
    else
    {
        Console.WriteLine("Briefing:");
        foreach (var item in briefing)
        {
            Console.WriteLine($"  - [{item.Kind}] {item.Message}");
        }
    }

    Console.WriteLine($"Resolution: {quarter.Routes.Count} routes, {quarter.Plan.TotalCarried:N0} pax/qtr carried " +
        $"({quarter.Plan.NonstopCarried:N0} nonstop + {quarter.Plan.OneStopCarried:N0} one-stop)");

    previousQuarter = quarter;
    routes = quarter.Routes;
    actionCap = quarter.ActionCap;
}

// --- M6: multiple airlines, shared demand pool ---
// A thin route where raw capacity comfortably exceeds demand for a single
// operator — the "demand-constrained" regime where competition actually has
// something to redistribute (a busy route like JFK-ORD is capacity-
// constrained for any realistic small fleet, so nobody's share would move).
var baselineA = new Airline("Trans-Aero", new[] { new Route(byId["jfk"], byId["yqx"], regional, AircraftCount: 1) });
var boostedA = new Airline("Trans-Aero", new[] { new Route(byId["jfk"], byId["yqx"], regional, AircraftCount: 3) });
var rivalB = new Airline("Northern Cross", new[] { new Route(byId["jfk"], byId["yqx"], regional, AircraftCount: 1) });

var beforeBoost = CompetitiveMarket.Resolve(new[] { baselineA, rivalB }, cities, demandModel);
var afterBoost = CompetitiveMarket.Resolve(new[] { boostedA, rivalB }, cities, demandModel);

Console.WriteLine();
Console.WriteLine("--- M6: shared demand pool, JFK-YQX (Gander refueling stop) ---");
Console.WriteLine($"Before: Trans-Aero 1 aircraft vs Northern Cross 1 aircraft");
Console.WriteLine($"  Trans-Aero carried:    {beforeBoost.TotalCarried(baselineA),6:N0}");
Console.WriteLine($"  Northern Cross carried: {beforeBoost.TotalCarried(rivalB),6:N0}");
Console.WriteLine($"After: Trans-Aero boosts to 3 aircraft, Northern Cross unchanged");
Console.WriteLine($"  Trans-Aero carried:    {afterBoost.TotalCarried(boostedA),6:N0}");
Console.WriteLine($"  Northern Cross carried: {afterBoost.TotalCarried(rivalB),6:N0}" +
    $"  (was {beforeBoost.TotalCarried(rivalB):N0} — eroded by Trans-Aero's added capacity, not by anything Northern Cross did)");

// Finishes M4's conditional pass: does the hub beat regional once the
// regional triangle is actually contested by multiple airlines, instead of
// tested in single-airline isolation?
var hubAirline = new Airline("Trans-Aero", hubRoutes);
var regional1 = new Airline("Liberty Air", regionalRoutes);
var regional2 = new Airline("Continental Domestic", regionalRoutes);
var regional3 = new Airline("Eagle National", regionalRoutes);

foreach (var regionalCompetitors in new[]
{
    new[] { regional1, regional2 },
    new[] { regional1, regional2, regional3 },
})
{
    var contestants = new[] { hubAirline }.Concat(regionalCompetitors).ToArray();
    var contested = CompetitiveMarket.Resolve(contestants, cities, demandModel);

    Console.WriteLine();
    Console.WriteLine($"--- M6: hub vs. {regionalCompetitors.Length} airlines contesting the same regional triangle ---");
    Console.WriteLine($"Hub (Trans-Aero) total carried:  {contested.TotalCarried(hubAirline),9:N0}  (was {hubPlan.TotalCarried:N0} in isolation)");
    foreach (var competitor in regionalCompetitors)
    {
        Console.WriteLine($"{competitor.Name,-33} carried: {contested.TotalCarried(competitor),9:N0}  (was {regionalPlan.TotalCarried:N0} in isolation)");
    }
    Console.WriteLine(contested.TotalCarried(hubAirline) > contested.TotalCarried(regional1)
        ? "Verdict: hub beats each regional competitor once the triangle is this contested."
        : "Verdict: hub still trails even under this much contest.");
}

// --- M8: baseline AI opponent ---
// CLAUDE.md: the game is always exactly 4 airlines. This runs 1 human-
// scripted seat alongside 3 AI seats, each independently resolved through
// QuarterEngine (its own routes, its own action cap, its own orders), then
// combined once per quarter through CompetitiveMarket — no hand-authored
// orders for the AI airlines at any point.
var leagueNames = new[] { "Trans-Aero (human)", "AI Corp Alpha", "AI Corp Beta", "AI Corp Gamma" };
var leagueRoutes = leagueNames.ToDictionary(n => n, _ => (IReadOnlyList<Route>)Array.Empty<Route>());
var leagueCaps = leagueNames.ToDictionary(n => n, _ => QuarterEngine.BaseActionCap);

var humanScript = new List<IReadOnlyList<QuarterOrder>>
{
    new QuarterOrder[] { new RouteOrder(byId["jfk"], byId["lhr"], narrowbody) },
    new QuarterOrder[] { new RouteOrder(byId["jfk"], byId["hnd"], narrowbody) },
    Array.Empty<QuarterOrder>(),
};

Console.WriteLine();
Console.WriteLine("--- M8: baseline AI opponent — 1 human seat + 3 AI seats, always 4 airlines ---");

// Illustrative only - RunQuarter below isn't called with fleet awareness in
// this standalone demo, so this is advisory input to BaselineAi's decision,
// not something actually consumed/enforced. A generous static pool keeps
// the AI seats from ever needing to lease across these few demo quarters.
var demoFleet = FleetLedger.ApplyDelivery(AirlineFleet.Empty, narrowbody, 3, FleetOwnershipMode.Owned);

for (var q = 0; q < humanScript.Count; q++)
{
    foreach (var name in leagueNames)
    {
        var orders = name == leagueNames[0]
            ? humanScript[q]
            : BaselineAi.DecideOrders(leagueRoutes[name], leagueCaps[name], cities, demandModel, new[] { narrowbody }, demoFleet);

        var result = QuarterEngine.RunQuarter(q + 1, leagueRoutes[name], leagueCaps[name], orders, cities, demandModel);

        leagueRoutes[name] = result.Routes;
        leagueCaps[name] = result.ActionCap;

        if (name != leagueNames[0])
        {
            Console.WriteLine($"  {name} orders: {string.Join(", ", orders.Select(DescribeOrder))}");
        }
    }

    var league = leagueNames.Select(n => new Airline(n, leagueRoutes[n])).ToArray();
    var standings = CompetitiveMarket.Resolve(league, cities, demandModel);

    Console.WriteLine($"Quarter {q + 1} standings:");
    foreach (var seat in league)
    {
        Console.WriteLine($"  {seat.Name,-20} {leagueRoutes[seat.Name].Count} routes, {standings.TotalCarried(seat),8:N0} pax/qtr");
    }
}

// --- M9: contested resolution — landing rights ---
// Silent-commitment bidding at a real gateway (Gander — a chokepoint with a
// capacity of 2). Submission and resolution timing use a fixed seed so this
// is reproducible; the actual resolve quarters aren't forced to coincide —
// "commit blind and learn late" means suitors don't know whether their bid
// will even face competition when it lands.
var gate = byId["yqx"];
var suitorNames = new[] { "Trans-Aero", "Northern Cross", "Liberty Air", "Continental Domestic" };
var suitorBids = new[]
{
    Money.FromDollars(3_000_000),
    Money.FromDollars(2_500_000),
    Money.FromDollars(2_000_000),
    Money.FromDollars(1_500_000),
};
var negotiationRng = new SimRng(seed: 7);

var landingBoard = LandingRightsBoard.Empty;
Console.WriteLine();
Console.WriteLine($"--- M9: landing rights at {gate.Id} (Gander) — gate capacity {gate.GateCapacity} ---");
Console.WriteLine("Quarter 1: all four suitors submit silently.");

for (var i = 0; i < suitorNames.Length; i++)
{
    var suitor = new Airline(suitorNames[i], Array.Empty<Route>());
    landingBoard = LandingRightsResolver.Submit(landingBoard, suitor, new NegotiationOrder(gate, suitorBids[i]), currentQuarter: 1, negotiationRng);
}

foreach (var pending in landingBoard.Pending.OrderBy(p => p.Airline.Name, StringComparer.Ordinal))
{
    Console.WriteLine($"  {pending.Airline.Name,-20} bid {pending.Bid} — resolves quarter {pending.ResolvesQuarter} (silent until then)");
}

for (var q = 2; q <= 6; q++)
{
    var (nextBoard, outcomes) = LandingRightsResolver.ResolveQuarter(landingBoard, q);
    landingBoard = nextBoard;

    if (outcomes.Count == 0)
    {
        continue;
    }

    Console.WriteLine($"Quarter {q}: {outcomes.Count} bid(s) resolve at {gate.Id}.");
    foreach (var outcome in outcomes)
    {
        Console.WriteLine($"  {outcome.Airline.Name,-20} {(outcome.Won ? "WON the slot" : "lost")} — standing now {outcome.StandingAfter:F2}");
    }
}

Console.WriteLine($"Gate slots granted: {landingBoard.SlotsGranted.GetValueOrDefault(gate.Id)} of {gate.GateCapacity}.");

// --- M10: contested resolution — aircraft orders and alliances ---
// Aircraft orders are PUBLIC (no secrecy, no random delay like landing
// rights) — delivery is purely a function of queue position and the
// production line's fixed rate.
var widebody = new AircraftType("747", "Boeing 747", Seats: 400, RangeKm: 13_000, Cost: Money.FromDollars(20_000_000), ProductionRatePerQuarter: 1);
var alpha = new Airline("Alpha Air", Array.Empty<Route>());
var beta = new Airline("Beta Airways", Array.Empty<Route>());
var gamma = new Airline("Gamma Skyways", Array.Empty<Route>());

var fleetBoard = AircraftOrderBoard.Empty;
Console.WriteLine();
Console.WriteLine("--- M10: aircraft order queue — Alpha and Beta both order a 747 in quarter 1 ---");
fleetBoard = AircraftOrderResolver.Submit(fleetBoard, alpha, new AircraftOrder(widebody, Quantity: 1), currentQuarter: 1);
fleetBoard = AircraftOrderResolver.Submit(fleetBoard, beta, new AircraftOrder(widebody, Quantity: 1), currentQuarter: 1);
fleetBoard = AircraftOrderResolver.Submit(fleetBoard, gamma, new AircraftOrder(narrowbody, Quantity: 2), currentQuarter: 1); // different type: independent queue

for (var q = 2; q <= 4; q++)
{
    var (nextFleetBoard, deliveries) = AircraftOrderResolver.ResolveQuarter(fleetBoard, q);
    fleetBoard = nextFleetBoard;

    Console.WriteLine($"Quarter {q}:");
    if (deliveries.Count == 0)
    {
        Console.WriteLine("  (no deliveries this quarter)");
    }
    foreach (var delivery in deliveries)
    {
        Console.WriteLine($"  {delivery.Airline.Name,-15} receives {delivery.Quantity}x {delivery.Type.Name}");
    }
}

var leaseDelivery = AircraftOrderResolver.ResolveLease(beta, new LeaseAircraftOrder(widebody, Quantity: 1), currentQuarter: 2);
Console.WriteLine($"Beta Airways leases instead of waiting: {leaseDelivery.Quantity}x {leaseDelivery.Type.Name} delivered quarter {leaseDelivery.DeliveredQuarter} — instant, at a premium.");

// Alliance courting — simultaneous, same-quarter, no queue, no money.
var regionalX = new RegionalCarrier("regional-x", "Regional Air X");
var courtOrders = new[]
{
    (alpha, new AllianceCourtOrder(regionalX)),
    (beta, new AllianceCourtOrder(regionalX)),
    (gamma, new AllianceCourtOrder(regionalX)),
};

var (allianceBoard, allianceOutcomes) = AllianceResolver.ResolveQuarter(AllianceBoard.Empty, courtOrders);

Console.WriteLine();
Console.WriteLine($"--- M10: alliance courting — 3 airlines court {regionalX.Name} ---");
foreach (var outcome in allianceOutcomes)
{
    Console.WriteLine($"  {outcome.Airline.Name,-15} {(outcome.Won ? "WINS the alliance" : "loses")}");
}

// A late suitor tries to court the same carrier next quarter — too late, it already joined.
var delta = new Airline("Delta Regional", Array.Empty<Route>(), Quality: 10.0); // huge quality edge, doesn't matter
var (_, lateOutcome) = AllianceResolver.ResolveQuarter(allianceBoard, new[] { (delta, new AllianceCourtOrder(regionalX)) });
Console.WriteLine($"  Later, {delta.Name} tries too: {(lateOutcome.Single().Won ? "wins" : "loses — already joined someone else, permanently")}");

// --- M11: fleet and range economics ---
// Real specs: an early piston airliner that historically needed Gander for
// the Atlantic crossing, versus the jet-age 707 that made that stop
// obsolete — the exact story CLAUDE.md's map design describes. DC-3's real
// range (~2,400 km) is too short even for the Gander-LHR leg, so a
// distinct early-era type is used here rather than stretching DC-3 into a
// role it doesn't fit.
var constellation = new AircraftType("l049", "Lockheed Constellation", Seats: 60, RangeKm: 4_200, Cost: Money.FromDollars(700_000), ProductionRatePerQuarter: 5);
var lhr = byId["lhr"];

Console.WriteLine();
Console.WriteLine("--- M11: range determines chokepoint viability ---");
Console.WriteLine($"JFK-LHR direct: {new Route(byId["jfk"], lhr, narrowbody, 1).DistanceKm:N0} km. " +
    $"Via {gate.Id}: {new Route(byId["jfk"], gate, narrowbody, 1).DistanceKm:N0} + {new Route(gate, lhr, narrowbody, 1).DistanceKm:N0} km.");

var earlyEraRoutes = new[]
{
    new Route(byId["jfk"], gate, constellation, AircraftCount: 1), // JFK-Gander
    new Route(gate, lhr, constellation, AircraftCount: 1),          // Gander-LHR
};
var earlyEraPlan = NetworkPlanner.Plan(earlyEraRoutes, cities, demandModel);
var earlyEraJfkLhr = earlyEraPlan.Itineraries.Single(i =>
    (i.Itinerary.Origin.Id == "jfk" && i.Itinerary.Destination.Id == "lhr") ||
    (i.Itinerary.Origin.Id == "lhr" && i.Itinerary.Destination.Id == "jfk"));
Console.WriteLine($"Early era (Constellation, range {constellation.RangeKm:N0} km): JFK-LHR resolves as {earlyEraJfkLhr.Itinerary.Kind} " +
    $"— {earlyEraJfkLhr.Itinerary.Legs.Count} leg(s), via {gate.Id}.");

var jetEraRoutes = new[] { new Route(byId["jfk"], lhr, narrowbody, AircraftCount: 1) };
var jetEraPlan = NetworkPlanner.Plan(jetEraRoutes, cities, demandModel);
var jetEraJfkLhr = jetEraPlan.Itineraries.Single(i => i.Itinerary.Origin.Id == "jfk" && i.Itinerary.Destination.Id == "lhr");
Console.WriteLine($"Jet era (Boeing 707, range {narrowbody.RangeKm:N0} km): JFK-LHR resolves as {jetEraJfkLhr.Itinerary.Kind} " +
    $"— {jetEraJfkLhr.Itinerary.Legs.Count} leg(s), {gate.Id} bypassed entirely.");

// A range-blind order attempt gets rejected with a clear reason, not a silently dead route.
var rejectedAttempt = QuarterEngine.RunQuarter(
    1, Array.Empty<Route>(), QuarterEngine.BaseActionCap,
    new QuarterOrder[] { new RouteOrder(byId["jfk"], lhr, constellation) },
    cities, demandModel);
var rejectionBriefing = BriefingGenerator.Generate(null, rejectedAttempt);
Console.WriteLine($"Ordering a Constellation direct on JFK-LHR: {rejectionBriefing.Single().Message}");

// --- M12: financial model and scoring ---
// "The scoring function, not the point" (CLAUDE.md) — replays the exact
// same M5/M7 hub-building script, now with a P&L tracked alongside it.
Console.WriteLine();
Console.WriteLine("--- M12: quarterly P&L, replaying the M5/M7 hub-building script ---");

var financeState = FinancialState.Start(Money.FromDollars(20_000_000), Money.FromDollars(10_000_000), interestRatePerQuarter: 0.02);
IReadOnlyList<Route> financeRoutes = Array.Empty<Route>();
var financeCap = QuarterEngine.BaseActionCap;
var soloAirlineName = "Trans-Aero";

for (var q = 0; q < quarterlyOrders.Count; q++)
{
    var quarter = QuarterEngine.RunQuarter(q + 1, financeRoutes, financeCap, quarterlyOrders[q], cities, demandModel);
    financeRoutes = quarter.Routes;
    financeCap = quarter.ActionCap;

    var solo = new Airline(soloAirlineName, financeRoutes);
    var market = CompetitiveMarket.Resolve(new[] { solo }, cities, demandModel);
    var (nextFinanceState, statement) = FinancialResolver.Resolve(financeState, solo, market, quarter.AppliedOrders);
    financeState = nextFinanceState;

    Console.WriteLine($"Q{q + 1}: revenue {statement.Revenue,14}  cost {statement.OperatingCost,14}  " +
        $"debt svc {statement.DebtService,10}  net {statement.NetIncome,14}  cash {statement.CashBalanceAfter,14}");
}

// A deliberately reckless operator: heavy debt, thin demand, capacity it can't fill.
Console.WriteLine();
Console.WriteLine("--- M12: bankruptcy trigger — reckless overexpansion on thin demand ---");

var recklessCities = new[] { byId["yqx"], byId["ter"], byId["awk"] }; // three of the thinnest chokepoints in the dataset
var recklessAirline = new Airline("Reckless Air", new[]
{
    new Route(recklessCities[0], recklessCities[1], widebody, AircraftCount: 14), // huge capacity, negligible demand
});
var recklessMarket = CompetitiveMarket.Resolve(new[] { recklessAirline }, cities, demandModel);
var recklessState = FinancialState.Start(Money.FromDollars(500_000), Money.FromDollars(5_000_000), interestRatePerQuarter: 0.05);

for (var q = 1; q <= 4 && !recklessState.IsBankrupt; q++)
{
    var (nextState, statement) = FinancialResolver.Resolve(recklessState, recklessAirline, recklessMarket, Array.Empty<QuarterOrder>());
    recklessState = nextState;
    Console.WriteLine($"Q{q}: revenue {statement.Revenue,10}  cost {statement.OperatingCost,14}  " +
        $"net {statement.NetIncome,14}  cash {statement.CashBalanceAfter,14}  bankrupt={statement.IsBankrupt}");
}

// --- M13: historical arc — the full ~240-quarter campaign, 1955-2015 ---
// Always 4 airlines, all AI-driven (0 human-scripted satisfies "at most 1").
Console.WriteLine();
Console.WriteLine("--- M13: full campaign, 1955-2015, 4 AI-driven airlines ---");

var fleetCatalog = AircraftCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "aircraft.json"));
var growthSchedule = DemandGrowthCatalog.LoadFile(Path.Combine(AppContext.BaseDirectory, "data", "demand_growth.json"));

const int campaignQuarters = 244; // 1955 Q1 through 2015 Q4
var campaignAirlineNames = new[] { "Atlas Air", "Meridian", "Skyward", "Continental Wings" };
var campaignRoutes = campaignAirlineNames.ToDictionary(n => n, _ => (IReadOnlyList<Route>)Array.Empty<Route>());
var campaignCaps = campaignAirlineNames.ToDictionary(n => n, _ => QuarterEngine.BaseActionCap);
var campaignFinances = campaignAirlineNames.ToDictionary(n => n, _ => FinancialState.Start(Money.FromDollars(20_000_000), Money.FromDollars(0), interestRatePerQuarter: 0.02));

// M13's campaign is deliberately flat/symmetric (no GameSetup/region
// distinction, matching its own established design) - all 4 airlines get
// the same smallest-range 1955-available type, at M18's same starting
// quantity (2), owned outright.
var campaignStartingAircraft = Timeline.AvailableAircraft(fleetCatalog, Timeline.StartYear)
    .OrderBy(a => a.RangeKm)
    .ThenBy(a => a.Id, StringComparer.Ordinal)
    .First();
var campaignFleets = campaignAirlineNames.ToDictionary(n => n, n => FleetLedger.ApplyDelivery(
    AirlineFleet.Empty, campaignStartingAircraft, StartingConditionsResolver.StartingAircraftQuantity, FleetOwnershipMode.Owned));
var campaignAircraftOrderBoard = AircraftOrderBoard.Empty;

// Reference chokepoint check: does the era's best available aircraft still
// need Gander for JFK-LHR, or can it fly direct?
string ChokepointStatus(int year)
{
    var best = Timeline.AvailableAircraft(fleetCatalog, year).OrderByDescending(a => a.RangeKm).First();
    var direct = new Route(byId["jfk"], byId["lhr"], best, 1).IsWithinRange;
    return direct ? $"{best.Name} flies JFK-LHR direct" : $"{best.Name} still needs a Gander stop";
}

// Re-measured after M31 (docs/BASELINE_AI_ROADMAP.md) taught BaselineAi
// to hire an executive every quarter, unconditionally - the action cap
// climbs by 1/quarter instead of sitting flat at 3 forever, and this AI
// spends that extra room opening a genuinely new route almost every
// quarter (1 route at quarter 1, up to 22 by quarter 22) instead of
// endlessly re-leasing onto the same 2 saturated ones (M34's own
// frozen-network shape). The result is a dramatically bigger climb -
// peak cash $42,113,450.14 at quarter 18, more than double M34's own
// peak - but also a dramatically sharper crash once route count outruns
// what the network can actually support: bankruptcy at quarter 22
// (1960, -$445,156.38), only slightly later than M34's quarter 17, not
// the multi-quarter runway the cash peak alone would suggest. Frozen at
// 22 routes/-$445,156.38 from there; carried pax keeps climbing after
// bankruptcy the same way M34's writeup already explained (NetworkPlanner
// still recomputes against real, growing demand regardless of financial
// bankruptcy). This is the expected, *necessary but not sufficient*
// result the roadmap doc itself predicts: M31 gives BaselineAi room to
// grow a real network, but nothing yet stops it from over-expanding past
// what its fares/costs can sustain - that's M39's job, still ahead.
//
// M35 (retire/close) re-measured on top of this and produced byte-for-
// byte identical figures - confirmed why, not just observed it:
// instrumented this exact loop and found CloseRouteOrder/RetireAircraftOrder
// apply zero times across the entire 22-quarter run. Idle units are 0
// every single quarter (BaselineAi always immediately uses or leases
// exactly what it needs, never over-acquires - M35's retire target never
// exists here), and every multi-aircraft route's load factor stays
// pinned at a perfect 1.000 the whole run (this loop's demand keeps
// growing fast enough that capacity never outpaces it - M35's close
// target never exists here either). Both mechanics are real and
// correctly built (verified directly against the QA harness's 150 real
// games instead, where they DO fire and meaningfully help - see M35's
// own "Done" writeup) - this loop's specific demand-rich environment
// just never triggers either one, the same kind of loop-specific
// insensitivity M31's own hiring change already showed in reverse (there
// it was the QA harness that didn't move; here it's this loop).
//
// M36 (debt tools) re-measured on top of this too, and stays a third
// consecutive byte-for-byte match here - no diagnostic needed this time,
// the cash figures already printed above make the reason plain: cash
// never once drops below ~$15,760,767 across all 22 quarters, nowhere
// near the $2,000,000 negotiation bid that triggers a borrow, and this
// loop's debt balance stays at exactly $0 throughout (nothing here ever
// borrows), so PayDownDebtOrder's own trigger (debt must be nonzero)
// never has anything to act on either. Both mechanics fire for real
// against the QA harness's 150 games instead - see M36's own "Done"
// writeup. Three milestones in a row now where this loop's specific
// demand-rich, cash-rich environment simply never exercises the newest
// mechanic, while the harder QA harness games do.
var checkpoints = new HashSet<int> { 1, 18, 22, 121, campaignQuarters };

var campaignDominance = RegionalDominanceState.Empty;
string? campaignWinner = null;
int? campaignWinQuarter = null;
CompetitiveMarketResult? campaignLastMarket = null;

for (var q = 1; q <= campaignQuarters; q++)
{
    var year = Timeline.YearForQuarter(q);
    var availableFleet = Timeline.AvailableAircraft(fleetCatalog, year);
    var yearDemandModel = new GravityDemandModel(globalMultiplier: Timeline.DemandMultiplierForYear(growthSchedule, year));
    var appliedOrdersByAirline = new Dictionary<string, IReadOnlyList<QuarterOrder>>();
    var retiredUnitsByAirline = new Dictionary<string, IReadOnlyList<FleetUnit>>();

    IReadOnlyList<AircraftDelivery> campaignDeliveries;
    (campaignAircraftOrderBoard, campaignDeliveries) = AircraftOrderResolver.ResolveQuarter(campaignAircraftOrderBoard, q);
    foreach (var delivery in campaignDeliveries)
    {
        campaignFleets[delivery.Airline.Name] = FleetLedger.ApplyDelivery(campaignFleets[delivery.Airline.Name], delivery, FleetOwnershipMode.Owned);
    }

    foreach (var name in campaignAirlineNames)
    {
        // A bankrupt airline is out: routes and action cap freeze at
        // whatever they were, rather than an insolvent airline
        // nonsensically continuing to grow forever.
        if (campaignFinances[name].IsBankrupt)
        {
            appliedOrdersByAirline[name] = Array.Empty<QuarterOrder>();
            retiredUnitsByAirline[name] = Array.Empty<FleetUnit>();
            continue;
        }

        // M33's gate-slot gating is deliberately left off (availableSlotsByCity: null,
        // both calls) — this loop never wires NegotiationOrders into
        // LandingRightsResolver at all (a pre-existing, already-documented
        // gap; see FullGameRunner.cs's own doc comment), so it never builds
        // a real LandingRightsBoard to compute a genuine balance from.
        // Gating on an always-empty board would block every gateway route
        // outright rather than leaving this demo loop's existing behavior
        // alone. M28's cash-gating on NegotiationOrder bids is NOT in the
        // same boat, though — this loop already tracks real per-airline
        // cash, so BaselineAi's negotiation step becomes genuinely
        // cash-aware here too.
        var orders = BaselineAi.DecideOrders(campaignRoutes[name], campaignCaps[name], cities, yearDemandModel, availableFleet, campaignFleets[name], availableSlotsByCity: null, availableCash: campaignFinances[name].CashBalance, currentDebtBalance: campaignFinances[name].DebtBalance);
        var result = QuarterEngine.RunQuarter(q, campaignRoutes[name], campaignCaps[name], orders, cities, yearDemandModel, campaignFleets[name], availableCash: campaignFinances[name].CashBalance, currentDebtBalance: campaignFinances[name].DebtBalance);
        campaignRoutes[name] = result.Routes;
        campaignCaps[name] = result.ActionCap;
        campaignFleets[name] = result.UpdatedFleet!;
        appliedOrdersByAirline[name] = result.AppliedOrders;
        retiredUnitsByAirline[name] = result.RetiredUnits!;

        var campaignAirlineSnapshot = new Airline(name, campaignRoutes[name]);
        foreach (var aircraftOrder in result.AppliedOrders.OfType<AircraftOrder>())
        {
            campaignAircraftOrderBoard = AircraftOrderResolver.Submit(campaignAircraftOrderBoard, campaignAirlineSnapshot, aircraftOrder, q);
        }
    }

    var campaignLeague = campaignAirlineNames.Select(n => new Airline(n, campaignRoutes[n])).ToArray();
    var campaignMarket = CompetitiveMarket.Resolve(campaignLeague, cities, yearDemandModel);

    foreach (var seat in campaignLeague)
    {
        var (nextState, _) = FinancialResolver.Resolve(campaignFinances[seat.Name], seat, campaignMarket, appliedOrdersByAirline[seat.Name], fleet: campaignFleets[seat.Name], retiredUnits: retiredUnitsByAirline[seat.Name]);
        campaignFinances[seat.Name] = nextState;
    }

    campaignLastMarket = campaignMarket;

    var campaignEligible = campaignAirlineNames.Where(n => !campaignFinances[n].IsBankrupt).ToHashSet(StringComparer.OrdinalIgnoreCase);
    campaignDominance = RegionalDominanceTracker.Advance(campaignDominance, campaignMarket, campaignEligible);

    var checkedCampaignWinner = WinConditionChecker.CheckWinner(campaignDominance);
    if (checkedCampaignWinner is not null)
    {
        campaignWinner = checkedCampaignWinner;
        campaignWinQuarter = q;
    }

    if (!checkpoints.Contains(q) && campaignWinner is null)
    {
        continue;
    }

    Console.WriteLine();
    Console.WriteLine($"Quarter {q} ({year}): {ChokepointStatus(year)}, fleet has {availableFleet.Count} aircraft type(s) available");
    foreach (var seat in campaignLeague)
    {
        var finance = campaignFinances[seat.Name];
        Console.WriteLine($"  {seat.Name,-18} {campaignRoutes[seat.Name].Count,3} routes, " +
            $"{campaignMarket.TotalCarried(seat),10:N0} pax/qtr, cash {finance.CashBalance,16}, bankrupt={finance.IsBankrupt}");
    }

    if (campaignWinner is not null)
    {
        Console.WriteLine($"  *** {campaignWinner} WINS: led {WinConditionChecker.RequiredRegionCount}+ regions for {WinConditionChecker.RequiredStreakQuarters}+ consecutive quarters, as of quarter {campaignWinQuarter} ***");
        break;
    }
}

if (campaignWinner is null)
{
    var campaignNetWorth = campaignAirlineNames.ToDictionary(n => n, n => campaignFinances[n].CashBalance - campaignFinances[n].DebtBalance);
    var campaignRanking = CampaignRanking.Rank(campaignDominance, campaignLastMarket!, campaignNetWorth);

    Console.WriteLine();
    Console.WriteLine("Timeline ended with no win-condition winner - final ranking:");
    for (var i = 0; i < campaignRanking.Count; i++)
    {
        var r = campaignRanking[i];
        Console.WriteLine($"  {i + 1}. {r.AirlineName,-18} regions led {r.RegionsLed}, passengers {r.TotalPassengersCarried,10:N0}, net worth {r.NetWorth}");
    }
}

// ---------------------------------------------------------------------
// M15 (Game Setup & Initial Conditions roadmap): home region model.
// Confirms Africa is excluded as a home region and prints each home
// region's eligible (non-gateway) starting cities, straight from the same
// cities.json every other demo section already loads above.
// ---------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== M15: Home regions and eligible starting cities ===");
Console.WriteLine($"Home regions: {string.Join(", ", HomeRegions.All)} (Africa excluded: {!HomeRegions.IsHomeRegion(Region.Africa)})");

foreach (var region in HomeRegions.All)
{
    var eligible = HomeRegions.EligibleStartingCities(region, cities);
    var names = string.Join(", ", eligible.Select(c => $"{c.Name} ({c.Id})"));
    Console.WriteLine($"  {region,-13} {eligible.Count} eligible: {names}");
}

// ---------------------------------------------------------------------
// M16 (Game Setup & Initial Conditions roadmap): regional economics and
// region-appropriate starting aircraft, at the 1955 campaign start.
// ---------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== M16: Regional economics and region-appropriate aircraft (1955) ===");

foreach (var region in HomeRegions.All)
{
    var factor = RegionalEconomics.Factor(region, cities);
    var appropriate = RegionalFleet.AppropriateAircraft(region, fleetCatalog, Timeline.StartYear);
    var options = string.Join(", ", appropriate.Select(a => $"{a.Name} ({a.Manufacturer})"));
    Console.WriteLine($"  {region,-13} economic factor {factor,5:F3}  starting aircraft: {options}");
}

// ---------------------------------------------------------------------
// M17 (Game Setup & Initial Conditions roadmap): player setup flow.
// GameSetup.CreateSeats is a pure function over structured input; this is
// the "ask the player" sequence AeroNext.Sim supplies on its behalf. Three
// configurations at the scope boundary: 0 humans, 1 human + 3 AI, 4 humans.
// ---------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== M17: Player setup flow ===");

void PrintSeats(string label, IReadOnlyList<HumanPlayerChoice> humanChoices)
{
    var seats = GameSetup.CreateSeats(humanChoices, cities);
    Console.WriteLine($"  {label}:");
    foreach (var seat in seats)
    {
        var role = seat.IsHuman ? "human" : "AI  ";
        Console.WriteLine($"    [{role}] {seat.AirlineName,-20} {seat.HomeRegion,-13} starting in {seat.StartingCity.Name} ({seat.StartingCity.Id})");
    }
}

PrintSeats("0 human players", Array.Empty<HumanPlayerChoice>());
PrintSeats("1 human + 3 AI", new[] { new HumanPlayerChoice("Founders Airways", "yyz") });
PrintSeats("4 human players", new[]
{
    new HumanPlayerChoice("Atlas Air", "ord"),
    new HumanPlayerChoice("Meridian", "cdg"),
    new HumanPlayerChoice("Skyward", "sin"),
    new HumanPlayerChoice("Continental Wings", "eze"),
});

// Regions are no longer exclusive - two humans can now share a home
// region, as long as their starting cities are distinct.
PrintSeats("2 humans sharing NorthAmerica", new[]
{
    new HumanPlayerChoice("Atlas Air", "ord"),
    new HumanPlayerChoice("Meridian", "sfo"), // also NorthAmerica - now allowed
});

try
{
    GameSetup.CreateSeats(new[]
    {
        new HumanPlayerChoice("Atlas Air", "ord"),
        new HumanPlayerChoice("Meridian", "ord"), // same city - still rejected
    }, cities);
}
catch (ArgumentException ex)
{
    Console.WriteLine($"  Rejected (two humans claiming the same city): {ex.Message}");
}

// ---------------------------------------------------------------------
// M18 (Game Setup & Initial Conditions roadmap): deriving starting cash,
// aircraft, and a guaranteed turn-1 route from each seat's starting city.
// Confirms the guaranteed route is actually accepted by the real
// QuarterEngine on quarter 1 — not just computed, but proven to work.
// ---------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== M18: Starting conditions and guaranteed turn-1 route ===");

var setupSeats = GameSetup.CreateSeats(new[]
{
    new HumanPlayerChoice("Atlas Air", "ord"),
    new HumanPlayerChoice("Meridian", "cdg"),
    new HumanPlayerChoice("Skyward", "sin"),
    new HumanPlayerChoice("Continental Wings", "eze"),
}, cities);

foreach (var seat in setupSeats)
{
    var conditions = StartingConditionsResolver.Resolve(seat, cities, fleetCatalog);
    Console.WriteLine($"  {seat.AirlineName,-18} cash {conditions.StartingCash,14}  " +
        $"{conditions.AircraftQuantity}x {conditions.StartingAircraft.Name,-20} -> {conditions.GuaranteedRouteDestination.Name}");

    var order = new RouteOrder(seat.StartingCity, conditions.GuaranteedRouteDestination, conditions.StartingAircraft);
    var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, cities, demandModel);
    Console.WriteLine($"    Quarter 1 route order: {(quarter1.RejectedOrders.Count == 0 ? "accepted" : "REJECTED")}");
}

// ---------------------------------------------------------------------
// M19 (Game Setup & Initial Conditions roadmap): the full chain, wired
// into one entry point, run through the real QuarterEngine ->
// CompetitiveMarket -> FinancialResolver pipeline (not a mock path) for
// the three configurations at the scope boundary. This is the roadmap's
// last milestone.
// ---------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("=== M19: Full setup-through-quarter-1 integration ===");

void RunSetupThroughQuarter1(string label, IReadOnlyList<HumanPlayerChoice> humanChoices)
{
    Console.WriteLine($"  --- {label} ---");

    var seats = GameSetup.CreateSeats(humanChoices, cities);

    var conditionsBySeat = seats.ToDictionary(s => s, s => StartingConditionsResolver.Resolve(s, cities, fleetCatalog));
    var routesBySeat = new Dictionary<AirlineSeat, IReadOnlyList<Route>>();
    var appliedOrdersBySeat = new Dictionary<AirlineSeat, IReadOnlyList<QuarterOrder>>();

    foreach (var seat in seats)
    {
        var conditions = conditionsBySeat[seat];
        var order = new RouteOrder(seat.StartingCity, conditions.GuaranteedRouteDestination, conditions.StartingAircraft);
        var quarter1 = QuarterEngine.RunQuarter(1, Array.Empty<Route>(), QuarterEngine.BaseActionCap, new[] { order }, cities, demandModel);

        routesBySeat[seat] = quarter1.Routes;
        appliedOrdersBySeat[seat] = quarter1.AppliedOrders;

        if (quarter1.RejectedOrders.Count > 0)
        {
            throw new InvalidOperationException($"{seat.AirlineName}'s guaranteed quarter-1 route was rejected — a real bug, not expected.");
        }
    }

    var league = seats.Select(s => new Airline(s.AirlineName, routesBySeat[s])).ToArray();
    var market = CompetitiveMarket.Resolve(league, cities, demandModel);

    foreach (var seat in seats)
    {
        var conditions = conditionsBySeat[seat];
        var airline = league.Single(a => a.Name == seat.AirlineName);
        var financeStart = FinancialState.Start(conditions.StartingCash, Money.FromDollars(10_000_000), interestRatePerQuarter: 0.02);
        var (_, statement) = FinancialResolver.Resolve(financeStart, airline, market, appliedOrdersBySeat[seat]);

        var role = seat.IsHuman ? "human" : "AI  ";
        Console.WriteLine($"    [{role}] {seat.AirlineName,-18} {seat.HomeRegion,-13} " +
            $"carried {market.TotalCarried(airline),8:N0} pax  net {statement.NetIncome,14}  cash {statement.CashBalanceAfter,14}  bankrupt={statement.IsBankrupt}");
    }
}

RunSetupThroughQuarter1("0 human players", Array.Empty<HumanPlayerChoice>());
RunSetupThroughQuarter1("1 human + 3 AI", new[] { new HumanPlayerChoice("Founders Airways", "yyz") });
RunSetupThroughQuarter1("4 human players", new[]
{
    new HumanPlayerChoice("Atlas Air", "ord"),
    new HumanPlayerChoice("Meridian", "cdg"),
    new HumanPlayerChoice("Skyward", "sin"),
    new HumanPlayerChoice("Continental Wings", "eze"),
});
