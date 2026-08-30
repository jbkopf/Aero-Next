# Financial Model Roadmap

Continues the shared milestone numbering from `WIN_CONDITIONS_ROADMAP.md`
(M23-M24). Picks up at M25 — `HISTORICAL_DYNAMICS_ROADMAP.md`'s M20-M22
stay reserved for whenever that roadmap's own work lands.

**Origin:** working on bankruptcy surfaced that none of the game's "big
decisions" actually cost cash. Traced every order type through to
`FinancialResolver` (the only place cash actually moves) and confirmed:
only **operating cost** (capacity flown × `CostPerAvailableSeatKm`) and
**debt service** (`DebtBalance × InterestRatePerQuarter`) touch cash today,
and neither is a player action — both are passive, automatic charges.
`AircraftOrder`, `LeaseAircraftOrder`, and `RouteOrder` have no `Money`
field at all. `NegotiationOrder.Bid` exists but is only ever used as a
competitive *score* input (`Bid.Cents × Standing`) — never actually
deducted from cash, win or lose. `AircraftType.Cost` is real data that no
resolver reads. This roadmap closes that gap end to end, plus the new
mechanics discussed with the user: closing a route, paying down debt
manually, and a headcount-scaled salary cost.

**Nothing in this roadmap is built yet** — every milestone below is
prospective, the same shape `HISTORICAL_DYNAMICS_ROADMAP.md`'s own M20-M22
already have. All formulas/rates marked *(proposed, tunable)* are concrete
enough to implement and test against, not settled as objectively correct —
flagged the same way `WIN_CONDITIONS_ROADMAP.md` flagged its own region
count and streak length, for the same reason: real data from actually
running the campaign is worth more than guessing the "right" number up
front.

**Settled cross-cutting rule, applies to every milestone below rather than
each inventing its own:** cash is deducted **at the moment an order or bid
is placed**, and the order is **rejected outright if the amount would
exceed cash currently on hand** — not silently accepted and left to
surface as bankruptcy next quarter. This applies to every *discrete,
player-submitted* cash cost: an aircraft purchase (M26), a negotiation bid
(M28), a manual debt paydown (M29). It does **not** apply to *passive,
automatic* charges — operating cost, debt service, a recurring lease fee
(M26), salaries (M30) — which keep working exactly as they do today: they
accumulate every quarter regardless of affordability, and running out of
cash because of them is exactly what bankruptcy already exists to catch.
The distinction is deliberate, not an inconsistency: you can always be
caught out by the bills you already committed to, but you can never
knowingly commit to a new one you can't currently pay for.

One direct consequence worth stating plainly rather than leaving implicit:
M26's leasing proposal has **no upfront cost** by design (only the
recurring per-quarter fee), so this rule doesn't gate a `LeaseAircraftOrder`
at submission at all — an airline at $0 cash can still lease, and only
faces a real affordability problem once that first recurring payment comes
due. Flagged here so it reads as an intentional edge of the design, not
something M26 forgot to apply this rule to.

**A resolver-side rejection is not sufficient on its own — settled with
the user as a hard requirement, not a nice-to-have.** Because quarters
resolve simultaneously (CLAUDE.md's core architecture), a player commits
blind and doesn't see any outcome until the whole quarter resolves.
If an unaffordable order is only caught at resolution time, that action
slot is gone for the entire quarter, with nothing to show for it and no
chance to have picked something else instead — the exact "attempt and
fail and waste the turn" outcome that must never happen. A backstop
rejection in `QuarterEngine`/`FinancialResolver` still belongs in this
roadmap as a defensive invariant (catching a bug, not routine play), but
it cannot be the *primary* mechanism.

The real fix is a **shared, queryable affordability check** — e.g.
`FinancialState.CanAfford(Money cost)` — that every decision-maker calls
*before* constructing an order, not just something the resolver checks
after submission. `BaselineAi.DecideOrders` must call it before proposing
an `AircraftOrder`, `NegotiationOrder`, or paydown, and simply choose
something else (or nothing) if it can't afford its first choice — the same
primitive a future human UI would call to grey out or block an
unaffordable action before the player can even select it. One source of
truth, used by both the resolver's backstop and every real decision-maker,
rather than the same affordability logic reimplemented separately in each
place it's needed.

---

## M25 — Fleet inventory (foundation) ✅

**The gap:** there is no persistent record of what aircraft an airline
owns. `Route.Aircraft` references a type, not a specific unit; nothing
tracks a unit's existence once `AircraftOrderResolver` produces an
`AircraftDelivery`. Every milestone below — buying, leasing, retiring,
salaries scaled to fleet size — needs a real ledger to operate on.

**Proposed shape:** a per-airline fleet record, one entry per aircraft
*unit* (not just type + quantity), tracking: aircraft type, ownership mode
(`Owned` or `Leased`), and whether it's currently assigned to a route or
idle (a spare). `AircraftDelivery` (from both `AircraftOrderResolver.
ResolveQuarter` and `ResolveLease`) becomes the event that adds units to
this ledger, tagged by which path produced them. Assigning a unit to a
`RouteOrder` and reassigning it later (moving a plane from a cut route to
a new one) both become fleet operations, not just route-record edits.

**Done when:** an airline's total unit count always reconciles with its
delivery history minus retirements; a unit can be traced from delivery
through however many route reassignments to eventual retirement. Tests:
ownership mode is preserved correctly through delivery; idle units are
queryable; double-assigning the same unit to two simultaneous routes is
rejected.

Done. `AirlineFleet`/`FleetUnit`/`FleetOwnershipMode`/`FleetLedger`
(`AeroNext.Core.Fleet`) — `FleetLedger.ApplyDelivery`/`Assign`/`Unassign`/
`Retire`, all pure functions producing a new `AirlineFleet`, matching every
other resolver in this codebase. `FleetUnit.Id` is a plain per-airline
sequential integer assigned by the ledger (never caller-supplied, never a
GUID or other ambient source — CLAUDE.md's determinism rules), so two
otherwise-identical idle units of the same type stay individually
addressable without any randomness.

**Two real discoveries made while scoping this milestone, before writing
any code, that change what "done" means for every milestone after this
one:** `AircraftOrderResolver` — the existing production-queue/lease
resolver — is wired into *nothing* real. Grepped both real quarter loops
(`AeroNext.Sim`'s M13 loop, `FullGameRunner.cs`): zero references outside
a standalone, disconnected demo scenario in `Program.cs`. `QuarterEngine.
RunQuarter` charges `AircraftOrder`/`LeaseAircraftOrder` their action cost
and era-gates them, but never calls `AircraftOrderResolver.Submit` or
`ResolveQuarter` — so in every real game played so far, ordering or
leasing an aircraft spends an action and produces nothing. Separately,
`RouteOrder` has no ownership check at all — any era-available, in-range
aircraft type can be assigned to a route regardless of whether the
airline has ever ordered, leased, or been delivered one, and M18's
`AirlineStartingConditions.AircraftQuantity` (the "2 starting aircraft")
is the same story: real data, never consumed by anything.

**Deliberately not fixed here.** This milestone builds the ledger as
pure, fully-tested `AeroNext.Core` logic only — it does not wire
`AircraftOrderResolver` into either real loop, and `RouteOrder` still
doesn't check fleet ownership. Both are real, consequential behavior
changes (an AI or player could no longer fly a type it hasn't actually
acquired) that belong to an explicit later step, not a side effect of
adding the ledger. **After this milestone, ordering or leasing an
aircraft still has no effect on an actual game** — ledger, deliveries,
and route assignment all becoming real and connected is M26 and M31's
job, not this one's.

**Whichever future milestone builds a `Dictionary<?, AirlineFleet>`
across quarters must key by airline name (a string), not the `Airline`
record** — the exact bug class the win-condition work (M24) already hit
and fixed: `Airline` has structural equality over its `Routes` list, and
a fresh `Airline` object gets built every quarter at both real call
sites, so record-keyed state would never match its own prior-quarter
entry. Noted here so it isn't rediscovered the hard way a second time.

330 tests total (12 new), 100% line/branch coverage on all 4 new classes;
overall project coverage unchanged at 99.80%/99.66%. No `AeroNext.Sim` or
`AeroNext.QaHarness` changes and no QA harness re-run needed — this
milestone adds infrastructure nothing calls yet, by design, so there was
zero regression risk to check for.

---

## M25b — Wiring fleet ownership into the real campaign ✅

Closes the two gaps M25 flagged and deliberately left open, done before
M26 at the user's request rather than after: `AircraftOrderResolver` now
actually feeds the fleet ledger in both real quarter loops, and
`RouteOrder` now genuinely requires an owned or leased idle unit of the
aircraft type it names — not just an era-available, in-range one. Turning
ownership enforcement on for real required more than the ledger existing:
`BaselineAi` never submitted `AircraftOrder`/`LeaseAircraftOrder` at all,
so with a fixed 2-unit starting fleet it would have opened at most 2
routes ever, then been permanently stuck. Fixed as part of the same pass,
not left broken.

**`QuarterEngine.RunQuarter` gained one new optional parameter,
`AirlineFleet? fleet = null`.** `null` (the default) preserves the fully
permissive behavior every pre-existing call site relies on — confirmed
directly: all **44 existing call sites** across 8 test files needed zero
changes, proving the backward-compatible default actually holds, not just
assumed to. Passing a real fleet turns on enforcement: a `RouteOrder`
needing a new or different aircraft type is rejected without an idle unit
of it, the same rejection list range/era failures already use.
`LeaseAircraftOrder` resolves *inline*, immediately, updating the engine's
working fleet mid-batch — deliberate, since leasing's whole documented
point is same-quarter delivery, and a `RouteOrder` later in the same
order list needs to actually see that unit. `AircraftOrder` (the
production queue) stays external — it genuinely needs cross-quarter,
cross-airline board state a single airline's `RunQuarter` call doesn't
have. `QuarterResult` gained `AirlineFleet? UpdatedFleet` to carry the new
state forward, the same shape `Routes`/`ActionCap` already carry.
`FleetUnit.AssignedRouteKey`'s format (left open in M25) is settled:
`"{OriginId}-{DestinationId}"`.

**Both real loops** (`AeroNext.Sim`'s M13 loop, `FullGameRunner.cs`) gained
a per-airline `Dictionary<string, AirlineFleet>` (keyed by name, not the
`Airline` record — M24's already-hit bug class, avoided deliberately this
time) and one shared `AircraftOrderBoard` (the production line is
genuinely cross-airline/contested). Each airline's starting fleet is
seeded from M18's `AircraftQuantity` (2, real data never consumed until
now) via `FleetLedger.ApplyDelivery` — the thing that actually backs the
guaranteed quarter-1 route now, not an unchecked assumption. M13's own
campaign has no per-seat starting-aircraft concept (it's deliberately flat
and symmetric, no `GameSetup`/region distinction) — all 4 airlines get the
same smallest-range 1955-available type, at the same M18 quantity, keeping
that symmetry intact rather than inventing a per-seat distinction M13 was
never designed to have.

**`BaselineAi` gained minimal acquisition logic, not a redesign.** Renamed
the old `availableFleet` parameter to `eraAvailableTypes` (a real naming
collision with the new concept — it's the era-available *catalog*, not
this airline's own holdings). New logic for opening a route: search for
the best unserved pair reachable using a type it has **idle right now**
first (free, always preferred); only if nothing idle can reach anywhere
worth serving does it lease the longest-range era-available type *and*
propose a route for it in the same order batch — relying on
`QuarterEngine`'s inline lease resolution to make that same-quarter use
possible. Deliberately never uses `AircraftOrder` (the production-queue
path) — leasing alone is enough to stay unstuck, and avoids teaching it
to reason about queue contention, real AI-strategy work out of scope here.
Embodies the same "never attempt what will predictably fail" principle
already settled for cash: a dedicated adversarial test drives it to
depletion and confirms it never once proposes an order the fleet check
would have to reject.

**The M13 campaign trajectory moved again, re-measured properly rather
than assumed unaffected** — real fleet constraints now shape route timing,
not just an unconstrained pick. New peak **quarter 35 (1963):
$33,671,545.43**, bankruptcy **quarter 52 (1967): -$477,460.18**, both
re-found via a full `Enumerable.Range(1, 80)` sweep, not guessed from the
previous round's checkpoints. `AeroNext.Sim`'s checkpoint list rewritten
accordingly (1, 15, 35, 52, 121, 244). M13 still never produces a win —
correct, not a gap: it's still perfectly symmetric, so every region ties
every quarter, and ties never extend a leadership streak (M24's own rule),
confirming that rule holds even under the scenario most likely to expose
a flaw in it.

**Done when:** the 44 pre-existing `RunQuarter` call sites pass unmodified
(proving the default truly is backward compatible); dedicated
`QuarterEngine`/`BaselineAi` tests cover fleet-aware rejection, acceptance
and consumption, aircraft-type swaps freeing the old unit, same-quarter
lease-then-route, deterministic tie-breaks among multiple idle units, and
the AI never proposing what the engine would reject; both real loops
re-verified at full scale.

Done. 341 tests total (21 new — 9 `QuarterEngine`, 3 net new `BaselineAi`,
plus rewriting the 16 pre-existing `BaselineAi` tests that needed a
matching idle fleet to keep their original intent, not just compile).
100% line/branch coverage on every touched class, including a real branch
gap `QuarterEngine`'s coverage report caught (the existing-route lookup's
short-circuit on origin mismatch alone, distinct from same-origin-
different-destination) — closed with a dedicated test, not waived.
Overall project coverage **99.81%/99.70%**, above the established
baseline. 150-game QA harness sweep at 244 quarters: 150/150 clean, 0
invariant violations — win rate held at 2/150 (avg quarter 149, unchanged,
since the win-condition mechanism itself wasn't touched), regional win
*distribution* shifted some (EastAsia's win count roughly doubled,
NorthAmerica's roughly a third of before) — an expected consequence of
route-opening now being genuinely fleet-constrained, not a regression.

---

## M26 — Aircraft economics: purchase, lease, and retirement all cost real cash ✅

**Purchase** (`AircraftOrder`): charge `AircraftType.CostDollars × Quantity`
in cash at the quarter the order is *submitted* — ties the cost directly to
the action, no separate "pending payment" state to track alongside the
existing pending-delivery queue. Per the cross-cutting rule above, an
order that would take cash negative is rejected outright, the same
quarter — not silently accepted.

**Leasing** (`LeaseAircraftOrder`): the opposite cost shape from
purchase, matching CLAUDE.md's "expensive instant-metal escape hatch"
framing as an actual cost curve, not just a label. **No large upfront
charge** — that's the point of bypassing the production queue — but a
**recurring per-quarter lease payment** for as long as the unit stays
leased: *(proposed, tunable)* `LeaseFeePerQuarter = AircraftType.
CostDollars × 0.04` (4%/quarter — a leased unit costs its full purchase
price back out in about 25 quarters, then keeps costing beyond that;
worse than buying if you keep it long-term, which is the honest trade for
never waiting on the production line).

**Retirement/sale** (new, private action per CLAUDE.md's own
already-named-but-never-built "retiring aircraft" — settled with the user
as the *same* action as "sell aircraft," not two): removes a unit from the
fleet ledger.
- **Owned unit:** returns cash — *(proposed, tunable)* a flat 40% of
  `AircraftType.CostDollars`, regardless of age (no age-tracking exists
  or is proposed here — keeps this simple, matching the same instinct
  behind M29's loan design).
- **Leased unit:** ends the lease (stops the recurring `LeaseFeePerQuarter`
  charge). No cash back — you never owned it.

Assigned-to-a-route units can be retired too (freeing that route's
aircraft slot, effectively force-closing it — this is the one place
retirement and M27's route-closing interact; M27 should reuse whatever
"detach aircraft from route" logic this milestone builds, not duplicate
it).

**Done when:** ordering and leasing both move real cash the quarter
they're submitted (or, for leasing, every quarter thereafter until
retired); retiring an owned unit returns the flat resale value and stops
any further cost from it; retiring a leased unit stops its lease payment
with no cash back. Tests: leasing N units costs `N ×
LeaseFeePerQuarter` every quarter, indefinitely, until retired;
retiring mid-quarter doesn't double-charge or skip that quarter's lease
fee inconsistently with retiring mid-quarter for a route's operating cost
(need a consistent rule for "cost this quarter if held at any point during
it" vs. "cost only if held at quarter start" — pick one, apply it
everywhere in this milestone).

**Turned out smaller than sketched above, once traced through:**
`BaselineAi` never submits `AircraftOrder` (confirmed in M25b) and leasing
has no upfront cost by design, so nothing it does was newly blocked by the
affordability gate — zero `BaselineAi` changes needed. **One deliberate
scope narrowing:** retiring a unit *assigned to a route* was sketched
above as force-closing the route, reusing M27's not-yet-built detach
logic. Reconsidered while planning — that couples an aircraft-economics
milestone to route-management logic that doesn't exist yet. **M26's
`RetireAircraftOrder` only retires idle units**; retiring an assigned one
waits for M27's own close-route action. Two single-purpose actions, not
one action implicitly doing two things.

**New order, `RetireAircraftOrder(int UnitId)`** — private, free
(`Cost() => 0`, matching `HireExecutiveOrder`/`RefinanceOrder`). Unlike
`RouteOrder`'s fleet check (permissive when `fleet` is null), retiring
fundamentally requires fleet awareness to mean anything: rejected outright
whenever `fleet` is null, the unit doesn't exist, or the unit is currently
assigned to a route.

**`QuarterEngine.RunQuarter` gained `Money? availableCash = null`.** Null
preserves fully permissive behavior for every pre-existing call site (the
same shape `fleet` established in M25b). When provided (quarter-*start*
cash, before this quarter's own revenue), an `AircraftOrder` whose
`Type.Cost × Quantity` would exceed what's left of it — after every
already-applied order's own cost this quarter — is rejected outright,
never silently accepted to surface as a problem next quarter, per the
user's own settled affordability rule. `QuarterResult` gained
`IReadOnlyList<FleetUnit> RetiredUnits` — each retired unit's
*pre-removal* record, collected because by the time `FinancialResolver`
runs, the unit is already gone from `UpdatedFleet` and can't be looked up
there. `QuarterEngine` deliberately doesn't compute a dollar value itself
— that stays `FinancialResolver`'s constant to own, the same boundary
`UpdatedFleet` already drew for fleet-vs-money separation.

**`FinancialResolver.Resolve` gained two new optional parameters:**
`AirlineFleet? fleet = null` (the *post*-quarter fleet, so a same-quarter
lease is charged immediately and a unit retired this quarter stops
accruing right away, not double-charged) and `IReadOnlyList<FleetUnit>?
retiredUnits = null` (the pre-removal records). Three new computations,
all folded into `NetIncome`, all in `QuarterlyStatement` as their own
lines (`AircraftPurchaseCost`, `LeaseFee`, `RetirementProceeds`) rather
than a lump delta — CLAUDE.md's legibility pillar. Purchases are expensed
the quarter they happen, not capitalized/depreciated — kept deliberately
simple, matching the flat resale fraction. `LeaseFeeRatePerQuarter = 0.04`
and `RetirementResaleFraction = 0.40`, both flagged `(proposed, tunable)`
same as their sketch above — no data yet on whether these values feel
right, only that the wiring is correct.

**Both real loops** pass `finances[name].CashBalance` as `availableCash`
into `RunQuarter`, and the post-quarter `fleet`/`RetiredUnits` into
`FinancialResolver.Resolve`. No `BaselineAi` changes (see above) — it
keeps working exactly as M25b left it, just with its existing leases now
costing something real for the first time.

**Done when:** the pre-existing `RunQuarter`/`Resolve` call sites pass
unmodified (proving the new optional parameters are genuinely backward
compatible); dedicated tests cover affordability rejection/acceptance at
the exact boundary, `RetireAircraftOrder`'s three rejection paths plus its
success path, lease fees scaling with unit count, retirement crediting
owned units and not leased ones, and no double-charge the quarter a unit
is retired; both real loops re-verified at full scale.

Done. 358 tests total (17 new — 9 `QuarterEngine`, 8 `FinancialResolver`,
including one integration-style test driving an unaffordable
`AircraftOrder` through `QuarterEngine` into `FinancialResolver` to prove
the rejection happens before the resolver ever sees it applied, not as a
resolver-side backstop). 100% line/branch coverage on every touched/new
class (`QuarterEngine`, `QuarterResult`, `RetireAircraftOrder`,
`FinancialResolver`, `QuarterlyStatement`). Overall project coverage
**99.82%/99.72%**, at the established baseline. 150-game QA harness
sweep at 244 quarters: 150/150 clean, 0 invariant violations, 0 crashes.

**The campaign trajectory shifted dramatically, re-measured properly
rather than assumed.** `AeroNext.Sim`'s M13 loop: no peak anymore — cash
declines monotonically from quarter 1 ($19.8M) straight to bankruptcy at
**quarter 25 (1961, -$135,587.80)**, never once rising above its starting
figure, found via a full `Enumerable.Range(1, 244)` sweep (previously:
peak quarter 35 at $33.7M, bankruptcy quarter 52). Checkpoint list
rewritten to `{ 1, 12, 24, 25, 121, 244 }` — 121 kept purely as a
stickiness sanity check (cash stays frozen for the ~220 quarters after).
The QA harness sweep confirms this isn't an M13-only artifact: **150/150
games had at least one airline go bankrupt before quarter 244**, and per
region, bankruptcy is now near-universal among seats (e.g. NorthAmerica
144/144, Europe 95/95) at an average bankruptcy quarter in the high teens
to mid-20s across every region. Win rate dropped in count and shifted
drastically earlier — 2/150 clean games won outright, average **quarter
24.0** (versus M25b's 2/150 at average quarter 149) — consistent with
wins now coming from rivals collapsing early rather than sustained
regional leadership.

**Root cause, not a bug:** `BaselineAi` leases a fresh aircraft for
essentially every new route it opens (M25b's fallback logic), and every
leased unit now carries a real, recurring 4%-of-cost fee every quarter
forever. An AI that keeps opening routes every quarter accumulates leased
aircraft — and their fees — faster than route revenue grows early in the
campaign, so the recurring lease drag catches up with cash well before
the campaign's growth curve does. This is the wiring working as designed,
not a defect in it — but the practical result is that `BaselineAi`'s
existing route-opening strategy is now essentially unsustainable long-term
under real lease economics. Worth a deliberate look before or during the
bankruptcy-mechanic work that follows this milestone: whether
`LeaseFeeRatePerQuarter` needs retuning, or `BaselineAi` needs real
buy-vs-lease judgment (a purchase has no recurring cost at all once paid
off) — a decision for the next design pass, not resolved here.

---

## M27 — Closing a route ✅

**The gap:** "opening a route" costs an action; there's no way to
voluntarily exit one. Operating cost is charged on every route flown
regardless of profitability, so a losing route was a permanent drag once
opened — the only "control" a player had was letting an aircraft sit on a
bad route forever, or retiring it entirely (M26), a bigger hammer than
the situation calls for if the aircraft itself is fine and it's just this
one pairing that isn't working. `RetireAircraftOrder`'s own M26 doc
comment named the exact dependency this milestone closes: retirement only
works on an *idle* unit — freeing an *assigned* one first needed this.

**Refined for the `AircraftCount` world M32 introduced, after this text
was first written:** a route can now carry more than one aircraft, so
"closing a route" is redesigned as **detaching one aircraft** — the same
one-at-a-time granularity `RouteOrder` itself already uses to *grow* a
route. New private, free order `CloseRouteOrder(City Origin, City
Destination)` (`src/AeroNext.Core/Turns/CloseRouteOrder.cs`) — no
`UnitId`, unlike `RetireAircraftOrder`: every unit on a route is the same
aircraft type (`Route.Aircraft` is singular) and units carry no
per-unit condition, so which specific one gets freed is immaterial;
`QuarterEngine` picks deterministically (lowest ID). Against a route with
`AircraftCount > 1` it shrinks by one and the route stays; against the
last aircraft on it, the route is removed from the list entirely rather
than lingering as a zero-capacity ghost entry — "close" and "shrink" are
one primitive, not two. Flagged as a deliberate, reasoned refinement of
the pre-M32 text above, not silently revised.

**`QuarterEngine.RunQuarter`** gained `CloseRouteOrder` handling in the
same order-scan loop as every other fleet-aware action: rejected when
`fleet` is null, when `currentRoutes` shows no matching pair for this
airline, or (a defensive guard for a fleet/route state that shouldn't go
inconsistent in practice) when no unit is actually found assigned to that
route key. On success, `FleetLedger.Unassign` frees the unit — its
`Mode` (owned/leased) untouched, so "done when"'s preservation bullet
falls out for free, no code needed for it specifically. `ApplyRouteOrders`
was generalized from folding only `RouteOrder`s to folding `RouteOrder`s
and `CloseRouteOrder`s together in submission order against the same
evolving route dictionary, so a second `CloseRouteOrder` for the same
pair in one batch correctly frees a *different* unit (the first's already
gone from the working fleet by the time the second is processed, the same
sequential-mutation pattern the fleet-aware `RouteOrder` path already
relies on).

**Nothing else needed to change**, confirmed by reasoning through the
architecture rather than assumed, and then verified: operating
cost/revenue for freed capacity stop being charged the *same* quarter the
close happens (`FinancialResolver`/`CompetitiveMarket` both build from
`result.Routes` after `RunQuarter` returns, same as any other route
change); the freed unit is available for reassignment even *within* the
same quarter's batch (close A-B, open A-C with the same type, one batch —
more immediate than the "following quarter" bar originally sketched, a
natural consequence of `workingFleet` threading through the loop in
order); M33's `AvailableSlotsByCity` recomputes "in use" fresh from live
`AircraftCount` every call, so a gateway slot freed by closing becomes
available next quarter with zero coupling needed; `BriefingGenerator`
needed no new case (a rejected `CloseRouteOrder` falls into the existing
generic `OrderRejectedOverBudget` bucket, the same treatment
`RetireAircraftOrder` rejections already get). `BaselineAi` got **zero
changes, deliberately** — it never submits this order, matching M26's own
"buys nothing, so nothing to touch" precedent; teaching the AI to *decide*
when to close an underperforming route is real, valuable future work
directly relevant to the "BaselineAi needs a rework" finding from
M32/M33, but a separate, larger effort than wiring the mechanism itself.

**Done when:** closing a route immediately stops its operating cost from
being charged; the freed aircraft unit is available for reassignment
(same quarter's batch, even). Tests: closing a route an airline doesn't
operate is rejected (including the same-origin/wholly-different-origin
edge cases already given this rigor for `RouteOrder`); the freed unit's
ownership mode is preserved; two closes on the same multi-aircraft pair
in one batch each free a distinct unit.

Done. 396 tests total (11 new). 100% line/branch coverage on every
touched/new class (`CloseRouteOrder`, `QuarterEngine`) — closing a
coverage gap took two extra rounds of targeted tests (the same-origin and
wholly-different-origin edge cases the `&&` inside the route-lookup
needed, mirroring the exact rigor an equivalent `RouteOrder` gap already
got in M25b). Overall project coverage **99.84%/99.54%**, at the
established baseline (only pre-existing, untouched gaps remain —
`BriefingGenerator`, `QuarterOrder`, `NetworkPlanner`). 150-game QA
harness sweep at 244 quarters: 150/150 clean, 0 invariant violations.

**Campaign trajectory confirmed unchanged, not just assumed** — full M13
checkpoint sweep is byte-identical to M33's own output (`BaselineAi`
never submits `CloseRouteOrder`, so every new code path is genuinely
inert for existing scripted content). The QA harness sweep's numbers held
too: bankruptcy timing and the 0/150 win rate are unaffected, exactly as
expected for a purely additive mechanism nothing yet calls.

---

## M28 — Landing rights become a real cash commitment, plus relinquishing a slot ✅

**Reconciled against M33 (this text predates it).** When this milestone
was first sketched, `LandingRightsBoard` tracked only a city-wide
`SlotsGranted` total — nothing recorded which airline held what, so
"relinquish a slot" had no real state to act on yet. M33 built exactly
that missing piece as a side effect of gate-scarcity enforcement:
per-airline `SlotsHeld`, and `LandingRightsBoard.AvailableSlotsByCity`
(held minus what current routes already occupy, computed fresh from live
`AircraftCount` every call). Relinquishment turns out to need almost none
of its own new infrastructure — it's built almost entirely on what M33
already shipped. The two halves of this milestone remain independent of
each other and can ship separately if useful.

### Part 1 — real cash commitment (unaffected by M33, unchanged from the original sketch)

**The gap:** `NegotiationOrder.Bid` is only ever used as `Bid.Cents ×
Standing` to rank bidders — never deducted from cash. CLAUDE.md's own
language ("commit cash + diplomatic effort," "you commit blind") clearly
intends the bid to be a real, at-risk expenditure, not a scorekeeping
number with no consequence.

**Shape:** deduct `Bid` from cash **at submission**, regardless of
outcome — matching "you commit blind" precisely, and consistent with the
already-built "losers get partial payoff" via rising Standing (that
mechanic only makes sense as compensation for a *real* sunk cost; today
it's compensating for nothing). Mechanically, this is a near-exact repeat
of M26's `AircraftOrder` affordability pattern: `QuarterEngine` already
has `availableCash`/`cashSpent` machinery from M26 — a `NegotiationOrder`
just becomes a second order type gated by it (reject outright if
`cashSpent + Bid > availableCash`, per the cross-cutting affordability
rule everywhere else already follows). `FinancialResolver` gains a new
`NegotiationBidCost` line (summed from `appliedOrders.OfType<NegotiationOrder>()`,
folded into `NetIncome` and broken out on `QuarterlyStatement`, the same
treatment `AircraftPurchaseCost` already gets) — win or lose, the bid is
already spent by the time the negotiation resolves quarters later, so
losing charges nothing *additional*, it just never refunds what was
already deducted at submission.

### Part 2 — slot relinquishment, redesigned on top of M33's SlotsHeld/AvailableSlotsByCity

**Settled shape:** private, free order (per CLAUDE.md — resolves against
nothing, same framing `CloseRouteOrder` already uses), returns one held
gate slot to the contested pool for future negotiation by anyone — **not**
a direct sale to a specific rival airline, which would need a whole new
bilateral-transaction mechanic this roadmap doesn't otherwise touch. No
cash returned (the bid was already spent per Part 1; this just stops
occupying a slot you don't want anymore). Still flagged for review since
"sell" was the user's own original word and a literal reading implies a
buyer, which this proposal deliberately simplifies away from.

**The one real design question M33 settles cleanly: relinquish is gated
by *available* balance, not raw *held* count.** A held slot currently
backing a flown route (i.e., counted in `AvailableSlotsByCity`'s
`inUse`) can't be relinquished directly — the airline would end up
holding fewer slots than aircraft it's flying through that gateway, an
inconsistent state nothing else in the sim allows. The player closes the
route first (M27's `CloseRouteOrder`, already shipped, frees the unit and
drops `inUse`), *then* relinquishes the now-unused slot. This wasn't
fully resolvable before M27/M33 existed; it falls out naturally now that
both are in place, and is a genuine sequencing win from having built the
mechanism-first pieces before this one.

**Mechanically:** new `RelinquishSlotOrder(City TargetCity)`. Gated
inside `QuarterEngine` by the *same* `availableSlotsByCity` parameter
M33 already added — reused, not duplicated — checked against a single
city rather than a route's two endpoints, and drawing down the *same*
per-quarter `slotsSpentByCity` running tracker `RouteOrder`'s gateway
consumption already uses (a relinquish and a same-quarter route-growth
order genuinely compete for the same available balance — only one can
draw down the last available slot at a city in one quarter). `QuarterEngine`
only decides eligibility, the same split `NegotiationOrder` already has:
the actual `LandingRightsBoard` mutation — decrementing both `SlotsHeld`
for that airline and the city-wide `SlotsGranted` (returning it to what
`SlotsRemaining` sees as contestable) — happens in the caller, from
`result.AppliedOrders`, via a new `LandingRightsResolver.Relinquish(board, airline, city)`,
mirroring `Submit`'s own shape.

**Done when:** submitting a `NegotiationOrder` immediately reduces cash by
the bid amount, win or lose, and is rejected outright if it would exceed
cash on hand; relinquishing a slot with a positive *available* balance
returns it to the city's contestable pool immediately, visible to the
very next negotiation resolution; relinquishing a slot that's zero,
absent, or fully in-use (backing a live route) is rejected. Tests: losing
a negotiation still leaves the bid spent (not refunded); relinquishing a
slot the airline never held is rejected; relinquishing a slot that's
currently backing a route is rejected until the route is closed first;
a relinquish and a route-growth order for the same gateway in one batch
correctly can't both succeed off the same last available slot.

**Shipped exactly as reconciled above — one scope addition found while
planning, not in the original sketch:** unlike M26/M27 (where `BaselineAi`
never used the newly-gated feature at all, so it needed zero changes),
`BaselineAi` *already* submits a `NegotiationOrder` every game with a
fixed `$2,000,000` bid. Left cash-blind, it would have started proposing
bids it can't afford the moment M28 shipped — exactly the "predictably
fails" scenario avoided everywhere else. `BaselineAi.DecideOrders` gained
one new optional `Money? availableCash = null` parameter and a single
guard before its existing negotiation step (skip if cash on hand is below
the bid amount) — negotiation is the only cash-consuming action it ever
proposes, so no running "spent so far" tracker was needed the way its
existing fleet/slot reservation trackers are. `RelinquishSlotOrder` itself
needed no `BaselineAi` awareness — it never submits one, the same "doesn't
use the new mechanism" precedent M26/M27 established for their own orders.

**Done when:** matched exactly. `QuarterEngine.RunQuarter`'s existing
`availableCash`/`cashSpent` machinery gates `NegotiationOrder` the same
way it already gated `AircraftOrder`; `availableSlotsByCity`/
`slotsSpentByCity` gates `RelinquishSlotOrder` the same way it already
gated `RouteOrder`'s gateway consumption — both extensions to
already-proven M26/M33 infrastructure, not new architecture.
`LandingRightsResolver.Relinquish` mirrors `Submit`'s own shape exactly.

Done. 417 tests total (21 new — 11 `QuarterEngine`, 3 `FinancialResolver`,
4 `LandingRightsResolver`, 3 `BaselineAi`). 100% line/branch coverage on
every touched/new class (`RelinquishSlotOrder`, `QuarterEngine`,
`FinancialResolver`, `QuarterlyStatement`, `LandingRightsBoard`,
`LandingRightsResolver`, `BaselineAi`). Overall project coverage
**99.84%/99.57%**, at the established baseline (only pre-existing,
untouched gaps remain). 150-game QA harness sweep at 244 quarters:
150/150 clean, 0 invariant violations, 0 crashes.

**Campaign trajectory shifted again, measured not assumed — and this
time both real loops carry genuinely new behavior, not just one.**
`AeroNext.Sim`'s M13 loop: bankruptcy moved from quarter 16 to **quarter
11 (1957, -$891,513.07)**, found via a full checkpoint sweep. Route count
still freezes at 2 from quarter 3 (M32's structural finding, unchanged),
but now every quarter `BaselineAi` affords its $2M gateway bid, it pays
it immediately whether or not the negotiation eventually wins — a real
cash drain stacked directly on top of the lease fees (M26) and
action-budget competition (M32/M33) already squeezing it. The QA harness
sweep confirms this isn't M13-specific: bankruptcy now averages quarter
4.9-7.2 across regions (down from M27/M33's 5.8-9.6 — earlier again), win
rate holds at **0/150**. This is the fourth milestone running where a
correct, individually-justified mechanic has made this un-tuned heuristic
AI's campaign trajectory worse — reinforcing rather than changing the
standing finding: `BaselineAi` needs a real rework (buy-vs-lease
judgment, load-factor-aware growth throttling, genuine cash budgeting
across *all* its actions rather than a single guard on the newest one)
before M26/M32/M33/M28's mechanics can be fairly judged on campaign feel.

---

## M29 — Loan restructuring: automatic amortization + manual paydown

**Settled with the user, precisely:** additional borrowing keeps merging
into one running `DebtBalance` — no per-loan tracking, same as today.
Automatic quarterly debt service becomes interest *plus* a portion of
principal, "based on reasonable life of the loan" — since balances merge,
this can only be a percentage *of the current balance* each quarter (a
declining-balance schedule), not a fixed payment computed against some
original principal that no longer exists as a distinct number once loans
combine.

*(Proposed, tunable)* `PrincipalRepaymentRatePerQuarter = 0.05` (5% of
current balance per quarter) — roughly a 10-year amortization *feel*: about
90% of an untouched balance clears within 40 quarters under geometric
decay at this rate (it never reaches exactly zero — a real property of
declining-balance schedules, not a bug — the balance asymptotically
shrinks rather than terminating on a fixed date, which is the honest
consequence of not tracking a fixed loan term).

**New player action:** manual paydown — a private order, reduces
`DebtBalance` by a specified amount using cash on hand, on top of (or
instead of relying on) the automatic principal component. Per the
cross-cutting rule above, rejected if the amount exceeds either the
current debt balance or cash on hand.

**Done when:** every quarter's debt service automatically reduces
`DebtBalance` by principal-rate × balance, not just charging interest on a
static number. Tests: an untouched debt balance visibly shrinks over many
quarters without any player action; a manual paydown reduces the balance
immediately and correspondingly reduces the next quarter's automatic
principal-plus-interest charge (since it's computed against the new,
smaller balance); paying down more than the current balance or more than
available cash is rejected.

---

## M30 — General salaries: a headcount/scale cost

**Settled with the user:** independent of `HireExecutiveOrder` (which
stays exactly as it is — free, raises the action cap) — a separate,
ongoing cost representing rank-and-file staff, scaled to something other
than executives.

**Proposed hook:** fleet size (owned + leased units, from M25's ledger) —
the most natural already-available scaling basis; a bigger fleet
genuinely needs more crew, maintenance staff, and schedulers to run. Route
count was considered and rejected as the primary hook: a route uses an
aircraft unit already counted, so scaling by both would double-count the
same underlying capacity. *(Proposed, tunable)*
`SalaryCostPerAircraftPerQuarter` — a flat per-unit rate, charged
automatically alongside operating cost and debt service, regardless of
whether that unit is actively flying a route or idle (staff still need
paying whether or not every plane is in the air that specific quarter).

**Done when:** salary cost appears in every quarter's P&L, scaling
correctly with total fleet size and moving immediately when a unit is
delivered or retired. Tests: an airline with zero aircraft pays zero
salary cost; salary cost per unit is identical regardless of that unit's
route-assignment status.

---

## M31 — Campaign integration and re-verification

Wire every milestone above into the two real consumers — `AeroNext.Sim`'s
M13 loop and `AeroNext.QaHarness/FullGameRunner.cs` — replacing the flat
scripted `BaselineAi` behavior (which currently never leases, retires,
closes a route, pays down debt, or manages a fleet beyond "route exists or
doesn't") with whatever minimal decision logic lets it exercise these new
costs realistically, without overreaching into the deeper AI-fleshout work
this roadmap explicitly isn't trying to do.

**The affordability question is settled, and settled as a hard
requirement, not routine behavior to design around:** a player (or AI)
must never be able to even *attempt* an order it can't afford — not
"attempt and get rejected harmlessly," which is not good enough on its
own. Verified directly against `QuarterEngine.RunQuarter` before writing
this: a rejected order today (range, era-availability, action-cap) is
skipped *before* `spent` is incremented, so it costs nothing, not even the
action slot — but "costs nothing" is beside the point. Because quarters
resolve simultaneously, an order that fails at resolution time still
consumed the *turn's opportunity* to have chosen something else, and
nobody finds out until it's too late to fix. `BaselineAi`'s new decision
logic must call the shared `CanAfford` check (see the cross-cutting rule
above) before it ever constructs an `AircraftOrder`, `NegotiationOrder`,
or paydown — filtering its own candidate moves down to ones it can
actually pay for, the same way it already filters candidate routes down
to ones within aircraft range, rather than proposing its first choice
blind and hoping. **Done when:** a dedicated test drives `BaselineAi`
with an airline down to near-zero cash and confirms it never once
proposes an order the resolver's backstop would have to reject — the
backstop should be provably unreachable in every real game the QA harness
runs, not just theoretically present.

This is expected to legitimately move every already-recorded campaign
figure from `SIM_CORE_ROADMAP.md` and `WIN_CONDITIONS_ROADMAP.md` — the
same way every city-expansion round and the Asia split each did — since
real cash costs on aircraft/leasing/negotiation/salaries change the whole
trajectory, not just add a footnote. Full re-measurement against real
data required, not assumed to still hold.

---

## Kept in mind, not scheduled

- **Fare stance / service level / marketing "postures"** — CLAUDE.md
  describes these as free dials that should affect competitiveness and
  revenue; today `Airline.Quality` is a single placeholder knob and
  `DefaultYieldPerPassengerKm` is a flat global rate, both pre-existing
  gaps unrelated to what this roadmap was scoped to fix. Adjacent to
  "financial model," but a real, separate body of work.
- **Interest rate scaling with leverage** — today's `InterestRatePerQuarter`
  is fixed at game start (2%) regardless of how much debt an airline
  carries. A realistic "the more you borrow, the worse your rate gets"
  dynamic is a natural future refinement, not required for this roadmap's
  own scope.
- **Ongoing gate-holding cost** — once a slot is won, there's no per-quarter
  cost to keep holding it (unlike a real airport gate lease). Worth
  revisiting once M28's slot-relinquishment mechanic has been played with
  for a while and it's clear whether "free to hold forever" undermines the
  contest.
- **Age-based aircraft depreciation** — M26 proposes a flat resale
  fraction regardless of age, deliberately simple. A real depreciation
  curve is a plausible enrichment once fleet age is actually being
  tracked for other reasons (it isn't, yet).
