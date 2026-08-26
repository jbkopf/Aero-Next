using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// A contested action (CLAUDE.md: "several airlines target the same scarce
/// object") — commits cash toward landing rights at TargetCity. Always
/// costs 1 action, unlike market actions which can be free; see
/// <see cref="QuarterEngine"/>. Doesn't resolve this quarter — see
/// AeroNext.Core.Negotiations.LandingRightsResolver.
/// </summary>
public sealed record NegotiationOrder(City TargetCity, Money Bid) : QuarterOrder;
