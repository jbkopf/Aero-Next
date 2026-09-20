using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroNext.Core.World;

/// <summary>
/// Loads and validates the trade-restriction dataset
/// (docs/GEOPOLITICAL_AIRCRAFT_RESTRICTIONS_ROADMAP.md), same
/// deserialize-then-validate shape <see cref="Airlines.AircraftCatalog"/>
/// already has for aircraft.json. Unlike that loader, validating a
/// restriction's own CityId needs an external list - a City is defined in
/// a separate file this class has no reason to load itself - so the known
/// city ids are supplied by the caller rather than re-derived here.
/// </summary>
public static class TradeRestrictionCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<TradeRestriction> Load(string json, IReadOnlySet<string> knownCityIds)
    {
        var restrictions = JsonSerializer.Deserialize<List<TradeRestriction>>(json, JsonOptions)
            ?? throw new InvalidDataException("Trade restriction data deserialized to null.");

        Validate(restrictions, knownCityIds);

        return restrictions
            .OrderBy(r => r.CityId, StringComparer.Ordinal)
            .ThenBy(r => r.RestrictedBloc)
            .ToList();
    }

    public static IReadOnlyList<TradeRestriction> LoadFile(string path, IReadOnlySet<string> knownCityIds) =>
        Load(File.ReadAllText(path), knownCityIds);

    private static void Validate(List<TradeRestriction> restrictions, IReadOnlySet<string> knownCityIds)
    {
        foreach (var restriction in restrictions)
        {
            if (string.IsNullOrWhiteSpace(restriction.CityId))
            {
                throw new InvalidDataException("Trade restriction is missing a CityId.");
            }

            if (!knownCityIds.Contains(restriction.CityId))
            {
                throw new InvalidDataException($"Trade restriction references unknown city '{restriction.CityId}'.");
            }

            if (restriction.EndYear is not null && restriction.EndYear < restriction.StartYear)
            {
                throw new InvalidDataException($"Trade restriction for '{restriction.CityId}'/{restriction.RestrictedBloc} has an EndYear before its own StartYear.");
            }
        }

        // Catches a copy-paste mistake, not a legitimate case - a city can
        // legitimately have two restrictions for two different blocs (or
        // even the same bloc reimposed after a real historical thaw and
        // relapse), just never the exact same row twice.
        if (restrictions.Count != restrictions.Distinct().Count())
        {
            throw new InvalidDataException("Trade restriction data contains an exact duplicate row.");
        }
    }
}
