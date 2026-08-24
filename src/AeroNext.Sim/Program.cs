using AeroNext.Core;

// Headless sim entry point. Per CLAUDE.md's current phase: load cities, run
// scripted quarters, print results as text. No rendering, no input, no UI.
var rng = new SimRng(seed: 1);

Console.WriteLine("AeroNext headless sim scaffold — nothing simulated yet.");
Console.WriteLine($"RNG smoke test (seed 1): {rng.NextDouble():F4}");
Console.WriteLine($"Money smoke test: {Money.FromDollars(1_000_000)}");
