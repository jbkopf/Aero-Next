using AeroNext.Core.Airlines;
using AeroNext.Core.World;

namespace AeroNext.Core.Negotiations;

/// <summary>
/// Persistent negotiation state across quarters: bids still in flight,
/// each airline's standing per city, how many gate slots have already
/// been granted at each city in total, — M33 — which specific airline
/// holds how many of them, and — M45 — how many consecutive quarters
/// each holding has sat unused. Immutable — <see cref="LandingRightsResolver"/>
/// produces a new board rather than mutating this one, matching every other
/// piece of turn state in the sim (QuarterResult, NetworkPlan, ...).
/// </summary>
public sealed record LandingRightsBoard(
    IReadOnlyList<PendingNegotiation> Pending,
    IReadOnlyDictionary<(string AirlineName, string CityId), double> Standing,
    IReadOnlyDictionary<string, int> SlotsGranted,
    IReadOnlyDictionary<(string AirlineName, string CityId), int> SlotsHeld,
    IReadOnlyDictionary<(string AirlineName, string CityId), int> IdleStreak)
{
    /// <summary>Standing is a multiplier on bid strength; 1.0 is the neutral baseline for an airline with no history at a city.</summary>
    public const double BaselineStanding = 1.0;

    /// <summary>M45: consecutive quarters a held slot must sit unused before it becomes contestable by another airline.</summary>
    public const int IdleExpiryThreshold = 4;

    public static LandingRightsBoard Empty { get; } = new(
        Array.Empty<PendingNegotiation>(),
        new Dictionary<(string, string), double>(),
        new Dictionary<string, int>(),
        new Dictionary<(string, string), int>(),
        new Dictionary<(string, string), int>());

    public double GetStanding(Airline airline, City city) => Standing.GetValueOrDefault((airline.Name, city.Id), BaselineStanding);

    /// <param name="capacityBonus">
    /// M46: extra slots this city has earned via periodic, demand-driven
    /// expansion (see <see cref="CityCapacityBoard"/>) — 0 (the default)
    /// preserves every pre-existing caller's exact behavior. Added
    /// directly on top of GateCapacity before subtracting what's already
    /// granted; a city with a null GateCapacity (never modeled as scarce)
    /// stays at int.MaxValue regardless — a bonus has nothing to add to.
    /// </param>
    public int SlotsRemaining(City city, int capacityBonus = 0) => Math.Max(0, (city.GateCapacity is null ? int.MaxValue : city.GateCapacity.Value + capacityBonus) - SlotsGranted.GetValueOrDefault(city.Id));

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

            result[cityId] = Math.Max(0, held - InUse(cityId, currentRoutes));
        }

        return result;
    }

    private static int InUse(string cityId, IReadOnlyList<Route> routes) => routes
        .Where(r => r.Origin.Id == cityId || r.Destination.Id == cityId)
        .Sum(r => r.AircraftCount);

    /// <summary>
    /// M45: run once per quarter (alongside LandingRightsResolver.ResolveQuarter)
    /// to advance each held slot's idle clock. For every (airline, city) in
    /// SlotsHeld, "unused" is recomputed live from that airline's current
    /// routes there (held minus in-use, floored at zero) — same arithmetic
    /// AvailableSlotsByCity already uses, never a separately drifting
    /// number. Unused this quarter increments the streak; used-in-full
    /// resets it to zero (an airline that starts flying its held slots
    /// again stops the clock immediately, no partial credit needed since
    /// the streak only ever matters once it crosses IdleExpiryThreshold).
    /// A holding with zero streak is simply absent from the result, same
    /// GetValueOrDefault convention as everywhere else on this record.
    /// </summary>
    public LandingRightsBoard AdvanceIdleTracking(IReadOnlyDictionary<string, IReadOnlyList<Route>> routesByAirline)
    {
        var idleStreak = new Dictionary<(string, string), int>(IdleStreak);

        foreach (var ((holderName, cityId), held) in SlotsHeld)
        {
            var routes = routesByAirline.GetValueOrDefault(holderName, Array.Empty<Route>());
            var unused = Math.Max(0, held - InUse(cityId, routes));
            var key = (holderName, cityId);

            if (unused > 0)
            {
                idleStreak[key] = idleStreak.GetValueOrDefault(key) + 1;
            }
            else
            {
                idleStreak.Remove(key);
            }
        }

        return this with { IdleStreak = idleStreak };
    }

    /// <summary>
    /// M45: this city's idle-expired inventory — unused slots held by
    /// airlines whose IdleStreak has reached IdleExpiryThreshold, ordered
    /// by holder name for a deterministic consumption order when
    /// LandingRightsResolver hands them out to challengers (determinism
    /// rule #3: never iterate an unordered collection where it affects
    /// outcomes). Each entry appears once per contestable unit, so an
    /// airline with 2 unused idle-expired slots at a city appears twice.
    /// </summary>
    public IReadOnlyList<string> IdleExpiredHolders(City city, IReadOnlyDictionary<string, IReadOnlyList<Route>> routesByAirline) =>
        SlotsHeld
            .Where(kv => kv.Key.CityId == city.Id && IdleStreak.GetValueOrDefault(kv.Key) >= IdleExpiryThreshold)
            .OrderBy(kv => kv.Key.AirlineName, StringComparer.Ordinal)
            .SelectMany(kv =>
            {
                var routes = routesByAirline.GetValueOrDefault(kv.Key.AirlineName, Array.Empty<Route>());
                var unused = Math.Max(0, kv.Value - InUse(city.Id, routes));
                return Enumerable.Repeat(kv.Key.AirlineName, unused);
            })
            .ToList();

    /// <summary>
    /// M45: the public-information count version of IdleExpiredHolders,
    /// across every gateway — how many idle-expired, contestable slots
    /// exist at each city, with no holder identity attached, matching
    /// CLAUDE.md's contested-action info gradient (a slot's mere
    /// existence is observable; who specifically holds it isn't needed to
    /// decide whether it's worth bidding on). This is what
    /// StrategyContext.IdleContestableSlotsByCity is populated from — the
    /// same city-wide count for every airline asking, not a per-airline
    /// balance. A city with zero contestable inventory is simply absent,
    /// same GetValueOrDefault convention as everywhere else on this
    /// record.
    /// </summary>
    public IReadOnlyDictionary<string, int> IdleContestableSlotsByCity(IReadOnlyList<City> cities, IReadOnlyDictionary<string, IReadOnlyList<Route>> routesByAirline)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var city in cities.Where(c => c.IsGateway))
        {
            var count = IdleExpiredHolders(city, routesByAirline).Count;

            if (count > 0)
            {
                result[city.Id] = count;
            }
        }

        return result;
    }
}
