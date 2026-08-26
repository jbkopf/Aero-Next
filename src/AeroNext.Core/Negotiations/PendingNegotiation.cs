using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Negotiations;

public sealed record PendingNegotiation(Airline Airline, City TargetCity, Money Bid, int SubmittedQuarter, int ResolvesQuarter);
