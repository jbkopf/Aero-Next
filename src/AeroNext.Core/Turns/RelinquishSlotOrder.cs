using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// A private action (CLAUDE.md: "resolves against nothing") — always
/// free, same framing CloseRouteOrder already uses. Returns one held
/// gate slot at TargetCity to the contested pool for future negotiation
/// by anyone — not a direct sale to a specific rival, which would need a
/// whole new bilateral-transaction mechanic this game doesn't otherwise
/// have. No cash returned: the bid was already spent at submission (M28),
/// this just stops occupying a slot that isn't wanted anymore.
///
/// Gated by *available* balance (held minus what current routes already
/// occupy — see AeroNext.Core.Negotiations.LandingRightsBoard.
/// AvailableSlotsByCity), not raw held count: a slot backing a live route
/// can't be given up without first freeing it via CloseRouteOrder (M27) —
/// otherwise the airline would end up flying more aircraft through a
/// gateway than it holds slots for. See AeroNext.Core.Negotiations.
/// LandingRightsResolver.Relinquish for the actual board mutation, which
/// happens outside QuarterEngine (same split NegotiationOrder already
/// has between engine-side eligibility and caller-side board state).
/// </summary>
public sealed record RelinquishSlotOrder(City TargetCity) : QuarterOrder;
