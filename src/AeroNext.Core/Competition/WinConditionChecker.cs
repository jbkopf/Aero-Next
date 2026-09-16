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
/// campaign.
///
/// Difficulty follow-up: <see cref="RequiredRegionCount"/> is now a real,
/// setup-time-selectable difficulty axis - see <see cref="GameDifficulty"/>
/// and <see cref="DifficultySettings"/>. The constant below is
/// <see cref="GameDifficulty.Normal"/>'s own value and the default for any
/// caller that doesn't pass an explicit one (every existing call site
/// before this round); <see cref="CheckWinner"/>'s own optional parameter
/// is what a real difficulty-aware caller overrides.
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
    /// A real, contestable bar, not a formality - GameDifficulty.Normal's
    /// own value. Raising this alone to 4 (round 1 of the difficulty
    /// investigation, docs/OPEN_WORK.md) very nearly breaks the win
    /// condition outright: genuine dominance wins collapsed from 89.7% of
    /// games to 0.7%. A follow-up diagnostic found why: not a
    /// streak-length problem (RequiredStreakQuarters is nearly irrelevant
    /// here) - a breadth-*ambition* problem. BalancedGrowthStrategy's own
    /// "keep chasing new regions" gate was still capped at 3, so every
    /// region except Europe/SouthWestAsia never even attempted a 4th
    /// region - only 2 of 1,200 sampled seats (0.17%) ever touched 4
    /// simultaneous regions led at all, even momentarily. Round 2 (raising
    /// both in lockstep) recovered a real 40.7% genuine-dominance rate,
    /// still meaningfully harder than Normal - that pairing is now
    /// GameDifficulty.Hard, see DifficultySettings.
    /// </summary>
    public const int RequiredRegionCount = 3;

    /// <summary>One year — long enough that a single lucky quarter can't flip the game.</summary>
    public const int RequiredStreakQuarters = 4;

    /// <param name="eligibleAirlineNames">Non-bankrupt airlines as of this exact quarter — see the class doc comment's own sole-survivor path.</param>
    /// <param name="requiredRegionCount">
    /// Difficulty follow-up: defaults to <see cref="RequiredRegionCount"/>
    /// (GameDifficulty.Normal) for every pre-existing call site - a real
    /// difficulty-aware caller passes <see cref="DifficultySettings.RegionCounts"/>'s
    /// own value instead. Never read from the constant implicitly inside
    /// this method, so there is exactly one place a caller can get this
    /// wrong: forgetting to pass it at all, which is the same as choosing
    /// Normal.
    /// </param>
    /// <returns>The winning airline's name, or null if nobody has met either condition yet.</returns>
    public static string? CheckWinner(RegionalDominanceState state, IReadOnlySet<string> eligibleAirlineNames, int requiredRegionCount = RequiredRegionCount)
    {
        var dominanceWinner = state.StreakByLeader
            .Where(kv => kv.Value >= RequiredStreakQuarters)
            .GroupBy(kv => kv.Key.AirlineName, kv => kv.Key.Region)
            .Where(g => g.Count() >= requiredRegionCount)
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
