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
/// </summary>
public sealed record QuarterResult(
    int QuarterNumber,
    IReadOnlyList<Route> Routes,
    NetworkPlan Plan,
    int ActionCap,
    IReadOnlyList<QuarterOrder> AppliedOrders,
    IReadOnlyList<QuarterOrder> RejectedOrders,
    AirlineFleet? UpdatedFleet = null);
