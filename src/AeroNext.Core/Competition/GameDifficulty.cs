namespace AeroNext.Core.Competition;

/// <summary>
/// Game-setup-time win-condition difficulty - closes CLAUDE.md's own
/// long-deferred note (WinConditionChecker's own doc comment:
/// "difficulty tiers (which would vary RequiredRegionCount) don't exist
/// anywhere in the game yet"). One value per game, chosen at setup,
/// never per-seat/per-airline - every airline in the same game plays the
/// same bar. Currently the only axis this controls is regional-dominance
/// breadth (see <see cref="DifficultySettings"/>); a natural place to add
/// further axes later without touching this enum's own existing values.
/// </summary>
public enum GameDifficulty
{
    /// <summary>
    /// The shipped, validated default - WinConditionChecker.RequiredRegionCount's
    /// own historical value (3 of DominanceTrackedRegions.All's 7 regions).
    /// A real 300-game sweep found this a reliable, contestable bar:
    /// genuine dominance decides 89.7% of games, average win quarter 97.4.
    /// </summary>
    Normal,

    /// <summary>
    /// A real, measured, genuinely harder bar - not a placeholder value.
    /// docs/OPEN_WORK.md's "RequiredRegionCount 3->4" investigation (two
    /// rounds): raising the win threshold alone very nearly breaks the
    /// win condition outright (genuine dominance collapses to 0.7% of
    /// games) because BalancedGrowthStrategy's own breadth ambition was
    /// still capped at 3 - fixed by raising
    /// <see cref="Ai.BalancedGrowthStrategy"/>'s own target in lockstep
    /// (see <see cref="DifficultySettings"/>), which recovers a real
    /// 40.7% genuine-dominance rate. Still meaningfully harder and slower
    /// than Normal (average win quarter 205.0, EastAsia's own advantage
    /// compounds rather than levels out) - a real difficulty choice, not
    /// a balanced alternative default.
    /// </summary>
    Hard,
}

/// <summary>
/// Maps <see cref="GameDifficulty"/> to the concrete values it drives.
/// <see cref="RegionCountSettings.RequiredRegionCount"/> and
/// <see cref="RegionCountSettings.TargetRegionCount"/> are set together,
/// not independently - the round-1 experiment (RequiredRegionCount alone)
/// found the AI's own ambition, not its strength, was the actual
/// bottleneck once the bar was raised; round 2 (both together) is what's
/// encoded in <see cref="GameDifficulty.Hard"/> above. A future difficulty
/// axis that only needs one of the two should still route through this
/// same record rather than reading either strategy's own constant
/// directly, so every knob stays visible in one place.
/// </summary>
public static class DifficultySettings
{
    public static RegionCountSettings RegionCounts(GameDifficulty difficulty) => difficulty switch
    {
        GameDifficulty.Normal => new RegionCountSettings(RequiredRegionCount: WinConditionChecker.RequiredRegionCount, TargetRegionCount: Ai.BalancedGrowthStrategy.DefaultTargetRegionCount),
        GameDifficulty.Hard => new RegionCountSettings(RequiredRegionCount: 4, TargetRegionCount: 4),
        _ => throw new ArgumentOutOfRangeException(nameof(difficulty), difficulty, "Unknown GameDifficulty."),
    };
}

/// <summary>
/// One game's own region-count bar, for both the win condition itself
/// (<see cref="RequiredRegionCount"/>, read by <see cref="WinConditionChecker.CheckWinner"/>)
/// and the AI's own matching breadth ambition
/// (<see cref="TargetRegionCount"/>, read by <see cref="Ai.BalancedGrowthStrategy"/>
/// via <see cref="Ai.StrategyContext.TargetRegionCountOverride"/>). Kept as
/// two named fields rather than a single int specifically because a
/// future difficulty tier might legitimately want them to diverge - see
/// <see cref="DifficultySettings"/>'s own doc comment.
/// </summary>
public readonly record struct RegionCountSettings(int RequiredRegionCount, int TargetRegionCount);
