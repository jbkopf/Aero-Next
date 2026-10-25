using AeroNext.Core;

namespace AeroNext.Core.Competition;

/// <summary>
/// One airline's offer on a city pair, as passengers compare them
/// (CompetitiveMarket's contest weight): seats on its itinerary's tightest
/// leg, its Quality, and the journey's cost (JourneyCost.PerPassenger).
/// Public in the CLAUDE.md sense - rivals' routes and fares are visible -
/// so the AI can judge how well a new offer would compete
/// (docs/CONNECTING_PASSENGERS_DESIGN.md).
/// </summary>
public sealed record PairOffer(string AirlineName, long Seats, double Quality, Money JourneyCost);
