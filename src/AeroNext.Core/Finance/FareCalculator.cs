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
/// SeatsPerQuarter is built as a multiple of AircraftCount). A future
/// per-aircraft recurring cost (e.g. a lease fee once a route can carry
/// leased units) is meant to be added as one more term summed in here —
/// a single, obvious extension point, not a change to every call site
/// that reads a default fare.
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

    public static Money CostPerSeatCarried(Route route)
    {
        var seatsPerQuarterPerAircraft = route.SeatsPerQuarter / route.AircraftCount;
        var operatingCostPerSeat = route.DistanceKm * route.Aircraft.CostPerAvailableSeatKm;
        var salaryPerSeat = FinancialResolver.SalaryCostPerAircraftPerQuarter.Cents / (double)seatsPerQuarterPerAircraft / 100.0;
        var cents = (long)Math.Round((operatingCostPerSeat + salaryPerSeat) * 100, MidpointRounding.AwayFromZero);

        return new Money(cents);
    }

    public static Money SuggestedFarePerPassenger(Route route)
    {
        var cents = (long)Math.Round(CostPerSeatCarried(route).Cents * (1 + DefaultFareMarginOverCost), MidpointRounding.AwayFromZero);

        return new Money(cents);
    }
}
