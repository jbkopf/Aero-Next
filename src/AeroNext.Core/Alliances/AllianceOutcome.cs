using AeroNext.Core.Airlines;

namespace AeroNext.Core.Alliances;

public sealed record AllianceOutcome(Airline Airline, RegionalCarrier Target, bool Won);
