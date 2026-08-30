using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Fleet;
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
/// Yield (revenue per passenger-km) is a flat global placeholder, not tied
/// to Airline.Quality or fare stance — a richer "premium carriers charge
/// more" model is a natural future refinement, not required for a
/// plausible cash trajectory and a bankruptcy trigger to both work.
///
/// Operating cost (per available seat-km) is per-aircraft, not a flat
/// global rate — AircraftType.CostPerAvailableSeatKm, so a route's cost
/// depends on what's actually flying it, giving aircraft selection real
/// operating-economics weight alongside range/capacity/purchase price.
/// </summary>
public static class FinancialResolver
{
    public const double DefaultYieldPerPassengerKm = 0.12;

    /// <summary>
    /// M23: closes a real gap found while designing the win condition —
    /// RefinanceOrder has no limit, so without this an airline could stay
    /// technically solvent forever by borrowing just enough cash each
    /// quarter, regardless of how deep its debt hole actually is. A flat
    /// ceiling, not scaled per airline's own starting cash (which varies
    /// by RegionalEconomics.Factor) — keeps this resolver self-contained
    /// rather than coupling it to AeroNext.Core.Setup for one constant.
    /// </summary>
    public const long DebtCeilingDollars = 80_000_000;

    /// <summary>
    /// M26: recurring fee on every currently-leased unit, charged every
    /// quarter for as long as it stays leased — leasing's own "instant
    /// metal, no upfront cost" design (CLAUDE.md) means this recurring fee
    /// is the only price it ever carries. A flat fraction of the type's
    /// purchase Cost, not a separate lease-rate field on AircraftType —
    /// keeps M26 from needing new aircraft data.
    /// </summary>
    public const double LeaseFeeRatePerQuarter = 0.04;

    /// <summary>
    /// M26: flat resale fraction credited when retiring an *owned* unit.
    /// No depreciation/book-value tracking exists (purchases are expensed
    /// the quarter they happen, not capitalized), so this is a flat rate
    /// against the original purchase Cost rather than a declining one.
    /// </summary>
    public const double RetirementResaleFraction = 0.40;

    /// <summary>
    /// M29: automatic quarterly principal repayment, as a fraction of the
    /// current balance (a declining-balance schedule — additional
    /// borrowing keeps merging into one running DebtBalance, so this can
    /// only be a percentage of what exists now, not a fixed payment
    /// against some original principal that no longer exists as a
    /// distinct number once loans combine). DebtService genuinely expands
    /// to mean interest *plus* this — real-world "debt service" has
    /// always meant both combined; the interest-only version was a
    /// placeholder simplification, not a deliberate definition this
    /// changes. Never reaches exactly zero under geometric decay — the
    /// honest consequence of not tracking a fixed loan term.
    /// </summary>
    public const double PrincipalRepaymentRatePerQuarter = 0.05;

    /// <param name="fleet">
    /// The airline's *post*-quarter fleet (QuarterResult.UpdatedFleet) —
    /// so a unit leased this same quarter is charged its fee immediately,
    /// consistent with leasing's own "delivers the same quarter" story,
    /// and a unit retired this quarter stops accruing a fee right away
    /// rather than being double-charged. Null (the default) charges no
    /// lease fees at all, preserving every pre-M26 call site's behavior.
    /// </param>
    /// <param name="retiredUnits">
    /// Pre-removal records of units retired this quarter (QuarterResult.
    /// RetiredUnits) — needed because by the time this resolver runs,
    /// `fleet` no longer has them to look up. Null (the default) credits
    /// no retirement proceeds, preserving every pre-M26 call site.
    /// </param>
    public static (FinancialState State, QuarterlyStatement Statement) Resolve(
        FinancialState state,
        Airline airline,
        CompetitiveMarketResult marketResult,
        IReadOnlyList<QuarterOrder> appliedOrders,
        double yieldPerPassengerKm = DefaultYieldPerPassengerKm,
        AirlineFleet? fleet = null,
        IReadOnlyList<FleetUnit>? retiredUnits = null)
    {
        if (state.IsBankrupt)
        {
            return (state, new QuarterlyStatement(Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, state.CashBalance, IsBankrupt: true));
        }

        var revenue = ComputeRevenue(airline, marketResult, yieldPerPassengerKm);
        var operatingCost = ComputeOperatingCost(airline, marketResult);
        var borrowed = appliedOrders.OfType<RefinanceOrder>().Aggregate(Money.Zero, (sum, o) => sum + o.AdditionalBorrowing);
        var manualPaydown = appliedOrders.OfType<PayDownDebtOrder>().Aggregate(Money.Zero, (sum, o) => sum + o.Amount);
        var debtBalance = state.DebtBalance + borrowed - manualPaydown;
        var interest = new Money((long)Math.Round(debtBalance.Cents * state.InterestRatePerQuarter, MidpointRounding.AwayFromZero));
        var principalRepayment = new Money((long)Math.Round(debtBalance.Cents * PrincipalRepaymentRatePerQuarter, MidpointRounding.AwayFromZero));
        var debtService = interest + principalRepayment;
        var finalDebtBalance = debtBalance - principalRepayment;

        var purchaseCost = appliedOrders.OfType<AircraftOrder>().Aggregate(Money.Zero, (sum, o) => sum + o.Type.Cost * o.Quantity);
        var leaseFee = ComputeLeaseFee(fleet);
        var retirementProceeds = ComputeRetirementProceeds(retiredUnits);
        var negotiationBidCost = appliedOrders.OfType<NegotiationOrder>().Aggregate(Money.Zero, (sum, o) => sum + o.Bid);

        var netIncome = revenue - operatingCost - debtService - purchaseCost - leaseFee + retirementProceeds - negotiationBidCost;
        var cashAfter = state.CashBalance + borrowed - manualPaydown + netIncome;
        var isBankrupt = cashAfter < Money.Zero || finalDebtBalance > Money.FromDollars(DebtCeilingDollars);

        var newState = state with { CashBalance = cashAfter, DebtBalance = finalDebtBalance, IsBankrupt = isBankrupt };
        var statement = new QuarterlyStatement(revenue, operatingCost, debtService, borrowed, netIncome, cashAfter, isBankrupt, purchaseCost, leaseFee, retirementProceeds, negotiationBidCost, principalRepayment, manualPaydown);

        return (newState, statement);
    }

    private static Money ComputeLeaseFee(AirlineFleet? fleet)
    {
        if (fleet is null)
        {
            return Money.Zero;
        }

        var cents = 0L;

        foreach (var unit in fleet.Units.Where(u => u.Mode == FleetOwnershipMode.Leased))
        {
            cents += (long)Math.Round(unit.Type.Cost.Cents * LeaseFeeRatePerQuarter, MidpointRounding.AwayFromZero);
        }

        return new Money(cents);
    }

    private static Money ComputeRetirementProceeds(IReadOnlyList<FleetUnit>? retiredUnits)
    {
        if (retiredUnits is null)
        {
            return Money.Zero;
        }

        var cents = 0L;

        foreach (var unit in retiredUnits.Where(u => u.Mode == FleetOwnershipMode.Owned))
        {
            cents += (long)Math.Round(unit.Type.Cost.Cents * RetirementResaleFraction, MidpointRounding.AwayFromZero);
        }

        return new Money(cents);
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

    private static Money ComputeOperatingCost(Airline airline, CompetitiveMarketResult marketResult)
    {
        var cents = 0L;

        foreach (var usage in marketResult.RouteUsage.Where(u => u.Airline == airline))
        {
            cents += (long)Math.Round(usage.SeatsPerQuarter * usage.Route.DistanceKm * usage.Route.Aircraft.CostPerAvailableSeatKm * 100, MidpointRounding.AwayFromZero);
        }

        return new Money(cents);
    }
}
