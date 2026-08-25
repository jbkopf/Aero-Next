using AeroNext.Core.Airlines;
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
var narrowbody = new AircraftType("707", "Boeing 707", Seats: 140);
var regional = new AircraftType("dc3", "Douglas DC-3", Seats: 28);

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
