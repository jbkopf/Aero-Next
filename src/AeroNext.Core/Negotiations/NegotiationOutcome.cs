using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Negotiations;

/// <summary>M59: SlotsWon - real quantity granted (0 for a full loss, up to the bid's own clamped SlotsRequested for a full win, anything between for a partial fulfillment). Won is kept, unchanged in meaning (SlotsWon > 0) - every pre-M59 consumer keeps reading it exactly as before.</summary>
public sealed record NegotiationOutcome(Airline Airline, City TargetCity, Money Bid, bool Won, double StandingAfter, int SlotsWon = 0);
