using AeroNext.Core;
using AeroNext.Core.Ai;
using AeroNext.Core.Airlines;
using AeroNext.Core.Alliances;
using AeroNext.Core.Competition;
using AeroNext.Core.Finance;
using AeroNext.Core.Fleet;
using AeroNext.Core.Negotiations;
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
    new Route(byId["jfk"], byId["ord"], narrowbody, WeeklyRoundTrips: 7),  // dense short-haul: capacity-constrained
    new Route(byId["jfk"], byId["lax"], narrowbody, WeeklyRoundTrips: 1),  // transcon: capacity-constrained
    new Route(byId["jfk"], byId["lhr"], narrowbody, WeeklyRoundTrips: 3),  // transatlantic gateway: capacity-constrained
    new Route(byId["jfk"], byId["yqx"], regional, WeeklyRoundTrips: 1),    // Gander refueling stop: demand-constrained
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
const int weeklyRoundTrips = 3;

Route Spoke(string originId, string destId) => new(byId[originId], byId[destId], narrowbody, weeklyRoundTrips);

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
        new RouteOrder(byId["jfk"], byId["ord"], narrowbody, WeeklyRoundTrips: 3),
        new RouteOrder(byId["jfk"], byId["lhr"], narrowbody, WeeklyRoundTrips: 3),
    },
    new QuarterOrder[] // Q2: extend to Asia, and hire an executive (free — raises next quarter's cap)
    {
        new RouteOrder(byId["jfk"], byId["hnd"], narrowbody, WeeklyRoundTrips: 3),
        new HireExecutiveOrder(),
    },
    Array.Empty<QuarterOrder>(), // Q3: quiet quarter, nothing ordered
    new QuarterOrder[] // Q4: cap is now 4 — 4 new routes fit, a 5th gets rejected over budget
    {
        new RouteOrder(byId["jfk"], byId["gru"], narrowbody, WeeklyRoundTrips: 3),
        new RouteOrder(byId["jfk"], byId["cai"], narrowbody, WeeklyRoundTrips: 3),
        new RouteOrder(byId["jfk"], byId["mia"], narrowbody, WeeklyRoundTrips: 3),
        new RouteOrder(byId["jfk"], byId["lax"], narrowbody, WeeklyRoundTrips: 3),
        new RouteOrder(byId["jfk"], byId["sfo"], narrowbody, WeeklyRoundTrips: 3), // over budget
    },
    new QuarterOrder[] // Q5: frequency bump on an existing pair is free — doesn't touch the budget
    {
        new RouteOrder(byId["jfk"], byId["ord"], narrowbody, WeeklyRoundTrips: 7),
    },
};

static string DescribeOrder(QuarterOrder order) => order switch
{
    RouteOrder ro => $"{ro.Origin.Id}-{ro.Destination.Id}@{ro.WeeklyRoundTrips}/wk",
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
var baselineA = new Airline("Trans-Aero", new[] { new Route(byId["jfk"], byId["yqx"], regional, WeeklyRoundTrips: 1) });
var boostedA = new Airline("Trans-Aero", new[] { new Route(byId["jfk"], byId["yqx"], regional, WeeklyRoundTrips: 3) });
var rivalB = new Airline("Northern Cross", new[] { new Route(byId["jfk"], byId["yqx"], regional, WeeklyRoundTrips: 1) });

var beforeBoost = CompetitiveMarket.Resolve(new[] { baselineA, rivalB }, cities, demandModel);
var afterBoost = CompetitiveMarket.Resolve(new[] { boostedA, rivalB }, cities, demandModel);

Console.WriteLine();
Console.WriteLine("--- M6: shared demand pool, JFK-YQX (Gander refueling stop) ---");
Console.WriteLine($"Before: Trans-Aero 1 rt/wk vs Northern Cross 1 rt/wk");
Console.WriteLine($"  Trans-Aero carried:    {beforeBoost.TotalCarried(baselineA),6:N0}");
Console.WriteLine($"  Northern Cross carried: {beforeBoost.TotalCarried(rivalB),6:N0}");
Console.WriteLine($"After: Trans-Aero boosts to 3 rt/wk, Northern Cross unchanged");
Console.WriteLine($"  Trans-Aero carried:    {afterBoost.TotalCarried(boostedA),6:N0}");
Console.WriteLine($"  Northern Cross carried: {afterBoost.TotalCarried(rivalB),6:N0}" +
    $"  (was {beforeBoost.TotalCarried(rivalB):N0} — eroded by Trans-Aero's added frequency, not by anything Northern Cross did)");

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
    new QuarterOrder[] { new RouteOrder(byId["jfk"], byId["lhr"], narrowbody, WeeklyRoundTrips: 3) },
    new QuarterOrder[] { new RouteOrder(byId["jfk"], byId["hnd"], narrowbody, WeeklyRoundTrips: 3) },
    Array.Empty<QuarterOrder>(),
};

Console.WriteLine();
Console.WriteLine("--- M8: baseline AI opponent — 1 human seat + 3 AI seats, always 4 airlines ---");

for (var q = 0; q < humanScript.Count; q++)
{
    foreach (var name in leagueNames)
    {
        var orders = name == leagueNames[0]
            ? humanScript[q]
            : BaselineAi.DecideOrders(leagueRoutes[name], leagueCaps[name], cities, demandModel, new[] { narrowbody });

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
    new Route(byId["jfk"], gate, constellation, WeeklyRoundTrips: 3), // JFK-Gander
    new Route(gate, lhr, constellation, WeeklyRoundTrips: 3),          // Gander-LHR
};
var earlyEraPlan = NetworkPlanner.Plan(earlyEraRoutes, cities, demandModel);
var earlyEraJfkLhr = earlyEraPlan.Itineraries.Single(i =>
    (i.Itinerary.Origin.Id == "jfk" && i.Itinerary.Destination.Id == "lhr") ||
    (i.Itinerary.Origin.Id == "lhr" && i.Itinerary.Destination.Id == "jfk"));
Console.WriteLine($"Early era (Constellation, range {constellation.RangeKm:N0} km): JFK-LHR resolves as {earlyEraJfkLhr.Itinerary.Kind} " +
    $"— {earlyEraJfkLhr.Itinerary.Legs.Count} leg(s), via {gate.Id}.");

var jetEraRoutes = new[] { new Route(byId["jfk"], lhr, narrowbody, WeeklyRoundTrips: 3) };
var jetEraPlan = NetworkPlanner.Plan(jetEraRoutes, cities, demandModel);
var jetEraJfkLhr = jetEraPlan.Itineraries.Single(i => i.Itinerary.Origin.Id == "jfk" && i.Itinerary.Destination.Id == "lhr");
Console.WriteLine($"Jet era (Boeing 707, range {narrowbody.RangeKm:N0} km): JFK-LHR resolves as {jetEraJfkLhr.Itinerary.Kind} " +
    $"— {jetEraJfkLhr.Itinerary.Legs.Count} leg(s), {gate.Id} bypassed entirely.");

// A range-blind order attempt gets rejected with a clear reason, not a silently dead route.
var rejectedAttempt = QuarterEngine.RunQuarter(
    1, Array.Empty<Route>(), QuarterEngine.BaseActionCap,
    new QuarterOrder[] { new RouteOrder(byId["jfk"], lhr, constellation, WeeklyRoundTrips: 3) },
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
    new Route(recklessCities[0], recklessCities[1], widebody, WeeklyRoundTrips: 14), // huge capacity, negligible demand
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
var campaignFinances = campaignAirlineNames.ToDictionary(n => n, _ => FinancialState.Start(Money.FromDollars(20_000_000), Money.FromDollars(10_000_000), interestRatePerQuarter: 0.02));

// Reference chokepoint check: does the era's best available aircraft still
// need Gander for JFK-LHR, or can it fly direct?
string ChokepointStatus(int year)
{
    var best = Timeline.AvailableAircraft(fleetCatalog, year).OrderByDescending(a => a.RangeKm).First();
    var direct = new Route(byId["jfk"], byId["lhr"], best, 1).IsWithinRange;
    return direct ? $"{best.Name} flies JFK-LHR direct" : $"{best.Name} still needs a Gander stop";
}

// Chosen to capture the actual arc found by running this: steady growth,
// a peak, rapid decline, and bankruptcy at quarter 22 - not evenly spaced.
var checkpoints = new HashSet<int> { 1, 10, 16, 20, 22, 41, 121, campaignQuarters };

for (var q = 1; q <= campaignQuarters; q++)
{
    var year = Timeline.YearForQuarter(q);
    var availableFleet = Timeline.AvailableAircraft(fleetCatalog, year);
    var yearDemandModel = new GravityDemandModel(globalMultiplier: Timeline.DemandMultiplierForYear(growthSchedule, year));
    var appliedOrdersByAirline = new Dictionary<string, IReadOnlyList<QuarterOrder>>();

    foreach (var name in campaignAirlineNames)
    {
        // A bankrupt airline is out: routes and action cap freeze at
        // whatever they were, rather than an insolvent airline
        // nonsensically continuing to grow forever.
        if (campaignFinances[name].IsBankrupt)
        {
            appliedOrdersByAirline[name] = Array.Empty<QuarterOrder>();
            continue;
        }

        var orders = BaselineAi.DecideOrders(campaignRoutes[name], campaignCaps[name], cities, yearDemandModel, availableFleet);
        var result = QuarterEngine.RunQuarter(q, campaignRoutes[name], campaignCaps[name], orders, cities, yearDemandModel);
        campaignRoutes[name] = result.Routes;
        campaignCaps[name] = result.ActionCap;
        appliedOrdersByAirline[name] = result.AppliedOrders;
    }

    var campaignLeague = campaignAirlineNames.Select(n => new Airline(n, campaignRoutes[n])).ToArray();
    var campaignMarket = CompetitiveMarket.Resolve(campaignLeague, cities, yearDemandModel);

    foreach (var seat in campaignLeague)
    {
        var (nextState, _) = FinancialResolver.Resolve(campaignFinances[seat.Name], seat, campaignMarket, appliedOrdersByAirline[seat.Name]);
        campaignFinances[seat.Name] = nextState;
    }

    if (!checkpoints.Contains(q))
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
}
