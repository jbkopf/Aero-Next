using AeroNext.Core.Competition;
using AeroNext.Core.World;
using Xunit;

namespace AeroNext.Core.Tests;

public class WinConditionCheckerTests
{
    private static readonly IReadOnlySet<string> NoEligibility = new HashSet<string>();

    [Fact]
    public void CheckWinner_EmptyState_ReturnsNull()
    {
        Assert.Null(WinConditionChecker.CheckWinner(RegionalDominanceState.Empty, NoEligibility));
    }

    [Fact]
    public void CheckWinner_StreakBelowRequiredQuarters_NotCounted()
    {
        // Two eligible airlines - isolates the dominance-streak check
        // from the sole-survivor path (which would trivially return "A"
        // if it were the only one eligible, masking what this test means
        // to prove).
        var streaks = new Dictionary<(string, Region), int>
        {
            [("A", Region.NorthAmerica)] = WinConditionChecker.RequiredStreakQuarters - 1,
        };

        Assert.Null(WinConditionChecker.CheckWinner(new RegionalDominanceState(streaks), new HashSet<string> { "A", "B" }));
    }

    [Fact]
    public void CheckWinner_EnoughRegionsAtRequiredStreak_ReturnsThatAirline()
    {
        var regions = HomeRegions.All.Take(WinConditionChecker.RequiredRegionCount).ToList();
        var streaks = regions.ToDictionary(r => ("A", r), _ => WinConditionChecker.RequiredStreakQuarters);

        Assert.Equal("A", WinConditionChecker.CheckWinner(new RegionalDominanceState(streaks), new HashSet<string> { "A", "B" }));
    }

    [Fact]
    public void CheckWinner_OneRegionShortOfRequiredCount_ReturnsNull()
    {
        var regions = HomeRegions.All.Take(WinConditionChecker.RequiredRegionCount - 1).ToList();
        var streaks = regions.ToDictionary(r => ("A", r), _ => WinConditionChecker.RequiredStreakQuarters);

        Assert.Null(WinConditionChecker.CheckWinner(new RegionalDominanceState(streaks), new HashSet<string> { "A", "B" }));
    }

    [Fact]
    public void CheckWinner_RegionsSpreadAcrossDifferentAirlines_NeitherQualifies()
    {
        // Each airline individually falls short of RequiredRegionCount,
        // even though together they cover enough regions - the condition
        // is per-airline, not a combined total.
        var regions = HomeRegions.All.Take(WinConditionChecker.RequiredRegionCount).ToList();
        var streaks = new Dictionary<(string, Region), int>();
        for (var i = 0; i < regions.Count; i++)
        {
            streaks[(i % 2 == 0 ? "A" : "B", regions[i])] = WinConditionChecker.RequiredStreakQuarters;
        }

        Assert.Null(WinConditionChecker.CheckWinner(new RegionalDominanceState(streaks), new HashSet<string> { "A", "B" }));
    }

    [Fact]
    public void CheckWinner_MultipleAirlinesQualifySimultaneously_PicksAlphabeticallyFirst_Deterministically()
    {
        var regions = HomeRegions.All.Take(WinConditionChecker.RequiredRegionCount).ToList();
        var streaks = new Dictionary<(string, Region), int>();
        foreach (var r in regions)
        {
            streaks[("Zulu", r)] = WinConditionChecker.RequiredStreakQuarters;
            streaks[("Alpha", r)] = WinConditionChecker.RequiredStreakQuarters;
        }

        Assert.Equal("Alpha", WinConditionChecker.CheckWinner(new RegionalDominanceState(streaks), new HashSet<string> { "Alpha", "Zulu" }));
    }

    // --- M23 follow-up: sole survivor / mutual elimination ---

    [Fact]
    public void CheckWinner_SoleEligibleSurvivor_NoDominanceState_WinsOutright()
    {
        // A solo survivor that never independently satisfied the
        // dominance condition (e.g. a home-region-only airline that
        // never expanded) still wins the instant it's the last one
        // standing - "at any point," per the user's own direction.
        Assert.Equal("A", WinConditionChecker.CheckWinner(RegionalDominanceState.Empty, new HashSet<string> { "A" }));
    }

    [Fact]
    public void CheckWinner_DominanceWinnerTakesPriorityOverSoleSurvivorCheck()
    {
        // Not a real conflict in practice (a bankrupt airline can never
        // appear as a dominance leader - RegionalDominanceTracker already
        // excludes it), but confirms the dominance path is checked first
        // and returns the same, correct name either way.
        var regions = HomeRegions.All.Take(WinConditionChecker.RequiredRegionCount).ToList();
        var streaks = regions.ToDictionary(r => ("A", r), _ => WinConditionChecker.RequiredStreakQuarters);

        Assert.Equal("A", WinConditionChecker.CheckWinner(new RegionalDominanceState(streaks), new HashSet<string> { "A" }));
    }

    [Fact]
    public void CheckWinner_TwoOrMoreEligible_NoDominanceWinner_StaysNull()
    {
        // This method's own job stops here - the end-of-campaign
        // "declare a winner from the ranking" fallback is
        // CampaignRanking.DeclareWinner's job, not this one's.
        Assert.Null(WinConditionChecker.CheckWinner(RegionalDominanceState.Empty, new HashSet<string> { "A", "B", "C" }));
    }

    [Fact]
    public void CheckWinner_ZeroEligible_MutualElimination_StaysNull()
    {
        // The one real stalemate case: every remaining airline went
        // bankrupt the same quarter, nobody left to declare a winner from.
        Assert.Null(WinConditionChecker.CheckWinner(RegionalDominanceState.Empty, NoEligibility));
    }
}
