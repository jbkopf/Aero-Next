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

## M36 — Debt tools

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

---

## M37 — Fares

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

---

## M38 — Aircraft ownership: buy, not just lease

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

---

## Phase 2 — strategy

## M39 — Growth saturation awareness (the real bankruptcy fix)

Gives the "grow best-performing route" step an actual stopping condition:
stop leasing another unit onto a route once its load factor shows
capacity is already ahead of demand there, and only then fall through to
a new pair, a fare cut (M37), paying down debt (M36), or leaving budget
unspent. This is the milestone that actually addresses the diagnosed
bankruptcy driver from this doc's own origin section — everything in
phase 1 is necessary (the tools have to exist) but not sufficient
without this (nothing before this milestone stops the AI from using
those tools to dig the same hole).

**Done when:** the diagnostic reproduced in this doc's origin section
(instrument a real M13 run, watch fleet size against route count over
many quarters) no longer shows unconditional quarter-over-quarter
leasing onto an already-saturated route; the full M13 checkpoint sweep
and 150-game QA harness sweep are re-measured, not assumed, against this
new behavior.

---

## M40 — Network/hub awareness

`BaselineAi`'s own doc comment already names this gap directly: "no
hub-awareness — picks the single best pair/gateway in the whole world,
not the best one connected to its existing network." Real strategy work,
not a mechanical gap — building toward the hub-and-spoke value
`NetworkPlanner` (M4) already proved emerges from the demand model, which
nothing in `BaselineAi`'s decision logic currently tries to capture.

---

## M41 — Competitive awareness

React to rivals directly: contest a gateway more aggressively where a
rival looks weak there, avoid overinvesting where a rival already
dominates a pair, and — once M34's own explicit scope boundary is
revisited — factor a rival's fare into route/pair decisions. Fare
doesn't yet affect demand size or competitive share at all (M34
deliberately left that out); this milestone either works within that
limit or is the natural trigger to revisit it.

---

## M42 — Re-verification and closing the loop

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
