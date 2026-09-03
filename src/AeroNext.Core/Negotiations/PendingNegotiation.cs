using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Negotiations;

/// <summary>M59: SlotsRequested is the already-clamped figure (see LandingRightsResolver.Submit) - never the raw, unclamped NegotiationOrder value.</summary>
public sealed record PendingNegotiation(Airline Airline, City TargetCity, Money Bid, int SubmittedQuarter, int ResolvesQuarter, int SlotsRequested = 1);
