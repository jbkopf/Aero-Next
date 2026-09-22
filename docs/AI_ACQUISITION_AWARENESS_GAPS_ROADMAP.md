# Closing the AI's Remaining Acquisition/Restriction Awareness Gaps

**Status: Gap B implemented (per the user's own direct "start on Gap B"
follow-up); Gap A resolved by deletion, not implementation** — see the
"Gap A: resolved by deletion" section below, added after the user's own
direct "if that API is not called anywhere in the code outside tests,
then it sounds like orphaned code that should be cleaned up" follow-up.
Written per the user's own direct
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

**Update, post-cleanup:** this method's one real caller — a small
league-demo loop that used to live at `src/AeroNext.Sim/Program.cs:315`
(the old M3-M12 milestone-demo block) — was deleted outright in a
later codebase-cleanup pass (those milestones were frozen, never
automated, and superseded by the QA harness/test suite as the real
validation surface). `BaselineAi.DecideOrders` now has **zero callers
anywhere in `src/`** — its only remaining callers are direct unit tests
(`BaselineAiTests.cs`, `DeterminismRegressionTests.cs`), which still
exercise it thoroughly as a tested legacy API. Neither real campaign loop
(`FullGameRunner.cs`/`AeroNext.Sim/Program.cs`'s own M13 section) has
called it since M39 replaced it with the `StrategyContext`/`IAiStrategy`
path. This method has also been explicitly, repeatedly left alone before:
M50's and M51's own doc comments both call out a "frozen legacy branch...
untouched, same precedent" when extending unrelated AI behavior. Losing
its last real caller only strengthens that precedent.

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

1. **Leave it alone, matching M50/M51's own precedent directly.** Now
   that the one small league demo that used to call this method has been
   deleted (see the update above), this method has **no consequence at
   all today** — not even the cosmetic demo-output risk this option
   originally described, since there's no caller left to produce
   misleading output. Zero risk, zero work, consistent with how this
   exact method has been treated every other time this codebase added a
   new acquisition-side concern (`EndOfManufactureYear`'s own Phase (c)
   already skipped it; M50/M51 both skipped it for unrelated reasons).
2. **Thread it through anyway** — add `City? homeCity = null` and
   `IReadOnlyList<TradeRestriction>? tradeRestrictions = null` as two new
   optional parameters (permissive-default, same convention as every
   other addition this session), compute `Timeline.AvailableForPurchase`/
   `AvailableForLease` once inside the method the same way
   `FullGameRunner.cs`/`Program.cs` already do, and apply it to both of
   this method's own acquisition branches. With no real caller left at
   all (see above), this would purely be future-proofing a currently
   test-only API against a caller that doesn't exist yet — even less
   justified than when this option was first written.

Recommendation: **option 1.** The method has no home-city concept at all
today — adding one is a real signature change to a method this codebase
has explicitly, repeatedly decided not to extend, for a caller that isn't
part of any actual game. Option 2 is here so the choice is visible, not
because it's the better default.

## Gap A: resolved by deletion

The user's own follow-up, after the option-1/option-2 write-up above:
"if that API is not called anywhere in the code outside tests, then it
sounds like orphaned code that should be cleaned up." Correct call —
"leave it frozen" was the right precedent while a real caller (however
minor) still existed; once that caller was gone, continuing to carry the
method was just debt with no offsetting value. `BaselineAi.DecideOrders`
was deleted outright, along with its now-unreachable `<param>` doc block
and the 60-test `BaselineAiTests.cs` file that existed solely to exercise
it (every test in that file called `DecideOrders` directly, confirmed by
listing every `[Fact]` name before deleting anything).

**The class-level doc comment was rewritten**, not just trimmed — it used
to describe `DecideOrders`'s own greedy single-heuristic behavior as if
that were the whole class's purpose, which was already stale post-M39 and
would have been actively misleading with the method gone. It now
describes what `BaselineAi` actually is: home of the real `Strategies`
roster/`SelectStrategy` dispatcher plus the shared static helpers every
concrete `IAiStrategy` draws on.

**Coverage caught three more casualties**, not assumed: a before/after
`dotnet test --collect:"XPlat Code Coverage"` diff on `BaselineAi.cs`
showed line coverage dropping from 98.87% to 53.76%, and per-method rates
isolated exactly which methods lost *all* coverage — `FindBestUnservedPair`,
`FindBestUnservedPairUsingIdleFleet`, and `FindBestUnservedGateway`, each
100% covered before, 0% after. Grepping for real call sites (not doc
comments — several files had prose like "mirrors
BaselineAi.FindBestUnservedGateway's shape," which look like usage at a
glance but aren't) confirmed all three had already lost every real caller:
`RapidExpansionStrategy`, `DomesticProfitOptimizerStrategy`, and
`BalancedGrowthStrategy` each evolved their own separate, more specific
search helper over time and stopped calling into `BaselineAi`'s plain
global-best versions — `DecideOrders` was the last caller of all three, so
they were exactly as orphaned as `DecideOrders` itself, just not visible
as such until it was gone. Deleted alongside it, restoring `BaselineAi.cs`
to 84.74% (up from the post-deletion 53.76%).

**Update — `enableRealProfitabilityGate` removed too**, per the user's own
direct follow-up ("the false path seems unnecessary now. the only thing
that currently would want to not check actual profitability uses its own
separate method. This can be refactored out."). Correct call, and the
same principle as Gap A's own resolution: the parameter had a real,
documented purpose once (letting `BaselineAi.DecideOrders` and, later,
`RapidExpansionStrategy` opt out of profitability-gating), but both of
those opt-outs are gone now — `DecideOrders` deleted, `RapidExpansionStrategy`
moved to its own `FindBestExistingRouteByDemand` specifically *because*
the gate regressed it (see that class's own doc comment) — so the "off"
state had no remaining reason to exist as a runtime choice, only as
history. `FindBestPerformingRoute` now always gates on real profitability
and ranks by real load factor; the parameter and its `false` branch are
gone, and the two real call sites (`NetworkOptimizerStrategy`,
`BalancedGrowthStrategy`) dropped their now-redundant
`enableRealProfitabilityGate: true` argument. The measured history (300-game
sweep numbers, the Europe bankruptcy regression story) was preserved by
folding it into the method's own `<remarks>` and each strategy's class
doc comment, not deleted — it's still the reason this method behaves the
way it unconditionally does now.

Verified: build clean, full suite green (1051 + 38, no change — no test
ever referenced the internal parameter directly), a 30-game QA sweep at
seed 1 byte-identical to before the refactor (expected: both real callers
already always passed `true`, so this only removed dead branching, not
live behavior), and `BaselineAi.cs` coverage rose again, to 96.07%
line / 79.16% branch (from 84.74%/76.92%). What's left is `SelectStrategy`'s
own smaller, pre-existing coverage gap (84.61%/62.5%, unchanged throughout
this whole cleanup) plus two small, pre-existing edge-case branches inside
`FindBestPerformingRoute` itself (an `excludePairKeys is null` short-circuit
and a `SeatsPerQuarter == 0` divide-by-zero guard) — confirmed by matching
hit counts that both predate this round of changes, not introduced by it.
Gap A is now fully closed, not just scoped.

**Verification, not just argued:** full build (0 warnings, 0 errors),
full test suite (1051 + 38 passing, down from 1115 core tests — the 64
removed are exactly `BaselineAiTests.cs`'s own 60 `DecideOrders_*` tests
plus a few smaller casualties), and a 30-game QA sweep at seed 1 producing
byte-identical output to the pre-cleanup run — confirming the real game
(which never called `DecideOrders`, only `Strategies`/`SelectStrategy`)
is untouched. `DeterminismRegressionTests.cs`'s own scenario, which *did*
call `DecideOrders` as a convenient deterministic order source, was
rewired to go through the real `StrategyContext`/`IAiStrategy` path
instead (its own comment already noted it doesn't care which AI logic
runs, only that the whole resolver chain reproduces identically).

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
