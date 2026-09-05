using AeroNext.Core.Airlines;
using AeroNext.Core.Competition;
using AeroNext.Core.Fleet;
using AeroNext.Core.Turns;

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
/// Revenue (M34) comes from each route's own FarePerPassenger — a player
/// override, or FareCalculator's cost-plus-margin default when none is
/// set — multiplied by that route's SeatsUsed, mirroring exactly how
/// operating cost is already computed per route/leg below. No more flat
/// global yield: fare now depends on the aircraft, the distance, and
/// whatever the player set, though it still doesn't depend on
/// Airline.Quality or affect competitive share — see M34's roadmap
/// writeup for what's still deliberately out of scope.
///
/// Operating cost (per available seat-km) is per-aircraft, not a flat
/// global rate — AircraftType.CostPerAvailableSeatKm, so a route's cost
/// depends on what's actually flying it, giving aircraft selection real
/// operating-economics weight alongside range/capacity/purchase price.
/// </summary>
public static class FinancialResolver
{
    /// <summary>
    /// M23: closes a real gap found while designing the win condition —
    /// RefinanceOrder had no limit, so without this an airline could stay
    /// technically solvent forever by borrowing just enough cash each
    /// quarter, regardless of how deep its debt hole actually is. A flat
    /// ceiling, not scaled per airline's own starting cash (which varies
    /// by RegionalEconomics.Factor) — keeps this resolver self-contained
    /// rather than coupling it to AeroNext.Core.Setup for one constant.
    ///
    /// Bankruptcy-conditions follow-up: this used to also be a direct
    /// bankruptcy trigger here in Resolve (debt ending the quarter above
    /// this line meant instant IsBankrupt, even with positive cash) —
    /// removed. Enforcement moved earlier, to the loan request itself:
    /// QuarterEngine.RunQuarter now rejects a RefinanceOrder outright if
    /// it would push debt past this ceiling (mirroring PayDownDebtOrder's
    /// own "rejected outright, not silently clamped" convention), so
    /// DebtBalance can no longer drift past it at all — principal
    /// repayment only ever shrinks it, applied borrowing is the only thing
    /// that grows it. The constant's purpose (stop infinite borrowing) is
    /// unchanged; only *how* it's enforced moved from punishing the whole
    /// airline after the fact to rejecting the specific over-limit ask.
    /// See RemainingBorrowingCapacity below for the queryable headroom a
    /// loan-request UI would show.
    /// </summary>
    public const long DebtCeilingDollars = 80_000_000;

    /// <summary>
    /// Bankruptcy-conditions follow-up: the one named, testable source of
    /// truth for "how much more can this airline borrow before hitting
    /// DebtCeilingDollars" — what a real loan-request UI would show the
    /// player, and what QuarterEngine.RunQuarter's own RefinanceOrder
    /// validation checks against. Floors at Money.Zero rather than going
    /// negative for an airline already at or past the ceiling (shouldn't
    /// happen going forward now that borrowing past it is rejected at the
    /// source, but stays a sane answer rather than a nonsensical negative
    /// one if it ever does).
    /// </summary>
    public static Money RemainingBorrowingCapacity(Money currentDebtBalance)
    {
        var headroom = Money.FromDollars(DebtCeilingDollars) - currentDebtBalance;
        return headroom > Money.Zero ? headroom : Money.Zero;
    }

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

    /// <summary>
    /// M30: flat, per-aircraft-unit quarterly staffing cost — rank-and-file
    /// crew, maintenance, and scheduling staff, independent of
    /// ExecutiveSalaryPerQuarter (a separate, M49 line for the executives
    /// who raise the action cap, not this rank-and-file fleet staff).
    /// Scaled by total fleet size (owned + leased, from M25's
    /// ledger), not route count — a route already uses an aircraft unit
    /// that's counted, so scaling by both would double-count the same
    /// capacity. Charged regardless of whether a unit is idle or actively
    /// flying a route — staff still need paying either way. Deliberately a
    /// flat dollar rate, not scaled by the unit's own Cost or Seats the
    /// way LeaseFee reasonably is — real staffing cost doesn't track
    /// purchase price the way a lease payment does.
    /// </summary>
    public static readonly Money SalaryCostPerAircraftPerQuarter = Money.FromDollars(250_000);

    /// <summary>(Proposed, tunable) — M49: HireExecutiveOrder's one-time cost, charged the same quarter it's applied — recruiting/signing/relocation, not an ongoing expense (see ExecutiveSalaryPerQuarter for that).</summary>
    public static readonly Money ExecutiveHiringCost = Money.FromDollars(600_000);

    /// <summary>(Proposed, tunable) — M49: recurring per-quarter cost for every executive currently on staff, same "broken out, not folded" legibility treatment as SalaryCostPerAircraftPerQuarter. Charged starting the quarter an executive's own action-cap boost takes effect (one quarter after hiring), not the quarter they're hired — symmetric with when they start actually helping, not the same quarter as the one-time hiring cost above.</summary>
    public static readonly Money ExecutiveSalaryPerQuarter = Money.FromDollars(300_000);

    /// <summary>
    /// M34 amendment to M30: an idle (unassigned) unit costs less than a
    /// flying one, not the same flat rate — a flat rate with no reduced
    /// idle tier meant a starting airline that leaves one of its two
    /// starting aircraft unassigned (plausible first-turn play, not
    /// misplay) bled cash forever with no path back short of retiring
    /// it, verified directly against the 1955 starting scenario in
    /// docs/FINANCIAL_MODEL_ROADMAP.md's M34 section. 0.15 was chosen
    /// with margin under the worst case a distance-based fare could ever
    /// cover (approaches ~0.17 as route distance approaches zero) — idle
    /// capacity stays a real, felt drag (M30's actual point) without
    /// being an unrecoverable one.
    /// </summary>
    public const double IdleSalaryCostFraction = 0.15;

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
    /// <param name="currentActionCap">
    /// M49: the airline's own ActionCap entering this quarter (before any
    /// HireExecutiveOrder applied this same quarter takes effect) — the
    /// same value QuarterEngine.RunQuarter's own currentActionCap
    /// parameter was called with. Null (the default) charges no executive
    /// salary at all, preserving every pre-M49 call site's behavior.
    /// Passing a real value derives the currently-on-staff executive
    /// headcount from it (ActionCap - QuarterEngine.BaseActionCap) and
    /// charges ExecutiveSalaryPerQuarter for each — using the cap as it
    /// stood *entering* the quarter, not QuarterResult's own post-hire
    /// ActionCap, so a newly hired executive's salary starts next quarter
    /// too, the same delay their own action-cap boost already has.
    /// </param>
    public static (FinancialState State, QuarterlyStatement Statement) Resolve(
        FinancialState state,
        Airline airline,
        CompetitiveMarketResult marketResult,
        IReadOnlyList<QuarterOrder> appliedOrders,
        AirlineFleet? fleet = null,
        IReadOnlyList<FleetUnit>? retiredUnits = null,
        int? currentActionCap = null)
    {
        if (state.IsBankrupt)
        {
            return (state, new QuarterlyStatement(Money.Zero, Money.Zero, Money.Zero, Money.Zero, Money.Zero, state.CashBalance, IsBankrupt: true));
        }

        var revenue = ComputeRevenue(airline, marketResult);
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
        var salaryCost = ComputeSalaryCost(fleet);
        var marketingCost = ComputeMarketingCost(airline, marketResult, appliedOrders);
        var executiveHiringCost = ExecutiveHiringCost * appliedOrders.OfType<HireExecutiveOrder>().Count();
        var executiveSalaryCost = ComputeExecutiveSalaryCost(currentActionCap);

        var netIncome = revenue - operatingCost - debtService - purchaseCost - leaseFee + retirementProceeds - negotiationBidCost - salaryCost - marketingCost - executiveHiringCost - executiveSalaryCost;
        var cashAfter = state.CashBalance + borrowed - manualPaydown + netIncome;

        // Bankruptcy-conditions follow-up: cash negative no longer freezes
        // the same quarter it happens - the airline's very next turn gets
        // a real chance to react (borrow, sell aircraft, close/shrink a
        // route - all free, Private actions) before this actually bites.
        // Only cash negative *again* while already InDistress from last
        // quarter's crossing becomes real bankruptcy; recovering to >= 0
        // at any point clears InDistress instead. See FinancialState's own
        // doc comment for the full rule.
        var isCashNegative = cashAfter < Money.Zero;
        var isBankrupt = isCashNegative && state.InDistress;
        var newInDistress = isCashNegative && !isBankrupt;

        var newState = state with { CashBalance = cashAfter, DebtBalance = finalDebtBalance, IsBankrupt = isBankrupt, InDistress = newInDistress };
        var statement = new QuarterlyStatement(revenue, operatingCost, debtService, borrowed, netIncome, cashAfter, isBankrupt, purchaseCost, leaseFee, retirementProceeds, negotiationBidCost, principalRepayment, manualPaydown, salaryCost, marketingCost, executiveHiringCost, executiveSalaryCost, InDistress: newInDistress);

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

    private static Money ComputeSalaryCost(AirlineFleet? fleet)
    {
        if (fleet is null)
        {
            return Money.Zero;
        }

        var idleCount = fleet.Idle.Count();
        var assignedCount = fleet.Units.Count - idleCount;
        var idleRateCents = (long)Math.Round(SalaryCostPerAircraftPerQuarter.Cents * IdleSalaryCostFraction, MidpointRounding.AwayFromZero);

        return SalaryCostPerAircraftPerQuarter * assignedCount + new Money(idleRateCents) * idleCount;
    }

    /// <summary>M49: null (the default, preserving every pre-M49 call site) charges nothing; otherwise ExecutiveSalaryPerQuarter times however many executives ActionCap - QuarterEngine.BaseActionCap implies were already on staff entering the quarter.</summary>
    private static Money ComputeExecutiveSalaryCost(int? currentActionCap)
    {
        if (currentActionCap is null)
        {
            return Money.Zero;
        }

        var executiveCount = Math.Max(0, (currentActionCap.Value - QuarterEngine.BaseActionCap) / QuarterEngine.ActionCapPerExecutive);

        return ExecutiveSalaryPerQuarter * executiveCount;
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

    /// <summary>
    /// M47: re-derives each applied campaign's own cost independently
    /// from state this resolver already has — network size from the
    /// airline's own route count, a route campaign's demand from
    /// marketResult.Pairs (an orientation-insensitive lookup, via
    /// QuarterEngine.NormalizedPairKey, since a pair's canonical
    /// Origin/Destination there isn't guaranteed to match the order's
    /// own) — never a value threaded from QuarterEngine's own
    /// affordability gate, so the two can never drift apart; see
    /// MarketingCostCalculator's own doc comment.
    /// </summary>
    private static Money ComputeMarketingCost(Airline airline, CompetitiveMarketResult marketResult, IReadOnlyList<QuarterOrder> appliedOrders)
    {
        var cost = Money.Zero;

        foreach (var _ in appliedOrders.OfType<NetworkMarketingOrder>())
        {
            cost += MarketingCostCalculator.NetworkCampaignCost(airline.Routes.Count);
        }

        foreach (var order in appliedOrders.OfType<RouteMarketingOrder>())
        {
            var pairKey = QuarterEngine.NormalizedPairKey(order.Origin, order.Destination);
            var demand = marketResult.Pairs.FirstOrDefault(p => QuarterEngine.NormalizedPairKey(p.Origin, p.Destination) == pairKey)?.DemandPassengers ?? 0;
            cost += MarketingCostCalculator.RouteCampaignCost(demand);
        }

        return cost;
    }

    /// <summary>
    /// M34: revenue is each route's own FarePerPassenger times how many
    /// seats it actually carried this quarter — mirroring
    /// ComputeOperatingCost's per-leg RouteUsage iteration exactly,
    /// rather than the old flat-yield-times-O-D-distance calculation.
    /// A connecting (one-stop) passenger already incurs operating cost
    /// on both legs they fly (RouteUsage's existing behavior); revenue
    /// now mirrors that — their fare is the sum of both legs' own fares,
    /// reflecting the real distance flown via the hub. Pure integer
    /// math (Money * long), no rounding needed — unlike the constant
    /// this replaces.
    /// </summary>
    private static Money ComputeRevenue(Airline airline, CompetitiveMarketResult marketResult)
    {
        var revenue = Money.Zero;

        foreach (var usage in marketResult.RouteUsage.Where(u => u.Airline == airline))
        {
            revenue += usage.Route.FarePerPassenger * usage.SeatsUsed;
        }

        return revenue;
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
