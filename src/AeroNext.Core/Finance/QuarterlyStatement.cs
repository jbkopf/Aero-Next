namespace AeroNext.Core.Finance;

/// <summary>
/// M26: AircraftPurchaseCost, LeaseFee, and RetirementProceeds are broken
/// out as their own lines rather than folded silently into NetIncome —
/// CLAUDE.md's legibility pillar means a player sees *why* cash moved,
/// not just the net result of three new cost/revenue sources at once.
/// All three default to Money.Zero so a Resolve call that never touches
/// aircraft economics (fleet/retiredUnits both null) reads exactly as it
/// did before this milestone. M28: NegotiationBidCost gets the same
/// treatment — every applied NegotiationOrder's Bid this quarter, charged
/// whether or not it eventually wins (see FinancialResolver).
///
/// M29: DebtService now means interest *plus* PrincipalRepayment combined
/// (real-world "debt service" has always meant both) — PrincipalRepayment
/// is broken out separately so DebtService - PrincipalRepayment recovers
/// interest alone. ManualDebtPaydown mirrors Borrowed's own treatment: a
/// discretionary balance-sheet swap, not an operating cost, kept outside
/// NetIncome (see FinancialResolver) the same way Borrowed already is.
///
/// M30: SalaryCost is a flat, per-aircraft-unit charge (owned + leased,
/// idle or assigned) — the same "broken out, not folded" legibility
/// treatment, folded into NetIncome like an ordinary operating cost.
///
/// M47: MarketingCost is the same treatment again — every applied
/// NetworkMarketingOrder/RouteMarketingOrder's own cost this quarter
/// (see MarketingCostCalculator), folded into NetIncome like an ordinary
/// operating cost, broken out on its own line rather than silently
/// absorbed.
///
/// M49: ExecutiveHiringCost (a one-time charge the same quarter a
/// HireExecutiveOrder is applied) and ExecutiveSalaryCost (a recurring
/// per-quarter charge for however many executives are already on staff)
/// get the same treatment — both fold into NetIncome, both broken out on
/// their own line.
///
/// Bankruptcy-conditions follow-up: InDistress mirrors FinancialState's
/// own field of the same name — true when this quarter ended cash
/// negative but hasn't (yet) triggered IsBankrupt, the one-quarter grace
/// window. Broken out here, not just left on FinancialState, so a caller
/// (or future UI) can see the warning from the statement alone, the same
/// legibility treatment every other line on this record already gets.
/// </summary>
public sealed record QuarterlyStatement(
    Money Revenue,
    Money OperatingCost,
    Money DebtService,
    Money Borrowed,
    Money NetIncome,
    Money CashBalanceAfter,
    bool IsBankrupt,
    Money AircraftPurchaseCost = default,
    Money LeaseFee = default,
    Money RetirementProceeds = default,
    Money NegotiationBidCost = default,
    Money PrincipalRepayment = default,
    Money ManualDebtPaydown = default,
    Money SalaryCost = default,
    Money MarketingCost = default,
    Money ExecutiveHiringCost = default,
    Money ExecutiveSalaryCost = default,
    bool InDistress = default);
