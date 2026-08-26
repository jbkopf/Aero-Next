using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Finance;

/// <summary>
/// The quarterly P&amp;L: revenue from competitively-resolved passengers,
/// operating cost from capacity flown, debt service on the current
/// balance. CLAUDE.md is explicit this is "the scoring function, not the
/// point" — kept subordinate to the map/area-control layer, so it stays
/// three flat placeholder rates rather than a deeper economic model.
///
/// Revenue must use CompetitiveMarketResult, not an airline's own
/// NetworkPlan — an airline's monopoly plan assumes no rivals, but real
/// revenue depends on what it actually carries once M6's demand splitting
/// resolves. Operating cost is charged on capacity offered (available
/// seat-km), not just seats filled — a route with a bad load factor is a
/// real cash drain even before it earns anything, the same causal story
/// M3's load factor already tells, now with financial stakes.
///
/// Yield (revenue per passenger-km) and cost (per available seat-km) are
/// flat global placeholders, not tied to Airline.Quality or fare stance —
/// a richer "premium carriers charge more" model is a natural future
/// refinement, not required for a plausible cash trajectory and a
/// bankruptcy trigger to both work.
/// </summary>
public static class FinancialResolver
{
    public const double DefaultYieldPerPassengerKm = 0.12;
    public const double DefaultCostPerAvailableSeatKm = 0.09;

    public static (FinancialState State, QuarterlyStatement Statement) Resolve(
        FinancialState state,
        Airline airline,
        CompetitiveMarketResult marketResult,
        IReadOnlyList<QuarterOrder> appliedOrders,
        double yieldPerPassengerKm = DefaultYieldPerPassengerKm,
        double costPerAvailableSeatKm = DefaultCostPerAvailableSeatKm)
    {
        if (state.IsBankrupt)
        {
            return (state, new QuarterlyStatement(Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, state.CashBalance, IsBankrupt: true));
        }

        var revenue = ComputeRevenue(airline, marketResult, yieldPerPassengerKm);
        var operatingCost = ComputeOperatingCost(airline, marketResult, costPerAvailableSeatKm);
        var borrowed = appliedOrders.OfType<RefinanceOrder>().Aggregate(Money.Zero, (sum, o) => sum + o.AdditionalBorrowing);
        var debtBalance = state.DebtBalance + borrowed;
        var debtService = new Money((long)Math.Round(debtBalance.Cents * state.InterestRatePerQuarter, MidpointRounding.AwayFromZero));

        var netIncome = revenue - operatingCost - debtService;
        var cashAfter = state.CashBalance + borrowed + netIncome;
        var isBankrupt = cashAfter < Money.Zero;

        var newState = state with { CashBalance = cashAfter, DebtBalance = debtBalance, IsBankrupt = isBankrupt };
        var statement = new QuarterlyStatement(revenue, operatingCost, debtService, borrowed, netIncome, cashAfter, isBankrupt);

        return (newState, statement);
    }

    private static Money ComputeRevenue(Airline airline, CompetitiveMarketResult marketResult, double yieldPerPassengerKm)
    {
        var cents = 0L;

        foreach (var pair in marketResult.Pairs)
        {
            var carried = pair.CarriedByAirline.GetValueOrDefault(airline);
            if (carried == 0)
            {
                continue;
            }

            var distanceKm = GreatCircle.DistanceKm(pair.Origin, pair.Destination);
            cents += (long)Math.Round(carried * distanceKm * yieldPerPassengerKm * 100, MidpointRounding.AwayFromZero);
        }

        return new Money(cents);
    }

    private static Money ComputeOperatingCost(Airline airline, CompetitiveMarketResult marketResult, double costPerAvailableSeatKm)
    {
        var cents = 0L;

        foreach (var usage in marketResult.RouteUsage.Where(u => u.Airline == airline))
        {
            cents += (long)Math.Round(usage.SeatsPerQuarter * usage.Route.DistanceKm * costPerAvailableSeatKm * 100, MidpointRounding.AwayFromZero);
        }

        return new Money(cents);
    }
}
