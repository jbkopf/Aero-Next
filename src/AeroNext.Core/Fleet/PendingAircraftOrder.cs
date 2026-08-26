using AeroNext.Core.Airlines;

namespace AeroNext.Core.Fleet;

public sealed record PendingAircraftOrder(Airline Airline, AircraftType Type, int Quantity, int OrderedQuarter);
