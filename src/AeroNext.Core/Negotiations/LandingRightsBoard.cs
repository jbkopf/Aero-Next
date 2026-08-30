using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Negotiations;

/// <summary>
/// Persistent negotiation state across quarters: bids still in flight,
/// each airline's standing per city, how many gate slots have already
/// been granted at each city in total, and — M33 — which specific airline
/// holds how many of them. Immutable — <see cref="LandingRightsResolver"/>
/// produces a new board rather than mutating this one, matching every other
/// piece of turn state in the sim (QuarterResult, NetworkPlan, ...).
/// </summary>
public sealed record LandingRightsBoard(
    IReadOnlyList<PendingNegotiation> Pending,
    IReadOnlyDictionary<(string AirlineName, string CityId), double> Standing,
    IReadOnlyDictionary<string, int> SlotsGranted,
    IReadOnlyDictionary<(string AirlineName, string CityId), int> SlotsHeld)
{
    /// <summary>Standing is a multiplier on bid strength; 1.0 is the neutral baseline for an airline with no history at a city.</summary>
    public const double BaselineStanding = 1.0;

    public static LandingRightsBoard Empty { get; } = new(
        Array.Empty<PendingNegotiation>(),
        new Dictionary<(string, string), double>(),
        new Dictionary<string, int>(),
        new Dictionary<(string, string), int>());

    public double GetStanding(Airline airline, City city) => Standing.GetValueOrDefault((airline.Name, city.Id), BaselineStanding);

    public int SlotsRemaining(City city) => Math.Max(0, (city.GateCapacity ?? int.MaxValue) - SlotsGranted.GetValueOrDefault(city.Id));

    /// <summary>How many gate slots this specific airline has won at this city, across every negotiation it's ever won there.</summary>
    public int SlotsHeldBy(Airline airline, City city) => SlotsHeld.GetValueOrDefault((airline.Name, city.Id));

    /// <summary>
    /// M33: this airline's per-city credit balance for QuarterEngine's
    /// availableSlotsByCity / BaselineAi's parameter of the same name —
    /// held slots minus how many the airline's current routes already
    /// occupy there. Computed fresh from live route state each call
    /// (AircraftCount summed per gateway city touched) rather than a
    /// separately persisted "in use" ledger, so a route closing or
    /// shrinking frees capacity automatically with no extra bookkeeping.
    /// Only cities this airline holds at least one slot at appear here —
    /// everywhere else is implicitly zero, same as GetValueOrDefault.
    /// </summary>
    public IReadOnlyDictionary<string, int> AvailableSlotsByCity(Airline airline, IReadOnlyList<Route> currentRoutes)
    {
        var result = new Dictionary<string, int>();

        foreach (var ((holderName, cityId), held) in SlotsHeld)
        {
            if (!string.Equals(holderName, airline.Name, StringComparison.Ordinal))
            {
                continue;
            }

            var inUse = currentRoutes
                .Where(r => r.Origin.Id == cityId || r.Destination.Id == cityId)
                .Sum(r => r.AircraftCount);

            result[cityId] = Math.Max(0, held - inUse);
        }

        return result;
    }
}
