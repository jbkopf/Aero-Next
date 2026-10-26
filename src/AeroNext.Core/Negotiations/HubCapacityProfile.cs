using System.Text.Json;
using AeroNext.Core.World;

namespace AeroNext.Core.Negotiations;

/// <summary>One point of a hub's real traffic history: approximate annual passengers, in millions, for the whole metro area that year.</summary>
public sealed record HubTrafficPoint(int Year, double Value);

/// <summary>
/// A major hub's capacity growth profile (docs/HUB_CAPACITY_GROWTH_DESIGN.md):
/// its real traffic history, from data/hub_traffic_history.json. The
/// city's capacity in a year follows its absolute real traffic, square-root
/// compressed - <see cref="CapacityScale"/> x peakTraffic ^
/// <see cref="CapacityExponent"/> - so a 100M-passenger hub reaches ~16, a
/// 40M hub ~10, a 10M hub ~5. Real traffic grew far more than the game's
/// demand does; compression keeps hubs scarce while tracking their real
/// relative size and timing (Dubai's post-1985 surge, Beijing and
/// Shanghai after reform, mature hubs levelling off).
/// </summary>
public sealed record HubCapacityProfile(string CityId, string History, IReadOnlyList<HubTrafficPoint> AnnualPassengersMillions)
{
    /// <summary>(Proposed, tunable) - capacity per square-root million annual passengers.</summary>
    public const double CapacityScale = 1.6;

    /// <summary>(Proposed, tunable) - compression of real traffic into game capacity; 0.5 is a square root.</summary>
    public const double CapacityExponent = 0.5;

    /// <summary>Traffic in a year, interpolated log-linearly between the history's points and held flat outside them.</summary>
    public double TrafficIn(int year)
    {
        var points = AnnualPassengersMillions;

        if (year <= points[0].Year)
        {
            return points[0].Value;
        }

        for (var i = 0; i < points.Count - 1; i++)
        {
            var (from, to) = (points[i], points[i + 1]);

            if (year <= to.Year)
            {
                var t = (double)(year - from.Year) / (to.Year - from.Year);
                return Math.Exp(Math.Log(from.Value) * (1 - t) + Math.Log(to.Value) * t);
            }
        }

        return points[^1].Value;
    }

    /// <summary>The highest traffic reached by this year - a ratchet, so a dip (Moscow after 1991) never takes capacity away.</summary>
    public double PeakTrafficBy(int year)
    {
        var first = AnnualPassengersMillions[0].Year;

        return Enumerable.Range(first, Math.Max(1, year - first + 1)).Max(TrafficIn);
    }

    /// <summary>The capacity this profile gives the city in a year (before the starting-capacity floor).</summary>
    public int CapacityIn(int year) => (int)Math.Floor(CapacityScale * Math.Pow(PeakTrafficBy(year), CapacityExponent));
}

/// <summary>Loads data/hub_traffic_history.json into one <see cref="HubCapacityProfile"/> per city id.</summary>
public static class HubTrafficHistoryCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyDictionary<string, HubCapacityProfile> Load(string json, IReadOnlySet<string>? knownCityIds = null)
    {
        var profiles = JsonSerializer.Deserialize<List<HubCapacityProfile>>(json, JsonOptions)
            ?? throw new InvalidDataException("Hub traffic history deserialized to null.");

        var byId = new Dictionary<string, HubCapacityProfile>(StringComparer.Ordinal);

        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.CityId))
            {
                throw new InvalidDataException("A hub traffic history entry has no cityId.");
            }

            if (knownCityIds is not null && !knownCityIds.Contains(profile.CityId))
            {
                throw new InvalidDataException($"Hub traffic history names unknown city '{profile.CityId}'.");
            }

            var points = profile.AnnualPassengersMillions;

            if (points is null || points.Count == 0)
            {
                throw new InvalidDataException($"Hub traffic history for '{profile.CityId}' has no traffic points.");
            }

            if (points.Any(p => p.Value <= 0) || points.Zip(points.Skip(1)).Any(x => x.Second.Year <= x.First.Year))
            {
                throw new InvalidDataException($"Hub traffic history for '{profile.CityId}' needs positive values in strictly ascending years.");
            }

            if (!byId.TryAdd(profile.CityId, profile))
            {
                throw new InvalidDataException($"Hub traffic history lists '{profile.CityId}' twice.");
            }
        }

        return byId;
    }

    public static IReadOnlyDictionary<string, HubCapacityProfile> LoadFile(string path, IReadOnlySet<string>? knownCityIds = null) =>
        Load(File.ReadAllText(path), knownCityIds);
}
