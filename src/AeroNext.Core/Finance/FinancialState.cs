namespace AeroNext.Core.Finance;

/// <summary>
/// IsBankrupt is sticky — once true, it stays true permanently. There's no
/// reorganization mechanic; a bankrupt airline is out, full stop.
/// </summary>
public sealed record FinancialState(Money CashBalance, Money DebtBalance, double InterestRatePerQuarter, bool IsBankrupt)
{
    public static FinancialState Start(Money startingCash, Money startingDebt, double interestRatePerQuarter) =>
        new(startingCash, startingDebt, interestRatePerQuarter, IsBankrupt: false);

    /// <summary>
    /// M31 (docs/BASELINE_AI_ROADMAP.md): the one shared affordability
    /// primitive — "cost does not exceed what's actually available."
    /// Static, taking a plain Money rather than reading this record's own
    /// CashBalance, because neither real caller (BaselineAi,
    /// QuarterEngine) has a whole FinancialState in hand at the point it
    /// needs this check — both work from a raw available-cash figure,
    /// often already reduced by whatever's been tentatively committed
    /// earlier in the same planning pass. One named predicate instead of
    /// a `>=`/`>` comparison re-derived inline at every decision point.
    /// </summary>
    public static bool CanAfford(Money available, Money cost) => cost <= available;
}
