using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;

namespace AeroNext.Core.Fleet;

/// <summary>
/// Aircraft orders are public (CLAUDE.md's information gradient) and
/// resolve on delivery-line position, not an auction — no money, no
/// secrecy, no randomness, just FIFO by order quarter. Each aircraft type
/// has its own production line, shipping up to that type's own
/// AircraftType.ProductionRatePerQuarter units (across every airline's
/// orders for that type) per quarter — small/medium-range types are
/// produced at much higher rates than large long-haul types in practice,
/// so M11's real aircraft set that generously (only the top long-haul tier
/// is meant to be genuinely scarce; a short-haul type bottlenecking all 4
/// players from day one would be a design bug, not a feature). An order
/// submitted this quarter can't ship this same quarter — "order late and
/// you take delivery years later" implies a floor of at least one
/// quarter's wait, not instant turnaround. Ties for the same queue
/// position (orders placed the same quarter) break deterministically by
/// airline name.
///
/// Leasing used to bypass the queue entirely and deliver the same quarter
/// it was ordered. Aircraft-acquisition-limits follow-up
/// (docs/AIRCRAFT_ACQUISITION_ROADMAP.md): now that a real, contested,
/// cross-airline lease cap exists, leasing needs the same two-phase
/// shape purchasing already has - see LeaseAllocationResolver, this
/// class's own direct counterpart. The instant, uncontested ResolveLease
/// this comment used to describe is gone; there's no board-free shortcut
/// left to document.
/// </summary>
public static class AircraftOrderResolver
{
    public static AircraftOrderBoard Submit(AircraftOrderBoard board, Airline airline, AircraftOrder order, int currentQuarter)
    {
        var pending = new PendingAircraftOrder(airline, order.Type, order.Quantity, currentQuarter);

        return board with { Queue = board.Queue.Append(pending).ToList() };
    }

    public static (AircraftOrderBoard Board, IReadOnlyList<AircraftDelivery> Deliveries) ResolveQuarter(AircraftOrderBoard board, int currentQuarter)
    {
        var deliveries = new List<AircraftDelivery>();
        var remainingQueue = new List<PendingAircraftOrder>();

        foreach (var typeGroup in board.Queue.GroupBy(p => p.Type.Id).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var eligible = typeGroup
                .Where(p => p.OrderedQuarter < currentQuarter)
                .OrderBy(p => p.OrderedQuarter)
                .ThenBy(p => p.Airline.Name, StringComparer.Ordinal)
                .ToList();
            var notYetEligible = typeGroup.Where(p => p.OrderedQuarter >= currentQuarter);

            var capacityLeft = typeGroup.First().Type.ProductionRatePerQuarter;

            foreach (var pending in eligible)
            {
                if (capacityLeft <= 0)
                {
                    remainingQueue.Add(pending);
                    continue;
                }

                var shippedQty = Math.Min(pending.Quantity, capacityLeft);
                capacityLeft -= shippedQty;

                deliveries.Add(new AircraftDelivery(pending.Airline, pending.Type, shippedQty, currentQuarter));

                if (shippedQty < pending.Quantity)
                {
                    remainingQueue.Add(pending with { Quantity = pending.Quantity - shippedQty });
                }
            }

            remainingQueue.AddRange(notYetEligible);
        }

        var sortedDeliveries = deliveries
            .OrderBy(d => d.Type.Id, StringComparer.Ordinal)
            .ThenBy(d => d.Airline.Name, StringComparer.Ordinal)
            .ToList();

        return (board with { Queue = remainingQueue }, sortedDeliveries);
    }
}
