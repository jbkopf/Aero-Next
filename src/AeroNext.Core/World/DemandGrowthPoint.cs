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
/// </summary>
public sealed record DemandGrowthPoint(int Year, double GlobalMultiplier, Region? Region = null, string? CityId = null);
