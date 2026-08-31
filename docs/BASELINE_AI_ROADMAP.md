# Baseline AI Roadmap

Continues the shared milestone numbering. `FINANCIAL_MODEL_ROADMAP.md`
issued M25–M34 (M32/M33 built separately in `ROUTE_CAPACITY_ROADMAP.md`);
its own M31 ("Campaign integration and re-verification") is retired and
replaced by this document — nothing was ever built under that name, so
the number carries over rather than being wasted, the same way
`HISTORICAL_DYNAMICS_ROADMAP.md`'s M20–22 sit reserved elsewhere. This
doc's milestones after M31 pick up at **M35** (M32–34 are already spoken
for).

**Origin:** `FINANCIAL_MODEL_ROADMAP.md`'s M31 was drafted before M27–M34
existed, and a review found its stated goal — wire every financial
milestone into `AeroNext.Sim`'s M13 loop and
`AeroNext.QaHarness/FullGameRunner.cs` — already done; each milestone
wired itself in as part of its own commit. What was left of real value:
a never-built shared affordability primitive, a precise list of order
types `BaselineAi` never touches, and an obligation to re-measure numbers
recorded elsewhere once its behavior actually changes. Separately,
diagnosed empirically (instrumented a real M13 run, not guessed) *why*
the campaign still goes bankrupt even under M34's healthier fares:
`BaselineAi`'s "grow whichever route has the best current load factor"
step has no saturation awareness. That run's fleet grew from 2 to 18
aircraft by quarter 17 — one more leased unit every single quarter
without exception, all piled onto the same 2 routes opened turn one,
long after both were demand-saturated (carried passengers plateau at
quarter 11). It pays a growing recurring lease fee and M30's flat salary
on units producing zero incremental revenue, forever, until bankruptcy.
That is the dominant, concrete bankruptcy driver in the current game —
not a missing order type in the abstract.

**Shape, settled with the user:** start with milestones that teach
`BaselineAi` to interact with every system and mechanic the game now
has — mechanical completeness — before any milestone that teaches it
actual strategy. Checked directly against `QuarterEngine.Cost()`'s
switch: of the 12 order types that exist, `BaselineAi` today only ever
constructs 3 (`RouteOrder`, `LeaseAircraftOrder`, `NegotiationOrder`).
Never touched at all: `HireExecutiveOrder`, `RefinanceOrder`,
`RetireAircraftOrder`, `CloseRouteOrder`, `RelinquishSlotOrder`,
`PayDownDebtOrder`, `SetFareOrder`, `AircraftOrder` (the buy path — only
ever leases), `AllianceCourtOrder`. That last one keeps its existing,
reasoned exemption (see "Kept in mind" below) rather than being treated
as a gap.

**Nothing in this roadmap is built yet** — every milestone below is
prospective, marked *(proposed, tunable)* wherever it introduces a
concrete rule or threshold, the same way every prior roadmap in this
repo flags its own placeholder numbers: measured against real campaign
data once implemented, not asserted correct in advance.

**Cross-cutting rule, applies to every milestone below:** a real, shared
`FinancialState.CanAfford(Money cost)` gets extracted as part of M31
(the first milestone) and is the only affordability check any later
milestone should add — not a fresh inline comparison duplicated at each
new decision point the way today's one-off `NegotiationOrder` cash check
already is. One source of truth, reused everywhere, matching the
principle `FINANCIAL_MODEL_ROADMAP.md` stated but never actually
extracted into code.

---

## Phase 1 — mechanical completeness (interact with every system)

## M31 — Executive hiring + a real `CanAfford` primitive ✅

**Why first:** `BaselineAi` already spends its full 3-action budget every
single quarter today — confirmed in the same diagnostic run above
(lease + grow = 2 actions, negotiate = 1, exactly 3 every time). It has
no room to add any mechanic below without either more actions to spend
or displacing something it already does. `HireExecutiveOrder` is free,
private, and raises the cap starting next quarter — the one mechanic
that removes this ceiling rather than competing against it.

Extract `FinancialState.CanAfford(Money cost)` (checks quarter-start cash
minus whatever this planning pass has already committed to spend) and
route `BaselineAi`'s existing `NegotiationOrder` gate through it instead
of its current inline comparison. Have `BaselineAi` hire an executive
whenever it isn't otherwise spending its full budget on something with a
real payoff.

**Worth flagging plainly, not fixing here:** `HireExecutiveOrder` is
unconditionally free today — nothing prices hiring or caps how large the
action cap can grow. Given the mechanic as it exists, "always hire when
idle" is the *correct* AI behavior, not a heuristic bug — but an
uncapped, costless action-cap ceiling is arguably a separate, pre-existing
design gap worth the user's own attention at some point. Not addressed
by this milestone.

**Done when:** `FinancialState.CanAfford` exists and is unit tested
directly; `BaselineAi`'s negotiation step uses it instead of its own
inline check (behavior-preserving refactor, not a new capability); a
dedicated test drives an airline down to near-zero cash and confirms
`BaselineAi` never once proposes an order the resolver's backstop would
have to reject; `BaselineAi` hires an executive under some measured
condition and the action cap visibly grows over a multi-quarter test.

**`FinancialState.CanAfford(Money available, Money cost)` shipped as a
static two-argument method, not the instance `CanAfford(Money cost)`
originally sketched.** Neither real caller (`BaselineAi`, `QuarterEngine`)
holds a whole `FinancialState` at the point it needs this check — both
work from a raw available-cash figure, often already reduced by whatever
this same planning pass has committed to spend. Forcing either to
fabricate a full record (with irrelevant `DebtBalance`/
`InterestRatePerQuarter`/`IsBankrupt`) just to ask one question would
have been worse than the inline comparison it replaces. `BaselineAi`'s
negotiation gate now reads `FinancialState.CanAfford(availableCash.Value,
NegotiationBidAmount)` — behavior-preserving, confirmed by every
pre-existing cash-gating test passing unmodified. `QuarterEngine`'s own
existing inline affordability checks (`AircraftOrder`, `NegotiationOrder`,
`PayDownDebtOrder` ×2) were deliberately **not** swept into this — they
predate the cross-cutting rule and are already logically equivalent;
refactoring them wasn't part of this milestone's own "Done when" and
would have been scope creep for its own sake.

`BaselineAi` now appends `new HireExecutiveOrder()` unconditionally, on
every call, after every other candidate order — exactly as the roadmap
called for, since it's free and never competes with anything else for
the action budget.

**Test blast radius, checked precisely rather than guessed at:** because
the hire is unconditional, it appears in every one of `BaselineAiTests`'
25 pre-existing tests' output now. Exactly 5 needed real fixes (the ones
asserting a raw order count or `Assert.Single(orders)`/`Assert.Empty(orders)`
against the *whole* list) — 3 needed only a filter to `.OfType<RouteOrder>()`
first, and 2 needed an honest rename: `..._ProducesNoOrders` became
`..._ProducesNoActionConsumingOrders_ButStillHires`, since "cap 0 produces
nothing" is no longer true — a free order that never touches the budget
still shows up even there. Every other test, using `.OfType<T>()` filters
or checking `RejectedOrders`, needed no change at all.

Done. 454 tests total (7 new: 4 in a new `FinancialStateTests.cs`
directly exercising `CanAfford`; 3 in `BaselineAiTests` — full-budget
quarters still hire on top of everything else, a cap-1 `QuarterEngine`
run confirms the hire never counts against the budget and the next
quarter's cap actually grows, and a 3-quarter loop confirms the cap
climbs by exactly 1 every single quarter). 100% line/branch coverage on
both touched classes (`FinancialState`, `BaselineAi`). Overall project
coverage **99.85%/99.60%**, at the established baseline — same
pre-existing gaps as always (`BriefingGenerator`, `QuarterOrder`,
`NetworkPlanner`), untouched here. 150-game QA harness sweep: 150/150
clean, 0 invariant violations, 0 crashes.

**Campaign trajectory — measured in both real loops, and they tell two
genuinely different stories, not a contradiction.** `AeroNext.Sim`'s M13
loop (flat, symmetric, gate-scarcity left permissive) reshapes
dramatically: with a growing action cap, `BaselineAi` now diversifies
into a real network — 1 route at quarter 1, 22 by quarter 22 — instead
of endlessly re-leasing onto the same 2 saturated routes forever (M34's
own frozen shape). Peak cash more than doubles, to **$42,113,450.14 at
quarter 18** (versus M34's $18.75M), but the crash is sharper too once
route count outruns what the network actually supports: bankruptcy at
**quarter 22 (1960, -$445,156.38)** — only slightly later than M34's
quarter 17, not the multi-quarter runway the much higher peak alone
would suggest.

The 150-game QA harness sweep — real, region-differentiated, gate-scarce
games — came back **numerically unchanged from M34's own figures**,
down to the cent, across every region's bankruptcy-quarter average and
win count. Verified this wasn't a stale build or a silent no-op:
instrumented a single real game directly and confirmed `HireExecutiveOrder`
applies and the action cap genuinely climbs every quarter (3 → 10 by
quarter 8) exactly as designed. The reason the aggregate outcome doesn't
move: `BaselineAi` only ever has 3 candidate action-consuming branches
(grow, new pair, negotiate), and in the harder QA harness conditions —
gate scarcity actually enforced, harsher regional starting cash, faster
bankruptcy (average quarter 4.9–8.5) — those 3 branches never actually
needed more than the pre-existing cap of 3 to fire everything they were
going to fire anyway, confirmed directly against the instrumented game's
own per-quarter applied-order lists. The extra headroom this milestone
built is real and correctly wired, but nothing in `BaselineAi` today has
a fourth thing to spend it on under harder conditions — that's exactly
what M35–M38 exist to give it. The M13 loop's dramatic reshaping is real
too, not an artifact — it's what the same extra headroom does once
gate-scarcity and harsh regional variation aren't there to absorb it
first.

---

## M35 — Fleet/route lifecycle: retire and close ✅

Teach `RetireAircraftOrder` (an idle unit with no route worth assigning
it to) and `CloseRouteOrder` (detach one aircraft from an existing
route). Deliberately scoped *reactive* here — respond to a unit that's
already idle, or a route that's already fallen below some load-factor
floor — not yet the saturation-*prevention* fix itself (that's M39).
Still meaningfully exercises both mechanics, and gives M39 real tools to
call once it exists.

**Done when:** `BaselineAi` retires an idle unit rather than leaving it
to accrue salary cost indefinitely; `BaselineAi` closes a route (or
shrinks it by one aircraft) once its load factor has stayed below some
*(proposed, tunable)* floor for a measured stretch; tests cover both
independently of M39's growth logic.

Built as designed, with two decisions made concrete during
implementation, both stated up front rather than discovered as bugs:
`CloseRouteOrder` requires `AircraftCount > 1` and never fully closes a
route here — a route dropping to zero would read as "unserved" again
next quarter, and the new-pair step's own greedy highest-demand search
could very plausibly reopen the exact same pair for free, forever.
Fully abandoning a pair is real strategy (M39's job), not a reactive
threshold check. `RetireAircraftOrder`'s trigger is deliberately narrow
— *genuinely* stranded only (no served route uses the unit's type, and
no unserved reachable pair has positive demand for it either) — because
`BaselineAi`'s existing growth/new-pair steps already prefer idle
inventory before leasing, so a second or third idle unit of an
otherwise-useful type isn't wasted, just not this quarter's single
greedy pick; retiring it prematurely would remove exactly what next
quarter's growth step wants. `CloseRouteLoadFactorFloor = 0.5` *(proposed,
tunable)*. Both orders are free, so — unlike the budget-gated
growth/new-pair/negotiate steps — every qualifying route/unit is acted
on the same quarter, not just the single best one.

Done. 465 tests total (11 new), all in `BaselineAiTests` — close/retire
triggering correctly, each guard (`AircraftCount > 1`, "type still used
elsewhere," "could still open a pair") independently *not* triggering,
both orders confirmed free via `QuarterEngine.Cost`, both coexisting with
a full budget of other orders in the same quarter, and both running
clean (zero rejections) through `QuarterEngine.RunQuarter`. One of those
integration tests surfaced a real, worth-documenting interaction rather
than a bug: with only one existing route and nothing else to grow, the
pre-existing (M32) "grow best route" step re-grows the exact route M35
shrinks the same quarter — both orders individually legal, net
`AircraftCount` unchanged that quarter. Kept as a test, not a special
case worked around, since it's an honest illustration of exactly the
gap M39 exists to close (M35 is reactive/uncoordinated by design). 100%
line/branch coverage on `BaselineAi` (the only touched class). Overall
project coverage **99.85%/99.61%**, at the established baseline. 150-game
QA harness sweep: 150/150 clean, 0 invariant violations, 0 crashes.

**Campaign trajectory — measured in both real loops again, and this
time the *QA harness* is the one that moves, not the M13 loop, the
reverse of M31's own finding.** `AeroNext.Sim`'s M13 loop came back
byte-for-byte identical to M31's own figures (peak $42,113,450.14 at
quarter 18, bankruptcy at quarter 22, -$445,156.38) — confirmed why, not
just observed it: instrumented the loop directly and found
`CloseRouteOrder`/`RetireAircraftOrder` apply **zero times** across the
entire 22-quarter run. Idle units sit at 0 every single quarter
(`BaselineAi` never over-acquires, so a retire target never exists), and
every multi-aircraft route's load factor stays pinned at a perfect 1.000
the whole run (this loop's demand keeps growing fast enough that
capacity never outpaces it, so a close target never exists either).

The 150-game QA harness sweep, by contrast, moved substantially — the
harder, region-differentiated, gate-scarce conditions genuinely produce
over-built routes and stranded units that the M13 loop's rich, ever-growing
demand pool never does. Bankruptcy quarter averages moved later in
every region (6.5–9.3, versus M31's 4.9–8.4), and average/median cash at
bankruptcy improved dramatically — e.g. NorthAmerica from -$6,094,987 to
-$753,886, SouthAmerica from -$4,045,019 to -$379,522. Exactly the
inverse pattern from M31, where the QA harness didn't move and the M13
loop reshaped dramatically — the same lesson from the other direction:
these two loops' environments are different enough that a milestone can
matter enormously in one and not at all in the other, and only measuring
both catches that.

---

## M36 — Debt tools ✅

`RefinanceOrder` (borrow when a real growth opportunity — a lease, an
aircraft order, a negotiation bid — exceeds cash comfortably on hand) and
`PayDownDebtOrder` (pay down an idle nonzero balance once cash is
abundant and nothing better to spend it on is queued). `BaselineAi` has
zero debt-awareness in either direction today.

**Done when:** `BaselineAi` borrows to fund an otherwise-unaffordable
action it judges worth taking, gated by `CanAfford`-style reasoning
about the resulting debt service, not just the immediate cash gap; it
pays down debt when cash is idle and debt is nonzero; tests cover both
independently.

**A real bug caught before it shipped, not after — worth recording.**
The first design borrowed and negotiated in the *same* quarter. Traced
through `QuarterEngine` before writing any tests and found that would
have gotten the `NegotiationOrder` rejected every time regardless: its
cash gate checks only the `availableCash` passed into `RunQuarter`,
quarter-start — a same-quarter `RefinanceOrder` does no
`QuarterEngine`-side processing at all (established back in M29, for
`PayDownDebtOrder`'s identical situation, and stated plainly in
`QuarterEngine`'s own doc comment). Attempting both together would have
been exactly the "attempt and fail, wasting the turn's opportunity"
outcome the affordability cross-cutting rule exists to prevent. Fixed
before implementation: `BaselineAi` borrows the gap this quarter with
*no* attempt at the negotiation, so `FinancialResolver` applies the
borrowed cash to `CashBalance` before next quarter, and next quarter's
own `availableCash` genuinely covers the bid.

The only cash-gated action `BaselineAi` proposes today is still
`NegotiationOrder` (it never constructs an `AircraftOrder`, only
leases) — so `RefinanceOrder`'s scope here is exactly that one real
case: borrow just enough to cover the bid shortfall, gated by real
debt-ceiling headroom (`FinancialState.CanAfford` reused a second way —
`DebtCeilingDollars − currentDebtBalance` as "available," the borrow
amount as "cost"), only when a real target (`bestGateway`) exists.
`PayDownDebtOrder` fires whenever cash clears `PayDownCashBuffer =
$10,000,000` *(proposed, tunable)* and debt is nonzero, paying down
`min(cash − buffer, debt)`. The buffer is 5× `NegotiationBidAmount`
deliberately — cash can never be simultaneously short of the $2,000,000
bid and above a $10,000,000 buffer, so borrow and paydown can never
fire the same quarter for the same airline; no explicit mutual-exclusion
check needed, the thresholds guarantee it on their own. New parameter
`Money? currentDebtBalance = null` (mirrors `availableCash`'s exact
shape) wired into both real loops.

Done. 475 tests total (10 new, all in `BaselineAiTests`) — borrowing
exactly the bid gap without attempting the same-quarter negotiation;
never borrowing when it would breach the debt ceiling; never borrowing
with no debt balance passed (permissive); never borrowing when cash
already covers the bid; paying down the full debt when it's smaller than
the affordable amount; paying down only the affordable portion when debt
is larger; never paying down at zero debt or at/below the buffer; both
scenarios running clean (zero rejections) through
`QuarterEngine.RunQuarter`. One redundant condition
(`shortfall > Money.Zero`, always true given the surrounding branch
already guarantees a shortfall) was caught by the coverage gate as a
missing branch and removed rather than tested around — genuinely dead
code, not a gap. 100% line/branch coverage on `BaselineAi`. Overall
project coverage **99.85%/99.62%**, at the established baseline.
150-game QA harness sweep: 150/150 clean, 0 invariant violations, 0
crashes.

**Campaign trajectory — a third consecutive milestone where the M13 loop
doesn't move at all, and this time no fresh diagnostic was even needed
to explain why.** `AeroNext.Sim`'s M13 loop came back byte-for-byte
identical to M31/M35's own figures (peak $42,113,450.14 at quarter 18,
bankruptcy at quarter 22, -$445,156.38) — the reason is plainly visible
in the already-printed per-quarter cash figures themselves: cash never
once drops below ~$15,760,767 across all 22 quarters, nowhere near the
$2,000,000 bid that would trigger a borrow, and this loop's debt balance
sits at exactly $0 the whole run (nothing here has ever borrowed), so
`PayDownDebtOrder`'s own "debt must be nonzero" gate never has anything
to act on either.

The 150-game QA harness sweep moved again, real but smaller than M35's
own shift — bankruptcy-quarter averages barely changed (6.6–9.6, versus
M35's 6.5–9.3), but average/median cash *at* bankruptcy improved
meaningfully in most regions: EastAsia from -$1,451,876 to -$333,262,
AustralAsia from -$2,168,937 to -$1,306,416, SouthWestAsia from
-$2,271,026 to -$1,592,343. Consistent with the mechanism actually
built: debt tools smooth the cash trajectory (borrow through a real
shortfall, pay down idle debt before it accrues more interest) rather
than delaying bankruptcy outright the way M35's route/fleet corrections
did. Three milestones running now where the M13 loop's specific
demand-rich, cash-rich, debt-free environment simply never exercises
the newest mechanic, while the harder, region-differentiated QA harness
games do — the same lesson restated a third time, worth trusting by now
rather than re-deriving from scratch each time: measure both real
loops, expect them to diverge, and don't assume either one is more
"representative" than the other.

---

## M37 — Fares ✅

`SetFareOrder`, reactively at minimum: cut the fare on a route whose load
factor has stayed low for a measured stretch, mirroring M35's
close-route threshold logic rather than inventing a separate one.

**Worth flagging honestly:** a fare reaction to market conditions is
already a small strategic judgment, not a pure mechanical checkbox the
way retire/refinance are — this milestone sits closer to the phase
boundary than the others in phase 1, and that's stated here rather than
pretended away.

**Done when:** `BaselineAi` sets a fare below the computed default on a
persistently underperforming route and the effect on that route's
revenue/load factor is directly observable in a test.

**Reconsidered before building, not after.** The roadmap text above said
"cut the fare on a route whose load factor has stayed low." Re-checked
against M34's own explicit scope first: fare has zero effect on demand
size or competitive share today, so cutting a fare on an underperforming
route wins no additional passengers — a straight revenue loss under
current mechanics, not a correction. Raised directly with the user
before writing any code; decided: **build both directions.** Raise fares
on saturated routes (`LoadFactor >= SaturatedLoadFactorCeiling = 0.95`
*(proposed, tunable)*, target `FareCalculator.SuggestedFarePerPassenger
× (1 + FareRaiseFraction)`, `FareRaiseFraction = 0.15`) — sound revenue
upside today, since a saturated route's capacity-capped carried volume
pays more per passenger for free. Cut fares on underperforming routes
(reuses `CloseRouteLoadFactorFloor` — M35's own constant, per the
roadmap's own "mirroring M35's threshold logic" wording — target
`SuggestedFarePerPassenger × (1 − FareCutFraction)`, `FareCutFraction =
0.20`) as originally scoped, built anyway to exercise the full mechanic
and measure its real effect honestly rather than assume it's sound.

Both targets are flat against the *computed default*, not the route's
current fare — deliberately non-compounding, so a route that stays
saturated or underperforming across many quarters doesn't spiral its
fare toward infinity or zero; each is skipped once the route's current
fare is already at or past the target, avoiding a pointless repeat
order. `RouteEvaluator.EvaluateNetwork` is now computed once per
`DecideOrders` call and shared across the close/raise/cut checks,
rather than three separate evaluations.

Done. 483 tests total (8 new, all in `BaselineAiTests`) — raise fires
and targets the right value on a saturated route; already-at-target
skips a repeat; cut fires and targets the right value on an
underperforming route; already-at-target skips a repeat; a route with a
load factor between the two thresholds gets neither (a real,
calibrated mid-range scenario, not just asserted); both confirmed free
via `QuarterEngine.Cost`; both run clean (zero rejections) through
`QuarterEngine.RunQuarter`; and a full-loop test ties the roadmap's own
"Done when" bar together directly — the fare `BaselineAi` actually
proposes, once applied, measurably raises `FinancialResolver` revenue
for the identical carried-passenger count (fare still doesn't affect
demand, so carried volume matches exactly; only revenue moves). 100%
line/branch coverage on `BaselineAi`. Overall project coverage
**99.86%/99.63%**, at/above the established baseline. 150-game QA
harness sweep: 150/150 clean, 0 invariant violations, 0 crashes.

**Campaign trajectory — the largest single shift measured all
session, and it breaks the three-milestone streak of the M13 loop
staying untouched.** Instrumented the loop directly to confirm *why*,
not just observe it: every route there stays saturated (load factor ≥
0.95) essentially permanently once opened — underperforming routes are
0 every single quarter through bankruptcy — so the raise mechanic fires
on nearly every newly-opened route while the cut mechanic never fires
at all in this loop. Pure, uncontested upside: peak cash more than
doubled to **$110,877,173.79 at quarter 20** (versus M36's
$42,113,450.14), and bankruptcy was pushed from quarter 22 all the way
to **quarter 30 (1962, -$13,225,238.57)** before the same underlying
problem M31 first diagnosed — route count outrunning what salary and
operating cost can sustain — finally caught up anyway.

The QA harness's 150 real games told a genuinely more mixed story
instead, consistent with the honest scope decision above: bankruptcy
timing barely moved (6.4–9.7, versus M36's 6.5–9.6), but average cash
at bankruptcy got *worse* in most regions — North America from
-$638,521 to -$2,057,415, East Asia from -$333,262 to -$1,457,523,
South West Asia from -$1,592,343 to -$3,313,977 — because these harder,
region-differentiated games genuinely do produce underperforming routes
the currently-revenue-negative cut mechanic acts on, unlike the M13
loop's permanently-saturated network. This is exactly the outcome
flagged as a live possibility when the direction was decided, now
confirmed with real numbers rather than assumed: the cut side of this
milestone is a measured, real cost today, worth revisiting once fare
can actually affect demand or competitive share (M41's own scope, or a
future revisit of M34's boundary) rather than removed pre-emptively —
the roadmap's own phase-1 goal is mechanical completeness first, and
this milestone delivered that honestly, cost included.

---

## M38 — Aircraft ownership: buy, not just lease ✅

`AircraftOrder` (the production-queue purchase path). `BaselineAi` only
ever leases today, paying a recurring 4%/quarter fee forever against a
one-time purchase cost that's cheaper in the long run once delivery
timing isn't urgent. Teach it to choose ordering over leasing under some
*(proposed, tunable)* rule — e.g. cash comfortably covers the purchase
outright and the need isn't immediate.

**Done when:** `BaselineAi` places an `AircraftOrder` under the measured
condition instead of always falling back to `LeaseAircraftOrder`; a test
confirms it still leases when the condition isn't met (delivery-position
scarcity, M10's contested resource, means ordering isn't always better).

**The load-bearing architectural fact, worth stating plainly:**
`AircraftOrder` doesn't deliver the same quarter the way
`LeaseAircraftOrder` does — `QuarterEngine` resolves a lease inline, so
a `RouteOrder` in the same batch can use it immediately; an order only
resolves later, through `AircraftOrderResolver`'s contested production
queue (M10). So choosing to order instead of lease means *not*
submitting the route this quarter either — the growth/new-pair happens
whenever the order eventually delivers and the unit shows up idle,
picked up naturally by the existing idle-preferring logic. Both real
loops already handled `AircraftOrder` generically in
`result.AppliedOrders` (since M26) — this milestone needed zero wiring
changes to `Program.cs` or `FullGameRunner.cs`, purely a `BaselineAi.cs`
change.

`AircraftOrderCashComfortMultiplier = 3` *(proposed, tunable)*:
`ComfortableOrderCost(type) = type.Cost × 3`, deliberately more
conservative than `QuarterEngine`'s own affordability gate for
`AircraftOrder` (1× cost) — this AI never gets its own order rejected,
keeping real margin for the same quarter's salary/operating cost.
Checked against both fallback sites that used to always lease: growing
an existing route, and opening a new pair. `availableCash is null`
(permissive callers) keeps the exact pre-M38 lease-only behavior
unchanged — no new gating for anyone who doesn't pass cash, the same
convention every prior milestone's own cash/debt parameter followed.

Done. 490 tests total (7 new, all in `BaselineAiTests`) — orders instead
of leasing to grow an existing route when cash is comfortable (no route
growth that quarter, since the order can't deliver yet); still leases
when cash is present but not comfortable; the same pair for opening a
new route; still leases with no cash passed at all (permissive,
unchanged); a tight-budget test showing ordering fits at a 1-action cap
where lease+route (2 actions) never could — a genuinely new capability,
not just a substitution; and a `QuarterEngine.RunQuarter` integration
test confirming zero rejections. 100% line/branch coverage on
`BaselineAi`. Overall project coverage **99.86%/99.64%**, at/above the
established baseline. 150-game QA harness sweep: 150/150 clean, 0
invariant violations, 0 crashes.

**Campaign trajectory — and something no milestone before it has
produced.** `AeroNext.Sim`'s M13 loop moved further still: Atlas Air's
peak cash climbed to **$122,534,810.05 at quarter 44** (1965, beyond
M37's own peak), and bankruptcy was pushed to **quarter 56** (1968,
-$2,320,032.11) — nearly double M37's quarter 30. But for the first
time all session, **the 4 airlines in this loop genuinely diverged from
each other**, despite `BaselineAi` remaining fully deterministic and
every airline making byte-identical decisions from identical state.
`AircraftOrder` is a real *contested* resource (M10's production-line
position) — this is the first milestone where `BaselineAi` ever actually
submits one, and with 4 identical airlines simultaneously ordering the
same best-range type, the production queue can't satisfy all 4 the same
quarter; `AircraftOrderResolver`'s own deterministic tie-break sends
them down genuinely different delivery timelines from that point on.
`NegotiationOrder` was contested in principle since M9 too, but this M13
loop never actually wires bids into `LandingRightsResolver` (confirmed
in `FullGameRunner.cs`'s own doc comment), so that contest was never
real here — `AircraftOrder` is the first mechanic in this loop where
contention has a genuine, measured effect.

The QA harness's 150 real games moved broadly positive this time —
unlike M37's mixed result — bankruptcy quarter averages later in every
region (6.7–12.5, versus M37's 6.4–9.7) and average cash at bankruptcy
improved in 5 of 6 regions (e.g. East Asia -$1,457,523 → -$648,100,
South West Asia -$3,313,977 → -$741,079). Consistent with the mechanism
built: eliminating leasing's recurring 4%/quarter fee compounds
meaningfully over a long campaign regardless of how harsh the starting
conditions are, unlike M37's raise/cut split which depended heavily on
each region's own saturation pattern.

**Phase 1 (mechanical completeness) closes here.** `BaselineAi` now
constructs every order type this session's own review found it never
touched: `HireExecutiveOrder` (M31), `RetireAircraftOrder`/
`CloseRouteOrder` (M35), `RefinanceOrder`/`PayDownDebtOrder` (M36),
`SetFareOrder` (M37), `AircraftOrder` (M38) — 7 of the 12 order types
that existed, all built, all measured against both real loops rather
than assumed. `AllianceCourtOrder` remains the one deliberate exclusion
(no mechanical consequence yet — M9's own reasoning, restated in this
doc's "Kept in mind" section). None of these six milestones taught this
AI to play *well* — that's phase 2's job, starting with M39, the actual
fix for the bankruptcy pattern M31 first diagnosed and every milestone
since has measured but not addressed: nothing yet stops this AI from
growing (or ordering) past what its network can sustain.

---

## Phase 2 — strategy

## M39 — A strategy framework (the real bankruptcy fix, redefined) ✅

**Redefined from its original "growth saturation" framing.** That
version gave the existing greedy heuristic a single stopping-condition
tweak. Before building either version, diagnosed the *current*
bankruptcy driver directly rather than assuming this doc's own
pre-M35–M38 origin section still describes it: instrumented Atlas Air's
last 12 quarters before its M13-loop bankruptcy (quarter 56, after M38)
and found it loses $10–18M/quarter while its own load-factor math says
it's 99–100% sold out the whole time. Root cause: every decision this
AI makes (`FindBestPerformingRoute`, M35's close check, M37's raise/cut
checks, `FindBestUnservedPair`) is computed through
`RouteEvaluator`/`GravityDemandModel` directly — both assume monopoly,
`RouteEvaluator`'s own doc comment says so plainly. The real game splits
demand through `CompetitiveMarket`, and the M13 loop's 4 airlines are,
by original M23/M24 design, perfectly identical and always chase the
exact same globally-best pair — a permanent 4-way split on their own
best markets that nothing in their decision math ever looks at.

Discussed directly with the user, who proposed a different fix than
tuning the single heuristic further: short-to-medium-term named
strategies an airline commits to and periodically re-evaluates, e.g. a
domestic profit optimizer (top 5 home-region routes) versus rapid
cross-region expansion. This directly addresses both the saturation
problem (a route cap *is* a stopping condition, just goal-driven instead
of threshold-driven) and the cannibalization problem (4 seats no longer
making identical decisions, if assigned different strategies) at once.
Settled: each AI seat is **assigned** a starting strategy (not
self-selected); re-evaluated on a **fixed cadence — once per in-game
year** (4 quarters); build exactly two strategies now, but architect a
real, reusable framework with an **intended-era tag** per strategy from
day one.

**`GameEra` enum** (`AeroNext.Core.Ai`) — `{ Starting, MidGame, LateGame
}`, `GameEraForQuarter(int quarter)`, boundaries *(proposed, tunable)*:
Starting = quarters 1–40 (1955–1964), MidGame = 41–160 (1965–1994),
LateGame = 161–244 (1995–2015) — chosen against this session's own
measured data, every M13-loop bankruptcy from M34 through M38 landed
inside quarter 17–56, entirely within the proposed Starting window.

**`IAiStrategy`** — `Name`, `ApplicableEras`, `Suitability(context)`
(how well it fits current state, used to pick among eligible
strategies), `DecideOrders(context)`. A new `StrategyContext` record
bundles what's currently 9 loose parameters on `BaselineAi.DecideOrders`
**plus one no version of this AI has ever had: `Region homeRegion`** —
needed for "domestic" to mean anything. Existing helpers
(`FindBestPerformingRoute`, `FindBestUnservedPair`,
`HasSlotAtEveryGateway`, `ReserveSlots`, fare/debt/hire logic) become a
shared internal toolkit both strategies call into, not duplicated.

**Selection mechanism**, unifying initial assignment and later switching
into one path: `BaselineAi.SelectStrategy(IAiStrategy? current, int
quarterNumber, StrategyContext context)` — re-evaluates only on a year
boundary or when `current` is null (quarter 1, or nothing assigned yet);
otherwise returns `current` unchanged. Filters the roster to strategies
whose `ApplicableEras` contains the current era, picks the highest
`Suitability` among eligible ones (deterministic tie-break by name).
**Explicit, honest gap:** with only two strategies, there may be no
eligible one once `MidGame`/`LateGame` arrives — falls back to keeping
the current strategy rather than crashing. Deliberate: the era tag
exists so a *future* milestone can add mid/late-game-native strategies
into the same roster without touching the selection mechanism.

**`DomesticProfitOptimizerStrategy`** (`{Starting, MidGame}`): caps at
`MaxDomesticRoutes = 5` *(proposed, tunable)* routes, both endpoints
inside `homeRegion`. Below 5: opens the best *intra-region* pair ranked
by estimated profitability (fare margin × demand), not raw demand. At
5: stops opening new routes entirely, switches fully to optimizing the
5 it has. Never proposes a cross-region `RouteOrder` or
`NegotiationOrder`. *(Swapping a weak one of the 5 for a better
still-unclaimed candidate is a natural richer behavior, deliberately
left for later.)*

**`RapidExpansionStrategy`** (`{Starting}`): inverts the existing
priority order — opening a new pair (cross-region preferred) before
growing an existing one, gateway negotiation weighted toward
cross-region-reachable chokepoints (CLAUDE.md's own chokepoint pillar),
more willing to lease/order than `DomesticOptimizer`. No route cap, no
region restriction.

**The M13 loop's own gap, surfaced not silently patched:**
`AeroNext.Sim`'s M13 loop is "deliberately flat/symmetric... no
GameSetup/region distinction" (its own standing doc comment) — it has no
home regions for its 4 airlines today, so it could never meaningfully
run `DomesticOptimizer` at all. Proposed fix, not a workaround: give the
M13 loop's 4 airlines distinct starting regions/cities, the same way
`GameSetup`/`StartingConditionsResolver` already would for a real game —
serves two goals at once, enabling the framework there *and*
independently reducing the diagnosed cannibalization pathology. A
deliberate reversal of a long-standing design choice in that loop,
flagged plainly — every M13 checkpoint figure recorded all session will
move once this lands, expected and fine, not a regression to chase.

**Done when:** both strategies exist and are unit tested independently
(cap/region-restriction behavior for `DomesticOptimizer`, priority-order
behavior for `RapidExpansion`); the selection mechanism is tested for
initial assignment, year-boundary switching, mid-cycle stability, and
the no-eligible-strategy fallback; both real loops assign/track a
current strategy per airline and re-evaluate on schedule; the M13 loop's
4 airlines have distinct home regions; the full M13 checkpoint sweep and
150-game QA harness sweep are re-measured, not assumed, against this new
behavior.

Done. `BaselineAi.DecideOrders` stays byte-for-byte unchanged (8 private
helpers + 6 constants widened to `internal` only) — every one of the
~490 pre-existing tests kept passing untouched, confirmed by running the
full suite immediately after the visibility change, before any new code
was written. New: `GameEra`/`GameEras`, `IAiStrategy`, `StrategyContext`,
`AiSuitability` (shared cash-based signal), `AiToolkit` (extracted
close/raise/cut/retire/debt/hire + negotiate-or-borrow logic, reused by
both strategies), `DomesticProfitOptimizerStrategy`,
`RapidExpansionStrategy`, and `BaselineAi.SelectStrategy`. 543 tests
total (53 new, across `GameEraTests`, `DomesticProfitOptimizerStrategyTests`,
`RapidExpansionStrategyTests`, `SelectStrategyTests`). 100% line coverage
on every new/touched file; 100% branch coverage on `AiSuitability` and
`DomesticProfitOptimizerStrategy`; `AiToolkit` and `RapidExpansionStrategy`
each carry exactly one confirmed-understood partial branch — both are a
compound condition's left operand already guaranteed true by an outer
check by the time that line runs (an `AvailableCash is not null` and a
`spent + 1 <= ActionCap` re-check respectively), so the "false" half of
that specific combination is structurally unreachable, not an untested
path; `BaselineAi`'s own new `SelectStrategy` carries the same class of
tooling artifact around a `?? throw` expression, where both logical
outcomes (return `current`, throw) are directly and separately tested.
Overall project coverage **99.88%/99.29%**, at/above the established
baseline; the pre-existing `BriefingGenerator`/`QuarterOrder`/
`NetworkPlanner` gaps are untouched by this milestone.

Both real loops wired: `FullGameRunner.cs` now selects and tracks a
strategy per seat (its existing `homeRegionByName` feeds straight into
`StrategyContext`) instead of calling `BaselineAi.DecideOrders` directly
from quarter 2 on; `AeroNext.Sim`'s M13 loop gained real per-region
starting conditions for the first time — mirroring
`GameSetup.CreateSeats`'s own AI-region-picking algorithm (top 4 home
regions by `RegionalEconomics.Factor`, each paired with its own top
`EligibleStartingCities` entry by `DemandWeight`) zipped against the
loop's existing flavor names (Atlas Air/Meridian/Skyward/Continental
Wings, kept) — replacing the old flat "$20M/smallest-1955-type for
everyone" setup this loop's own M31-M38 history was measured under.
Deliberately did not also adopt `FullGameRunner.cs`'s M18
guaranteed-quarter-1-route seed there, per the plan's own stated scope
boundary — each strategy already picks a sensible, region-aware quarter-1
target on its own.

**Campaign trajectory — a fundamentally different shape, not a bigger
number.** Every prior milestone this session (M31 through M38) measured
the M13 loop sliding further down the same curve: bigger peak cash,
later bankruptcy, same eventual crash. M39 breaks that pattern
outright — re-measured with a full per-quarter checkpoint sweep, not
assumed: all 4 airlines grow steadily with **zero bankruptcies**, and
**Continental Wings wins outright at quarter 10** (1957) via regional
dominance (3+ regions led for 4+ consecutive quarters), the first time
any milestone this session has driven this loop to an actual win
condition instead of a bankruptcy. Confirms the diagnosed root cause
directly: 4 airlines with real, distinct home regions and (initially,
cash-differentiated) strategies no longer chase the identical
globally-best pair every quarter, so the permanent 4-way split on the
same markets that was driving the M34-M38 bankruptcy spiral is gone.
Checkpoints in `Program.cs` now show quarters 1/4/7/10, the whole short
arc to the win.

The 150-game QA harness sweep stayed structurally clean — 150/150 games
passed with 0 invariant violations and 0 crashes — but surfaced a real,
un-scoped finding rather than confirming a clean win: **0/150 clean
games reached a win condition** before the 244-quarter timeline ended
(unlike the M13 loop above), and the regional breakdown is severely
skewed — EastAsia and AustralAsia seats rarely go bankrupt and finish
with average cash in the hundreds of millions to low billions, while
NorthAmerica/Europe/SouthAmerica/SouthWestAsia seats bankrupt in nearly
every game, on average within 8-16 quarters. This is a genuine result of
wiring real per-region starting conditions and region-aware strategies
together at scale across varied seat configurations, not a bug in this
milestone's own new code (both real loops pass every existing invariant
check) — but it's a severe enough imbalance that it likely traces back to
`RegionalEconomics.Factor` itself, independent of the AI layer, and
undermines CLAUDE.md's own "a home region cannot fund a win" pillar in
the opposite direction (some regions now look unbeatable, others
unplayable). Reported here as a finding for a future milestone to pick
up, not chased inside this one — M39's own scope was strategy diversity
fixing the *identical-decision* cannibalization pathology, which it
measurably did; regional economic balance is a separate, pre-existing
system this milestone didn't touch and shouldn't silently absorb.

---

## M40 — Network/hub awareness ✅

`BaselineAi`'s own doc comment already names this gap directly: "no
hub-awareness — picks the single best pair/gateway in the whole world,
not the best one connected to its existing network." Real strategy work,
not a mechanical gap — building toward the hub-and-spoke value
`NetworkPlanner` (M4) already proved emerges from the demand model, which
nothing in `BaselineAi`'s decision logic currently tries to capture.
Reframed after M39: a refinement layered onto the strategy framework,
not a prerequisite to it — both M39's strategies already carry an
implicit form of network-shape awareness (a regional cluster; explicit
cross-region reach), so this becomes "make each strategy's own network
sense sharper," not "add network sense from nothing."

**Traced the real mechanism, not just the gap's description.**
`FinancialResolver.ComputeRevenue` pays each *route (leg)* its own
`FarePerPassenger × SeatsUsed`, and `CompetitiveMarket` gets `SeatsUsed`
per leg from `NetworkPlanner.Plan`, which allocates nonstop demand to a
leg first, then fills *leftover* capacity with one-stop itineraries
connecting through that leg as a hub. So a new route's real revenue
potential already includes any one-stop demand it would carry —
`CompetitiveMarket`/`FinancialResolver` already account for this in the
real resolver. What didn't: `DomesticProfitOptimizerStrategy`'s own
`EstimatedProfit`, ranking unserved-pair candidates by
`PairDemand.PassengersPerQuarter` alone — blind to a candidate connecting
to an already-served city carrying real, currently-uncounted one-stop
passengers at that same leg's own fare. The same class of gap M39
diagnosed for `RouteEvaluator` (decision-time math blind to a mechanic
the real resolver already applies), this time for hub value instead of
competitive splitting.

**Scoped to `DomesticProfitOptimizerStrategy` only.**
`RapidExpansionStrategy`'s own defining trait is breadth — cross-region
reach, no route cap, no consolidation — deliberately the opposite of
hub-building; rewarding connectivity there would fight its own purpose,
and the roadmap already credits it with a different form of network
awareness (explicit cross-region reach). `DomesticProfitOptimizerStrategy`
is the one whose whole concept — a small, profitable, capped home
network — is exactly where rewarding hub-and-spoke shape over disconnected
point-to-point pairs makes sense. `BaselineAi.DecideOrders` stays
untouched (dead/demo-only after M39's wiring).

**`EstimatedHubBonusDemand`** (new, `DomesticProfitOptimizerStrategy.cs`):
for a candidate pair, for whichever endpoint is already served (the
"connector"), every other city already reachable from it represents a
brand-new one-stop opportunity once the candidate opens — carried on the
candidate's own new leg, earning *that leg's own* fare, exactly matching
`FinancialResolver`'s real per-leg accounting, not approximating it.
Skips any city already directly connected to the candidate's other
endpoint elsewhere (that demand is already nonstop-served, matching
`NetworkPlanner.Plan`'s own nonstop-first allocation) and skips a city
already counted from the other connector (both endpoints served at
once). `EstimatedProfit` now caps carried passengers at
`pair.PassengersPerQuarter + hubBonus`, computed once per candidate pair
and reused across every aircraft type tried for it — same complexity
class as before, no new O(n²) blowup. Legitimately returns 0 when nothing
is served yet (quarter 1) — no special-cased fallback, `EstimatedProfit`
already reduces to the pre-M40 pure-nonstop ranking in that case.

**Done when:** the cap/region-restriction/toolkit logic is unchanged;
`EstimatedProfit` is hub-aware; a numerically-verified test shows a
network-connected candidate beating a higher-nonstop-demand disconnected
one once one-stop value is counted; a second test confirms demand
already directly served elsewhere isn't double-counted; both real loops
are re-measured, not assumed.

Done. 545 tests total (2 new, in `DomesticProfitOptimizerStrategyTests`),
both numerically pre-verified before writing (this session's own
established discipline) against a hand-computed `GravityDemandModel`/
`FareCalculator`/`FlightPerformance` reconstruction, not guessed: one
proves a hub-connected candidate ($1,826,396.60 real value) beats a
higher-raw-nonstop-demand disconnected one ($1,612,260.00) once one-stop
value is counted, where a nonstop-only ranking would have picked the
disconnected one instead; the other proves demand already directly
served by an existing route elsewhere is correctly excluded, not
double-counted ($1,826,396.60 correct vs. $2,110,988.04 if double-counted
would have wrongly won). `DomesticProfitOptimizerStrategy` at 100%/100%
line/branch coverage. Overall project coverage **99.89%/99.30%**,
at/above the established baseline; every remaining partial branch
(`AiToolkit`, `BaselineAi`, `RapidExpansionStrategy`) is the same
confirmed-structurally-unreachable set M39 already documented, untouched
by this milestone.

**Campaign trajectory.** The M13 loop came back byte-for-byte identical
to M39's own numbers — expected, not a bug: that loop resolves via a
`RapidExpansionStrategy` win at quarter 10, before
`DomesticProfitOptimizerStrategy`'s ranking ever gets a chance to matter
in such a short game. The 150-game QA harness sweep, which runs far
longer and covers more varied seat configurations, moved for real:
150/150 clean, 0 violations, 0 crashes, still 0/150 reaching a win
condition before the timeline ends. Regionally, the effect was mixed,
not a uniform improvement — reported in full rather than only the
flattering part:

| Region | Bankrupt (M39→M40) | Avg cash (M39→M40) | Wins (M39→M40) |
|---|---|---|---|
| NorthAmerica | 144→140 | -$1.2M→**+$4.6M** | 3→6 |
| Europe | 94→95 | $11.4M→-$1.1M | 1→2 |
| EastAsia | 3→9 | $735.8M→$167.8M | 116→**132** |
| SouthWestAsia | 54→55 | $34.4M→-$1.3M | 2→0 |
| AustralAsia | 16→41 | $1,415.1M→$65.8M | 28→8 |
| SouthAmerica | 119→119 | -$1.66M→-$1.66M (unchanged) | 0→2 |

NorthAmerica got measurably healthier (fewer bankruptcies, cash flipped
positive) — smarter domestic route selection genuinely helps a region
that isn't already winning by brute economic force. But this did **not**
fix M39's own flagged regional-imbalance finding — if anything the
extreme concentration got *more* lopsided at the very top: EastAsia's
win share rose from 116/150 to 132/150, even as AustralAsia's runaway
wealth (avg cash was $1.4B, briefly exceeding SouthAmerica-style
implausibility) collapsed toward a far more modest $65.8M. Read
together: hub-aware ranking reshuffles who's healthy *beneath* the top,
it doesn't touch why EastAsia dominates in the first place — consistent
with M39's own diagnosis that the imbalance traces to
`RegionalEconomics.Factor` itself, a system this milestone still hasn't
touched, on purpose.

---

## M41 — Stop wasting cash on repeat gateway negotiation bids ✅

**Redefined from "Competitive awareness."** That framing was itself
explicitly deferred pending re-diagnosis once M39/M40 landed and got
measured — its own text said so. Re-diagnosed with real data, not
assumed: instrumented one QA-harness game (`FullGameRunner.Run`, game
index 0, all real code) quarter-by-quarter through its first 25
quarters, then again printing every `NegotiationOrder` submitted.

**Root cause: not competitive at all.**
`FindBestUnservedGatewayCrossRegionFirst` (`RapidExpansionStrategy`) and
`FindBestUnservedDomesticGateway` (`DomesticProfitOptimizerStrategy`)
both rank "unserved" gateways using only `touchedCityIds` built from
current routes — a gateway stays "unserved" (and gets re-targeted) until
a *route* actually opens there, even with a `NegotiationOrder` already
**pending** at that exact city. `LandingRightsResolver.Submit` never
checks for an existing pending bid either — it just appends another one.
Measured directly: EastAsia Air bid $2,000,000 on "jfk" for **6
consecutive quarters** (Q2-Q7, $12M spent on one already-pending bid);
"QA Human 1" bid $2,000,000 on "hnd" for **11 consecutive quarters**
(Q2-Q12, $22M). This — not aircraft leasing/ordering, and not any rival
contesting anything — was the real mechanism behind every region's early
bankruptcy wave the M13-loop trace and M39/M40's own QA sweeps kept
showing. A real, pre-existing bug: the identical `touchedCityIds`
pattern already existed in `BaselineAi.FindBestUnservedGateway` itself
(M9/M28-era, predates M39) — it just hit `RapidExpansionStrategy`
hardest because that strategy negotiates far more readily.

**The fix.** `StrategyContext` gains `PendingNegotiationCityIds`
(nullable, permissive-default — `null` reproduces the exact pre-M41 bug,
so no existing test/caller changes behavior silently). New shared
`AiToolkit.CitiesAlreadyPursuedForNegotiation(context)` unions that with
`AvailableSlotsByCity`'s own keys (an already-won-but-unused slot is
equally no reason to bid again — that field already existed, just
wasn't consulted by gateway search). Both gateway-finder functions add
one `.Where` clause excluding it. `FullGameRunner.cs` computes the new
field each quarter from its own `landingBoard.Pending` (already built
there). Deliberately not touched: `BaselineAi.DecideOrders`/
`FindBestUnservedGateway` (dead/demo-only since M39, frozen by
established convention) and `AeroNext.Sim`'s M13 loop (still never
builds a real `LandingRightsBoard` at all — a separate, already-flagged
gap; it passes nothing for the new field and keeps its pre-M41 behavior,
confirmed below, not assumed).

**Done when:** the new field/helper exist with a permissive default;
both gateway-finders skip an already-pursued city; a test proves a
pending bid causes the next-best gateway to be targeted instead of
re-bid; a test proves the same for an already-held unused slot; the
repeat-bid pattern is directly confirmed gone via the same instrumented
trace; both real loops are re-measured.

Done. 549 tests total (4 new, 2 per strategy test file), `StrategyContext`
and `DomesticProfitOptimizerStrategy` at 100%/100% coverage; `AiToolkit`
and `RapidExpansionStrategy`'s only partial branches are the exact same
confirmed-structurally-unreachable ones M39 already documented (line
numbers only shifted by the new code inserted above them). Overall
project coverage **99.89%/99.31%**. Re-ran the same instrumented trace
after the fix: negotiation targets now diversify every quarter (jfk →
lhr → ams → lax → gru → syd → cai → ...) instead of repeating.

**Campaign trajectory — the single biggest measured shift this session.**
The M13 loop came back byte-for-byte identical to M39/M40 (confirmed
directly, not assumed) — it never builds a real `LandingRightsBoard`, so
it has nothing to populate the new field with, exactly as scoped. The
150-game QA harness sweep moved dramatically:

| Metric | M40 | M41 |
|---|---|---|
| Clean games with a win before quarter 244 | 0/150 | **118/150** (avg quarter 25.3) |
| NorthAmerica bankrupt / wins | 140/144, 6 | 118/144, 14 |
| Europe bankrupt / wins | 95/95, 2 | 79/95, 4 |
| EastAsia bankrupt / wins | 9/142, 132 | 15/142, 88 |
| SouthWestAsia bankrupt / wins | 55/55, 0 | 17/55, 18 |
| AustralAsia bankrupt / wins | 41/45, 8 | 23/45, 7 |
| SouthAmerica bankrupt / wins | 119/119, 2 | 62/119, 19 |

Every region got measurably healthier, and the game actually produces a
winner most of the time now (0% → 79% of clean games), something no
milestone this session had achieved in this harness before. The
regional imbalance M39/M40 flagged didn't disappear — EastAsia still
wins the most (88/118) — but it's meaningfully less extreme (was
132/150, effectively unbeatable; SouthWestAsia went from *never*
winning to 18 wins, SouthAmerica from 2 to 19). Consistent with the
diagnosis: most of what looked like "regional economics" was actually
every region's airlines bleeding out on the same self-inflicted,
easily-avoidable mistake — fixing the mistake let the underlying
region-vs-region picture show through far more clearly than before, and
that remaining picture is still worth investigating in a future
milestone, not this one.

---

## M42 — Re-verification and closing the loop ✅

Once `BaselineAi`'s real behavior has changed across M31–M41, every
number recorded *under the old heuristic* elsewhere in this repo needs
re-measuring, not left stale — the same obligation this doc's own origin
section flagged when M31 was retired from `FINANCIAL_MODEL_ROADMAP.md`.
Explicitly: `WIN_CONDITIONS_ROADMAP.md`'s M24 section carries its own
live forward-pointer — *"re-tuning `RequiredRegionCount`/
`RequiredStreakQuarters` once a stronger AI... exists — 1.3% under
`BaselineAi` is a real data point, not a target either constant was
tuned to hit"* — re-run the 150-game QA sweep's win-rate and average
win-quarter figures and revisit those constants if the shape genuinely
changed. Also re-sweep `AeroNext.Sim`'s M13 checkpoint figures (recorded
across `FINANCIAL_MODEL_ROADMAP.md`'s own M26–M34 writeups and
`Program.cs`'s own checkpoint-set comments) the same "measure, don't
guess" way every milestone this session has, and update whichever of
those figures no longer hold.

**Done when:** the QA harness's win-rate/streak numbers and the M13
checkpoint figures are re-measured and written down, and
`WIN_CONDITIONS_ROADMAP.md`'s M24 constants are either reaffirmed with
the new data or revised with a stated reason — not silently left
pointing at pre-`BaselineAi`-rework numbers.

Done. Re-ran the 150-game QA sweep at 244 quarters (identical command
M39-M41 already used): **118/150 (78.7%) win outright, averaging quarter
25.3** — up from M25b's original baseline of 2/150 (1.3%), averaging
quarter 149. Tried actually re-tuning `RequiredRegionCount`/
`RequiredStreakQuarters` against this new data rather than assuming they
still hold or blindly reaffirming them: raising `RequiredRegionCount`
from 3 to 4 collapses the win rate to **0/150** — a hard cliff, not a
dial, since no current strategy ever builds a network reaching 4+ of the
6 regions at once. Raising `RequiredStreakQuarters` (tried 8 and 20
quarters) barely moves win rate (116/150 avg 29.8; 114/150 avg 42.6) —
once an airline reaches 3-region dominance it holds it almost
indefinitely, because M23's bankruptcy is permanent and a frozen rival
can never contest a region back. **Reaffirmed both constants at their
original values, with a stated reason** (written into
`WIN_CONDITIONS_ROADMAP.md`'s own M24 section): the real lever
controlling campaign pacing isn't either win-condition constant, it's
that bankruptcy elimination is permanent — flagged there as a real,
separate question for a future milestone, not fixed here.

`AeroNext.Sim`'s M13 loop re-run and confirmed **byte-for-byte identical**
to its own M39/M40/M41 figures (Continental Wings wins at quarter 10) —
`Program.cs`'s own checkpoint-set comment, written during M39, is still
accurate; no update needed, confirmed rather than assumed.
`FINANCIAL_MODEL_ROADMAP.md`'s M26-M34 writeups carry real figures
measured under the pre-M39 flat `BaselineAi.DecideOrders` path this repo
no longer runs in either real loop — added one forward-pointer note in
that doc's own "Kept in mind" section rather than rewriting dozens of
historical dollar figures throughout; each of those writeups documents
what that milestone measurably did *at the time*, a development-log
entry worth preserving as history, not a live claim to keep current.

**Phase 2 closes here.** M39 (strategy framework) fixed the
identical-decision cannibalization pathology; M40 (hub-awareness)
sharpened one strategy's own network sense; M41 (repeat-negotiation fix)
turned out to be the single biggest lever measured all session, taking
the QA harness from a game that essentially never produced a winner to
one that does most of the time. M42 closes the loop by re-measuring
everything downstream rather than leaving old numbers to quietly rot,
and by being honest that the resulting pacing — most games settling by
around quarter 25-40, EastAsia still winning disproportionately often —
isn't the end of the story: this session's own memory now records that
regional balance work is deliberately deferred until a future "dynamic
demand" milestone, and this M42 pass additionally surfaces permanent
bankruptcy elimination as a second, separate structural question worth
its own milestone before either win-condition constant gets touched
again.

---

## Kept in mind, not scheduled

- **`AllianceCourtOrder`** — deliberately excluded from every milestone
  above, carrying forward `BaselineAi`'s own existing reasoning (M9):
  the alliance mechanic has no consequence today that would change this
  AI's route decisions, so teaching it to court an alliance partner
  would be cosmetic. Revisit once alliances actually do something.
- **`HireExecutiveOrder`'s uncapped, costless nature** — flagged under
  M31 above. Not a `BaselineAi` gap; a pre-existing core-mechanic
  question (should raising the action cap ever cost something, or have
  a ceiling?) that this roadmap surfaces but doesn't resolve.
