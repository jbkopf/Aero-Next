namespace AeroNext.Core.Airlines;

/// <summary>
/// Minimal aircraft spec: seat count only. Range, cost, and era availability
/// arrive at M10 — until then, every route is assumed flyable.
/// </summary>
public sealed record AircraftType(string Id, string Name, int Seats);
