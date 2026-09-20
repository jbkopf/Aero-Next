using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;
using AeroNext.Core.World;

namespace AeroNext.Core.Fleet;

/// <summary>
/// Aircraft-acquisition-limits follow-up (docs/AIRCRAFT_ACQUISITION_ROADMAP.md):
/// leasing is now two-phase, like purchasing already is - a
/// LeaseAircraftOrder is accepted by QuarterEngine (era check, Quantity
/// check) but no longer delivers inline. Submitted requests are resolved
/// together, cross-airline, the quarter after they're placed - the same
/// "at least one quarter's wait" floor AircraftOrderResolver already has,
/// for the identical reason: a shared, contested pool can't be resolved
/// from inside one airline's own single-airline-at-a-time QuarterEngine
/// call.
///
/// Genuinely simpler than AircraftOrderResolver in one real way: no
/// backlog. Every call to ResolveQuarter fully drains the board it's
/// given - an unfulfilled remainder is gone, not carried forward, since
/// LeaseCapForQuarter is a fresh per-quarter allowance (a flow cap, not a
/// standing stock), not a production line with a queue position to hold.
/// </summary>
public static class LeaseAllocationResolver
{
    /// <summary>(Proposed, tunable) — the smallest per-quarter lease cap any type ever has, at the moment it first becomes lease-eligible. See LeaseCapForQuarter.</summary>
    public const int MinLeaseCapPerQuarter = 2;

    /// <summary>(Proposed, tunable) — the largest per-quarter lease cap any type ever ramps up to. See LeaseCapForQuarter.</summary>
    public const int MaxLeaseCapPerQuarter = 20;

    /// <summary>(Proposed, tunable) — how many years after first becoming lease-eligible a type's own cap takes to ramp from MinLeaseCapPerQuarter to MaxLeaseCapPerQuarter. See LeaseCapForQuarter.</summary>
    public const int LeaseCapRampYears = 15;

    /// <summary>(Proposed, tunable) — the nominal upfront lease cost, as a fraction of the type's own purchase Cost, charged per unit actually delivered.</summary>
    public const double LeaseUpfrontCostFraction = 0.015;

    /// <summary>
    /// The per-quarter lease pool size for one type, this year - a linear
    /// ramp from MinLeaseCapPerQuarter (at the moment the type first
    /// becomes lease-eligible) up to MaxLeaseCapPerQuarter over
    /// LeaseCapRampYears, then flat. Age is measured from
    /// *lease*-eligibility start (AvailableFromYear + Timeline.LeaseEligibilityDelayYears),
    /// not from AvailableFromYear itself - "newest aircraft" in a leasing
    /// context means "just became leasable at all," not "just announced."
    /// </summary>
    public static int LeaseCapForQuarter(AircraftType type, int year)
    {
        var leaseEligibleYear = type.AvailableFromYear + Timeline.LeaseEligibilityDelayYears;
        var ageInYears = Math.Max(0, year - leaseEligibleYear);
        var ramped = MinLeaseCapPerQuarter + ageInYears * (MaxLeaseCapPerQuarter - MinLeaseCapPerQuarter) / LeaseCapRampYears;

        return Math.Min(MaxLeaseCapPerQuarter, ramped);
    }

    public static LeaseRequestBoard Submit(LeaseRequestBoard board, Airline airline, LeaseAircraftOrder order, int currentQuarter)
    {
        var pending = new PendingLeaseRequest(airline, order.Type, order.Quantity, currentQuarter);

        return board with { Queue = board.Queue.Append(pending).ToList() };
    }

    /// <summary>
    /// Resolves every pending request together, one type at a time.
    /// Under cap: everyone gets their full request. Over cap: each
    /// airline first gets min(request, cap / requestingAirlineCount) —
    /// an airline that asked for *less* than its even share keeps its
    /// full ask, no water-filling redistribution of what it left
    /// unclaimed (the same simplification CompetitiveMarket.Resolve's
    /// own doc comment already accepts for an analogous reason). Any
    /// capacity still left after that first pass is handed out one unit
    /// at a time, drawn uniformly at random (via rng) from airlines that
    /// still want more, until either it's exhausted or nobody does.
    /// Always returns an empty Queue — see this class's own doc comment.
    /// </summary>
    public static (LeaseRequestBoard Board, IReadOnlyList<LeaseDelivery> Deliveries) ResolveQuarter(
        LeaseRequestBoard board, int currentQuarter, int year, SimRng rng)
    {
        var deliveries = new List<LeaseDelivery>();

        foreach (var typeGroup in board.Queue.GroupBy(p => p.Type.Id).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var type = typeGroup.First().Type;
            var cap = LeaseCapForQuarter(type, year);

            // One entry per airline for this type - an airline submitting
            // more than one LeaseAircraftOrder for the same type in the
            // same quarter (unusual, but not prevented) has its own
            // requests combined before allocation, not treated as two
            // independent competitors for the same pool.
            var requestByAirline = typeGroup
                .GroupBy(p => p.Airline.Name, StringComparer.Ordinal)
                .Select(g => (Airline: g.First().Airline, Requested: g.Sum(p => p.Quantity)))
                .OrderBy(r => r.Airline.Name, StringComparer.Ordinal)
                .ToList();

            var totalDemand = requestByAirline.Sum(r => r.Requested);
            var allocatedByAirlineName = new Dictionary<string, int>(StringComparer.Ordinal);

            if (totalDemand <= cap)
            {
                foreach (var (airline, requested) in requestByAirline)
                {
                    allocatedByAirlineName[airline.Name] = requested;
                }
            }
            else
            {
                var baseShare = cap / requestByAirline.Count;
                var remaining = cap;

                foreach (var (airline, requested) in requestByAirline)
                {
                    var allocated = Math.Min(requested, baseShare);
                    allocatedByAirlineName[airline.Name] = allocated;
                    remaining -= allocated;
                }

                // Deterministic candidate order before every random draw
                // (CLAUDE.md rule #3) - still-wanting airlines, sorted by
                // name, re-filtered each iteration as individual airlines
                // get satisfied.
                while (remaining > 0)
                {
                    var stillWanting = requestByAirline
                        .Where(r => allocatedByAirlineName[r.Airline.Name] < r.Requested)
                        .OrderBy(r => r.Airline.Name, StringComparer.Ordinal)
                        .ToList();

                    if (stillWanting.Count == 0)
                    {
                        // Provably unreachable, not a bug: remaining > 0
                        // here means sum(allocated) < cap < totalDemand,
                        // so at least one airline's allocated must be
                        // below its own requested - stillWanting can
                        // never be empty while remaining > 0. Kept as a
                        // defensive guard against an infinite loop should
                        // that invariant ever break under a future edit,
                        // the same "coverage gap, not a bug" convention
                        // NetworkOptimizerStrategy's own step 2 already
                        // uses for an identically-provable case.
                        break;
                    }

                    var pickIndex = rng.Next(0, stillWanting.Count);
                    var winnerName = stillWanting[pickIndex].Airline.Name;
                    allocatedByAirlineName[winnerName] += 1;
                    remaining -= 1;
                }
            }

            foreach (var (airline, _) in requestByAirline)
            {
                var quantity = allocatedByAirlineName[airline.Name];

                if (quantity <= 0)
                {
                    continue;
                }

                var upfrontCost = new Money((long)Math.Round(type.Cost.Cents * LeaseUpfrontCostFraction * quantity, MidpointRounding.AwayFromZero));
                deliveries.Add(new LeaseDelivery(airline, type, quantity, upfrontCost, currentQuarter));
            }
        }

        var sortedDeliveries = deliveries
            .OrderBy(d => d.Type.Id, StringComparer.Ordinal)
            .ThenBy(d => d.Airline.Name, StringComparer.Ordinal)
            .ToList();

        return (LeaseRequestBoard.Empty, sortedDeliveries);
    }
}
