namespace AeroNext.Core.Airlines;

/// <summary>
/// RangeKm, Cost, and ProductionRatePerQuarter default generously (a synthetic
/// "Test" aircraft in a unit test shouldn't have to think about range or
/// scarcity) so only the real, named aircraft need to state them explicitly.
/// AvailableFromYear defaults to 1900 — comfortably before the 1955 campaign
/// start, so every aircraft built before M13 (all of them, constructed
/// inline) is implicitly "always available" and unaffected by the era
/// checks M13 adds. Only M13's JSON fleet catalog states it for real.
/// </summary>
public sealed record AircraftType(
    string Id,
    string Name,
    int Seats,
    int RangeKm = 20_000,
    Money Cost = default,
    int ProductionRatePerQuarter = 4,
    int AvailableFromYear = 1900);
