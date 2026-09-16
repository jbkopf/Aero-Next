using AeroNext.Core.Ai;
using AeroNext.Core.Competition;
using Xunit;

namespace AeroNext.Core.Tests;

public class GameDifficultyTests
{
    [Fact]
    public void RegionCounts_Normal_MatchesTheShippedDefaultsExactly()
    {
        var counts = DifficultySettings.RegionCounts(GameDifficulty.Normal);

        Assert.Equal(WinConditionChecker.RequiredRegionCount, counts.RequiredRegionCount);
        Assert.Equal(BalancedGrowthStrategy.DefaultTargetRegionCount, counts.TargetRegionCount);
    }

    [Fact]
    public void RegionCounts_Hard_RaisesBothInLockstep()
    {
        // docs/OPEN_WORK.md's round-2 measurement: raising RequiredRegionCount
        // alone (without also raising TargetRegionCount) very nearly broke
        // the win condition outright - Hard must always carry both.
        var counts = DifficultySettings.RegionCounts(GameDifficulty.Hard);

        Assert.Equal(4, counts.RequiredRegionCount);
        Assert.Equal(4, counts.TargetRegionCount);
    }

    [Fact]
    public void RegionCounts_UnknownDifficulty_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DifficultySettings.RegionCounts((GameDifficulty)999));
    }
}
