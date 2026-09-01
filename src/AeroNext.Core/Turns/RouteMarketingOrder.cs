using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// M47: an ad campaign targeted at one route — a real competitive boost
/// there (bigger than <see cref="NetworkMarketingOrder"/>'s network-wide
/// one), but nowhere else. Rejected outright if the airline doesn't
/// currently operate this exact (Origin, Destination) pair — the same
/// "never attempt what will predictably fail" principle SetFareOrder
/// already applies, exact-orientation match against a served route.
/// Mutually exclusive with any NetworkMarketingOrder the same quarter,
/// and capped at QuarterEngine.MaxConcurrentRouteMarketingCampaigns
/// distinct routes per quarter (see QuarterEngine.RunQuarter). Cost
/// scales with this pair's own current demand
/// (MarketingCostCalculator.RouteCampaignCost), not a player-chosen
/// amount the way NegotiationOrder.Bid is.
/// </summary>
public sealed record RouteMarketingOrder(City Origin, City Destination) : QuarterOrder;
