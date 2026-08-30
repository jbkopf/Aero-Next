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

## M29 — Loan restructuring: automatic amortization + manual paydown ✅

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

**One real design decision the settled text didn't fully pin down:**
where manual paydown and automatic principal repayment each sit relative
to `NetIncome`. Resolved by matching existing precedent rather than
inventing one fresh — `RefinanceOrder`'s `Borrowed` was *already* kept
**outside** `NetIncome`, added to cash as its own balance-sheet term
(`cashAfter = CashBalance + borrowed + netIncome`), since raising debt
isn't an operating expense. Manual paydown is the same kind of event in
reverse — kept outside `NetIncome` too, symmetric with `Borrowed`.
Automatic principal is different in kind: a mandatory, recurring
quarterly charge like interest already is, so it stays folded into
`DebtService`/`NetIncome` exactly where interest already lived — only its
*amount* changed, not its treatment. `DebtService`'s own meaning
genuinely expands to interest-plus-principal combined — real-world "debt
service" has always meant both; the interest-only version was a
placeholder simplification this corrects, not a redefinition.
`QuarterlyStatement` gained `PrincipalRepayment` (so `DebtService -
PrincipalRepayment` recovers interest alone) and `ManualDebtPaydown`
(mirroring `Borrowed`'s own field), both the same "broken out, not
silently folded" legibility treatment every prior cost/revenue line this
session got.

**`PayDownDebtOrder(Money Amount)`** — private, free, same framing as
`RefinanceOrder`. `QuarterEngine.RunQuarter` gained `Money?
currentDebtBalance = null` (mirrors `availableCash`'s exact shape) and a
running `debtPaidDownSoFar` tracker (mirrors `cashSpent`) — rejected
outright, not silently clamped, if it would exceed *either* available
cash or the quarter-start debt balance, reusing the existing cash
machinery for the first half and adding one new, small, symmetric piece
for the second. Validated against the quarter-start balance only — a
same-quarter `RefinanceOrder` never increases how much can be paid down
that same quarter, since `RefinanceOrder` still does no `QuarterEngine`-side
processing at all (resolved entirely in `FinancialResolver`, unchanged).
No `BaselineAi` changes — it never submits this order, per the user's own
explicit staging (`BaselineAi` work deferred until after M30, once both
milestones' real numbers are in).

**Blast radius was three pre-existing tests, not the one originally
estimated while planning** — found by actually running the suite, not
assumed: `Resolve_DebtService_IsInterestRateTimesDebtBalance` (expected,
rewritten to assert the combined interest+principal figure);
`Resolve_RefinanceOrder_IncreasesCashAndDebtByTheSameAmount` (not
anticipated — a freshly-borrowed balance isn't exempt from the *same*
quarter's automatic principal repayment, so "cash and debt move by
exactly the borrowed amount" is no longer true even at 0% interest, since
the principal rate applies independently of the interest rate); a debt-
ceiling test that borrowed exactly `ceiling + 1` (no longer sufficient to
trigger bankruptcy on its own, since the same quarter's automatic 5%
repayment pulls the post-borrowing balance back under the ceiling —
fixed by borrowing comfortably past `ceiling / 0.95` instead). All three
were genuine, correct consequences of the design, not bugs.

Done. 427 tests total (10 new — 4 `FinancialResolver`, 6 `QuarterEngine`).
100% line/branch coverage on every touched/new class (`PayDownDebtOrder`,
`QuarterEngine`, `FinancialResolver`, `QuarterlyStatement`). Overall
project coverage **99.85%/99.58%**, at the established baseline. 150-game
QA harness sweep at 244 quarters: 150/150 clean, 0 invariant violations.

**Campaign trajectory shifted again, measured not assumed — this time
purely from a passive/background change, no `BaselineAi` behavior
involved at all.** `AeroNext.Sim`'s M13 loop: bankruptcy moved from
quarter 11 to **quarter 9 (1957, -$601,426.82)**. The automatic principal
component alone adds roughly $500K/quarter against the $10M starting
debt, landing on every airline every quarter with zero player or AI
action required — on top of everything M26/M28/M32/M33 already stacked
on. QA harness sweep: bankruptcy now averages quarter 4.7-7.0 across
regions (essentially matching M28's 4.9-7.2 — this milestone's marginal
effect was real but smaller than M28's direct action-blocking one), win
rate holds at **0/150**. Noted for the upcoming `BaselineAi` work rather
than acted on here, per the user's own staging: this is now the fifth
milestone in a row where the financial model got measurably more
realistic and the same un-tuned heuristic AI's campaign got measurably
worse — the case for a real rework, after M30 lands, is about as
complete as the data can make it.

---

## M30 — General salaries: a headcount/scale cost ✅

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

**The smallest milestone in this sequence, genuinely.** No new order
type, no `QuarterEngine` change, no `BaselineAi` change — a pure passive
cost computed entirely from state `FinancialResolver` already had.
`SalaryCostPerAircraftPerQuarter = Money.FromDollars(250_000)` — a flat
rate, not scaled by a unit's own `Cost` or `Seats` the way `LeaseFee`
reasonably is (real staffing cost doesn't track purchase price the way a
lease payment does). `ComputeSalaryCost(AirlineFleet? fleet)` mirrors
`ComputeLeaseFee`'s exact shape (null fleet → zero, same guard used by
every pre-M30 call site), except summing **every** unit in `fleet.Units`
regardless of `Mode` or `AssignedRouteKey` — unlike `ComputeLeaseFee`,
which filters to `Leased` only. Folded into `NetIncome` alongside the
existing mandatory recurring charges (`DebtService`, `LeaseFee`), not
treated as a discretionary balance-sheet item the way `Borrowed`/
`ManualDebtPaydown` are. `QuarterlyStatement` gained a `SalaryCost` field
— the same "broken out, not silently folded" legibility treatment every
prior cost line this session got. Both real loops (`AeroNext.Sim`,
`AeroNext.QaHarness`) needed zero new wiring — they've passed `fleet:`
into `FinancialResolver.Resolve` since M26.

Done. 432 tests total (5 new, all in `FinancialResolverTests`: zero
salary cost for a null fleet, zero for an empty fleet, linear scaling
across a mixed owned/leased fleet, identical cost for an idle unit vs.
one assigned to a route, and folding correctly into `NetIncome`/cash).
100% line/branch coverage on both touched classes
(`FinancialResolver`, `QuarterlyStatement`). Overall project coverage
**99.85%/99.58%**, at the established baseline. 150-game QA harness
sweep at 244 quarters: 150/150 clean, 0 invariant violations, 0 crashes.

**Campaign trajectory shifted again, measured not assumed.** This
milestone landed just after the standalone decision to zero out starting
debt (see below) — so the "before" baseline is $0 starting debt with no
salary cost, not the $10M-debt baseline M29's writeup used.
`AeroNext.Sim`'s M13 loop: bankruptcy moved from quarter 11 (the $0-debt,
pre-M30 baseline) to **quarter 7 (1956, -$1,656,820.05)** — salary cost
alone is a bigger, faster-biting accelerant than the old $10M starting
debt ever was, at $250,000/quarter/aircraft against a 2-aircraft starting
fleet that grows to a frozen 2-route/2-route-worth-of-units network well
before quarter 7. Unlike every earlier milestone's frozen post-bankruptcy
state, carried passengers stay completely pinned too here (278,304
pax/qtr from quarter 7 through 244, not just cash) — this run's frozen
network happened to be fully capacity-constrained (demand growth can't
raise carried passengers past a route's own seat ceiling) rather than
demand-constrained, a property of this particular frozen network, not a
bug. QA harness sweep: bankruptcy now averages quarter 4.7–6.4 across
regions, win rate holds at **0/150** clean games region-wide (the
per-region `Wins` column in the harness's regional-dominance breakdown —
EastAsia 100, SouthWestAsia 18, NorthAmerica 14, AustralAsia 11, Europe
7, SouthAmerica 0 — tracks a different, weaker "ever led that region"
metric, not the win-condition trigger; no clean game produced an actual
win-condition winner).

This is the sixth milestone in a row (M26, M28, M29, M30, plus the
standalone debt-zero change) where the financial model got measurably
more realistic and the same un-tuned `BaselineAi` heuristic's campaign
got measurably worse, not better — bankruptcy now lands in the game's
second year almost universally, and win rate has never once moved off
zero across any of these changes. That closes out the financial-model
sequence proper. The pattern was never a sign any individual milestone
was wrong — each one, checked in isolation, made the P&L more honest and
was verified against real numbers, not tuned to produce a happier
outcome. But six milestones deep, the case is about as complete as the
data can make it: `BaselineAi` was calibrated against a much gentler cost
structure than now exists, and needs a real rework — not another
one-line constant tweak — before a further financial-model milestone (or
`docs/BASELINE_AI_ROADMAP.md`'s own integration work) can be
meaningfully evaluated against it.

**Standalone, alongside this milestone:** the flat $10,000,000 starting
debt constant (`AeroNext.Sim`'s M13 campaign loop and the QA harness's
`DefaultStartingDebt`) had no documented rationale anywhere in the
roadmap docs, unlike starting cash (`StartingConditionsResolver`'s
`BaseStartingCashDollars = 20_000_000`, scaled by
`RegionalEconomics.Factor`). Measured at $0 instead: M13's bankruptcy
quarter moved from 9 (M29's post-amortization baseline) to 11, and the
QA harness's bankruptcy-quarter averages moved later by roughly half a
quarter to a full quarter per region, everything else unchanged. Kept at
$0 per the user's explicit direction, committed standalone rather than
folded into any one milestone.

**Amendment (found while designing M34, not part of the original
commit):** a flat charge with no reduced rate for an idle unit means a
starting airline that leaves one of its two starting aircraft unassigned
— entirely plausible first-turn play, not misplay — bleeds cash forever
with no path back short of retiring it. Verified against the actual
1955 starting aircraft (Ilyushin Il-14, 28 seats, $0.13/ASK, 350 km/h;
`StartingConditionsResolver.StartingAircraftQuantity = 2`): even paired
with M34's cost-plus fare fix (below), one assigned + one idle unit
loses $53,000–$456,000/quarter across every distance the aircraft can
fly, because no fare can ever amortize a unit that isn't flying any
route. Fixed by splitting the flat rate into two: `SalaryCostPerAircraftPerQuarter`
(unchanged, $250,000 — an assigned/flying unit) and a new
`IdleSalaryCostFraction = 0.15` *(proposed, tunable)* applied to the
same rate for a unit with no `AssignedRouteKey` — a grounded aircraft
still retains some crew/maintenance cost, just far less than one in
active service, so idle capacity stays a real, felt drag (the M30
design's actual point) without being an unrecoverable one. 0.15 was
chosen with a margin under the worst case a distance-based fare could
ever cover: as route distance approaches zero, the maximum idle rate a
still-solvent single-assigned-aircraft-plus-one-idle-unit scenario can
sustain approaches `(DefaultFareMarginOverCost + 1) × 0.90 load - 1 ≈
0.17` — 0.15 clears that with room to spare, verified by direct
calculation at nine distances from 1 km to the aircraft's full 1,600 km
range, all strictly cash-positive. `ComputeSalaryCost` splits
`fleet.Units` by `AssignedRouteKey is not null` and applies each rate to
its own group rather than one flat multiplication.

---

## M34 — Fares: a direct, per-route price the player sets ✅

**Numbering note:** at the time this was written, M31 (this doc's own,
not-yet-built "Campaign integration and re-verification") and M32/M33
(already built, in `ROUTE_CAPACITY_ROADMAP.md`) were all already spoken
for in the shared milestone sequence, so this became M34 — the next
fresh number, placed directly before M31 in the document since fares
needed to exist before that integration pass tried to account for every
mechanic at once. M31 has since been retired from this document
entirely and rebuilt as `docs/BASELINE_AI_ROADMAP.md` (see "Kept in
mind, not scheduled" below) — this note is left as the historical record
of why M34 landed at this number and in this position, not a claim about
the document's current structure.

**Origin:** investigated how ticket revenue currently works, and it
turns out there is no fare at all. `FinancialResolver.ComputeRevenue`
sums `carried × O-D great-circle distance × DefaultYieldPerPassengerKm`
— a flat, hardcoded 0.12 $/passenger-km global constant, identical for
every airline, every route, every era from 1955 to 2015, with zero
player or AI control. `Airline.Quality` (the one knob that exists) only
weights competitive demand *share* in `CompetitiveMarket` — it never
touches price. Both files' own doc comments already flagged this as a
known placeholder, matching CLAUDE.md's still-unbuilt "fare stance...
free to set" market action.

**Settled with the user:** direct, per-route fare control — not a
discrete posture tier, a literal price. A route's default fare at
creation should reflect a reasonable profit margin over that route's own
costs, computed in a way that's easy to extend as more per-route cost
mechanics get added later, rather than a one-off calculation that needs
rewriting each time.

**Design.** `Route` gains `Money? FareOverride = null` — a new,
trailing, defaulted constructor parameter (non-breaking, the same safe
pattern `AircraftType.CruiseSpeedKmh` used, *not* M32's breaking
field-meaning change) — plus a computed property:
```csharp
public Money FarePerPassenger => FareOverride ?? FareCalculator.SuggestedFarePerPassenger(this);
```
This is the key design choice: fare is *not* a required, stored value
every one of the ~170 existing `new Route(...)` call sites would have to
fill in (which would silently zero out revenue everywhere it's
omitted) — it's an optional override with an always-sensible computed
fallback. Every existing route, test, and demo scenario gets a real,
non-zero, cost-derived fare automatically, with zero migration burden.

**New `FareCalculator` static class** (`AeroNext.Core.Finance`) — the
"easy to extend later" piece:
```csharp
public static class FareCalculator
{
    public const double DefaultFareMarginOverCost = 0.30; // (Proposed, tunable)

    // Sum of every currently-modeled per-route, per-*assigned*-aircraft
    // cost, expressed as $ per ONE seat flown this route's full one-way
    // distance. Today: operating cost (M12, the same rate
    // FinancialResolver.ComputeOperatingCost applies per available-seat-km)
    // plus this aircraft's own prorated share of its flat quarterly
    // salary (M30) -- divided across the seats ONE assigned aircraft
    // offers this quarter (route.SeatsPerQuarter / route.AircraftCount,
    // exact by construction, no remainder). Adding another recurring
    // per-aircraft cost later (e.g. a lease fee once a route can carry
    // leased units) means adding one more term here, not touching every
    // call site that reads a default fare -- this method is the single,
    // obvious extension point the user asked for.
    //
    // Necessary, not optional: a margin over operating cost alone can
    // never reliably cover a flat, distance-independent cost like
    // salary -- verified directly against the 1955 starting scenario
    // below, where operating-cost-only margin loses money at every
    // distance the starting aircraft can fly.
    public static Money CostPerSeatCarried(Route route) => ...;

    public static Money SuggestedFarePerPassenger(Route route) =>
        // CostPerSeatCarried(route) grossed up by DefaultFareMarginOverCost,
        // computed in double space and rounded once, same pattern
        // ComputeOperatingCost/ComputeLeaseFee already use.
}
```
Priced per **seat offered**, not per passenger actually carried — load
factor isn't baked into the default fare, so an under-filled route stays
a visible cash drain even at a "profitable" fare, consistent with
`FinancialResolver`'s own existing doc comment and CLAUDE.md's
legibility pillar.

**Verified against a concrete scenario, not assumed correct** — the
user posed it directly: starting conditions, $0 debt, one valid initial
route opened turn one using the starting aircraft allotment, load factor
holds at 90% forever, no other spending, should never go bankrupt.
Checked against the real 1955 starting aircraft (Ilyushin Il-14, 28
seats, $0.13/ASK, 350 km/h, 1,600 km range; 2 owned units) with both
assigned to the route:

| Distance | Op. cost/qtr | Salary/qtr | Net income, margin-over-operating-cost-only (original) | Net income, margin-over-cost-plus-salary-share (fixed) |
|---|---|---|---|---|
| 100 km | $511,056 | $500,000 | **-$413,120** | **+$171,880** |
| 400 km | $1,211,392 | $500,000 | **-$294,063** | **+$290,937** |
| 1,600 km | $1,817,088 | $500,000 | **-$191,095** | **+$393,905** |

The original operating-cost-only margin is cash-negative at *every*
distance the starting aircraft can fly, even the best case — a
guaranteed, structural bankruptcy, not a tuning problem. The fixed
per-assigned-aircraft cost basis above (`CostPerSeatCarried`) is
comfortably cash-positive across the full range, and — combined with the
idle-unit rate reduction recorded as an amendment to M30 above — holds
even in the less-optimal case where only one of the two starting
aircraft is ever put into service.

**New `SetFareOrder(City Origin, City Destination, Money FarePerPassenger) : QuarterOrder`**
— private, free (CLAUDE.md: "resolves against nothing"), same framing as
`PayDownDebtOrder`/`CloseRouteOrder`. Reinterprets CLAUDE.md's existing
"fare stance... free to set (a posture, not a number)" line as a literal
settable price rather than a discrete tier — the CLAUDE.md edit lands at
implementation time, not in this roadmap pass.

`QuarterEngine` gains one rejection rule, matching `CloseRouteOrder`'s
existing shape: a `SetFareOrder` is rejected outright if the airline
doesn't currently operate that pair (checked against quarter-start
`currentRoutes` — the same "can't set a fare on a route opening this
same quarter" punt `RouteOrder`'s type-swap restriction already
established precedent for) or if `FarePerPassenger` is negative. Cost 0.

`ApplyRouteOrders` gains a `SetFareOrder` fold branch
(`existing with { FareOverride = fareOrder.FarePerPassenger }`), and a
**necessary fix to the existing `RouteOrder` branch**: today it always
constructs `new Route(...)` fresh, which would silently reset a custom
fare back to the computed default every time another aircraft is added
to an already-fare-customized route. Must preserve `existing.FareOverride`
when the same type is already there.

`FinancialResolver.ComputeRevenue` gets rewritten to sum, per
`CompetitiveRouteUsage` entry (mirrors `ComputeOperatingCost`'s existing
per-leg iteration exactly): `Route.FarePerPassenger × SeatsUsed`
(`Money × long`, already an existing operator — pure integer math, no
double/round needed for this one, unlike the constant it replaces).
`DefaultYieldPerPassengerKm`/the `yieldPerPassengerKm` parameter on
`Resolve()` get removed entirely — confirmed via a repo-wide search that
no test or real call site ever overrides it, so this is a safe
simplification, not a breaking change to guard.

**Explicit consequence worth stating plainly, not hiding it:** a
one-stop (connecting) passenger already incurs operating cost on *both*
legs they fly (existing `RouteUsage` behavior, unchanged). Revenue now
mirrors that exactly — a connecting passenger's fare becomes the *sum*
of both legs' `FarePerPassenger`, reflecting the real distance flown via
the hub rather than today's flattened direct O-D distance. This will
likely raise connecting-passenger revenue somewhat versus today's
model — expected, and to be measured (not assumed) the same way every
other milestone's trajectory shift was.

**Explicitly scoped out, flagged as a natural larger follow-on:** fare
does **not** yet affect demand size (`GravityDemandModel` untouched) or
competitive share (`CompetitiveMarket`'s `Quality`-weighted split
untouched) — this milestone is a revenue-per-passenger lever only. A
price-elastic demand response, or fare entering the competitive weight
formula (undercutting a rival to win more of a contested pair), is real
and left for later — not built here, so it doesn't get silently implied.

**No `BaselineAi` change** — `SetFareOrder` is optional/private, the
same treatment `PayDownDebtOrder` got; the AI simply never submits one
and every route it opens keeps the computed default fare, consistent
with the user's own `BaselineAi`-rework deferral.

**Done when:** a route's fare is visible and player-settable; a route's
default fare at creation reflects its own aircraft/distance cost-plus-
margin *and* that aircraft's own prorated salary share, not operating
cost alone; revenue for a fully-sold-out route is provably profitable at
the default fare, mirroring `Resolve_ProfitableRoute_IncreasesCash`'s
existing assumption; assigning another aircraft to an already-customized
route preserves the custom fare rather than resetting it; a
`SetFareOrder` against a pair the airline doesn't operate is rejected;
**a dedicated test reproduces the scenario above** (starting conditions,
$0 debt, one valid route opened turn one with the full starting
allotment, 90% load factor held indefinitely, no other spending) and
confirms cash balance never goes negative across a long run — the actual
invariant this milestone exists to guarantee, not just the individual
unit-level cost/revenue checks.

Built exactly as designed above, with no changes to the formulas or
constants already verified: `Route.FareOverride`/`FarePerPassenger`,
`FareCalculator`, `SetFareOrder`, `QuarterEngine`'s rejection rule and
`ApplyRouteOrders` fix, `FinancialResolver.ComputeRevenue`'s rewrite,
and the M30 amendment (`IdleSalaryCostFraction`) all landed together —
the fare mechanism and the idle-unit fix are inseparable in practice,
since the scenario test that proves either one only passes with both in
place. `DefaultYieldPerPassengerKm`/the `yieldPerPassengerKm` parameter
are gone from `FinancialResolver.Resolve` entirely, confirmed dead
before removal. No `BaselineAi` changes — `SetFareOrder` stays unused by
the AI for now, per the roadmap's own scope boundary; teaching it to use
fares is real follow-on work, not done here.

Done. 447 tests total (15 new: 5 in `FareCalculatorTests`, 7 in
`QuarterEngineTests` covering `SetFareOrder` acceptance/rejection and
the fare-preservation fix, 3 in `FinancialResolverTests` — two isolating
revenue's new per-route/per-override behavior, and the scenario test
itself, which runs 40 straight quarters from $0 cash and asserts net
income never goes negative even once). 100% line/branch coverage on
every touched/new class (`Route`, `FareCalculator`, `SetFareOrder`,
`QuarterEngine`, `FinancialResolver`). Overall project coverage
**99.85%/99.60%**, at the established baseline — the only remaining
gaps are the same pre-existing ones this session has confirmed multiple
times (`BriefingGenerator`, the abstract `QuarterOrder` base record,
`NetworkPlanner`), untouched by this milestone. 150-game QA harness
sweep at 244 quarters: 150/150 clean, 0 invariant violations, 0 crashes.

**Campaign trajectory shifted again, measured not assumed — and for the
first time in this whole sequence, in the *other* direction.** Every
milestone from M26 through the debt-zero change made the financial model
more realistic and made the same un-tuned `BaselineAi` campaign's
bankruptcy arrive sooner. M34 breaks that pattern: `AeroNext.Sim`'s M13
loop now peaks at **$18,752,722.38 (quarter 2)** before declining —
demand ramping up against real per-seat fares earns meaningfully more
per passenger than the old flat $0.12/passenger-km ever did — and
bankruptcy doesn't arrive until **quarter 17 (1959, -$4,101,673.48)**,
more than double M30's quarter 7. It's still bankruptcy, not a fix:
once one of the frozen network's two routes saturates its own seat
ceiling, fixed salary/operating cost still outrun a fare that can't grow
demand or win more competitive share (both explicitly out of scope,
per the design above) on a network `BaselineAi` never expands. The QA
harness sweep confirms the same shape at scale: bankruptcy quarter
averages moved later in every region (4.9–8.5, versus M30's 4.7–6.4),
while win rate holds at **0/150** — a materially healthier cash
trajectory doesn't by itself produce a win, because winning needs
network growth and regional dominance `BaselineAi` doesn't pursue.
That's exactly the gap the user asked M34 to unblock: the mechanism
`BaselineAi` will need in order to be taught to actually play the game,
not a substitute for teaching it.

---

## Kept in mind, not scheduled

- **M31 ("Campaign integration and re-verification") is retired from
  this document.** It was drafted before M27–M34 existed; a review found
  its stated wiring goal already done (each milestone since has wired
  itself into both real loops as part of its own commit) and its real
  remaining value — a shared affordability primitive, `BaselineAi`'s
  actual behavioral gaps, an obligation to re-measure numbers recorded
  elsewhere — moved to **`docs/BASELINE_AI_ROADMAP.md`**, which reuses
  the M31 number for its own first milestone (nothing was ever built
  under the old scope) and continues from M35.
- **Fare stance (fully settled by M34), service level, and marketing
  "postures"** — CLAUDE.md's original framing bundled all three as free
  competitiveness/revenue dials. Fare is no longer a gap: M34 built a
  real, direct per-route price (`Route.FarePerPassenger`,
  `SetFareOrder`), replacing the old flat `DefaultYieldPerPassengerKm`
  global rate entirely. `Airline.Quality` remains a single placeholder
  knob standing in for service level and marketing, and fare still
  doesn't affect demand size or competitive share (M34 scoped that out
  explicitly) — both real, separate gaps, adjacent to "financial model"
  but not this roadmap's own scope.
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
