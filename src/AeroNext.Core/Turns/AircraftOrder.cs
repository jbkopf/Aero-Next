using AeroNext.Core.Airlines;

namespace AeroNext.Core.Turns;

/// <summary>
/// A contested action — the scarce resource is delivery-line position, not
/// the airframe itself (CLAUDE.md). Always costs 1 action. Doesn't deliver
/// this quarter — see AeroNext.Core.Fleet.AircraftOrderResolver.
/// </summary>
public sealed record AircraftOrder(AircraftType Type, int Quantity) : QuarterOrder;
