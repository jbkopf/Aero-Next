using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Negotiations;

public sealed record NegotiationOutcome(Airline Airline, City TargetCity, Money Bid, bool Won, double StandingAfter);
