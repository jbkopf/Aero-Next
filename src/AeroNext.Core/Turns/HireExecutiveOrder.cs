namespace AeroNext.Core.Turns;

/// <summary>
/// A private action (CLAUDE.md: "resolves against nothing") — costs no
/// *action* budget, but as of M49 is no longer free: a one-time hiring
/// cost (FinancialResolver.ExecutiveHiringCost), a recurring per-quarter
/// salary for as long as the airline keeps this executive on staff
/// (FinancialResolver.ExecutiveSalaryPerQuarter), and a hard headcount
/// cap (QuarterEngine.MaxExecutives) — see QuarterEngine.RunQuarter's own
/// rejection logic. Raises the airline's action cap by
/// QuarterEngine.ActionCapPerExecutive starting next quarter; never
/// affects the budget for the quarter it's submitted in. No fields —
/// each order always means "hire exactly one" (a fixed per-executive cap
/// increase, not a player-chosen amount, is what makes "cap of 4
/// executives" a coherent headcount rather than an arbitrary point pool).
/// </summary>
public sealed record HireExecutiveOrder : QuarterOrder;
