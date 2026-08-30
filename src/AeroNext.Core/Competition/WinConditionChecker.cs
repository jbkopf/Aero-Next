namespace AeroNext.Core.Competition;

/// <summary>
/// M23: the campaign's actual win condition — Aerobiz Supersonic's own
/// "greatest passengers in multiple regions," adapted to this game's data.
/// An airline wins the instant it simultaneously leads
/// <see cref="RequiredRegionCount"/> different home regions, each for at
/// least <see cref="RequiredStreakQuarters"/> consecutive quarters — a
/// single achievement ("you now dominate N markets at once"), not a
/// historical checklist of regions ever led separately across the
/// campaign. Both constants are tunable defaults, not derived from
/// anything — difficulty tiers (which would vary RequiredRegionCount)
/// don't exist anywhere in the game yet.
/// </summary>
public static class WinConditionChecker
{
    /// <summary>Half of the 6 home regions — a real, contestable bar, not a formality.</summary>
    public const int RequiredRegionCount = 3;

    /// <summary>One year — long enough that a single lucky quarter can't flip the game.</summary>
    public const int RequiredStreakQuarters = 4;

    /// <returns>The winning airline's name, or null if nobody has met the condition yet.</returns>
    public static string? CheckWinner(RegionalDominanceState state)
    {
        return state.StreakByLeader
            .Where(kv => kv.Value >= RequiredStreakQuarters)
            .GroupBy(kv => kv.Key.AirlineName, kv => kv.Key.Region)
            .Where(g => g.Count() >= RequiredRegionCount)
            .Select(g => g.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}
