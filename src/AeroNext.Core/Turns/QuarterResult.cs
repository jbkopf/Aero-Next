using AeroNext.Core.Airlines;

namespace AeroNext.Core.Turns;

/// <summary>
/// ActionCap is the cap that applies to the *next* quarter — hiring an
/// executive this quarter never affects this quarter's own budget, only
/// future ones. Pass it back into RunQuarter's currentActionCap next call.
/// </summary>
public sealed record QuarterResult(
    int QuarterNumber,
    IReadOnlyList<Route> Routes,
    NetworkPlan Plan,
    int ActionCap,
    IReadOnlyList<QuarterOrder> AppliedOrders,
    IReadOnlyList<QuarterOrder> RejectedOrders);
