using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Competition;

/// <summary>
/// One city pair's demand as split across every airline that can reach it
/// (nonstop or one-stop, per that airline's own network). An airline absent
/// from CarriedByAirline doesn't serve this pair at all.
/// </summary>
/// <param name="Offers">Every airline's offer on the pair this quarter - each airline whose network reaches it, whether or not it won passengers.</param>
public sealed record CompetitivePairResult(City Origin, City Destination, long DemandPassengers, IReadOnlyDictionary<Airline, long> CarriedByAirline,
    IReadOnlyList<PairOffer>? Offers = null);
