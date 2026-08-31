using System.Text.Json;
using System.Text.Json.Serialization;

namespace AeroNext.Core.World;

/// <summary>
/// Loads the demand growth schedule — piecewise-constant breakpoints, not
/// interpolated, so a given year's multiplier is simple and deterministic:
/// whichever breakpoint's year is the latest one at or before it. M20:
/// breakpoints can now be global, region-scoped, or city-scoped
/// (<see cref="DemandGrowthPoint"/>) — "latest at or before" resolution
/// applies independently within each scope, not across all of them at
/// once, so two breakpoints sharing a year in different scopes are both
/// valid, not a duplicate. M21 (trend/event split): "latest at or
/// before" now also applies independently per <see cref="DemandGrowthPointKind"/>
/// within each scope, not just per scope - a global Trend breakpoint and
/// a global Event breakpoint can legitimately share a year too. Every
/// Event breakpoint's own <see cref="DemandGrowthPoint.EventGroup"/>
/// further isolates it from every *other* independent event at the same
/// scope, so multiple events can compose instead of one silently
/// overwriting another.
/// </summary>
public static class DemandGrowthCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }, // M20: Region is now a possible field, same converter CityCatalog already uses
    };

    public static IReadOnlyList<DemandGrowthPoint> Load(string json)
    {
        var points = Deserialize(json);

        Validate(points);

        return points.OrderBy(p => p.Year).ToList();
    }

    public static IReadOnlyList<DemandGrowthPoint> LoadFile(string path) => Load(File.ReadAllText(path));

    /// <summary>
    /// M21: the one entry point real callers use — loads the global-only
    /// baseline curve (<c>demand_growth.json</c>) together with every
    /// authored region/city-scoped story (<c>historical_events.json</c>,
    /// the file this roadmap's own round-by-round event series grows
    /// story by story) and validates the *combined* result once.
    /// Validating separately would be wrong, not just redundant:
    /// `historical_events.json` alone will always have zero global
    /// entries, which the "at least one global breakpoint" rule would
    /// wrongly reject on its own. `Load`/`LoadFile` stay independently
    /// useful (tests load a single inline schedule all the time) - this
    /// method doesn't replace them, it composes them.
    /// </summary>
    public static IReadOnlyList<DemandGrowthPoint> LoadCampaignSchedule(string dataDirectory)
    {
        var baseline = Deserialize(File.ReadAllText(Path.Combine(dataDirectory, "demand_growth.json")));
        var events = Deserialize(File.ReadAllText(Path.Combine(dataDirectory, "historical_events.json")));
        var combined = baseline.Concat(events).ToList();

        Validate(combined);

        return combined.OrderBy(p => p.Year).ToList();
    }

    private static List<DemandGrowthPoint> Deserialize(string json) =>
        JsonSerializer.Deserialize<List<DemandGrowthPoint>>(json, JsonOptions)
            ?? throw new InvalidDataException("Demand growth data deserialized to null.");

    private static void Validate(List<DemandGrowthPoint> points)
    {
        if (points.Count == 0)
        {
            throw new InvalidDataException("Demand growth data contained no breakpoints.");
        }

        if (points.All(p => p.Region is not null || p.CityId is not null || p.Kind != DemandGrowthPointKind.Trend))
        {
            throw new InvalidDataException("Demand growth data must include at least one global (unscoped) Trend breakpoint — Timeline.DemandMultiplierForYear's own fallback relies on one always existing. A global Event alone (e.g. the 747, an oil crisis) doesn't count - it composes on top of the trend, it doesn't replace it.");
        }

        var scopedTwice = points.Where(p => p.Region is not null && p.CityId is not null).ToList();

        if (scopedTwice.Count > 0)
        {
            throw new InvalidDataException(
                $"Demand growth breakpoints for year(s) {string.Join(", ", scopedTwice.Select(p => p.Year))} set both Region and CityId — a breakpoint must be global, region-scoped, or city-scoped, never more than one.");
        }

        // M21 (trend/event split, correctness fix found during
        // verification): an Event breakpoint without an EventGroup would
        // silently share one flat "latest wins" pool with every *other*
        // ungrouped event at the same scope - exactly the bug this
        // field exists to prevent (a later, unrelated event's
        // breakpoint permanently overwriting an earlier event's own
        // effect, including past its own reversion year). A Trend
        // breakpoint never needs one - there's only ever one trend line
        // per scope, nothing to group.
        var missingEventGroup = points.Where(p => p.Kind == DemandGrowthPointKind.Event && p.EventGroup is null).ToList();
        var trendWithEventGroup = points.Where(p => p.Kind == DemandGrowthPointKind.Trend && p.EventGroup is not null).ToList();

        if (missingEventGroup.Count > 0)
        {
            throw new InvalidDataException($"Demand growth Event breakpoints for year(s) {string.Join(", ", missingEventGroup.Select(p => p.Year))} have no EventGroup — every Event breakpoint must name which event it belongs to, so independent events can compose instead of overwriting each other.");
        }

        if (trendWithEventGroup.Count > 0)
        {
            throw new InvalidDataException($"Demand growth Trend breakpoints for year(s) {string.Join(", ", trendWithEventGroup.Select(p => p.Year))} set an EventGroup — only Event breakpoints use one.");
        }

        // M20: two breakpoints may now legitimately share a year if
        // they're in different scopes (e.g. a global 1970 entry and an
        // EastAsia-scoped 1970 entry) - a duplicate is the same year
        // *and* the same scope, not just the same year. M21 (trend/event
        // split): also not a duplicate if they're different *kinds* -
        // 1970 legitimately carries both a global Trend breakpoint
        // (the base curve) and a global Event breakpoint (the 747), the
        // same year, on purpose - nor if they're different *event
        // groups* at the same scope/kind/year (two independent events
        // both starting the same year).
        var duplicateScopeYears = points
            .GroupBy(p => (p.Year, p.Region, p.CityId, p.Kind, p.EventGroup))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Year)
            .ToList();

        if (duplicateScopeYears.Count > 0)
        {
            throw new InvalidDataException($"Duplicate demand growth years within the same scope: {string.Join(", ", duplicateScopeYears)}");
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
