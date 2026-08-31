namespace AeroNext.Core.Ai;

/// <summary>
/// M39: the shared cash signal both strategies' Suitability scores are
/// built from. Route-count-based signals don't differentiate airlines
/// at the moment it matters most - quarter 1, when every airline has 0
/// routes. Cash does, because StartingConditionsResolver already scales
/// starting cash by RegionalEconomics.Factor per region - a real,
/// deterministic, per-airline signal already flowing into
/// StrategyContext.AvailableCash, no new dependency needed.
/// </summary>
internal static class AiSuitability
{
    /// <summary>(Proposed, tunable) — the baseline every real game's starting cash is scaled around (StartingConditionsResolver.BaseStartingCashDollars).</summary>
    private static readonly Money ReferenceCash = Money.FromDollars(20_000_000);

    /// <summary>0..1, clamped. Null AvailableCash (a permissive caller) is treated as neutral.</summary>
    public static double CashRatio(StrategyContext context)
    {
        if (context.AvailableCash is null)
        {
            return 0.5;
        }

        var ratio = (double)context.AvailableCash.Value.Cents / ReferenceCash.Cents;
        return Math.Clamp(ratio, 0.0, 1.0);
    }
}
