namespace AeroNext.Core.Competition;

/// <summary>
/// M47: one airline's active marketing campaign for the quarter just
/// resolved — never both fields meaningful at once (QuarterEngine.RunQuarter
/// enforces the mutual exclusivity before this is ever built), but kept as
/// one small record rather than a discriminated pair of nullable types,
/// matching this codebase's own preference for simple records over
/// invented union types. `RouteKeys` uses QuarterEngine.NormalizedPairKey's
/// own unordered-pair convention, so CompetitiveMarket.Resolve's own pair
/// iteration (whose Origin/Destination orientation isn't guaranteed to
/// match a route's stored one) can match it reliably.
/// </summary>
public sealed record AirlineMarketingCampaign(bool NetworkWide, IReadOnlySet<(string OriginId, string DestinationId)> RouteKeys);
