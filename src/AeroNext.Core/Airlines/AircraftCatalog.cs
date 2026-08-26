using System.Text.Json;

namespace AeroNext.Core.Airlines;

/// <summary>
/// Loads and validates the fleet dataset. AvailableFromYear is what makes
/// "new aircraft types entering service" (CLAUDE.md's timeline data)
/// data-driven rather than a hardcoded per-quarter branch — M13's campaign
/// loop checks it directly. Deserializes through a DTO with a plain
/// CostDollars field rather than AircraftType's Money directly, since
/// System.Text.Json has no built-in notion of Money and a dollar figure in
/// the JSON is more legible than a nested cents object anyway.
/// </summary>
public static class AircraftCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record Entry(
        string Id, string Name, int Seats, int RangeKm, long CostDollars, int ProductionRatePerQuarter, int AvailableFromYear);

    public static IReadOnlyList<AircraftType> Load(string json)
    {
        var entries = JsonSerializer.Deserialize<List<Entry>>(json, JsonOptions)
            ?? throw new InvalidDataException("Aircraft data deserialized to null.");

        Validate(entries);

        return entries
            .Select(e => new AircraftType(e.Id, e.Name, e.Seats, e.RangeKm, Money.FromDollars(e.CostDollars), e.ProductionRatePerQuarter, e.AvailableFromYear))
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
        }
    }
}
