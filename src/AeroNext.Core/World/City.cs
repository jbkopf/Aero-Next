namespace AeroNext.Core.World;

/// <summary>
/// A static city definition loaded from data/cities.json. Gate capacity is
/// the number of simultaneous slot-holders a gateway or chokepoint city can
/// support; null means gate access isn't modeled as scarce there.
///
/// M45: <see cref="AutoCapacity"/> is the analogous ceiling for the other
/// 99-15=84, non-gateway cities — deliberately a *separate* field from
/// <see cref="GateCapacity"/> rather than the same one reused, because the
/// two are governed by entirely different mechanics: GateCapacity drives
/// the negotiated, per-airline-owned mechanic (LandingRightsBoard/
/// LandingRightsResolver) and is scoped to real gateways only; AutoCapacity
/// is an ownerless, shared ceiling on total AircraftCount from every
/// airline's routes touching the city, enforced directly by QuarterEngine
/// with no negotiation step. The two are mutually exclusive in practice —
/// a gateway city carries GateCapacity and leaves AutoCapacity null; a
/// non-gateway city carries AutoCapacity (when modeled) and leaves
/// GateCapacity null. Null means no automatic-capacity ceiling is modeled
/// there, same permissive philosophy as GateCapacity's own null.
/// </summary>
public sealed record City(
    string Id,
    string Name,
    Region Region,
    double Latitude,
    double Longitude,
    int DemandWeight,
    bool IsGateway,
    int? GateCapacity,
    int? AutoCapacity = null);
