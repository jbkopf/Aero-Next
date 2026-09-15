namespace AeroNext.Core.Competition;

/// <summary>
/// M23: the campaign's actual win condition — Aerobiz Supersonic's own
/// "greatest passengers in multiple regions," adapted to this game's data.
/// An airline wins the instant it simultaneously leads
/// <see cref="RequiredRegionCount"/> different regions (out of
/// DominanceTrackedRegions.All — the 6 home regions plus Africa, see that
/// class's own doc comment), each for at
/// least <see cref="RequiredStreakQuarters"/> consecutive quarters — a
/// single achievement ("you now dominate N markets at once"), not a
/// historical checklist of regions ever led separately across the
/// campaign. Both constants are tunable defaults, not derived from
/// anything — difficulty tiers (which would vary RequiredRegionCount)
/// don't exist anywhere in the game yet.
///
/// M23 follow-up: a second, real win path - the moment only one airline
/// remains solvent, "at any point" per the user's own direction, that
/// airline wins outright, whether or not it's independently satisfied
/// the dominance condition above (not guaranteed even for a sole
/// survivor - e.g. a home-region-only airline that never expanded into
/// 3+ regions). Checked every quarter, exactly like the dominance
/// condition, not just at the campaign's own end - a real 300-game sweep
/// found the campaign running to its full 244 quarters without any
/// winner 94.7% of the time under the dominance condition alone, most of
/// that with 2+ solvent airlines still contesting a real stalemate
/// (see CampaignRanking.DeclareWinner for how that end-of-campaign case
/// gets resolved instead). Zero eligible airlines (every remaining one
/// went bankrupt the same quarter) correctly falls through to null here
/// - nobody left to declare a winner from, the one case that stays a
/// real stalemate.
/// </summary>
public static class WinConditionChecker
{
    /// <summary>
    /// A real, contestable bar, not a formality. Left at 3 rather than
    /// raised when Africa joined DominanceTrackedRegions.All (7 total,
    /// so this is no longer exactly half) — changing the threshold at the
    /// same time as adding a new trackable region would confound
    /// measuring whether Africa itself moved anything.
    /// </summary>
    public const int RequiredRegionCount = 3;

    /// <summary>One year — long enough that a single lucky quarter can't flip the game.</summary>
    public const int RequiredStreakQuarters = 4;

    /// <param name="eligibleAirlineNames">Non-bankrupt airlines as of this exact quarter — see the class doc comment's own sole-survivor path.</param>
    /// <returns>The winning airline's name, or null if nobody has met either condition yet.</returns>
    public static string? CheckWinner(RegionalDominanceState state, IReadOnlySet<string> eligibleAirlineNames)
    {
        var dominanceWinner = state.StreakByLeader
            .Where(kv => kv.Value >= RequiredStreakQuarters)
            .GroupBy(kv => kv.Key.AirlineName, kv => kv.Key.Region)
            .Where(g => g.Count() >= RequiredRegionCount)
            .Select(g => g.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (dominanceWinner is not null)
        {
            return dominanceWinner;
        }

        return eligibleAirlineNames.Count == 1 ? eligibleAirlineNames.Single() : null;
    }
}
