# Aircraft Acquisition Limits: Purchase/Lease Windows, Lease Scarcity, Validation

**Status: proposal, not implemented.** This is a design document, not a
roadmap log — nothing here has shipped. Written per the user's own direct
request ("let's make a plan to address some of these issues"), following
a research pass into the current state of purchasing/leasing (no design
doc, reported directly in conversation).

## The six requested changes

1. Aircraft become unavailable for **purchase** after an end-of-manufacture
   date, and unavailable for **lease** 5 years after that same date.
2. Leasing is unavailable for the first **2 years** after an aircraft's
   own release (`AvailableFromYear`).
3. Leasing gets a **quantity cap**, dynamic by age — newest aircraft have
   few units available, older ones more.
4. When lease demand exceeds the cap, available units are **distributed
   as evenly as possible**, with any discrepancy (remainder) **given out
   randomly**.
5. Leasing gets a **nominal upfront cost per unit** (today it has none —
   only the existing recurring 4%/quarter fee).
6. **Invalid-value validation** — reject a non-positive `Quantity` on
   both `AircraftOrder` and `LeaseAircraftOrder`, closing a real gap
   (confirmed during the research pass: nothing in `QuarterEngine.cs`
   checks `Quantity > 0` today, for either order type).

## Decided scope

Answered directly by the user (recorded here so the reasoning survives
past this conversation):

- **Lease cap semantics: a per-quarter flow cap**, not a standing
  campaign-wide stock limit — the same shape
  `AircraftType.ProductionRatePerQuarter` already has for purchases, not
  a "how many are simultaneously out on lease right now" ceiling.
- **Cap curve shape: a linear ramp** from a small floor at first
  lease-eligibility up to a ceiling, then flat.
- **Upfront lease cost: a flat fraction of the type's purchase price**,
  smaller than the existing recurring fee.
- **Shortfall handling: partial fill.** An airline that asked for more
  than its even share still gets its even share (plus, for some,
  a randomly-awarded +1), not full rejection.
- **The same-quarter-use tension (found while designing #3/#4, see
  below): accepted.** Leasing under the cap resolves after the quarter,
  the same two-phase way purchasing already does. Same-quarter use by a
  paired `RouteOrder`/`AdjustRouteOrder` no longer happens automatically
  — every AI call site relying on that today needs updating (see its own
  section below, a real, scoped-but-substantial piece of this plan).

## 1 & 2: Two separate acquisition windows, split from today's one

Today, `Timeline.IsAvailable(type, year) => type.AvailableFromYear <= year`
is the *only* era check that exists, shared by every order type that
touches an aircraft (`RouteOrder`, `AdjustRouteOrder`, `AircraftOrder`,
`LeaseAircraftOrder`, via `QuarterEngine.IsAircraftAvailable`'s own
switch). It's a floor only — nothing ever becomes unavailable later in
the campaign.

**New `AircraftType` field:**

```csharp
int? EndOfManufactureYear = null
```

Nullable, trailing, defaulted — the same migration-safe shape
`AiRouteHistory.LastTypeSwapQuarter` used in M67 (`null` = never stops
being manufactured, so every existing catalog entry and test fixture
that doesn't set it keeps behaving exactly as today).

**Two new `Timeline` queries, replacing `IsAvailable` for the two order
types that actually *acquire* a new unit:**

```csharp
public const int LeaseEligibilityDelayYears = 2;
public const int LeaseAvailabilityExtensionYears = 5;

public static bool IsAvailableForPurchase(AircraftType aircraft, int year) =>
    aircraft.AvailableFromYear <= year
    && (aircraft.EndOfManufactureYear is null || year <= aircraft.EndOfManufactureYear);

public static bool IsAvailableForLease(AircraftType aircraft, int year) =>
    aircraft.AvailableFromYear + LeaseEligibilityDelayYears <= year
    && (aircraft.EndOfManufactureYear is null || year <= aircraft.EndOfManufactureYear + LeaseAvailabilityExtensionYears);
```

**`Timeline.IsAvailable` itself stays exactly as-is, unchanged, and keeps
being what `RouteOrder`/`AdjustRouteOrder` use.** This is a deliberate,
minimal-blast-radius choice: flying an aircraft you already own or have
leased doesn't need an upper bound — end-of-manufacture stops you from
*acquiring new* units, it doesn't retroactively ground the ones you have.
Only `QuarterEngine.IsAircraftAvailable`'s own switch changes, routing
`AircraftOrder` to `IsAvailableForPurchase` and `LeaseAircraftOrder` to
`IsAvailableForLease`:

```csharp
private static bool IsAircraftAvailable(QuarterOrder order, int year) => order switch
{
    RouteOrder ro => Timeline.IsAvailable(ro.Aircraft, year),
    AdjustRouteOrder aro => Timeline.IsAvailable(aro.AircraftType, year),
    AircraftOrder ao => Timeline.IsAvailableForPurchase(ao.Type, year),
    LeaseAircraftOrder lo => Timeline.IsAvailableForLease(lo.Type, year),
    _ => true,
};
```

## 3: The lease quantity cap (age-based, linear ramp, per-quarter flow)

New constants and a new method — proposed home: the new resolver class
(below), not `Timeline`, since this is genuinely part of the allocation
mechanic, not a pure calendar fact:

```csharp
public const int MinLeaseCapPerQuarter = 2;   // (proposed, tunable)
public const int MaxLeaseCapPerQuarter = 20;  // (proposed, tunable)
public const int LeaseCapRampYears = 15;      // (proposed, tunable)

public static int LeaseCapForQuarter(AircraftType type, int year)
{
    var leaseEligibleYear = type.AvailableFromYear + Timeline.LeaseEligibilityDelayYears;
    var ageInYears = Math.Max(0, year - leaseEligibleYear);
    var ramped = MinLeaseCapPerQuarter + ageInYears * (MaxLeaseCapPerQuarter - MinLeaseCapPerQuarter) / LeaseCapRampYears;
    return Math.Min(MaxLeaseCapPerQuarter, ramped);
}
```

Age is measured from **lease-eligibility start** (`AvailableFromYear + 2`),
not from `AvailableFromYear` itself — "newest aircraft" in a leasing
context means "just became leasable at all," not "just announced."
`MinLeaseCapPerQuarter`/`MaxLeaseCapPerQuarter`/`LeaseCapRampYears` are
first-principles proposals, explicitly not measured — flagged the same
way `ReactiveSwapMinimumProfitImprovement`/
`ProactiveSwapMinimumProfitImprovementFraction` were in M66, meant to be
revisited once real sweep data exists.

## 4: Even-plus-random distribution when demand exceeds the cap

**The algorithm**, given cap `C` for one type this resolution cycle and
`N` airlines requesting quantities `r_1..r_N` (total demand `R = Σr_i`):

- `R <= C`: no contention, everyone gets exactly what they asked for.
- `R > C`:
  1. `baseShare = C / N` (integer floor).
  2. Every airline provisionally receives `min(r_i, baseShare)` — an
     airline that asked for *less* than its even share keeps its full
     ask; nothing is held back from an under-demander. Deliberately no
     iterative water-filling to redistribute an under-demander's own
     unclaimed share to a bigger requester — the same simplification
     `CompetitiveMarket.Resolve`'s own doc comment already accepts
     ("leftover demand... goes uncarried... rather than exhaustively
     reallocated") for the identical legibility reason.
  3. Remaining capacity after step 2 is handed out **one unit at a time,
     drawn uniformly at random** (a new, dedicated `SimRng` stream — see
     below) from airlines that still want more (`r_i` not yet fully
     met), until the remainder is exhausted or nobody wants more.

**A new, dedicated RNG stream, not `campaignRng` reused.** `QuarterEngine`
itself has zero RNG access today and stays that way — this resolution
happens outside it, in `FullGameRunner.cs`'s own outer loop, the same
place `AircraftOrderResolver`/`LandingRightsResolver` already run.
Mirrors `routeWeightRng`'s own exact precedent (`FullGameRunner.cs:76`,
"a separate stream from campaignRng \[...\] so it never perturbs the real
negotiation-resolution draws"): a fresh consumer of `campaignRng` would
shift every subsequent negotiation-resolution draw for every existing
seed. Proposed: `leaseAllocationRng = new SimRng(unchecked(campaignSeed + <a fresh offset>))`.

## 5: Nominal upfront lease cost — and why it can't be charged where purchase cost is

**The formula**, per the confirmed decision: `Type.Cost * LeaseUpfrontCostFraction`
(proposed: `0.015`, i.e. 1.5% — smaller than the existing 4%/quarter
recurring fee, so a one-time payment costs less than one quarter's own
ongoing fee).

**Why this can't reuse `FinancialResolver`'s existing `purchaseCost`
pattern.** Purchase cost (`FinancialResolver.cs:203`) is charged against
the *requested* quantity, at order-submission time, inside the normal
per-quarter financial resolution — safe *only* because every purchased
unit eventually arrives (delayed, never dropped). Lease requests under
the new cap can go **partially or fully unfulfilled with no
carry-forward** (see the timing section below) — charging upfront cost
on the *requested* quantity would bill an airline for units it may never
receive. The upfront lease cost has to be charged against the *actually
allocated* quantity, which isn't known until the cross-airline
resolution step runs — one quarter after the request, the same delay
purchases already have.

**Where it actually gets charged:** in the same outer-loop step that
applies lease deliveries to `fleets[name]` (see the timing section
immediately below), alongside — not inside — the existing
`FinancialResolver.ResolveQuarter` call, mirroring how `AircraftOrderResolver`'s
own delivery-application loop (`FullGameRunner.cs:164-167`) already lives
outside `FinancialResolver` entirely.

## 6: Invalid-value validation

Straightforward, no open design question: `QuarterEngine`'s validation
loop gains a `Quantity <= 0` rejection for both `AircraftOrder` and
`LeaseAircraftOrder`, at the same point the existing era-availability
checks live (before any cost is computed or committed).

## The architectural consequence: leasing becomes two-phase, like purchasing

This is the single biggest structural change in this plan, and it wasn't
obvious until designing #3/#4 forced the question — flagged plainly
rather than folded in quietly.

**Today:** `LeaseAircraftOrder` resolves *inline*, inside one airline's
own `QuarterEngine.RunQuarter` call (`QuarterEngine.cs:542-545`) —
`workingFleet` gets the new unit immediately, so a `RouteOrder` (or,
since M66/M67, an `AdjustRouteOrder`) later in the *same submitted
batch* can use it. This is deliberate, existing design — the class doc
comment on `LeaseAircraftOrder` itself calls it "the expensive instant-metal
escape hatch," and CLAUDE.md's own design language echoes it.

**Why a cap breaks this:** `QuarterEngine` resolves one airline's own
quarter at a time, with no visibility into what any other airline is
requesting. "Distributed evenly among everyone requesting this quarter"
is inherently cross-airline — it can only be computed once every
airline's own request for the quarter is known, which isn't true until
every airline's own `QuarterEngine.RunQuarter` call for that quarter has
already returned.

**The confirmed resolution:** lease requests move to the exact two-phase
shape purchases already have.

- A `LeaseAircraftOrder` submitted in quarter `Q` is *accepted* by
  `QuarterEngine` (era check, `Quantity > 0` check, 1 action spent) but
  no longer mutates `workingFleet` inline — that line is removed.
- At the *end* of quarter `Q`'s own iteration of `FullGameRunner.cs`'s
  outer loop (the same point `AircraftOrderResolver.Submit` already
  runs, `FullGameRunner.cs:424`), each airline's own applied
  `LeaseAircraftOrder`s get submitted to a new pending-request
  collection for that type.
- At the *start* of quarter `Q+1`'s own iteration (the same point
  `AircraftOrderResolver.ResolveQuarter` already runs and applies
  purchase deliveries, `FullGameRunner.cs:157-167`), the new resolver
  runs the even-plus-random allocation across every airline that
  requested that type in quarter `Q`, applies the resulting units to
  each airline's `fleets[name]` (`FleetOwnershipMode.Leased`), and
  charges each airline the upfront cost for whatever it actually
  received.
- **No carry-forward, unlike purchases.** A purchase order's unfulfilled
  remainder stays in the board and competes again next quarter,
  eventually filling completely. A lease request's own unfulfilled
  remainder is simply gone once this resolution runs — "distributed as
  evenly as possible" describes one quarter's own contest, not a
  backlog. An airline that still wants more submits a fresh
  `LeaseAircraftOrder` (another action) and competes again in the next
  quarter's own pool. This keeps the new board genuinely simpler than
  `AircraftOrderBoard` — it never needs to hold anything past one
  resolution cycle.

**New types, mirroring the existing purchase-side shapes exactly:**

```csharp
public sealed record PendingLeaseRequest(Airline Airline, AircraftType Type, int Quantity, int RequestedQuarter);
public sealed record LeaseRequestBoard(IReadOnlyList<PendingLeaseRequest> Queue) { public static LeaseRequestBoard Empty { get; } = new(Array.Empty<PendingLeaseRequest>()); }
public sealed record LeaseDelivery(Airline Airline, AircraftType Type, int Quantity, Money UpfrontCost, int DeliveredQuarter);
```

`LeaseAllocationResolver` (new file, `src/AeroNext.Core/Fleet/`), two
methods mirroring `AircraftOrderResolver`'s own shape:

```csharp
public static LeaseRequestBoard Submit(LeaseRequestBoard board, Airline airline, LeaseAircraftOrder order, int currentQuarter);

public static (LeaseRequestBoard Board, IReadOnlyList<LeaseDelivery> Deliveries) ResolveQuarter(
    LeaseRequestBoard board, IReadOnlyList<AircraftType> catalog, int currentQuarter, int year, SimRng rng);
```

`ResolveQuarter` always returns `Board` with an **empty** `Queue` — the
one real difference from `AircraftOrderResolver`'s own version, which
can carry a nonempty remainder forward.

### The AI ripple: nine call sites need updating

A full search (`grep -rn "new LeaseAircraftOrder" src/AeroNext.Core/Ai/`)
found **every strategy in the codebase** pairs a `LeaseAircraftOrder`
with an immediate same-batch `RouteOrder`/similar today, relying on the
inline resolution this plan removes:

- `AiToolkit.cs:380` (`TryProposeCandidate`, shared by
  `BalancedGrowthStrategy`/`NetworkOptimizerStrategy`'s own new-pair
  search)
- `AiToolkit.cs:553` (`TryProposeTypeSwap`, M66/M67's own swap
  mechanism)
- `BalancedGrowthStrategy.cs:209`
- `BaselineAi.cs:234`, `BaselineAi.cs:278`
- `DomesticProfitOptimizerStrategy.cs:216`
- `NetworkOptimizerStrategy.cs:280`
- `RapidExpansionStrategy.cs:163`, `RapidExpansionStrategy.cs:217`

Once leasing resolves next-quarter instead of inline, a same-batch
`[LeaseAircraftOrder, RouteOrder]` pairing would have the `RouteOrder`
rejected outright (no idle unit exists yet) — a wasted action every time
this path fires, a real functional regression, not just a timing
nuance.

**The fix already exists as a precedent in this exact codebase, from
M66/M67's own `TryProposeTypeSwap`.** Its `CanBuyOutright` branch
already handles exactly this shape — submit `AircraftOrder` alone this
quarter, no paired action, and trust a *later* quarter's own routine
re-evaluation to notice the now-idle delivered unit and act on it then
(a fresh action, a fresh quarter, the ordinary idle-unit path every
strategy already has). Every one of the nine call sites above needs the
identical treatment: submit the `LeaseAircraftOrder` alone, drop the
paired order, let the strategy's own next real pass pick up the
delivered unit naturally once it shows up idle. This is mechanical once
recognized, not a new design problem nine times over — but it is nine
real call sites to touch, each in a different file, each needing its own
quick correctness check (a `RouteOrder`/`AdjustRouteOrder` doesn't
silently still get proposed alongside the lease anymore).

## New/changed files

- `src/AeroNext.Core/Airlines/AircraftType.cs` — new `EndOfManufactureYear` field.
- `src/AeroNext.Core/World/Timeline.cs` — new `IsAvailableForPurchase`/
  `IsAvailableForLease`, new `LeaseEligibilityDelayYears`/
  `LeaseAvailabilityExtensionYears` constants. `IsAvailable` unchanged.
- `src/AeroNext.Core/Fleet/LeaseAllocationResolver.cs` (new),
  `PendingLeaseRequest.cs`/`LeaseRequestBoard.cs`/`LeaseDelivery.cs` (new) —
  mirrors `AircraftOrderResolver`/`AircraftOrderBoard`/
  `PendingAircraftOrder`/`AircraftDelivery`'s own shapes.
- `src/AeroNext.Core/Turns/QuarterEngine.cs` — `IsAircraftAvailable`'s
  switch (route to the two new lease/purchase-specific checks), remove
  `LeaseAircraftOrder`'s own inline `workingFleet` mutation, add
  `Quantity <= 0` rejection for both `AircraftOrder`/`LeaseAircraftOrder`.
- `src/AeroNext.QaHarness/FullGameRunner.cs` **and**
  `src/AeroNext.Sim/Program.cs` — verified, not assumed: `AeroNext.Sim/Program.cs`
  runs its own separate `AircraftOrderBoard`/`AircraftOrderResolver`
  cycle in both its small M13 demo section and its own real campaign
  loop (`campaignAircraftOrderBoard`), and calls
  `AircraftOrderResolver.ResolveLease` directly (`Program.cs:422`) —
  unlike `RouteHistoryByPair`/`RegionsLedLastQuarter` (confirmed absent
  from this file in earlier sessions), the production-queue/leasing
  machinery genuinely is duplicated here, so this file needs the same
  lease-board treatment as `FullGameRunner.cs`, not a "no matching gap"
  pass. Both files: thread a new `leaseAllocationRng` stream; add the
  lease-request-board submit/resolve steps alongside the existing
  purchase-board ones; apply lease deliveries and their upfront cost to
  `fleets`/cash outside `FinancialResolver.ResolveQuarter`.
- `src/AeroNext.Core/Ai/AiToolkit.cs`, `BalancedGrowthStrategy.cs`,
  `BaselineAi.cs`, `DomesticProfitOptimizerStrategy.cs`,
  `NetworkOptimizerStrategy.cs`, `RapidExpansionStrategy.cs` — all nine
  call sites above, dropping the same-batch pairing.
- `data/aircraft.json` — every entry needs a real `endOfManufactureYear`
  (see the data-authoring section below).
- `AircraftOrderResolver.ResolveLease` becomes genuinely obsolete once
  the new two-phase resolver ships — it's the trivial, always-full-quantity,
  always-same-quarter passthrough this whole plan replaces. Confirmed via
  a full usage search: only `AeroNext.Sim/Program.cs`'s own small M13
  demo illustration (`Program.cs:422`) and one direct test
  (`AircraftOrderResolverTests.cs:139-142`) call it — both need updating
  or removing, not left calling a method that no longer reflects real
  leasing behavior.

## Data-authoring follow-up: `EndOfManufactureYear` needs real research

`data/aircraft.json` carries ~40 real historical aircraft, each already
researched for `availableFromYear`. `EndOfManufactureYear` needs the
same treatment — real production-end dates (e.g. the 707 stopped
commercial production in 1979 for something close to a `1979` value;
Concorde's whole fleet was famously small and short-lived; some
Airbus/Boeing types in this catalog are still in production as of the
campaign's own 2015 end date and should get `null`, not a fabricated
end year). Proposed as a **separate pass**, not bundled into this
mechanism's own implementation work — likely a good fit for a research
agent, mirroring how the original catalog was presumably built. Every
existing test fixture's own synthetic `AircraftType` stays unaffected
either way (`EndOfManufactureYear` defaults `null`).

## Testing plan

1. `Timeline.IsAvailableForPurchase`/`IsAvailableForLease` — boundary
   tests at `AvailableFromYear`, `AvailableFromYear + 2`,
   `EndOfManufactureYear`, `EndOfManufactureYear + 5`, and the `null`
   (never-ends) case for each.
2. `QuarterEngine` — an `AircraftOrder`/`LeaseAircraftOrder` rejected
   for `Quantity <= 0`; a purchase rejected once past
   `EndOfManufactureYear`; a lease rejected within the 2-year delay
   window and once past the 5-year extension; confirms `RouteOrder`/
   `AdjustRouteOrder` are unaffected by either new window (still just
   `IsAvailable`).
3. `LeaseAllocationResolver.LeaseCapForQuarter` — numeric fixtures at
   the floor, mid-ramp, and ceiling ages.
4. `LeaseAllocationResolver.ResolveQuarter` — demand under cap (everyone
   gets their full ask); demand over cap with an exact even split (no
   randomness needed); demand over cap with a real remainder (same seed
   reproduces the same random pick, different seed can differ — the
   determinism guarantee, not the specific pick); an under-demanding
   airline keeps its full ask even while others are capped; upfront cost
   charged only against delivered quantity, not requested.
4. `FullGameRunner.cs` integration — a lease request submitted in
   quarter `Q` shows up as a real fleet unit (and a real cash debit) at
   the start of quarter `Q+1`, never in `Q` itself.
5. Each of the nine AI call sites — confirm the paired same-batch order
   is gone, and (for at least one, e.g. `TryProposeTypeSwap`) confirm
   the strategy really does pick the unit up naturally the quarter after
   delivery, closing the loop end to end.
6. Full coverage check on every changed/new file.

## Open questions

None blocking a first implementation pass. Two things worth a quick
look before starting:

1. **The three lease-cap constants** (`MinLeaseCapPerQuarter` = 2,
   `MaxLeaseCapPerQuarter` = 20, `LeaseCapRampYears` = 15) and the
   upfront-cost fraction (1.5%) are first-principles proposals, the same
   status M66's own two thresholds had before measurement — flagged so
   they aren't mistaken for considered final numbers.
2. **Phasing** — this is large enough that shipping it as one PR is
   probably wrong. A natural split: (a) the two new acquisition windows
   + `Quantity` validation (self-contained, no architecture change), (b)
   the two-phase lease resolver + cap + even/random allocation +
   upfront cost (the real structural change), (c) the nine AI call-site
   fixes (mechanical once (b) lands, but real work), (d) the
   `EndOfManufactureYear` data-authoring pass (independent of all of the
   above — can happen any time, before or after). Confirm this split (or
   a different one) before starting, rather than assuming.
