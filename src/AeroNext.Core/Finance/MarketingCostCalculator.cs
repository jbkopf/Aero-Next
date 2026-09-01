namespace AeroNext.Core.Finance;

/// <summary>
/// M47: the two marketing-campaign costs — always a deterministic
/// formula, never a player-chosen amount the way NegotiationOrder.Bid
/// is. QuarterEngine's own affordability gate and FinancialResolver's
/// actual deduction each call these independently, from state they
/// already have (route count, current pair demand) — never a value
/// stored on the order itself, so the two can never drift apart.
/// </summary>
public static class MarketingCostCalculator
{
    /// <summary>(Proposed, tunable) — a network-wide campaign's cost per route the airline currently operates.</summary>
    public static readonly Money NetworkCampaignCostPerRoute = Money.FromDollars(100_000);

    /// <summary>(Proposed, tunable) — a route campaign's cost per passenger of that pair's own current quarterly demand.</summary>
    public const double RouteCampaignCostPerPassengerOfDemand = 50.0;

    public static Money NetworkCampaignCost(int routeCount) => NetworkCampaignCostPerRoute * routeCount;

    public static Money RouteCampaignCost(long demandPassengers) =>
        new((long)Math.Round(demandPassengers * RouteCampaignCostPerPassengerOfDemand * 100, MidpointRounding.AwayFromZero));
}
