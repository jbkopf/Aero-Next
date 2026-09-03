using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroNext.Core.World;

/// <summary>
/// Loads and validates the city dataset. Always returns cities sorted by Id
/// so downstream iteration is deterministic regardless of source file order
/// (determinism rule #3).
/// </summary>
public static class CityCatalog
{
    /// <summary>
    /// M59 (proposed, tunable) — every city's starting capacity
    /// (AutoCapacity or GateCapacity) must be at least this many slots,
    /// the base CityCapacityBoard's own growth math scales from. A city
    /// starting at 1 needed only a single aircraft, from any of the 4
    /// airlines, to be permanently maxed out (M46's own growth mechanic
    /// can at most double a city's starting figure) - measured against
    /// the real dataset, small starting capacity, not real strategic
    /// importance, was what actually drove saturation (Fukuoka,
    /// AutoCapacity 1, fully saturated 72.8% of sampled quarters in a
    /// real 300-game sweep, well above Atlanta - AutoCapacity 4, a
    /// genuinely major hub - at 20.4%).
    /// </summary>
    public const int MinimumStartingCapacity = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<City> Load(string json)
    {
        var cities = JsonSerializer.Deserialize<List<City>>(json, JsonOptions)
            ?? throw new InvalidDataException("City data deserialized to null.");

        Validate(cities);

        return cities.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
    }

    public static IReadOnlyList<City> LoadFile(string path) => Load(File.ReadAllText(path));

    private static void Validate(List<City> cities)
    {
        if (cities.Count == 0)
        {
            throw new InvalidDataException("City data contained no cities.");
        }

        var duplicateIds = cities
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Count > 0)
        {
            throw new InvalidDataException($"Duplicate city ids: {string.Join(", ", duplicateIds)}");
        }

        foreach (var city in cities)
        {
            if (string.IsNullOrWhiteSpace(city.Id))
            {
                throw new InvalidDataException("City is missing an id.");
            }

            if (string.IsNullOrWhiteSpace(city.Name))
            {
                throw new InvalidDataException($"City '{city.Id}' is missing a name.");
            }

            if (city.DemandWeight <= 0)
            {
                throw new InvalidDataException($"City '{city.Id}' must have a positive demand weight.");
            }

            // M59: below MinimumStartingCapacity - not just non-positive
            // (the pre-M59 check this replaces) - since the real fix a
            // real 300-game sweep called for was raising the floor
            // itself, not merely rejecting zero/negative garbage. C#'s
            // relational pattern doesn't match null, so a gateway with no
            // GateCapacity at all is still caught by the null branch
            // exactly as before.
            if (city.IsGateway && city.GateCapacity is null or < MinimumStartingCapacity)
            {
                throw new InvalidDataException($"Gateway city '{city.Id}' must have a GateCapacity of at least {MinimumStartingCapacity}.");
            }

            // M45: AutoCapacity and GateCapacity are two different
            // mechanisms (ownerless shared ceiling vs. negotiated
            // ownership) and never both apply to the same city - carrying
            // both would leave it ambiguous which one QuarterEngine should
            // enforce.
            if (city.AutoCapacity is not null && city.GateCapacity is not null)
            {
                throw new InvalidDataException($"City '{city.Id}' has both AutoCapacity and GateCapacity set - these are mutually exclusive.");
            }

            // M59: same MinimumStartingCapacity floor as gateways - a
            // present-but-null AutoCapacity (no capacity mechanism on
            // this city at all - a legacy fixture predating M45) still
            // doesn't match this relational pattern, preserving that
            // existing behavior unchanged.
            if (city.AutoCapacity is < MinimumStartingCapacity)
            {
                throw new InvalidDataException($"City '{city.Id}' has an AutoCapacity below the minimum starting capacity of {MinimumStartingCapacity}.");
            }
        }
    }
}
