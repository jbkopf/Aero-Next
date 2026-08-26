namespace AeroNext.Core.Fleet;

/// <summary>
/// The production backlog: orders not yet delivered, oldest first once
/// sorted for resolution. Immutable — AircraftOrderResolver produces a new
/// board rather than mutating this one.
/// </summary>
public sealed record AircraftOrderBoard(IReadOnlyList<PendingAircraftOrder> Queue)
{
    public static AircraftOrderBoard Empty { get; } = new(Array.Empty<PendingAircraftOrder>());
}
