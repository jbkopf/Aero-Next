namespace AeroNext.Core.Finance;

/// <summary>
/// IsBankrupt is sticky — once true, it stays true permanently. There's no
/// reorganization mechanic; a bankrupt airline is out, full stop.
/// </summary>
public sealed record FinancialState(Money CashBalance, Money DebtBalance, double InterestRatePerQuarter, bool IsBankrupt)
{
    public static FinancialState Start(Money startingCash, Money startingDebt, double interestRatePerQuarter) =>
        new(startingCash, startingDebt, interestRatePerQuarter, IsBankrupt: false);
}
