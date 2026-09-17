# Changing an Existing Route's Aircraft Composition: Design

**Status: implemented.** `AdjustRouteOrder` (`src/AeroNext.Core/Turns/AdjustRouteOrder.cs`),
the `QuarterEngine` changes below, `BriefingGenerator`'s
`RouteAircraftTypeChanged` notification, and `RouteAdjustmentOptions`
(the future UI's query surface) are all shipped, matching this document
exactly — see `QuarterEngineTests.cs`, `BriefingGeneratorTests.cs`, and
`RouteAdjustmentOptionsTests.cs` for the full test coverage. Closes the
"Changing an existing route's aircraft *type*" item in
`BASELINE_AI_ROADMAP.md`'s "Kept in mind" section. Written per the user's
own direct request; kept as a permanent design record, not just a
planning artifact — every rule below is the real, current behavior.

## The problem

Today, a player who wants to fly a route with a different aircraft type
than whatever it currently uses has no seamless way to do it. Two
constraints compound:

1. **`RouteOrder` rejects a type mismatch outright.** `QuarterEngine.cs`
   (lines 437-456): if a pair already has a committed type — from
   `currentRoutes` (last quarter's state) or `committedTypeByPair` (an
   earlier `RouteOrder` for the same pair already applied this same
   quarter) — a `RouteOrder` asking for a *different* type is rejected,
   full stop. No partial/silent replacement.
2. **A close-then-reopen workaround exists, but always costs a gap
   quarter.** `CloseRouteOrder` can free the old units (free, one
   aircraft per application), and next quarter a fresh `RouteOrder` for
   the same pair with a new type succeeds — the pair disappears from
   `currentRoutes` once fully closed, so no stale commitment blocks it.
   But same-quarter close+reopen doesn't work: `QuarterEngine`'s
   validation loop reads `currentRoutes` as the quarter's *starting*
   state, never mutated mid-batch by an applied `CloseRouteOrder`. The
   route is genuinely unserved for one real quarter — lost revenue on
   that pair, and if a rival also serves it, the rival gets the *whole*
   pair uncontested that quarter (`CompetitiveMarket.Resolve`'s
   single-competitor branch).

The user's own direct requirement: this needs to be **seamless from the
player's perspective** — no gap quarter, one menu, submitted as one action.
The player should be able to set both the aircraft count *and* the aircraft
type for an existing route in one step, constrained to types they
currently hold at least one idle unit of, with sufficient range for the
pair.

Separately confirmed: aircraft type has a real cost implication, so
resetting the route's fare to the recomputed default on a type change is
**desired** behavior (not a bug to avoid) — the player just needs a
notification, so they aren't surprised by it.

## Decided scope

Answered directly by the user (recorded here so the reasoning survives
past this conversation):

- **A type change always resets the route to exactly 1 aircraft of the
  new type.** Not a target count chosen up front — the type-change
  action itself is a single, fixed-shape operation. If the player wants
  more than 1 of the new type, they get there by issuing ordinary
  `RouteOrder`s afterward (see below) — which can happen in the very
  same quarter, action budget permitting.
- **Growing beyond 1 uses the existing cost structure, unchanged.**
  Once the swap has happened, the route is just an ordinary
  single-aircraft route of the new type — a ordinary `RouteOrder` for
  that same pair, same type, grows it exactly as it would grow any other
  route today, at today's existing price (1 action per aircraft added).
  This replaces the whole standalone "count" parameter and its own cost
  formula from the first draft of this document — growth is no longer
  something the new order needs to know about at all, it's just
  `RouteOrder` doing what it already does.
- **Freed aircraft** (every unit that was flying the old type):
  become idle, same as `CloseRouteOrder` today. No retire-or-idle choice
  folded into this action — the player manages freed units separately.
- **Relationship to `RouteOrder`/`CloseRouteOrder`**: additive, not a
  replacement. Both keep working exactly as today for ordinary same-type
  growth/shrink. The new order exists only for the one thing neither of
  them can do: change a served pair's aircraft type without a gap
  quarter.
- **AI scope**: player-only this round. The order lives in
  `AeroNext.Core` like every `QuarterOrder`, so nothing structurally
  stops an AI strategy from submitting one later, but no strategy is
  wired to decide when a swap is worthwhile in this pass.

## The new order: `AdjustRouteOrder`

Proposed name — open to a better one. Lives at
`src/AeroNext.Core/Turns/AdjustRouteOrder.cs`, matching the
one-file-per-order-type convention already established
(`RouteOrder.cs`, `CloseRouteOrder.cs`, etc.). No count field — the
result is always 1 aircraft of the new type, so there's nothing else to
parameterize.

```csharp
public sealed record AdjustRouteOrder(City Origin, City Destination, AircraftType AircraftType) : QuarterOrder;
```

- **Applies only to an already-served pair.** A pair with no current
  entry in `currentRoutes` is rejected — the player uses ordinary
  `RouteOrder` to open a genuinely new pair.
- **Rejected if `AircraftType.Id` equals the pair's current type.**
  This order means "change the type" — submitting the same type would
  otherwise be a legal-but-pointless way to shrink an N-aircraft route
  down to 1 for a cost of 1 action, when plain `CloseRouteOrder` already
  does that for free. Rejecting it keeps the order's contract
  unambiguous (it always represents a real type change) and removes a
  confusing, strictly-worse-than-existing-tools edge case from both
  tests and the future UI.
- **Never touches aircraft count beyond fixing it at 1.** A player who
  wants a specific final count submits this order plus however many
  ordinary `RouteOrder`s are needed to reach it — see "Growing after a
  swap" below for exactly how that composes in one quarter.

## `QuarterEngine` changes

### Validation, in order

Let `existingRoute` be the pair's entry in `currentRoutes` at quarter
start (always non-null — an `AdjustRouteOrder` for an unserved pair is
rejected as the very first check), `oldType = existingRoute.Aircraft`.
`existingRoute.AircraftCount` is always ≥ 1 (a route with 0 aircraft
doesn't exist as an entry — `CloseRouteOrder` removes it the instant it
would hit 0), which matters below.

1. **Reject if unserved pair** (no `existingRoute`).
2. **Reject if `AircraftType.Id == oldType.Id`** — not a real type
   change; see the order's own doc note above.
3. **Range check**: reject unless
   `GreatCircle.DistanceKm(Origin, Destination) <= AircraftType.RangeKm`
   — mirrors `RouteOrder.IsWithinRange`.
4. **Era-availability check**: reject unless
   `Timeline.IsAvailable(AircraftType, year)` — mirrors the existing
   `IsAircraftAvailable` check every order type already goes through.
5. **Fleet/idle-unit check**: need exactly 1 idle unit of the *new*
   type. The old-type units being freed are irrelevant to this check —
   they're a different type by rule 2 above, so there's no risk of
   double-counting regardless of processing order.

**No gate-slot or AutoCapacity check is needed at all.** This is a real
simplification over the first draft, and worth stating precisely why:
the route always transitions from `(oldType, oldCount ≥ 1)` to
`(newType, 1)` — the new count (1) can never exceed the old count (≥ 1),
so this order can only ever hold flat or *free* slot/capacity usage at
each endpoint, exactly like `CloseRouteOrder` today (which also needs no
slot check, for the identical reason). `availableSlotsByCity`/
`cityWideSlotsRemaining` only ever gate a net *increase* in aircraft at
a location — this order never causes one by itself.

### Fare handling

`FareOverride` always resets to `null` on an applied `AdjustRouteOrder`
— confirmed desired, since aircraft type has a real cost implication.
`Route.FarePerPassenger` falls back to `FareCalculator`'s recomputed
cost-plus-margin default for the new type.

### Action cost

Flat **1 action**, always — there's no count component to this order
any more, so there's no formula to get wrong. `CloseRouteOrder`-style
private-action pricing (0) was considered and rejected: a type change is
a real, deliberate network decision the player is making, not a
housekeeping action like retiring or refinancing, so it stays priced
like the market-category actions (`RouteOrder`, `NegotiationOrder`) it
sits alongside.

### Growing after a swap, same quarter

Once `AdjustRouteOrder` applies, the pair is just an ordinary
single-aircraft route of the new type — reaching a bigger final count is
nothing but ordinary `RouteOrder`s for that same pair/type, submitted
after it in the same order list, at their existing, unchanged price (1
action each). This is what makes "the player can increase the number,
even in the same turn" true without any new growth logic:

- **Submission order matters.** `QuarterEngine.RunQuarter` processes
  `orders` strictly in list order (already true today — see the
  existing note on `LeaseAircraftOrder` "so a `RouteOrder` later in this
  same batch can actually use the newly-leased unit"). The
  `AdjustRouteOrder` must appear before any same-pair `RouteOrder` in
  the submitted list for a same-quarter swap-then-grow to work. A future
  UI that lets the player pick "type X, count N" in one screen
  translates that into exactly this order internally: one
  `AdjustRouteOrder`, then `N - 1` `RouteOrder`s for the same pair/type,
  in that sequence.
- **`committedTypeByPair` needs one new line.** Today this dictionary is
  only written when an applied `RouteOrder` is processed
  (`QuarterEngine.cs:494`). `AdjustRouteOrder`'s own processing needs the
  identical line — `committedTypeByPair[pairKey] = adjustOrder.AircraftType`
  — so that a later same-batch `RouteOrder` for this pair sees the *new*
  committed type instead of falling back to `currentRoutes`' stale old
  one and getting rejected as a mismatch.
- **The idle-fleet check for those follow-up `RouteOrder`s just works.**
  `workingFleet` is already mutated inline as the batch processes
  (`FleetLedger.Assign`/`Unassign` calls happen per-order, not in one
  batch at the end), so a `RouteOrder` later in the list correctly sees
  the just-freed old-type units as idle (available to reassign to
  *other* routes, same quarter) and one fewer idle new-type unit (the
  one `AdjustRouteOrder` itself just consumed) — no new bookkeeping
  needed here, it's a direct consequence of processing order already
  being sequential.

**The emergent cost of "swap to type B and end up with N of them" is
exactly N actions, regardless of how many aircraft were on the route
before the swap** (1 for the `AdjustRouteOrder` + `N - 1` follow-up
`RouteOrder`s). Worth stating plainly since it's a clean, easy-to-explain
rule for both the player and the future UI, and it's a real behavior
change from the first draft's formula (which charged relative to the
*old* count, not a flat 1-then-count-up-from-there):

| Old state | Requested final count of new type | Actions |
|---|---|---|
| 1 of TypeA | 1 of TypeB | 1 (`AdjustRouteOrder` only) |
| 5 of TypeA | 1 of TypeB | 1 (`AdjustRouteOrder` only — the other 4 TypeA units land idle) |
| 3 of TypeA | 3 of TypeB | 3 (`AdjustRouteOrder` + 2 `RouteOrder`s) |
| 3 of TypeA | 6 of TypeB | 6 (`AdjustRouteOrder` + 5 `RouteOrder`s) |
| 3 of TypeA | 5 of TypeA (no type change) | 2 — unaffected, ordinary `RouteOrder` growth, `AdjustRouteOrder` never involved |

### `ApplyRouteOrders` changes

Needs a new case alongside the existing `RouteOrder`/`CloseRouteOrder`/
`SetFareOrder` cases (`QuarterEngine.cs:635-682`):

```csharp
else if (order is AdjustRouteOrder adjustOrder)
{
    var key = (adjustOrder.Origin.Id, adjustOrder.Destination.Id);
    byKey[key] = new Route(adjustOrder.Origin, adjustOrder.Destination, adjustOrder.AircraftType, AircraftCount: 1, FareOverride: null);
}
```

This is the piece that actually delivers "seamless, no gap": the pair's
entry transitions directly from `(oldType, oldCount)` to `(newType, 1)`
in one fold step — `CompetitiveMarket`/`NetworkPlanner` only ever see
the *final* post-fold route list for the quarter, never an intermediate
"removed" state. Because `byKey` is mutated in place as the fold
proceeds, a later `RouteOrder` for the same pair in the same `applied`
list sees `existing.Aircraft.Id == routeOrder.Aircraft.Id` (the type
this same batch just set) and correctly grows `AircraftCount` from
there — the existing `ApplyRouteOrders` logic for `RouteOrder` needs no
changes at all to make this work, it's a direct consequence of the fold
already being sequential and already comparing against whatever is
currently in `byKey`.

**Fleet bookkeeping** (in the main validation/apply loop, alongside the
existing `unitToAssign`/`unitToDetach` handling): detach every unit
currently assigned to this route (`FleetLedger.Unassign`, lowest `Id`
first — matching the deterministic ordering `CloseRouteOrder` already
uses), then assign exactly 1 idle unit of the new type
(`FleetLedger.Assign`).

## `BriefingGenerator` notification

New `ExceptionKind.RouteAircraftTypeChanged` (naming open to revision),
derived the same way `BriefingGenerator` already re-derives every other
signal — by diffing `previous.Routes` against `current.Routes` for the
same pair key, the identical pattern `ExceptionKind.RouteEntered` already
uses (`BriefingGenerator.cs:40-48`):

```csharp
foreach (var route in current.Routes)
{
    var previousRoute = previous?.Routes.FirstOrDefault(r => QuarterEngine.RouteKey(r) == QuarterEngine.RouteKey(route));

    if (previousRoute is not null && previousRoute.Aircraft.Id != route.Aircraft.Id)
    {
        items.Add(new BriefingItem(
            ExceptionKind.RouteAircraftTypeChanged,
            $"{route.Origin.Id}-{route.Destination.Id}: aircraft changed to {route.Aircraft.Id}, capacity reset to 1 aircraft, fare reset to {route.FarePerPassenger}"));
    }
}
```

The message calls out the capacity reset explicitly, not just the fare
— a player who swapped a 5-aircraft route and expected to still see 5
of the new type needs that surfaced just as plainly as the fare change,
since both are real, deliberate consequences of the same action.

Pure diff-derived, no new state on `QuarterResult` — matching this
file's own established philosophy ("re-derive rather than widen
`QuarterResult`'s shape for a display-only detail," `BriefingGenerator.cs:24-25`).
Competes for space in the existing `MaxItems = 5` cap alongside every
other signal, same as `RouteEntered`/load-factor items today — no
special-casing needed.

## Player-facing query surface

The menu itself (rendering, input) is Godot/UI work, out of scope while
this project stays in its "headless sim first" phase (CLAUDE.md) — no
Godot references belong in `AeroNext.Core`. What the sim core *should*
provide, so a future UI has a clean contract to build against: a
read-only helper answering "what can this menu offer the player right
now for this route," e.g.

```csharp
namespace AeroNext.Core.Airlines;

public static class RouteAdjustmentOptions
{
    /// <summary>Every type eligible for AdjustRouteOrder on this route: reachable, and at least 1 idle unit on hand. Excludes the route's own current type (AdjustRouteOrder itself would reject it).</summary>
    public static IReadOnlyList<AircraftType> EligibleTypesForSwap(Route route, AirlineFleet fleet) =>
        fleet.Idle
            .Select(u => u.Type)
            .Distinct()
            .Where(t => t.Id != route.Aircraft.Id && route.DistanceKm <= t.RangeKm)
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
}
```

Since a swap always lands on exactly 1 aircraft, the UI only needs to
know *which types are legal at all* — not "how many idle units," the
way the first draft's count-aware version did. If the player also wants
a bigger final count, the UI computes how many follow-up `RouteOrder`s
that needs and can check *those* against ordinary idle-unit counts using
whatever query `RouteOrder`'s own menu already uses today — nothing new
needed there either, since growth is just `RouteOrder` after the swap
(see "Growing after a swap" above).

## Known gaps, explicitly out of scope for this round

- **`AiRouteHistory` stamping** (`FullGameRunner.cs:379-389`) currently
  keys off `result.AppliedOrders.OfType<CloseRouteOrder>()` directly —
  not diff-derived like `BriefingGenerator`. An `AdjustRouteOrder`
  applying is, from this loop's perspective, silently invisible — it
  won't stamp `LastShrunkQuarter`/`LastClosedQuarter` the way an
  equivalent `CloseRouteOrder` sequence would have. Not fixed here:
  `FullGameRunner.cs`'s QA-sweep loop runs 100% AI-controlled seats today
  (no real human-driven seat exists in this headless phase), so this gap
  has no live consequence yet — it becomes real the moment a
  human-driven seat is wired into that loop, or if AI is ever taught to
  submit this order (both explicitly deferred above). Flagged so it
  isn't rediscovered as a surprise later.
- **AI usage** — no strategy submits this order this round (decided
  above). `AiToolkit.RecentlyClosedPairKeys`/`RecentlyShrunkPairKeys`
  (M55's reopen/regrow cooldowns) are irrelevant to a player-submitted
  order today, but would need a fresh look if an AI strategy is ever
  wired to use this — a deliberate type-swap wants to reopen *this*
  specific pair immediately, the opposite of what the cooldown is for.
- **No implicit ordering/leasing.** The menu only offers types the
  player already holds idle units of. A player who wants a type they
  don't have any idle units of still uses `AircraftOrder`/
  `LeaseAircraftOrder` first, same as today.

## Testing plan

Mirroring this codebase's existing `QuarterEngineTests.cs` conventions
(numeric fixtures, not guessed):

1. Rejects an `AdjustRouteOrder` for an unserved pair.
2. Rejects when `AircraftType.Id` matches the pair's current type.
3. Rejects on out-of-range / era-unavailable target type.
4. Rejects when zero idle units of the target type exist.
5. Applied: fare resets to `null`/default; cost is exactly 1; every
   previously-assigned unit ends up idle; exactly 1 new-type unit ends
   up assigned; `current.Routes` shows `(newType, 1)` for this pair with
   no gap — the pair is present, with the new type, in the same
   quarter's result. Covers a route that had more than 1 aircraft before
   the swap, to prove the reset-to-1 behavior directly (not just the
   already-1-aircraft case).
6. **No slot/capacity rejection** for a swap at a gateway/AutoCapacity
   city with zero spare balance, on a route that had more than 1
   aircraft before the swap — the key regression guard proving the "net
   change is never positive" reasoning holds, not just asserted.
7. **Same-quarter swap-then-grow, the highest-value scenario**: a batch
   containing one `AdjustRouteOrder` followed by N `RouteOrder`s for the
   same pair/new type all apply in one `RunQuarter` call — final
   `AircraftCount` is `1 + N`, total cost is `1 + N`, and
   `CompetitiveMarket`/`NetworkPlanner` fed this quarter's final route
   list see the full new capacity, not a partial one. Also test the
   *reverse* submission order (growth `RouteOrder` before the
   `AdjustRouteOrder`) to confirm it's correctly rejected/ignored as a
   type mismatch — pinning down that ordering actually matters, not
   assuming it.
8. `BriefingGenerator` emits `RouteAircraftTypeChanged` exactly when
   `Aircraft.Id` differs between quarters for the same pair, and stays
   silent for a same-type resize.
9. Full coverage check on every changed/new file, per CLAUDE.md's
   milestone gate.

## Open questions

None. Validation rule 2 (rejecting an `AdjustRouteOrder` whose type
matches the pair's current type) is confirmed — "changing the aircraft
type to the same as it is, is not valid." The design is settled and
ready to implement.
