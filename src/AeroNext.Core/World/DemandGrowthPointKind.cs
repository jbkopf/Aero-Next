namespace AeroNext.Core.World;

/// <summary>
/// Splits <see cref="DemandGrowthPoint"/> into two genuinely separate,
/// composed concepts (per the user's own direction, after round 2's own
/// single-curve authoring experience surfaced the need): the underlying,
/// secular <see cref="Trend"/> a scope grows along on its own, and a
/// discrete <see cref="Event"/> that multiplies on top of it, temporarily
/// or permanently. Resolved and composed independently, never merged
/// into one "latest wins" pool — see <see cref="GravityDemandModel"/>.
/// </summary>
public enum DemandGrowthPointKind
{
    /// <summary>The default (every entry authored before this kind existed is implicitly a trend). Exactly one applies per scope at a time, same "latest breakpoint at or before this year" rule as always.</summary>
    Trend,

    /// <summary>A separate multiplicative layer on top of the trend - defaults to neutral (1.0) whenever none is active. Revert one with a later 1.0 breakpoint in the same scope, or never revert it for a permanent structural shift.</summary>
    Event,
}
