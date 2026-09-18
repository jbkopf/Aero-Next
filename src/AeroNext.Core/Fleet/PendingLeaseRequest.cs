using AeroNext.Core.Airlines;

namespace AeroNext.Core.Fleet;

/// <summary>
/// Aircraft-acquisition-limits follow-up (docs/AIRCRAFT_ACQUISITION_ROADMAP.md):
/// one airline's own lease request, submitted RequestedQuarter, resolved
/// the following quarter once every airline's own request for that same
/// quarter is known. Unlike PendingAircraftOrder, this never carries a
/// partial remainder forward — LeaseAllocationResolver.ResolveQuarter
/// always fully drains the board it's given.
/// </summary>
public sealed record PendingLeaseRequest(Airline Airline, AircraftType Type, int Quantity, int RequestedQuarter);
