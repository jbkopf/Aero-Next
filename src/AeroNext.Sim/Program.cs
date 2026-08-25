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
    new Route(byId["jfk"], byId["lhr"], narrowbody, WeeklyRoundTrips: 3),  // transatlantic gateway: demand-constrained
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
