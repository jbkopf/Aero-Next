namespace AeroNext.Core.Airlines;

/// <summary>
/// RangeKm, Cost, and ProductionRatePerQuarter default generously (a synthetic
/// "Test" aircraft in a unit test shouldn't have to think about range or
/// scarcity) so only M11's real aircraft need to state them explicitly.
/// Cost isn't charged against anything yet — no treasury exists until M12.
/// </summary>
public sealed record AircraftType(
    string Id,
    string Name,
    int Seats,
    int RangeKm = 20_000,
    Money Cost = default,
    int ProductionRatePerQuarter = 4);
