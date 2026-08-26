using AeroNext.Core.Alliances;

namespace AeroNext.Core.Turns;

/// <summary>
/// A contested action — several airlines may court the same regional
/// carrier, but it joins only one alliance (CLAUDE.md). Always costs 1
/// action. Resolves the same quarter it's submitted — see
/// AeroNext.Core.Alliances.AllianceResolver.
/// </summary>
public sealed record AllianceCourtOrder(RegionalCarrier Target) : QuarterOrder;
