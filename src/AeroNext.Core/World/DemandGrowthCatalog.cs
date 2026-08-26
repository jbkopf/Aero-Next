using System.Text.Json;

namespace AeroNext.Core.World;

/// <summary>
/// Loads the demand growth schedule — piecewise-constant breakpoints, not
/// interpolated, so a given year's multiplier is simple and deterministic:
/// whichever breakpoint's year is the latest one at or before it.
/// </summary>
public static class DemandGrowthCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static IReadOnlyList<DemandGrowthPoint> Load(string json)
    {
        var points = JsonSerializer.Deserialize<List<DemandGrowthPoint>>(json, JsonOptions)
            ?? throw new InvalidDataException("Demand growth data deserialized to null.");

        Validate(points);

        return points.OrderBy(p => p.Year).ToList();
    }

    public static IReadOnlyList<DemandGrowthPoint> LoadFile(string path) => Load(File.ReadAllText(path));

    private static void Validate(List<DemandGrowthPoint> points)
    {
        if (points.Count == 0)
        {
            throw new InvalidDataException("Demand growth data contained no breakpoints.");
        }

        var duplicateYears = points
            .GroupBy(p => p.Year)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateYears.Count > 0)
        {
            throw new InvalidDataException($"Duplicate demand growth years: {string.Join(", ", duplicateYears)}");
        }

        foreach (var point in points)
        {
            if (point.GlobalMultiplier <= 0)
            {
                throw new InvalidDataException($"Demand growth multiplier for year {point.Year} must be positive.");
            }
        }
    }
}
