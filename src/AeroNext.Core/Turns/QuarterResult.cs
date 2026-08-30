using AeroNext.Core.Airlines;
using AeroNext.Core.Fleet;

namespace AeroNext.Core.Turns;

/// <summary>
/// ActionCap is the cap that applies to the *next* quarter — hiring an
/// executive this quarter never affects this quarter's own budget, only
/// future ones. Pass it back into RunQuarter's currentActionCap next call.
///
/// UpdatedFleet is null iff RunQuarter was called without fleet awareness
/// (the default) — non-null means "pass this back in as next quarter's
/// fleet," the same carry-forward shape Routes/ActionCap already have.
///
/// RetiredUnits holds each retired unit's pre-removal record (type +
/// ownership mode) — FinancialResolver needs it to credit an owned unit's
/// resale value, but by the time it runs, UpdatedFleet no longer has the
/// unit to look up. QuarterEngine deliberately doesn't compute the dollar
/// value itself — that stays FinancialResolver's constant to own, keeping
/// this class pure fleet/action logic, the same boundary UpdatedFleet
/// already draws.
/// </summary>
public sealed record QuarterResult(
    int QuarterNumber,
    IReadOnlyList<Route> Routes,
    NetworkPlan Plan,
    int ActionCap,
    IReadOnlyList<QuarterOrder> AppliedOrders,
    IReadOnlyList<QuarterOrder> RejectedOrders,
    AirlineFleet? UpdatedFleet = null,
    IReadOnlyList<FleetUnit>? RetiredUnits = null);
