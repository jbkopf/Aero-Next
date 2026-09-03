using AeroNext.Core.World;

namespace AeroNext.Core.Turns;

/// <summary>
/// A contested action (CLAUDE.md: "several airlines target the same scarce
/// object") — commits cash toward landing rights at TargetCity. Always
/// costs 1 action, unlike market actions which can be free; see
/// <see cref="QuarterEngine"/>. Doesn't resolve this quarter — see
/// AeroNext.Core.Negotiations.LandingRightsResolver.
///
/// M59: SlotsRequested — real multi-slot bidding, one bid requesting
/// several slots at once instead of needing several separate contested
/// rounds. Defaults to 1, so every pre-M59 call site (every AI strategy
/// today) compiles and behaves identically. Bid stays a single flat
/// amount for the whole request, regardless of quantity — no per-slot
/// cost scaling; the bidder chooses how much to commit for however many
/// slots they're asking for. Clamped at resolution time
/// (LandingRightsResolver.Submit) to at most
/// LandingRightsResolver.BidSlotRequestCapFraction of the target city's
/// own current total slots, minimum 1 — never rejected outright for
/// asking too much, just scaled down.
/// </summary>
public sealed record NegotiationOrder(City TargetCity, Money Bid, int SlotsRequested = 1) : QuarterOrder;
