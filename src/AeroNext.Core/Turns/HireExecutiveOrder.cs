namespace AeroNext.Core.Turns;

/// <summary>
/// A private action (CLAUDE.md: "resolves against nothing") — always free,
/// raises the airline's action cap starting next quarter. Never affects the
/// budget for the quarter it's submitted in.
/// </summary>
public sealed record HireExecutiveOrder(int CapIncrease = 1) : QuarterOrder;
