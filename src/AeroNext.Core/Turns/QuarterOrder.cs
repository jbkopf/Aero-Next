namespace AeroNext.Core.Turns;

/// <summary>
/// Something an airline submits for a quarter. Every concrete order type
/// costs a defined number of the airline's action budget — see
/// <see cref="QuarterEngine"/> — per CLAUDE.md's contested/market/private
/// classification.
/// </summary>
public abstract record QuarterOrder;
