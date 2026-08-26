namespace AeroNext.Core.Finance;

public sealed record QuarterlyStatement(
    Money Revenue,
    Money OperatingCost,
    Money DebtService,
    Money Borrowed,
    Money NetIncome,
    Money CashBalanceAfter,
    bool IsBankrupt);
