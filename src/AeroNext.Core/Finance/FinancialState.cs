namespace AeroNext.Core.Finance;

/// <summary>
/// IsBankrupt is sticky — once true, it stays true permanently. There's no
/// reorganization mechanic; a bankrupt airline is out, full stop.
///
/// InDistress is the one-quarter grace period before that: cash negative
/// for the first time sets it true without touching IsBankrupt, giving
/// the airline's very next turn a real chance to react (borrow, sell
/// aircraft, close or shrink a route — see FinancialResolver.Resolve).
/// Cash negative *again* the following quarter while still InDistress is
/// what actually flips IsBankrupt; recovering to non-negative cash at any
/// point clears it back to false instead. Not sticky the way IsBankrupt
/// is — it's a warning flag, not a verdict.
/// </summary>
public sealed record FinancialState(Money CashBalance, Money DebtBalance, double InterestRatePerQuarter, bool IsBankrupt, bool InDistress = false)
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
