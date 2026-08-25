namespace AeroNext.Core.Airlines;

public sealed record NetworkPlan(IReadOnlyList<ItineraryLoad> Itineraries, IReadOnlyList<RouteCapacityUsage> RouteUsage)
{
    public long TotalDemand => Itineraries.Sum(i => i.DemandPassengers);

    public long TotalCarried => Itineraries.Sum(i => i.PassengersCarried);

    public long NonstopCarried => Itineraries
        .Where(i => i.Itinerary.Kind == ItineraryKind.Nonstop)
        .Sum(i => i.PassengersCarried);

    public long OneStopCarried => Itineraries
        .Where(i => i.Itinerary.Kind == ItineraryKind.OneStop)
        .Sum(i => i.PassengersCarried);
}
