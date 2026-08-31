namespace AeroNext.Core.Ai;

/// <summary>
/// M39 (docs/BASELINE_AI_ROADMAP.md): which phase of the campaign a
/// quarter falls in — tags which strategies are eligible to run then
/// (see IAiStrategy.ApplicableEras). Boundaries are (proposed, tunable),
/// chosen against this session's own measured data: every AeroNext.Sim
/// M13-loop bankruptcy from M34 through M38 landed inside quarter 17-56,
/// entirely within the Starting window below.
/// </summary>
public enum GameEra
{
    Starting,
    MidGame,
    LateGame,
}

/// <summary>Naming mirrors AeroNext.Core.World.HomeRegions — a small static helper alongside its own enum, not an extension method.</summary>
public static class GameEras
{
    /// <summary>Quarters 1-40 (1955-1964), 41-160 (1965-1994), 161+ (1995-2015) — a 244-quarter campaign never exceeds LateGame.</summary>
    public static GameEra ForQuarter(int quarterNumber)
    {
        if (quarterNumber <= 40)
        {
            return GameEra.Starting;
        }

        return quarterNumber <= 160 ? GameEra.MidGame : GameEra.LateGame;
    }
}
