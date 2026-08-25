using AeroNext.Core.World;

namespace AeroNext.Core.Airlines;

public enum ItineraryKind
{
    Nonstop,
    OneStop,
    Unserved,
}

/// <summary>
/// One or two legs connecting Origin to Destination. Unserved means the
/// network has no nonstop or single-connection path between the two.
/// </summary>
public sealed record Itinerary(City Origin, City Destination, ItineraryKind Kind, IReadOnlyList<Route> Legs);
