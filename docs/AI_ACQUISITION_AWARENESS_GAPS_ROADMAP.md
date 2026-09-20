# Closing the AI's Remaining Acquisition/Restriction Awareness Gaps

**Status: Gap B implemented (per the user's own direct "start on Gap B"
follow-up); Gap A still an open scope question, not implemented.** Written
per the user's own direct
request ("make a plan for closing the AI gaps for the new systems"),
following a fresh investigation into exactly what's left after
`docs/AIRCRAFT_ACQUISITION_ROADMAP.md` and
`docs/GEOPOLITICAL_AIRCRAFT_RESTRICTIONS_ROADMAP.md` both shipped. Both
systems are fully enforced and correct today — `QuarterEngine` is the
authoritative gate regardless of what the AI does or doesn't know. What's
left is pure AI efficiency: two shapes of "the AI proposes an order it
can't know is doomed, wastes an action this quarter when it's rejected."

## The two gaps, confirmed directly against current `main`

**Gap A — `BaselineAi.DecideOrders` has no home-city concept at all.**
This is the pre-`StrategyContext` legacy API
(`src/AeroNext.Core/Ai/BaselineAi.cs:171-180`) — still raw parameters, no
`City`/`homeCity` anywhere in its signature (confirmed by grep: zero
matches). Two consequences, both inside this one method:
- Its own "new pair, nothing idle" branch (lines 251-286) picks a
  candidate type via `eraAvailableTypes.OrderByDescending(RangeKm)...`
  (line 261) — the plain, unrestricted list, since `PurchasableTypes`/
  `LeasableTypes` live on `StrategyContext`, which this method doesn't
  take.
- Its own "grow existing route, nothing idle" branch (lines 213-237)
  unconditionally submits `LeaseAircraftOrder(bestExisting.Aircraft)`
  (line 235) with no legality check of any kind.

This method's only real caller anywhere in `src/` is one small
league-demo loop, `src/AeroNext.Sim/Program.cs:315` — confirmed again
directly, not assumed. Neither real campaign loop
(`FullGameRunner.cs`/`AeroNext.Sim/Program.cs`'s own M13 section) has
called it since M39 replaced it with the `StrategyContext`/`IAiStrategy`
path. This method has also been explicitly, repeatedly left alone before:
M50's and M51's own doc comments both call out a "frozen legacy branch...
untouched, same precedent" when extending unrelated AI behavior.

**Gap B — four "grow an existing route via a fresh lease" branches never
check whether that route's own type is still legally acquirable.**
Identical shape in all four, confirmed by file+line:

| File | Lines |
|---|---|
| `BaselineAi.cs` | 213-237 (same branch as Gap A above) |
| `NetworkOptimizerStrategy.cs` | 278-287 |
| `BalancedGrowthStrategy.cs` | 207-216 |
| `RapidExpansionStrategy.cs` | 220-229 |

Each: idle unit of the route's own type found → `RouteOrder` (fine, this
is flying, never gated); cash affords buying outright → `AircraftOrder`;
otherwise → `LeaseAircraftOrder(bestExisting.Aircraft, Quantity: 1)`,
*unconditionally*. Unlike every "new pair" search this session already
fixed, this branch never searches a candidate list — it always targets
the one type already flying that route — which is exactly why it was
untouched by the `PurchasableTypes`/`LeasableTypes` work: there was no
list to swap in, only a single fixed candidate to validate.

The three `StrategyContext`-based files (everything but `BaselineAi.cs`)
already have `PurchasableOrEraTypes`/`LeasableOrEraTypes` on hand and can
be fixed today with no new plumbing. `BaselineAi.cs`'s own instance of
this same branch is blocked on Gap A first, since it has nothing to check
against.

## Fix for Gap B (the three `StrategyContext`-based files)

A single-item legality check — **against the raw nullable
`PurchasableTypes`/`LeasableTypes` fields directly, not the
`...OrEraTypes` fallback properties**:

```csharp
context.LeasableTypes is null || context.LeasableTypes.Contains(bestExisting.Aircraft)
context.PurchasableTypes is null || context.PurchasableTypes.Contains(bestExisting.Aircraft)
```

**Implemented, and this distinction turned out to matter in practice, not
just in theory**: the first version of this fix checked
`PurchasableOrEraTypes`/`LeasableOrEraTypes` instead (reasoning that they
"fall back to the unrestricted `EraAvailableTypes`, so `Contains` always
succeeds when nothing real was wired in"). That reasoning was wrong and
broke 5 real tests immediately: `EraAvailableTypes` is the *new-type
search* list, and several existing fixtures legitimately leave it empty
(`Array.Empty<AircraftType>()`) for a "grow an existing route" scenario,
since growing an already-served route was never supposed to need an
era-available catalog at all — it targets one fixed, already-known type.
Falling back to an empty list made the guard read "nothing is ever
legal" instead of "no restriction data, so everything is legal." The
`is null ||` form checks the actual opt-in signal directly instead of a
list whose emptiness can mean two different things depending on which
step is asking.

`AircraftType` being a `record` (real structural equality, not reference
identity) makes the `Contains` call itself exact once it's checking the
right list — no risk of a false negative from two logically-identical
instances that happen to be different objects.

Applied as an added guard on the existing `else` branch in each of the
three files:

```csharp
else if (context.LeasableTypes is null || context.LeasableTypes.Contains(bestExisting.Aircraft))
{
    orders.Add(new LeaseAircraftOrder(bestExisting.Aircraft, Quantity: 1));
    spent += 1;
}
// else: this route's own type is no longer legally leasable for this
// home city right now - do nothing this quarter rather than submit an
// order QuarterEngine would reject anyway. The route keeps flying
// (RouteOrder/AdjustRouteOrder are never gated); it just can't grow
// until either something goes idle or the restriction lifts.
```

The buy-outright branch one level up gets the same treatment against
`PurchasableTypes`, closing the identical gap for purchase specifically
(a route's own type could just as easily have passed its
`EndOfManufactureYear`'s purchase window, independent of leasing).

**Permissive by construction, verified against the full suite, not just
argued**: every existing test and every real caller that hasn't wired
real restriction data through yet has `PurchasableTypes`/`LeasableTypes`
null, so the guard is a true no-op until a real list is actually passed —
confirmed by running the full 1109-test suite after the fix, not assumed
from the shape of the code alone.

## Fix for Gap A — an open question, not a unilateral call

Two real options, given the explicit "frozen legacy" precedent already on
record for this exact method:

1. **Leave it alone, matching M50/M51's own precedent directly.** Its
   only real consequence today is cosmetic: the one small league demo in
   `AeroNext.Sim/Program.cs` might occasionally print a lease proposal
   for a type that (in a game with real restriction data loaded) would
   get rejected — a demo artifact, never the actual game. Zero risk,
   zero work, consistent with how this exact method has been treated
   every other time this codebase added a new acquisition-side concern
   (`EndOfManufactureYear`'s own Phase (c) already skipped it; M50/M51
   both skipped it for unrelated reasons).
2. **Thread it through anyway** — add `City? homeCity = null` and
   `IReadOnlyList<TradeRestriction>? tradeRestrictions = null` as two new
   optional parameters (permissive-default, same convention as every
   other addition this session), compute `Timeline.AvailableForPurchase`/
   `AvailableForLease` once inside the method the same way
   `FullGameRunner.cs`/`Program.cs` already do, and apply it to both of
   this method's own acquisition branches. Its real caller
   (`AeroNext.Sim/Program.cs:315`) would need updating too to actually
   pass something, or the new parameters stay permissively null there
   and nothing changes in practice anyway.

Recommendation: **option 1.** The method has no home-city concept at all
today — adding one is a real signature change to a method this codebase
has explicitly, repeatedly decided not to extend, for a caller that isn't
part of any actual game. Option 2 is here so the choice is visible, not
because it's the better default.

## Testing plan

1. For each of the three `StrategyContext`-based files: a
   `DecideOrders_GrowExisting_NothingIdle_TypeExcludedWhenNotInLeasableTypes_ProposesNothing`
   test — `LeasableTypes: Array.Empty<AircraftType>()`, nothing idle,
   cash not comfortable for an outright buy — asserting no
   `LeaseAircraftOrder` and no `RouteOrder`, mirroring the pattern already
   used for the "new pair" branches
   (`tests/AeroNext.Core.Tests/RapidExpansionStrategyTests.cs`'s own
   `..._TypeExcludedWhenNotInPurchasableOrLeasableTypes_ProposesNoAcquisition`
   from the prior round).
2. A matching purchase-side test (`PurchasableTypes` empty, cash
   comfortable) confirming the buy-outright branch is skipped the same
   way, not just the lease branch.
3. A positive control per file: the existing "still leases when nothing's
   restricted" test from before this change keeps passing unmodified —
   confirms the new guard is additive, not a behavior change for the
   unrestricted case.
4. Full coverage check on every changed file, per CLAUDE.md.

## Sizing

Gap B (all three files) is small, low-risk, and independently shippable
today — no architecture change, no new data, just a guard clause reusing
fields that already exist. Gap A is a scope decision, not an
implementation task, until the recommendation above is confirmed one way
or the other.
