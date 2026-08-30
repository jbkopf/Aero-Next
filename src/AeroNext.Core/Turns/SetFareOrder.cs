using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// A private action (CLAUDE.md: "resolves against nothing") — always
/// free, same framing PayDownDebtOrder/CloseRouteOrder already use.
/// Overrides the Origin-Destination pair's fare directly (M34) — a
/// literal settable price, not a discrete posture tier, reinterpreting
/// CLAUDE.md's "fare stance... free to set" line. Rejected outright if
/// the airline doesn't currently operate this pair (a fare has nothing
/// to attach to) or if FarePerPassenger is negative — the same
/// "never attempt what will predictably fail/make no sense" principle
/// already settled for cash affordability.
/// </summary>
public sealed record SetFareOrder(City Origin, City Destination, Money FarePerPassenger) : QuarterOrder;
