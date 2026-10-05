# Teaching the AI to Use AdjustRouteOrder: Design

**Status: implemented and measured (M66, `docs/BASELINE_AI_ROADMAP.md`),
extended to every non-Starting strategy in a direct follow-up round (M67,
same document).** Written per the user's own direct request, as the
follow-up to `docs/ROUTE_AIRCRAFT_ADJUSTMENT_DESIGN.md` (the player-facing
feature, already shipped, commit `5b8eb01`), whose own "Known gaps"
section flagged AI usage as explicitly deferred. Everything below
reflects the real, shipped mechanism; see "Measured" for M66's own real
300-game results and "M67 — extending to the rest of the non-Starting
strategies" below for the follow-up round's own design and results.

## Decided scope

Answered directly by the user (recorded here so the reasoning survives
past this conversation):

- **One strategy first, opt-in flag** — the same "ablation lever, off is
  exactly old behavior" shape `enableSustainedSingleAircraftClosure`
  already uses on `AiToolkit.ApplyManagementToolkit`, not a change every
  strategy gets at once.
- **Both reactive and proactive triggers** — fixing an already-
  unprofitable route *and* opportunistically re-fleeting a healthy one
  once a meaningfully better-fitting type exists.
- **A deliberate lease or purchase is allowed specifically to enable a
  swap** — not restricted to whatever happens to be idle already.
- **A same-pair cooldown is required infrastructure from the start** —
  new `AiRouteHistory` field, not deferred to a later round.

## Which strategy, and why

**`NetworkOptimizerStrategy`, confirmed directly.** Reasoning: it's the
MidGame/LateGame strategy (`ApplicableEras`
= `{ MidGame, LateGame }`, `NetworkOptimizerStrategy.cs`) whose own tenet
is "stabilize, don't expand" — it manages a mature network for the
majority of a 60-year campaign rather than growing one, which is exactly
the setting where "the world's aircraft catalog moved on since I last
touched this route" becomes a real, recurring situation, not a one-off.
It's also the strategy `enableSustainedSingleAircraftClosure` was first
enabled for (M58-follow-up round 2), so there's already a real precedent
for this exact "give NetworkOptimizerStrategy a new per-route remedy via
`ApplyManagementToolkit`'s own opt-in-flag shape" move working out.
`DomesticProfitOptimizerStrategy` (Starting era only) already has the
closest thing to this logic (`EstimatedProfitForAircraft`, Phase 2) but
its own era is short — less real time for a type to become obsolete —
and it's explicitly domestic-only, a narrower stage than this deserves
for a first real test.

## The reusable primitive already exists: `AiToolkit.EstimatedRealProfit`

The critical finding that shapes this whole design: **this codebase
already has a shared, safe, real-profit-for-one-(pair,aircraft)-combination
function**, and it's a better fit here than `DomesticProfitOptimizerStrategy`'s
own private `EstimatedProfitForAircraft` (which this document's first
draft assumed would need promoting to a shared location).
`AiToolkit.EstimatedRealProfit(context, pair, aircraft, requiresLease)`
(`AiToolkit.cs:276`) already:

- Applies the same single-peaked-in-capacity profit shape M57 proved
  safe (see that method's own doc comment, which cites
  `DomesticProfitOptimizerStrategy.EstimatedProfitForAircraft`'s proof
  directly) — safe specifically because it only ever compares candidates
  *within one fixed pair*, never leaks into cross-pair ranking. A
  periodic re-evaluation of an *existing* route's own current type
  against alternatives is the identical shape, just re-run later instead
  of only at open time — it inherits the same safety property, not a new
  kind of risk.
- Already derates for real competition (`RivalPresenceByPairKey`/
  `RivalPresenceByRegion`) — relevant here, since a route worth
  re-fleeting is one this airline actually holds real share on, not a
  fresh, contested unknown.
- Already includes the lease fee in the profit figure when
  `requiresLease` is true — exactly the cost a deliberate swap-motivated
  lease needs reflected before deciding it's worth it.
- Is already used by `NetworkOptimizerStrategy` itself, for its own step
  3 (`AiToolkit.FindMostProfitablePair`) — the exact strategy this
  proposal targets already trusts this function for a structurally
  identical decision (pick the best (pair, aircraft) by real profit).

This means the new mechanism doesn't need new profit math — it needs a
new *search shape* (evaluate candidate types for a route this airline
**already operates**, not a route it doesn't) built on the same
primitive.

## Where this lives: a new block in `ApplyManagementToolkit`

`AiToolkit.ApplyManagementToolkit` (`AiToolkit.cs:1025`) already iterates
every held route once per quarter (`routeLoads`, from
`RouteEvaluator.EvaluateNetwork`) and already computes real profit per
route (`FareCalculator.EstimatedProfit(load.Route, carried)`) to decide
fare cuts/shrinks/closure. The new logic is a **third opt-in parameter**,
`enableAircraftTypeSwap: bool = false` (only `NetworkOptimizerStrategy`
passes `true`), evaluated inside the same `foreach (var load in routeLoads)`
loop that already exists — not a second pass over the network.

### Reactive: inserted between the closure check and the fare-cut/shrink fallback

Today's structure inside the `profit < Money.Zero` branch:

```
if (enableSustainedSingleAircraftClosure && AircraftCount == 1 && sustained-and-uncontested)
    -> close, continue
// falls through to here if not closing
-> cut fare
-> shrink by 1 if AircraftCount > 1
```

The new check inserts **after** the closure check (a route already
judged unsalvageable stays unsalvageable — swapping its type isn't
attempted once closure has already won) and **before** the fare-cut/shrink
fallback:

```
if (enableAircraftTypeSwap && NOT sustained-and-uncontested)
{
    var swap = EvaluateTypeSwapCandidate(context, load, reservedUnitIds, isReactive: true);
    if (swap is not null && cooldown allows it)
    {
        propose swap order(s); continue; // skip cut/shrink this quarter - let the new type play out with real data next quarter
    }
}
-> cut fare (unchanged, reached only if no swap fired)
-> shrink by 1 if AircraftCount > 1 (unchanged)
```

Gated on `NOT sustained-and-uncontested` specifically — deliberately
**not** every unprofitable route. `ConsecutiveUnprofitableQuarters` is
already real, existing signal for "has this been a problem for a while,
or did it just turn bad this quarter": a route with a low streak is a
plausible type-fit problem worth testing; a route already at (or past)
`SustainedSingleAircraftClosureThreshold` is far more likely a
fundamentally weak *pair*, where swapping type burns an action and locks
the cooldown while the route dies anyway regardless of type. The
existing closure check already owns that end of the spectrum; this only
reaches in earlier.

**Threshold for the reactive case: any real improvement, plus a small
dead-band.** The route is already flagged as a genuine problem and
already costs an action this quarter regardless (fare cut is free, but
the swap itself costs 1-2, same budget check as everything else) — there's
no reason to demand a large margin the way the proactive case needs to.
Proposed: `ReactiveSwapMinimumProfitImprovement = Money.FromDollars(10_000)`
(tunable) — just enough to rule out swapping over rounding noise, not a
real bar.

### Proactive: a new, separate block for routes that are already profitable

Today, a profitable route (`profit >= Money.Zero`) only gets the
load-factor-driven fare-raise check earlier in the same method
(`AiToolkit.cs:1045-1061`) and otherwise nothing. The new block runs
**after** that raise check, for every route, gated by the same
`enableAircraftTypeSwap` flag:

```
if (enableAircraftTypeSwap)
{
    var swap = EvaluateTypeSwapCandidate(context, load, reservedUnitIds, isReactive: false);
    if (swap is not null && cooldown allows it)
        propose swap order(s);
}
```

**Threshold for the proactive case: a real, deliberately high bar.**
Proposed: `ProactiveSwapMinimumProfitImprovementFraction = 0.5` (tunable)
— the candidate type's own estimated profit must exceed the current
type's by at least 50%, not just be nominally better. A healthy route
isn't broken; disrupting it (reset to 1 aircraft, fare reset to default,
a real one-or-two-action cost) needs to be clearly worth it, not a
coin-flip-sized edge. This is the single number most worth a real 300-game
sweep before trusting — see the measurement plan below.

### The shared evaluator: `AiToolkit.EvaluateTypeSwapCandidate`

```csharp
public static (AircraftType Type, Money ProfitDelta, bool UsesIdleUnit, bool CanBuyOutright)? EvaluateTypeSwapCandidate(
    StrategyContext context, RouteLoad load, IReadOnlySet<int> reservedUnitIds, bool isReactive)
{
    var pair = context.DemandModel.ForPair(load.Route.Origin, load.Route.Destination);
    var currentProfit = AiToolkit.EstimatedRealProfit(context, pair, load.Route.Aircraft, requiresLease: false);
    var idleTypeIds = context.OwnFleet.Idle
        .Where(u => !reservedUnitIds.Contains(u.Id))
        .Select(u => u.Type.Id)
        .ToHashSet(StringComparer.Ordinal);

    var candidates = context.EraAvailableTypes
        .Where(type => type.Id != load.Route.Aircraft.Id && pair.DistanceKm <= type.RangeKm)
        .Select(type =>
        {
            var usesIdleUnit = idleTypeIds.Contains(type.Id);
            var profit = AiToolkit.EstimatedRealProfit(context, pair, type, requiresLease: !usesIdleUnit);
            return (Type: type, Profit: profit, UsesIdleUnit: usesIdleUnit);
        })
        .OrderByDescending(c => c.Profit.Cents)
        .ThenBy(c => c.Type.Id, StringComparer.Ordinal)
        .ToList();

    if (candidates.Count == 0) return null;

    var best = candidates[0];
    var delta = best.Profit - currentProfit;
    var required = isReactive
        ? ReactiveSwapMinimumProfitImprovement
        : new Money((long)(currentProfit.Cents * ProactiveSwapMinimumProfitImprovementFraction));

    if (delta < required) return null;

    var canBuyOutright = context.AvailableCash is not null
        && FinancialState.CanAfford(context.AvailableCash.Value, BaselineAi.ComfortableOrderCost(best.Type));

    return (best.Type, delta, best.UsesIdleUnit, canBuyOutright);
}
```

Notes on this sketch (exact numbers/signatures to firm up during
implementation, not load-bearing to the design):

- Excludes the route's own current type from the candidate set up
  front (`type.Id != load.Route.Aircraft.Id`) — mirrors
  `AdjustRouteOrder`'s own engine-level rejection of a same-type
  request, so the evaluator never produces a candidate the engine would
  reject anyway.
- `requiresLease: !usesIdleUnit` matches `FindMostProfitablePair`'s own
  exact idiom (`AiToolkit.cs:333-335`) — reuses the established
  lease-fee-aware costing rather than inventing a new convention.
- The proactive fraction is computed against `currentProfit.Cents`,
  which only makes sense when `currentProfit > 0` (true by construction
  in the proactive branch, since it only runs when the route is already
  profitable). The reactive branch's flat-dollar floor sidesteps this —
  `currentProfit` can be zero or negative there, where a fraction-of-current
  wouldn't mean anything sensible.

### Building the order(s): a new `AiToolkit.TryProposeTypeSwap`

Mirrors `TryProposeCandidate`'s own idle-vs-lease-vs-buy branching
(`AiToolkit.cs:354-387`), but simpler in one real way: **no gate-slot or
city-wide-capacity reservation is needed at all.** A swap targets a pair
this airline already serves — it's not claiming a new slot anywhere, and
`AdjustRouteOrder` itself needs no slot check at the engine level either
(see `docs/ROUTE_AIRCRAFT_ADJUSTMENT_DESIGN.md`'s own reasoning: the
route can only ever hold flat or free capacity versus its old count,
since a swap always resets to 1).

```csharp
public static void TryProposeTypeSwap(
    StrategyContext context, RouteLoad load,
    (AircraftType Type, Money ProfitDelta, bool UsesIdleUnit, bool CanBuyOutright)? candidate,
    List<QuarterOrder> orders, HashSet<int> reservedUnitIds, ref int spent)
{
    if (candidate is null) return;

    // Each path's own real cost is checked separately, not a shared
    // "idle vs. everything else" figure - buying outright costs 1
    // action this quarter (just the AircraftOrder; the AdjustRouteOrder
    // itself waits for a future quarter's delivery), distinct from
    // leasing's 2 (both orders, same quarter).
    if (candidate.Value.UsesIdleUnit)
    {
        if (spent + 1 > context.ActionCap) return;
        var idleUnit = context.OwnFleet.Idle.Where(u => u.Type.Id == candidate.Value.Type.Id && !reservedUnitIds.Contains(u.Id)).OrderBy(u => u.Id).First();
        reservedUnitIds.Add(idleUnit.Id);
        orders.Add(new AdjustRouteOrder(load.Route.Origin, load.Route.Destination, candidate.Value.Type));
        spent += 1;
    }
    else if (candidate.Value.CanBuyOutright)
    {
        if (spent + 1 > context.ActionCap) return;
        orders.Add(new AircraftOrder(candidate.Value.Type, Quantity: 1)); // production-queue - delivered later, not this quarter; the swap itself waits for a future quarter's re-evaluation to find it idle
        spent += 1; // no AdjustRouteOrder yet - nothing to assign until delivery
    }
    else
    {
        if (spent + 2 > context.ActionCap) return;
        orders.Add(new LeaseAircraftOrder(candidate.Value.Type, Quantity: 1)); // same-quarter delivery (QuarterEngine resolves leases inline)
        orders.Add(new AdjustRouteOrder(load.Route.Origin, load.Route.Destination, candidate.Value.Type));
        spent += 2;
    }
}
```

**The buy-outright path is deliberately fire-and-forget, not a
two-quarter "intent" plan.** `AircraftOrder` uses the production queue
(`AircraftType.ProductionRatePerQuarter`) — it isn't delivered the same
quarter, unlike leasing (`QuarterEngine`'s existing inline lease
resolution, "so a `RouteOrder` later in this same batch can actually use
the newly-leased unit," `QuarterEngine.cs:478-484` — the same mechanism
`AdjustRouteOrder`'s own idle-unit check already benefits from for a
same-quarter lease-then-swap). No new cross-quarter tracking is needed
for the buy case: once the unit is delivered and shows up idle, the
*same* per-quarter re-evaluation (proactive or reactive, whichever
applies then) naturally proposes the swap using the idle-unit path —
this is a direct, deliberate consequence of re-running one evaluator
every quarter rather than inventing "remembered intent" state, and it's
simpler and more honest about the real delay than pretending the order
is committed the moment it's placed.

**Preferring buy-outright over lease when affordable** mirrors
`DomesticProfitOptimizerStrategy`'s own established fallback order
(`CanAfford(...) -> AircraftOrder, else -> LeaseAircraftOrder`,
`DomesticProfitOptimizerStrategy.cs:206-221`) — avoids the recurring
lease fee once the airline can comfortably absorb the upfront cost
instead, same reasoning already applied to opening a new pair.

## Thrash prevention: `AiRouteHistory.LastTypeSwapQuarter`

New field on `AiRouteHistory` (`AiRouteHistory.cs`), alongside
`LastShrunkQuarter`/`LastClosedQuarter`:

```csharp
public sealed record AiRouteHistory(int? LastShrunkQuarter, int? LastClosedQuarter, int ConsecutiveUnprofitableQuarters, int? LastTypeSwapQuarter = null);
```

Trailing, with a default — not inserted before `ConsecutiveUnprofitableQuarters` the way an earlier pass at this sketch had it, which would have forced every one of the ~15 existing positional call sites across the test suite to be touched for a field they don't care about. A trailing optional field needs zero of them changed, the same "off is exactly old behavior" migration discipline this codebase already applies everywhere else.

Stamped in `FullGameRunner.cs`'s existing pass-1 loop
(`FullGameRunner.cs:379-389`, the same one that already stamps
`LastShrunkQuarter`/`LastClosedQuarter` from `result.AppliedOrders`) —
adds a matching `foreach (var swapOrder in result.AppliedOrders.OfType<AdjustRouteOrder>())`
case. Deliberately derived from **every** applied `AdjustRouteOrder`,
not just an AI-submitted one — matching this file's own established
principle that history is "built fresh from state it already resolves
… never a value the AI itself writes, so a strategy can only ever read
its own real history, not fabricate one." A player-submitted swap (once
a real UI exists) stamps the same cooldown a strategy's own swap would,
correctly protecting a just-changed route from being immediately
reconsidered regardless of who changed it.

**Verified, not assumed, before writing this: `AeroNext.Sim/Program.cs`
needs no matching change.** Unlike `RegionsLedLastQuarter` (which really
was silently unpopulated in `FullGameRunner.cs` while working correctly
in `AeroNext.Sim`, the exact bug `docs/OPEN_WORK.md`'s Africa section
found), `RouteHistoryByPair` isn't tracked in `AeroNext.Sim/Program.cs`
at all today — it stays null there, the same permissive-default every
other nullable `StrategyContext` field already falls back to safely.
`LastTypeSwapQuarter` inherits that same "no history there, cooldown
never blocks anything" behavior automatically, with no special-casing
needed — confirmed by grep, not assumed from the `RegionsLedLastQuarter`
precedent.

New constant, mirroring `GrowthCooldownQuarters`/`ReopenCooldownQuarters`'s
own shape (`AiToolkit.cs:390,393`):

```csharp
/// (Confirmed starting point) — a pair swapped within this many quarters
/// of context.QuarterNumber is excluded from further swap consideration,
/// either direction (reactive or proactive).
public const int TypeSwapCooldownQuarters = 4;
```

Deliberately double `GrowthCooldownQuarters`' own 2 — a type swap is a
bigger disruption than a same-type grow/shrink (it resets capacity to 1
and resets the fare to the computed default, discarding any prior
tuning), so it warrants a longer "let it stabilize and prove itself with
real data" window before being reconsidered. Read via a new
`AiToolkit.RecentlySwappedPairKeys(context, cooldownQuarters)`, the same
shape as `RecentlyShrunkPairKeys`/`RecentlyClosedPairKeys`
(`AiToolkit.cs:405,427`) — a no-op empty set when `RouteHistoryByPair`
is null, same permissive-default convention every nullable
`StrategyContext` field already follows.

**Considered and deliberately deferred, not built into this round:**
whether a swap should also respect `LastShrunkQuarter` (i.e., skip
swap-evaluating a route that was just shrunk, let that stabilize too
before layering a second kind of change on it). `LastTypeSwapQuarter`'s
own cooldown is the primary, required guard against swap-specific
thrash; a second cross-cooldown is a real but smaller refinement, and
this codebase's own history (`ConsecutiveUnprofitableQuarters` being
added only once a real problem was measured, not preemptively) argues
for confirming this is actually needed via the measurement round before
building more mechanism against a problem not yet observed here.

## A deliberate, large behavior change worth stating plainly: swap always collapses to 1 aircraft

`AdjustRouteOrder` always resets `AircraftCount` to 1, whatever it was
before (`docs/ROUTE_AIRCRAFT_ADJUSTMENT_DESIGN.md`'s own design, already
shipped). A reactive swap on a 3-aircraft unprofitable route is a much
bigger capacity cut in one action than the existing shrink-by-1 remedy —
freeing 2 units to idle in one move, not one. This is the correct,
intended behavior (a genuinely different remedy, not an incremental
one — the whole point of offering it as an alternative to shrink), but
it's a real, size-of-effect change from anything `ApplyManagementToolkit`
does today, and the measurement round should watch specifically for
whether this creates a new failure mode (a route that was fundamentally
fine, just having one bad quarter, getting swap-and-shrunk hard rather
than gently trimmed) rather than assuming the existing shrink-focused
intuition still applies.

## Interaction with existing mechanisms

- **`AiToolkit.RecentlyShrunkPairKeys`/`RecentlyClosedPairKeys`** (M55,
  `ReopenCooldownQuarters`/`GrowthCooldownQuarters` = 2) govern *new-pair
  search* eligibility for a route this airline previously shrunk or
  closed. Unrelated to swap eligibility — a swap targets a pair the
  airline still actively serves, never touches these sets.
- **`enableSustainedSingleAircraftClosure`** stays exactly as today; the
  new swap check is explicitly scoped to *not* run once that branch's
  own condition is met (see "Reactive," above) — closure still wins once
  a route is judged truly unsalvageable, the swap only gets a chance
  before that point.
- **Fare handling**: `AdjustRouteOrder` always resets `FareOverride` to
  `null` (the shipped design). No special AI-side fare logic needed —
  the very next quarter's ordinary fare-cut/raise checks in
  `ApplyManagementToolkit` will react to the new type's own real load
  factor with fresh data, exactly as they already do for any other
  route.
- **`enableAircraftTypeSwap` and Starting-era strategies**: not wired to
  either `RapidExpansionStrategy` or `DomesticProfitOptimizerStrategy`
  this round (matches the "one strategy first" decision) — both keep
  calling `ApplyManagementToolkit` with the new parameter defaulted
  `false`, byte-identical to today.
- **`DistressStrategy`**: also not wired this round, despite recovery
  being arguably in-mission for a type swap — deliberately out of scope,
  matching the earlier `RegionsLedLastQuarter` plan's own precedent of
  leaving `DistressStrategy` untouched by a new per-route mechanism
  unless the user directs otherwise. A distressed airline committing to
  a fresh lease specifically to swap type is a real cash-risk question
  (spending to fix a route while already in trouble) worth its own
  explicit decision, not a default inclusion.

## New/changed files

- `src/AeroNext.Core/Ai/AiRouteHistory.cs` — new `LastTypeSwapQuarter` field.
- `src/AeroNext.Core/Ai/AiToolkit.cs` — `EvaluateTypeSwapCandidate`,
  `TryProposeTypeSwap`, `RecentlySwappedPairKeys`,
  `TypeSwapCooldownQuarters`, `ReactiveSwapMinimumProfitImprovement`,
  `ProactiveSwapMinimumProfitImprovementFraction`; new blocks inside
  `ApplyManagementToolkit`, gated by a new `enableAircraftTypeSwap`
  parameter (default `false`).
- `src/AeroNext.Core/Ai/NetworkOptimizerStrategy.cs` — its own
  `ApplyManagementToolkit` call site passes `enableAircraftTypeSwap: true`.
- `src/AeroNext.QaHarness/FullGameRunner.cs` — stamp `LastTypeSwapQuarter`
  from applied `AdjustRouteOrder`s. `AeroNext.Sim/Program.cs` needs no
  change (verified — see above).

## Testing plan

Unit-level (`AiToolkitTests.cs`/similar), numeric fixtures:

1. `EvaluateTypeSwapCandidate` picks the highest-real-profit alternate
   type, excludes the route's own current type from candidates, and
   correctly marks `UsesIdleUnit` per type.
2. Reactive: returns a candidate when the delta clears
   `ReactiveSwapMinimumProfitImprovement`; returns `null` when it
   doesn't, and when no candidate improves on the current type at all.
3. Proactive: returns a candidate only when the delta clears
   `ProactiveSwapMinimumProfitImprovementFraction` of current profit;
   confirms the bar is meaningfully higher than the reactive case (same
   fixture, both modes, different outcome).
4. `TryProposeTypeSwap` emits `AdjustRouteOrder` alone for an idle-unit
   candidate, `[LeaseAircraftOrder, AdjustRouteOrder]` for a lease
   candidate, and `[AircraftOrder]` alone (no `AdjustRouteOrder` yet)
   for a buy-outright candidate.
5. `RecentlySwappedPairKeys` excludes a pair within
   `TypeSwapCooldownQuarters`, includes one just outside it — mirrors the
   existing `RecentlyShrunkPairKeys`/`RecentlyClosedPairKeys` boundary
   tests already in `AiToolkitTests.cs` exactly.
6. `ApplyManagementToolkit` integration: reactive swap fires instead of
   fare-cut/shrink for an eligible unprofitable route when
   `enableAircraftTypeSwap: true`; is a byte-identical no-op when
   `false` (the ablation guarantee); a route already at
   `SustainedSingleAircraftClosureThreshold` still closes, swap never
   attempted; cooldown blocks a second swap on the same pair within
   `TypeSwapCooldownQuarters`.
7. `AiRouteHistory.LastTypeSwapQuarter` stamping in `FullGameRunner.cs`:
   an applied `AdjustRouteOrder` (from any source, not just this new AI
   logic) sets it correctly.
8. Full coverage check on every changed file, per CLAUDE.md's milestone
   gate.

## Measured

Real 300-game sweep (seed 1, 244 quarters, current production defaults —
full-random filler, `RegionalOutcomeReport` bug already fixed), ablated
by temporarily reverting `NetworkOptimizerStrategy`'s own
`enableAircraftTypeSwap: true` to `false`, rebuilding, and diffing
against the identical-seed run with it restored — the same
temporarily-revert-measure-restore discipline every ablation in this
project uses, not a separate code path left lying around.

**Correctness: clean both ways.** 300/300 games passed cleanly, 0
invariant violations, 0 crashes, in both runs. The feature introduces no
new failure mode at the engine level.

**Overall bankruptcy: flat.** 19/300 (disabled) → 18/300 (enabled) —
noise-level, not a real move either direction.

**Regional win share: a real, honest redistribution, not a wash.**

| Region | Disabled | Enabled |
|---|---|---|
| Europe | 94 (31.3%) | **112 (37.3%)** |
| SouthWestAsia | 70 (23.3%) | 70 (23.3%) |
| AustralAsia | 60 (20.0%) | **45 (15.0%)** |
| EastAsia | 34 (11.3%) | 34 (11.3%) |
| NorthAmerica | 30 (10.0%) | 28 (9.3%) |
| SouthAmerica | 12 (4.0%) | 11 (3.7%) |

Europe gains, AustralAsia loses, the other four regions hold steady.
Not root-caused further this round — the plausible mechanism (Europe's
own denser network, established earlier in this same document series as
the most cities and highest total demand weight of any region, gives
`NetworkOptimizerStrategy` more real routes to find a genuinely
worthwhile swap on, while AustralAsia's sparser, historically weaker
network has fewer such opportunities) is consistent with this project's
own recurring finding that regional imbalance tends to amplify existing
structural differences rather than invent new ones, but this is a
plausible read, not a traced one. Flagged honestly, not smoothed over,
matching this document's own standing rule.

**Swap activity: real, and it clusters — the specific risk this document
named upfront.** 1,279 `AdjustRouteOrder`s across 300 games, 109 games
(36.3%) with at least one. Two calendar years account for **39% of all
swap activity**: 1965 (205 swaps) and 1970 (299 swaps). Traced, not just
observed: **1965 is `GameEras.MidGameStartQuarter`'s own first year**
(quarter 41 → year 1965 via `Timeline.YearForQuarter`) — the very first
quarter `NetworkOptimizerStrategy` can ever be selected for any airline,
so every eligible incumbent's entire network gets assessed for a type
mismatch simultaneously, for the first time, all at once. **1970
coincides with a real aircraft's own `AvailableFromYear: 1970`** in
`data/aircraft.json` unlocking a genuine proactive re-fleet wave. Both
are the same risk shape this document flagged before measuring (echoing
M58-follow-up-1's "backlog dump") — but neither shows a *correctness*
consequence: bankruptcy stayed flat across both runs, and neither
calendar year shows an elevated crash or violation count. Read as a
real, understood, first-pass "catch-up" burst rather than a recurring
thrash pattern — distinct from the M58 case specifically because that
one caused a measured win-rate collapse (7.3%→4.0%) and this one didn't
move the aggregate correctness numbers at all.

**Not isolated this round, flagged for a follow-up**: the reactive-vs-
proactive split (which of the two thresholds/branches is actually
driving the Europe/AustralAsia shift) and a per-mechanism ablation.
Two full sweeps (this ablation) already answered the load-bearing
question - does the feature work correctly and is its net effect
acceptable - honestly enough to ship on; a third and fourth sweep to
attribute the redistribution to reactive vs. proactive specifically is
real, valuable follow-up work, not something this round's own scope
required before shipping.

**Conclusion: shipped as designed, both thresholds untouched.** The
regional redistribution is real and worth knowing about, but it's a
redistribution among already-unequal regions (Europe already led,
AustralAsia was already mid-weak), not a new correctness problem or a
collapse - the same "real, honestly-reported side effect, not a reason
to revert" standard this whole roadmap has applied to comparable
findings throughout (e.g. the M56-follow-up EastAsia dominance finding).
`ReactiveSwapMinimumProfitImprovement`/`ProactiveSwapMinimumProfitImprovementFraction`
remain first-principles guesses, not retuned against this specific
result - a natural next lever if the redistribution turns out to matter
more once isolated.

## Decided / confirmed

- Strategy: `NetworkOptimizerStrategy`.
- Cooldown: `TypeSwapCooldownQuarters = 4`.

## Follow-up work, deliberately not done this round

- **Isolate reactive vs. proactive** — which branch actually drives the
  measured Europe/AustralAsia redistribution (see "Measured" above).
  Two more ablation sweeps, not attempted here.
- **Retune `ReactiveSwapMinimumProfitImprovement` ($10,000 flat) /
  `ProactiveSwapMinimumProfitImprovementFraction` (0.5)** — both remain
  first-principles guesses, unlike `MinLoadFactorBeforeSkipping`'s 0.25
  (tuned from a traced real case). The real measurement above didn't
  surface a reason to distrust them (no correctness regression, no
  runaway swap volume), so they're shipped as originally proposed rather
  than retuned blind — but they're the first lever to pull if the
  redistribution turns out to matter more once isolated.
- **The deferred `LastShrunkQuarter` cross-cooldown** — still not
  needed: no thrash pattern showed up in the real measurement (the two
  calendar clusters are one-time catch-up/new-type events, not a
  repeating oscillation), so the single `LastTypeSwapQuarter` cooldown
  already shipped appears sufficient on its own.
- **Wire additional strategies** — `RapidExpansionStrategy`/
  `DomesticProfitOptimizerStrategy` (both Starting-era only) still default
  `enableReactiveTypeSwap`/`enableProactiveTypeSwap` to `false`; out of
  scope by the user's own "non-Starting" framing (see M67 below, which
  extended to every *other* strategy instead).

---

## M67 — extending to the rest of the non-Starting strategies

Direct user follow-up: "add aircraft swap ability to the rest of the
non starting strategies." `RapidExpansionStrategy`/
`DomesticProfitOptimizerStrategy` are both `{ Starting }`-only
(`ApplicableEras`), out of scope by that framing — leaving
`BalancedGrowthStrategy` (`{ MidGame, LateGame }`) unambiguously in scope
and `DistressStrategy` (`{ Starting, MidGame, LateGame }`) genuinely
ambiguous, since it isn't confined to Starting the way the two excluded
strategies are, but M66's own "Interaction with existing mechanisms"
section had explicitly left it out pending its own decision. Asked
directly; the user chose to include it.

### The API needed splitting, not just two more callers

`enableAircraftTypeSwap` (M66's single flag) couldn't express what
`DistressStrategy` needed: reactive without proactive, and no lease/buy.
Split into three independent parameters on `ApplyManagementToolkit`:

```csharp
public static void ApplyManagementToolkit(
    StrategyContext context, List<QuarterOrder> orders, HashSet<int> reservedUnitIds, int spentBeforeToolkit,
    bool enableSustainedSingleAircraftClosure = false,
    bool enableReactiveTypeSwap = false, bool enableProactiveTypeSwap = false, bool allowTypeSwapLeaseOrBuy = true)
```

`EvaluateTypeSwapCandidate` gained a matching `allowLeaseOrBuy = true`
parameter — `false` restricts every candidate (reactive or proactive) to
types already sitting idle, filtered out up front rather than returned
and expected to be filtered by the caller. Safe to rename/restructure
freely — this API was one commit old (M66) with a single caller, not a
stable surface accumulating migration debt.

**Shipped:**
- `NetworkOptimizerStrategy` — unchanged from M66 (`enableReactiveTypeSwap:
  true, enableProactiveTypeSwap: true`, `allowTypeSwapLeaseOrBuy` default
  `true`).
- `BalancedGrowthStrategy` — `enableReactiveTypeSwap: true,
  enableProactiveTypeSwap: false` (see the real regression below),
  `allowTypeSwapLeaseOrBuy` default `true`.
- `DistressStrategy` — `enableReactiveTypeSwap: true,
  enableProactiveTypeSwap: false, allowTypeSwapLeaseOrBuy: false`. A
  distressed airline can fix a genuinely mismatched route using what it
  already has idle, but never commits fresh capital (a lease's recurring
  fee, or a purchase) specifically to enable a swap while already in
  financial trouble — a real cash-risk question, not a default inclusion.

### Measured: a real regression found, isolated, and shipped around

Real 300-game ablation (seed 1, 244 quarters), same methodology as M66 —
temporarily reverting each strategy's own flags, rebuilding, diffing
against the identical-seed run with the real config restored.

**Full rollout (both strategies at full capability - reactive + proactive
for BalancedGrowthStrategy, reactive for DistressStrategy) measured a
real problem, not just a redistribution:**

| | M66 baseline (NetworkOptimizer only) | Full rollout |
|---|---|---|
| Total bankruptcy | 18/300 | **25/300** |
| SouthAmerica bankruptcy | 4 | **12** |
| SouthAmerica avg bankruptcy quarter | 30.8 | **98.4** |
| Avg cash, every region | ~$1-2B | ~$2-5B |

SouthAmerica's bankruptcy count tripled, shifted from a mid-campaign
pattern to a late-campaign one, and every region's own average cash
inflated far beyond what a single-strategy rollout (M66) ever showed —
a real, substantial regression, not the kind of honest-redistribution
finding M66's own Europe/AustralAsia result was.

**Isolated by three further ablation sweeps, same seed:**

| Configuration | Total bankruptcy | SouthAmerica bankruptcy |
|---|---|---|
| Full rollout | 25 | 12 |
| `DistressStrategy` swap disabled | 26 | 12 |
| `BalancedGrowthStrategy` swap disabled entirely | 16 | 4 |
| `BalancedGrowthStrategy` reactive-only (proactive disabled) | 16 | 4 |

`DistressStrategy` is cleared — disabling it barely moves the total
(26 vs 25, noise) and SouthAmerica stays at 12 regardless, confirming it
isn't the driver; a separate region (AustralAsia, 7→5 bankrupt) actually
improves with it enabled, consistent with its own intended purpose.
`BalancedGrowthStrategy`'s **proactive** branch specifically is the
driver — reactive-only reproduces the fully-disabled numbers exactly
(16 total, SouthAmerica back to 4), while its reactive branch alone
contributes nothing harmful.

**Why proactive specifically, and why here:** `BalancedGrowthStrategy` is
the primary MidGame/LateGame workhorse — unlike `NetworkOptimizerStrategy`
(only takes over under real financial trouble,
`AiToolkit.NetworkTroubleRatio`-gated) or `DistressStrategy` (only runs
during a real emergency), it's active for most of a healthy airline's
own MidGame/LateGame quarters. Its own proactive branch re-evaluates
*every* held route each such quarter, so the same 50%-improvement bar
that kept M66's own NetworkOptimizerStrategy-only proactive activity
modest reaches a far larger surface here — enough swap volume,
concentrated on SouthAmerica's own thinner, less redundant network
(fewer routes to absorb a temporary capacity/fare reset), to
meaningfully destabilize it over the second half of a 60-year campaign.
Not root-caused to a single mechanism beyond that — a real, plausible,
volume-driven explanation, not a traced single bug.

**Shipped, by the user's own explicit decision, with the regression
scoped around rather than accepted:** `BalancedGrowthStrategy` ships
reactive-only (`enableProactiveTypeSwap: false`), confirmed clean by the
isolation table above. `DistressStrategy` ships as designed (reactive
only, no lease/buy), confirmed clean and mildly beneficial by the same
ablation. **`BalancedGrowthStrategy`'s own proactive branch is explicitly
deferred, not abandoned** — flagged for a real follow-up once the user's
own next area of work (aircraft ordering/leasing, especially around
newly available models) is further along, since that work is likely to
touch the same territory (how aggressively an AI strategy should react
to a newer aircraft type becoming available).

1052 tests passing (up from 1042), every changed file fully covered
(the same two small pre-existing, unrelated gaps from M66 left
untouched).

### Follow-up (2026-10-05): proactive swaps for BalancedGrowthStrategy, and two evaluation bugs

**Why revisited.** Contested routes stopped counting as network trouble
(`AiToolkit.NetworkTroubleRatio`, 2026-10-03), so `BalancedGrowthStrategy`
went from ~43% to ~96% of mid/late-game picks and
`NetworkOptimizerStrategy` - the only strategy with proactive swaps -
almost disappeared. Type swaps fell 94% (~32k -> ~1.9k per 300 games);
mid/late fleets stopped modernizing, including dropping refuelling stops
once range allows.

**First attempt: just turn it on.** No bankruptcy regression on 2x300
Hard and airlines 60-110% richer, but ~200 swaps a game and, traced on 60
games, 30% of them swap-backs (a route returning to the type it just
left), one route swapped 44 times.

**Root cause, traced on 40 games (temporary instrumentation, reverted):**

1. `EstimatedRealProfit` divided a pair's demand by rivals on the pair
   *plus* every rival airline touching the destination region. For a
   route already flown, only pair rivals share its passengers; 92% of
   swaps were on routes with no pair rival, and 86% of those were still
   cut 2-4x by the region term - often below what the (usually full)
   route really carried.
2. The proactive bar is `0.5 x current profit`, assuming that's positive
   ("the caller guarantees" it). The caller checks the route's *actual*
   profit; the bar used the *estimate*, negative in 36% of swaps. A
   negative bar let 1,141 swaps go to a type estimated worse than the
   current one, and 83% of swap-backs involved such a swap.

**Fixed:** `EstimatedRealProfit` gained `includeRegionRivals` (default
true, so new-route searches are unchanged); `EvaluateTypeSwapCandidate`
passes false for the current type and every candidate, and a proactive
swap now requires a positive current estimate. Tests:
`TypeSwapEvaluationTests.cs` (4), each fix mutation-checked.

**Measured.** 60-game trace: swap-backs 30% -> 8%, most swaps on one route
44 -> 10, stops dropped by a swap 92 -> 256, median seats after/before a
swap 1.08 -> 1.97 (growing with demand). 2x300 Hard:

| | Proactive off | On, unfixed | **On, fixed** |
|---|---|---|---|
| Games with a bankruptcy | 8 / 8 | 8 / 8 | 9 / 8 |
| Average win quarter | 152.8 / 153.7 | 168.1 / 155.4 | 166.2 / 161.1 |
| Type swaps | 1.9k / 1.8k | 66.5k / 60.1k | 58.4k / 56.7k |
| NorthAmerica average cash | $7.7B / $7.8B | $13.7B / $13.7B | $20.7B / $21.9B |
| Europe's wins | 47 / 44 | 62 / 59 | 59 / 62 |
| AustralAsia's wins | 33 / 40 | 32 / 34 | 19 / 29 |
| Network marketing campaigns | 32k / 33k | 42k / 41k | 120k / 116k |

The one extra bankruptcy is a single SouthAmerica airline at quarter 115
- the late-campaign pattern M67 found, but one airline in 600 games, not
4 -> 12. **Shipped on**, as a trade-off to revisit: much richer,
modernizing airlines against wins 8-13 quarters later and some of the
2026-10-03 win balance (Europe, AustralAsia) given back. Network
marketing tripling with richer airlines is unverified as to whether it
pays.
