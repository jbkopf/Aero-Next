namespace AeroNext.Core.Turns;

/// <summary>
/// M47: a network-wide ad campaign — a smaller competitive boost than
/// <see cref="RouteMarketingOrder"/>, but applied to every route this
/// airline operates, not just one. Mutually exclusive with any
/// RouteMarketingOrder the same quarter (see QuarterEngine.RunQuarter).
/// No fields — unlike a route campaign, it has nothing to target. Costs
/// scale with network size (MarketingCostCalculator.NetworkCampaignCost),
/// not a player-chosen amount the way NegotiationOrder.Bid is.
/// </summary>
public sealed record NetworkMarketingOrder : QuarterOrder;
