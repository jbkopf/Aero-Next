namespace AeroNext.Core.Airlines;

/// <summary>
/// Quality is a single-knob placeholder for CLAUDE.md's fare stance /
/// service level / marketing "postures" — it scales an airline's
/// competitiveness on a contested pair independently of raw capacity.
/// A real three-axis posture system belongs with M7's action economy,
/// once players actually have a way to set those postures; until then this
/// is the whole model.
/// </summary>
public sealed record Airline(string Name, IReadOnlyList<Route> Routes, double Quality = 1.0);
