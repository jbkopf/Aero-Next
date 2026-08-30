namespace AeroNext.Core.Turns;

/// <summary>
/// A private action (CLAUDE.md: "resolves against nothing") — always free,
/// per CLAUDE.md's own naming ("retiring aircraft" alongside refinancing
/// and hiring). Removes a specific fleet unit — see AeroNext.Core.Fleet.
/// FleetLedger.Retire. An owned unit returns cash (a flat resale fraction
/// of its AircraftType.Cost — see FinancialResolver); a leased unit
/// returns nothing, since it was never owned.
///
/// Unlike RouteOrder's fleet check (permissive when no fleet is tracked —
/// ownership simply isn't enforced), retiring fundamentally requires fleet
/// awareness to mean anything, so it's rejected outright whenever fleet
/// tracking is off, the unit doesn't exist, or the unit is currently
/// assigned to a route — this milestone only retires idle units; retiring
/// an assigned one requires freeing it via a close-route action first
/// (not built yet), rather than one action implicitly doing two things.
/// </summary>
public sealed record RetireAircraftOrder(int UnitId) : QuarterOrder;
