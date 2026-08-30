namespace AeroNext.Core.Turns;

/// <summary>
/// A private action (CLAUDE.md: "resolves against nothing") — always
/// free, same framing RefinanceOrder already uses. Reduces DebtBalance by
/// Amount using cash on hand — the reverse of RefinanceOrder's borrowing,
/// on top of (not instead of) the automatic principal component every
/// quarter's debt service already includes (M29). Rejected outright, not
/// silently clamped, if Amount would exceed either cash on hand or the
/// current debt balance — the same "never attempt what will predictably
/// fail" principle already settled for cash affordability.
/// </summary>
public sealed record PayDownDebtOrder(Money Amount) : QuarterOrder;
