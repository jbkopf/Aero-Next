using AeroNext.Core.Airlines;

namespace AeroNext.Core.Fleet;

public sealed record AircraftDelivery(Airline Airline, AircraftType Type, int Quantity, int DeliveredQuarter);
