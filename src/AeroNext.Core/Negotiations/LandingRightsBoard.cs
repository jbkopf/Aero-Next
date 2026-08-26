using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Negotiations;

/// <summary>
/// Persistent negotiation state across quarters: bids still in flight,
/// each airline's standing per city, and how many gate slots have already
/// been granted at each city. Immutable — <see cref="LandingRightsResolver"/>
/// produces a new board rather than mutating this one, matching every other
/// piece of turn state in the sim (QuarterResult, NetworkPlan, ...).
/// </summary>
public sealed record LandingRightsBoard(
    IReadOnlyList<PendingNegotiation> Pending,
    IReadOnlyDictionary<(string AirlineName, string CityId), double> Standing,
    IReadOnlyDictionary<string, int> SlotsGranted)
{
    /// <summary>Standing is a multiplier on bid strength; 1.0 is the neutral baseline for an airline with no history at a city.</summary>
    public const double BaselineStanding = 1.0;

    public static LandingRightsBoard Empty { get; } = new(
        Array.Empty<PendingNegotiation>(),
        new Dictionary<(string, string), double>(),
        new Dictionary<string, int>());

    public double GetStanding(Airline airline, City city) => Standing.GetValueOrDefault((airline.Name, city.Id), BaselineStanding);

    public int SlotsRemaining(City city) => Math.Max(0, (city.GateCapacity ?? int.MaxValue) - SlotsGranted.GetValueOrDefault(city.Id));
}
