using AeroNext.Core.Airlines;

namespace AeroNext.Core.Fleet;

/// <summary>
/// Aircraft-acquisition-limits follow-up: one airline's own real result
/// from one lease-pool resolution cycle - Quantity may be less than what
/// it originally requested (see LeaseAllocationResolver's own doc
/// comment for the even-plus-random allocation rule), and UpfrontCost is
/// charged against this real delivered Quantity, never the original
/// request - an airline is never billed for units it didn't receive.
/// </summary>
public sealed record LeaseDelivery(Airline Airline, AircraftType Type, int Quantity, Money UpfrontCost, int DeliveredQuarter);
