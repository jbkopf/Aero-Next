namespace AeroNext.Core.World;

/// <summary>
/// M13: a piecewise-constant demand-growth breakpoint. M20 generalizes
/// scope beyond "the whole world" — <see cref="Region"/> and
/// <see cref="CityId"/> are new, optional, and mutually exclusive
/// (<see cref="DemandGrowthCatalog"/> validates this at load time); a
/// breakpoint is global (today's only shape, every existing
/// `demand_growth.json` entry unchanged) exactly when both are null.
/// <see cref="GlobalMultiplier"/> keeps its name regardless of scope —
/// it's the breakpoint's own multiplier value either way, and renaming
/// it would mean renaming the `demand_growth.json` property key for
/// every existing entry too, for no real gain.
///
/// M21: <see cref="Name"/> — optional, mirroring the id/name convention
/// every other data file in this repo already uses
/// (<c>cities.json</c>, <c>aircraft.json</c>). Pure documentation, never
/// read by resolution logic (<see cref="Timeline"/>,
/// <see cref="GravityDemandModel"/> both ignore it entirely) — exists so
/// a data file full of authored historical stories
/// (<c>historical_events.json</c>) is self-identifying on its own, not
/// something a future author has to cross-reference against this
/// roadmap's prose to understand.
///
/// M21 (trend/event split): <see cref="Kind"/> — defaults to
/// <see cref="DemandGrowthPointKind.Trend"/>, so every entry authored
/// before this field existed (the base curve, Dubai's own story) is
/// unaffected. See <see cref="DemandGrowthPointKind"/> for why trend and
/// event are resolved and composed separately rather than sharing one
/// "latest breakpoint wins" pool per scope.
///
/// <see cref="EventGroup"/> — required on every <see cref="Kind"/> ==
/// <see cref="DemandGrowthPointKind.Event"/> breakpoint
/// (<see cref="DemandGrowthCatalog"/> validates this), null on every
/// <see cref="DemandGrowthPointKind.Trend"/> one (a scope only ever has
/// one trend line, nothing to group). A real, load-bearing correctness
/// fix, not just an id: two *independent* events sharing a scope (the
/// 747's permanent step-up and, later, the 1973 oil crisis's temporary
/// dip both being global) must resolve and compose separately from each
/// other too, not just from the trend - grouping is what makes that
/// possible instead of the later event's own breakpoint silently
/// overwriting the earlier event's, discarding it permanently even past
/// its own reversion year. Distinct from <see cref="Name"/>: this is a
/// stable machine key resolution logic actually reads
/// (<c>"747"</c>, <c>"oil-crisis-1973"</c>, ...), not the human-readable
/// label.
/// </summary>
public sealed record DemandGrowthPoint(
    int Year, double GlobalMultiplier, Region? Region = null, string? CityId = null, string? Name = null,
    DemandGrowthPointKind Kind = DemandGrowthPointKind.Trend, string? EventGroup = null);
