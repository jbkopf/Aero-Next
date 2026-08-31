using AeroNext.Core.Turns;

namespace AeroNext.Core.Ai;

/// <summary>
/// M39: a named, goal-driven posture an AI-controlled airline can be
/// assigned — see BaselineAi.SelectStrategy for how the roster gets
/// filtered by ApplicableEras and picked by Suitability, and
/// docs/BASELINE_AI_ROADMAP.md's M39 section for the full design.
/// </summary>
public interface IAiStrategy
{
    string Name { get; }

    /// <summary>Which GameEra(s) this strategy is designed for — filters the roster before Suitability is even consulted.</summary>
    IReadOnlySet<GameEra> ApplicableEras { get; }

    /// <summary>
    /// How well this strategy fits the airline's current situation —
    /// used to pick among the eligible (era-filtered) strategies at
    /// assignment/switching time. Not consulted again once assigned;
    /// the airline runs whatever it was assigned until the next
    /// evaluation point.
    /// </summary>
    double Suitability(StrategyContext context);

    IReadOnlyList<QuarterOrder> DecideOrders(StrategyContext context);
}
