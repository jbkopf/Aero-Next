namespace AeroNext.Core.Airlines;

/// <summary>
/// M32: how many round trips a single aircraft can realistically fly per
/// week on a given route, derived from cruise speed and distance rather
/// than treated as a free player-chosen number — CLAUDE.md's "legibility
/// over simulation depth" pillar, not a deep operations model. Isolated
/// here (not buried in <see cref="Route"/>) so the arithmetic is directly
/// unit-testable against known real-world routes.
///
/// TurnaroundHoursPerLanding and UtilizationHoursPerDay are flat, global,
/// tunable placeholders — the same "proposed, tunable" spirit as
/// FinancialResolver's LeaseFeeRatePerQuarter/RetirementResaleFraction.
/// Only CruiseSpeedKmh varies per aircraft (the "aircraft performance"
/// half of the calculation); turnaround/utilization don't vary enough
/// across the roster to be worth a second per-aircraft dimension yet.
///
/// Sanity-checked by hand against real routes: a Boeing 707 on JFK-LHR
/// (~5,550 km) comes out to ~4 round trips/week; a 747 near its own max
/// range (~12,000 km) comes out to ~2/week; a short regional hop comes out
/// to ~15/week (roughly 2/day) — plausible without claiming precision.
/// </summary>
public static class FlightPerformance
{
    /// <summary>Ground time assumed at each end of a round trip (2 landings), flat across every aircraft type.</summary>
    public const double TurnaroundHoursPerLanding = 1.0;

    /// <summary>Assumed hours per day an aircraft is actually flying, not sitting — a "block hours" placeholder.</summary>
    public const double UtilizationHoursPerDay = 10.0;

    /// <summary>
    /// Always at least 1 — an aircraft actually assigned to a route always
    /// flies it at least weekly, never a degenerate zero, even on a very
    /// long route near the aircraft's own range limit.
    /// </summary>
    /// <param name="distanceKm">One-way distance flown, every leg added together for a multi-stop route.</param>
    /// <param name="landingsPerDirection">
    /// Landings on each one-way trip: 1 for a nonstop route, plus one per
    /// intermediate stop (docs/MULTI_STOP_ROUTES_DESIGN.md) - each stop
    /// costs a turnaround's ground time in both directions.
    /// </param>
    public static int MaxWeeklyRoundTrips(AircraftType aircraft, double distanceKm, int landingsPerDirection = 1)
    {
        var roundTripHours = 2 * (distanceKm / aircraft.CruiseSpeedKmh) + 2 * landingsPerDirection * TurnaroundHoursPerLanding;
        var weeklyUtilizationHours = UtilizationHoursPerDay * 7;

        return Math.Max(1, (int)(weeklyUtilizationHours / roundTripHours));
    }
}
