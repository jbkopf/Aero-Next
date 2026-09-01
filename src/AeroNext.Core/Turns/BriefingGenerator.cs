using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// Builds the briefing's exception queue: what changed since last quarter
/// that crossed a threshold, per CLAUDE.md — not a data dump. Capped at
/// MaxItems regardless of network size.
/// </summary>
public static class BriefingGenerator
{
    private const double LoadFactorChangeThreshold = 0.15;
    private const int MaxItems = 5;

    public static IReadOnlyList<BriefingItem> Generate(QuarterResult? previous, QuarterResult current)
    {
        var items = new List<BriefingItem>();

        var year = Timeline.YearForQuarter(current.QuarterNumber);

        foreach (var order in current.RejectedOrders)
        {
            // Reason isn't stored on the order — re-derive it here rather
            // than widen QuarterResult's shape for a display-only detail.
            items.Add(DescribeRejection(order, year));
        }

        var capIncrease = current.AppliedOrders.OfType<HireExecutiveOrder>().Count() * QuarterEngine.ActionCapPerExecutive;
        if (capIncrease > 0)
        {
            items.Add(new BriefingItem(
                ExceptionKind.ActionCapIncreased,
                $"Action cap raised to {current.ActionCap} for next quarter (+{capIncrease})"));
        }

        var previousRouteKeys = previous?.Routes.Select(QuarterEngine.RouteKey).ToHashSet()
            ?? new HashSet<(string, string)>();

        foreach (var route in current.Routes
            .Where(r => !previousRouteKeys.Contains(QuarterEngine.RouteKey(r)))
            .OrderBy(r => r.Origin.Id, StringComparer.Ordinal)
            .ThenBy(r => r.Destination.Id, StringComparer.Ordinal))
        {
            items.Add(new BriefingItem(
                ExceptionKind.RouteEntered,
                $"New route entered: {route.Origin.Id}-{route.Destination.Id}"));
        }

        if (previous is not null)
        {
            var previousLoadFactorByRoute = previous.Plan.RouteUsage.ToDictionary(u => QuarterEngine.RouteKey(u.Route), u => u.LoadFactor);

            foreach (var usage in current.Plan.RouteUsage
                .OrderBy(u => u.Route.Origin.Id, StringComparer.Ordinal)
                .ThenBy(u => u.Route.Destination.Id, StringComparer.Ordinal))
            {
                var key = QuarterEngine.RouteKey(usage.Route);
                if (!previousLoadFactorByRoute.TryGetValue(key, out var previousLoadFactor))
                {
                    continue; // route is new this quarter; already reported above
                }

                var delta = usage.LoadFactor - previousLoadFactor;

                if (delta <= -LoadFactorChangeThreshold)
                {
                    items.Add(new BriefingItem(
                        ExceptionKind.LoadFactorDropped,
                        $"{usage.Route.Origin.Id}-{usage.Route.Destination.Id} load factor dropped {previousLoadFactor:P0} -> {usage.LoadFactor:P0}"));
                }
                else if (delta >= LoadFactorChangeThreshold)
                {
                    items.Add(new BriefingItem(
                        ExceptionKind.LoadFactorSurged,
                        $"{usage.Route.Origin.Id}-{usage.Route.Destination.Id} load factor surged {previousLoadFactor:P0} -> {usage.LoadFactor:P0}"));
                }
            }
        }

        return items.Take(MaxItems).ToList();
    }

    private static string Describe(QuarterOrder order) => order switch
    {
        RouteOrder ro => $"{ro.Origin.Id}-{ro.Destination.Id}",
        HireExecutiveOrder => "hire executive",
        _ => order.GetType().Name,
    };

    private static BriefingItem DescribeRejection(QuarterOrder order, int year)
    {
        if (order is RouteOrder ro && !ro.IsWithinRange)
        {
            return new BriefingItem(ExceptionKind.OrderRejectedOutOfRange, $"Order rejected, aircraft out of range: {Describe(order)}");
        }

        var aircraft = order switch
        {
            RouteOrder r => r.Aircraft,
            AircraftOrder a => a.Type,
            LeaseAircraftOrder l => l.Type,
            _ => null,
        };

        if (aircraft is not null && !Timeline.IsAvailable(aircraft, year))
        {
            return new BriefingItem(ExceptionKind.OrderRejectedAircraftNotYetAvailable, $"Order rejected, aircraft not yet available: {Describe(order)}");
        }

        return new BriefingItem(ExceptionKind.OrderRejectedOverBudget, $"Order rejected, over budget: {Describe(order)}");
    }
}
