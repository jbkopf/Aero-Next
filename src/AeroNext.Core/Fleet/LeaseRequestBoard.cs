namespace AeroNext.Core.Fleet;

/// <summary>
/// Aircraft-acquisition-limits follow-up: the lease-request counterpart
/// to AircraftOrderBoard - but genuinely simpler, since it never needs to
/// hold anything across more than one resolution cycle.
/// LeaseAllocationResolver.ResolveQuarter always returns this with an
/// empty Queue; an airline that wants more than it was allocated submits
/// a fresh LeaseAircraftOrder (a new action) and competes again in the
/// next cycle's own pool, rather than sitting in a backlog the way an
/// unfulfilled AircraftOrder does.
/// </summary>
public sealed record LeaseRequestBoard(IReadOnlyList<PendingLeaseRequest> Queue)
{
    public static LeaseRequestBoard Empty { get; } = new(Array.Empty<PendingLeaseRequest>());
}
