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

            if (city.IsGateway && city.GateCapacity is null or <= 0)
            {
                throw new InvalidDataException($"Gateway city '{city.Id}' must have a positive gate capacity.");
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

            if (city.AutoCapacity is <= 0)
            {
                throw new InvalidDataException($"City '{city.Id}' has a non-positive AutoCapacity.");
            }
        }
    }
}
