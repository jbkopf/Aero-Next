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
/// </summary>
public sealed record DemandGrowthPoint(int Year, double GlobalMultiplier, Region? Region = null, string? CityId = null, string? Name = null);
