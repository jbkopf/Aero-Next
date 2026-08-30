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

## M31 — Executive hiring + a real `CanAfford` primitive

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

---

## M35 — Fleet/route lifecycle: retire and close

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
