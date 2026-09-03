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
///
/// M61: <see cref="HubQuality"/> is a real, separate concept from both
/// <see cref="DemandWeight"/> (how attractive this city is as a
/// destination) and <see cref="IsGateway"/> (intercontinental chokepoint
/// status) — how good a real connecting point this city is, independent
/// of whether people actually want to visit it (Atlanta, Dubai). Defaults
/// to 0 (no distinctive real-world hub reputation), the large majority of
/// cities. See docs/HUB_DEMAND_DESIGN.md and docs/HUB_DEMAND_ROADMAP.md's
/// M61 section for the authoring methodology and full rationale. Deliberately
/// *not* derived from DemandWeight or IsGateway when authored, and enforced
/// by <see cref="CityCatalog"/> to stay a bounded fraction of DemandWeight
/// (see <see cref="CityCatalog.MaxHubQualityToDemandWeightRatio"/>) so any
/// future consumer that adds it to DemandWeight (M63's innate-demand bonus)
/// can never be more than a fraction of a city's own destination appeal.
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
    int? AutoCapacity = null,
    int HubQuality = 0);
