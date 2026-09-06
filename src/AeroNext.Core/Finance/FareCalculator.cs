using AeroNext.Core.Airlines;

namespace AeroNext.Core.Finance;

/// <summary>
/// M34: a route's default fare when no player override exists (see
/// Route.FarePerPassenger). Priced per seat *offered*, not per passenger
/// actually carried — load factor isn't baked in, so an under-filled
/// route stays a visible cash drain even at a "profitable" fare, the
/// same causal story FinancialResolver's own operating-cost comment
/// already tells.
///
/// CostPerSeatCarried sums every currently-modeled per-route,
/// per-*assigned*-aircraft cost, expressed as $ per one seat flown this
/// route's full one-way distance: today, operating cost (M12, the same
/// rate FinancialResolver.ComputeOperatingCost applies per
/// available-seat-km) plus this aircraft's own prorated share of its
/// flat quarterly salary (M30) — divided across the seats ONE assigned
/// aircraft offers this quarter, not the route's total (route.SeatsPerQuarter
/// / route.AircraftCount recovers exactly that, with no remainder, since
/// SeatsPerQuarter is built as a multiple of AircraftCount). BalancedGrowthStrategy
/// follow-up: the once-future per-aircraft recurring lease fee named
/// here is now that one more optional term, gated by includeLeaseFee —
/// see its own doc comment; a default fare (SuggestedFarePerPassenger,
/// every existing caller) still assumes ownership, unaffected.
///
/// This is necessary, not optional: a margin over operating cost alone
/// can never reliably cover a flat, distance-independent cost like
/// salary — verified directly against the 1955 starting scenario in
/// docs/FINANCIAL_MODEL_ROADMAP.md's M34 section, where an
/// operating-cost-only margin loses money at every distance the
/// starting aircraft can fly, even fully deployed.
/// </summary>
public static class FareCalculator
{
    /// <summary>(Proposed, tunable) — see M34's roadmap writeup.</summary>
    public const double DefaultFareMarginOverCost = 0.30;

    /// <summary>
    /// (Proposed, tunable) — M48: how strongly a fare set away from
    /// SuggestedFarePerPassenger moves demand, in the standard
    /// price-elasticity sense (percent demand change per percent price
    /// change). 1.5 means a 10% discount off the suggested fare yields a
    /// 15% demand increase for that route; a 10% premium yields a 15%
    /// decrease, symmetrically.
    /// </summary>
    public const double FareDemandElasticity = 1.5;

    /// <param name="includeLeaseFee">
    /// BalancedGrowthStrategy follow-up: the real per-quarter recurring
    /// cost a *leased* (not owned) unit carries — FinancialResolver.LeaseFeeRatePerQuarter
    /// of the aircraft's own purchase Cost, prorated across the same
    /// per-aircraft seat count the salary term already uses. Defaults
    /// false, reproducing every existing call site's exact prior
    /// behavior unchanged — Route itself carries no ownership-mode field
    /// (that's FleetUnit.Mode, one level up), so a caller evaluating a
    /// specific fulfillment path (idle fleet vs. a fresh lease) passes
    /// this explicitly rather than it being inferable from the Route
    /// alone. This is the exact extension point this class's own doc
    /// comment already named ("a lease fee... is meant to be added as
    /// one more term summed in here").
    /// </param>
    public static Money CostPerSeatCarried(Route route, bool includeLeaseFee = false)
    {
        var seatsPerQuarterPerAircraft = route.SeatsPerQuarter / route.AircraftCount;
        var operatingCostPerSeat = route.DistanceKm * route.Aircraft.CostPerAvailableSeatKm;
        var salaryPerSeat = FinancialResolver.SalaryCostPerAircraftPerQuarter.Cents / (double)seatsPerQuarterPerAircraft / 100.0;
        var leaseFeePerSeat = includeLeaseFee
            ? route.Aircraft.Cost.Cents * FinancialResolver.LeaseFeeRatePerQuarter / seatsPerQuarterPerAircraft / 100.0
            : 0.0;
        var cents = (long)Math.Round((operatingCostPerSeat + salaryPerSeat + leaseFeePerSeat) * 100, MidpointRounding.AwayFromZero);

        return new Money(cents);
    }

    public static Money SuggestedFarePerPassenger(Route route)
    {
        var cents = (long)Math.Round(CostPerSeatCarried(route).Cents * (1 + DefaultFareMarginOverCost), MidpointRounding.AwayFromZero);

        return new Money(cents);
    }

    /// <summary>
    /// M54: a real per-route profit estimate — revenue from what's
    /// actually carried (route.FarePerPassenger x carriedPassengers)
    /// against the full cost of the capacity offered
    /// (CostPerSeatCarried x route.SeatsPerQuarter, not just the seats
    /// carried), mirroring FinancialResolver's own real formula: operating
    /// cost is charged on capacity offered, not seats filled, so a route
    /// with a bad load factor is a real cash drain even before it earns
    /// anything. carriedPassengers is the caller's choice of "best
    /// available" figure — a real, competitively-resolved carried count
    /// when one exists (a contested route), or RouteEvaluator's own
    /// solo-carrier estimate otherwise (an uncontested route, where that
    /// estimate is already accurate). includeLeaseFee (BalancedGrowthStrategy
    /// follow-up) forwards to CostPerSeatCarried unchanged — see its own
    /// doc comment; defaults false, so every pre-existing caller here
    /// (which never modeled a route's own prorated lease fee) is
    /// unaffected.
    /// </summary>
    public static Money EstimatedProfit(Route route, long carriedPassengers, bool includeLeaseFee = false) =>
        route.FarePerPassenger * carriedPassengers - CostPerSeatCarried(route, includeLeaseFee) * route.SeatsPerQuarter;

    /// <summary>
    /// M48: how much this route's own actual fare (FarePerPassenger —
    /// a player override, or SuggestedFarePerPassenger itself when none
    /// is set) should move demand, as a signed fraction of baseline
    /// demand — positive for a discount (fare below suggested), negative
    /// for a premium (fare above), zero exactly when FareOverride is
    /// null (no override at all resolves to actual == suggested,
    /// ratio == 1.0 exactly, so every route nobody has ever touched
    /// SetFareOrder on is completely unaffected — no migration burden on
    /// any existing caller, same reasoning FareOverride's own default
    /// already established). CompetitiveMarket.Resolve is the only real
    /// consumer — see its own doc comments for how a contested pair uses
    /// this as one more multiplicative factor alongside Quality/marketing,
    /// and how an uncontested pair uses it as a genuine demand delta,
    /// symmetric with a discount's own gain but bounded so a premium can
    /// never force carried passengers down below whatever spare capacity
    /// would already have left uncarried anyway.
    /// </summary>
    public static double DemandDeltaFraction(Route route)
    {
        var suggested = SuggestedFarePerPassenger(route).Cents;

        if (suggested <= 0)
        {
            return 0.0;
        }

        var priceRatio = (double)route.FarePerPassenger.Cents / suggested;

        return -FareDemandElasticity * (priceRatio - 1.0);
    }
}
