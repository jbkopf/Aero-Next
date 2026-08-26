using AeroNext.Core.Airlines;
using AeroNext.Core.Turns;

namespace AeroNext.Core.Fleet;

/// <summary>
/// Aircraft orders are public (CLAUDE.md's information gradient) and
/// resolve on delivery-line position, not an auction — no money, no
/// secrecy, no randomness, just FIFO by order quarter. Each aircraft type
/// has its own production line, shipping up to productionRatePerQuarter
/// units total (across every airline's orders for that type) per quarter.
/// An order submitted this quarter can't ship this same quarter — "order
/// late and you take delivery years later" implies a floor of at least one
/// quarter's wait, not instant turnaround. Ties for the same queue
/// position (orders placed the same quarter) break deterministically by
/// airline name.
///
/// Leasing bypasses the queue entirely and delivers the same quarter it's
/// ordered — the "expensive instant-metal escape hatch." Not contested, so
/// it needs no board: nobody's lease competes with anybody else's.
/// </summary>
public static class AircraftOrderResolver
{
    public const int DefaultProductionRatePerQuarter = 1;

    public static AircraftOrderBoard Submit(AircraftOrderBoard board, Airline airline, AircraftOrder order, int currentQuarter)
    {
        var pending = new PendingAircraftOrder(airline, order.Type, order.Quantity, currentQuarter);

        return board with { Queue = board.Queue.Append(pending).ToList() };
    }

    public static AircraftDelivery ResolveLease(Airline airline, LeaseAircraftOrder order, int currentQuarter) =>
        new(airline, order.Type, order.Quantity, currentQuarter);

    public static (AircraftOrderBoard Board, IReadOnlyList<AircraftDelivery> Deliveries) ResolveQuarter(
        AircraftOrderBoard board, int currentQuarter, int productionRatePerQuarter = DefaultProductionRatePerQuarter)
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

            var capacityLeft = productionRatePerQuarter;

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
