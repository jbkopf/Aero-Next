using AeroNext.Core.Airlines;

namespace AeroNext.Core.Turns;

public sealed record QuarterResult(int QuarterNumber, IReadOnlyList<Route> Routes, NetworkPlan Plan);
