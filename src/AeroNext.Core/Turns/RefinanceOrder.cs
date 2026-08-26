namespace AeroNext.Core.Turns;

/// <summary>
/// A private action (CLAUDE.md: "resolves against nothing") — always free.
/// Borrows AdditionalBorrowing against future debt service: cash goes up
/// immediately, debt balance goes up by the same amount, so next quarter's
/// interest is higher. The bankruptcy-avoidance lever — trading a worse
/// future for cash today.
/// </summary>
public sealed record RefinanceOrder(Money AdditionalBorrowing) : QuarterOrder;
