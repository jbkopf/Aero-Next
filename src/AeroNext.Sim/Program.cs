using AeroNext.Core.World;

// Headless sim entry point. Per CLAUDE.md's current phase: load cities, run
// scripted quarters, print results as text. No rendering, no input, no UI.
var dataPath = Path.Combine(AppContext.BaseDirectory, "data", "cities.json");
var cities = CityCatalog.LoadFile(dataPath);

Console.WriteLine($"Loaded {cities.Count} cities from {dataPath}");
Console.WriteLine();
Console.WriteLine($"{"Id",-5} {"Name",-16} {"Region",-13} {"Demand",6} {"Gateway",-7} {"Cap",3}");

foreach (var city in cities)
{
    var gateway = city.IsGateway ? "yes" : "no";
    var cap = city.GateCapacity?.ToString() ?? "-";
    Console.WriteLine($"{city.Id,-5} {city.Name,-16} {city.Region,-13} {city.DemandWeight,6} {gateway,-7} {cap,3}");
}
