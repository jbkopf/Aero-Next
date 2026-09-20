using System.Text.Json;
using System.Text.Json.Serialization;
using AeroNext.Core.World;

namespace AeroNext.Core.Airlines;

/// <summary>
/// Loads and validates the fleet dataset. AvailableFromYear is what makes
/// "new aircraft types entering service" (CLAUDE.md's timeline data)
/// data-driven rather than a hardcoded per-quarter branch — M13's campaign
/// loop checks it directly. Deserializes through a DTO with a plain
/// CostDollars field rather than AircraftType's Money directly, since
/// System.Text.Json has no built-in notion of Money and a dollar figure in
/// the JSON is more legible than a nested cents object anyway.
/// Manufacturer/ManufacturerRegion (M16), CostPerAvailableSeatKm, and
/// CruiseSpeedKmh (M32) are optional — a test snippet that omits them gets
/// AircraftType's own "" / null / 0.09 / 900.0 defaults, matching every
/// other generously-defaulted field here.
///
/// Aircraft-acquisition-limits follow-up, Phase (d)
/// (docs/AIRCRAFT_ACQUISITION_ROADMAP.md): EndOfManufactureYear is
/// likewise optional and defaults null ("never stops"), same reasoning —
/// omitted in every real catalog entry still in production as of the
/// campaign's own 2015 end date, since a real-but-later end date (some of
/// these span past 2015, e.g. the 747) has no in-game effect a campaign
/// that never runs past 2015 could ever observe, so there's nothing to
/// research there. Validated against AvailableFromYear (can't stop being
/// manufactured before manufacturing starts) since, unlike the other
/// optional fields here, a backwards value would silently and permanently
/// disable purchase/lease for a real catalog aircraft.
///
/// Geopolitical-aircraft-restrictions follow-up
/// (docs/GEOPOLITICAL_AIRCRAFT_RESTRICTIONS_ROADMAP.md): Bloc is likewise
/// optional and defaults null ("not modeled") - no cross-field validation
/// needed, since unlike EndOfManufactureYear there's no invariant
/// relating it to any other field on this same entry.
/// </summary>
public static class AircraftCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record Entry(
        string Id, string Name, int Seats, int RangeKm, long CostDollars, int ProductionRatePerQuarter, int AvailableFromYear,
        string Manufacturer = "", Region? ManufacturerRegion = null, double CostPerAvailableSeatKm = 0.09, double CruiseSpeedKmh = 900.0,
        int? EndOfManufactureYear = null, PoliticalBloc? Bloc = null);

    public static IReadOnlyList<AircraftType> Load(string json)
    {
        var entries = JsonSerializer.Deserialize<List<Entry>>(json, JsonOptions)
            ?? throw new InvalidDataException("Aircraft data deserialized to null.");

        Validate(entries);

        return entries
            .Select(e => new AircraftType(
                e.Id, e.Name, e.Seats, e.RangeKm, Money.FromDollars(e.CostDollars), e.ProductionRatePerQuarter, e.AvailableFromYear,
                e.Manufacturer, e.ManufacturerRegion, e.CostPerAvailableSeatKm, e.CruiseSpeedKmh, e.EndOfManufactureYear, e.Bloc))
            .OrderBy(a => a.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static IReadOnlyList<AircraftType> LoadFile(string path) => Load(File.ReadAllText(path));

    private static void Validate(List<Entry> entries)
    {
        if (entries.Count == 0)
        {
            throw new InvalidDataException("Aircraft data contained no aircraft.");
        }

        var duplicateIds = entries
            .GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateIds.Count > 0)
        {
            throw new InvalidDataException($"Duplicate aircraft ids: {string.Join(", ", duplicateIds)}");
        }

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                throw new InvalidDataException("Aircraft is missing an id.");
            }

            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                throw new InvalidDataException($"Aircraft '{entry.Id}' is missing a name.");
            }

            if (entry.Seats <= 0)
            {
                throw new InvalidDataException($"Aircraft '{entry.Id}' must have a positive seat count.");
            }

            if (entry.RangeKm <= 0)
            {
                throw new InvalidDataException($"Aircraft '{entry.Id}' must have a positive range.");
            }

            if (entry.ProductionRatePerQuarter <= 0)
            {
                throw new InvalidDataException($"Aircraft '{entry.Id}' must have a positive production rate.");
            }

            if (entry.CostPerAvailableSeatKm <= 0)
            {
                throw new InvalidDataException($"Aircraft '{entry.Id}' must have a positive cost per available seat-km.");
            }

            if (entry.CruiseSpeedKmh <= 0)
            {
                throw new InvalidDataException($"Aircraft '{entry.Id}' must have a positive cruise speed.");
            }

            if (entry.EndOfManufactureYear is not null && entry.EndOfManufactureYear < entry.AvailableFromYear)
            {
                throw new InvalidDataException($"Aircraft '{entry.Id}' has an EndOfManufactureYear before its own AvailableFromYear.");
            }
        }
    }
}
