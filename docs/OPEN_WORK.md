# Open Work

A living index of deferred, not-yet-scheduled, and flagged-but-unresolved
work across the project. Unlike the `*_ROADMAP.md` docs (append-only
development logs — each entry is a record of what happened and when),
**this document reflects current state**: when an item is resolved, edit
or remove it rather than leaving it to rot. When a roadmap's own "Kept in
mind, not scheduled" section gains a new item, mirror it here too.

Compiled 2026-08-31, cross-referencing every `docs/*_ROADMAP.md`'s own
"Kept in mind, not scheduled" section plus session-memory notes.

---

## Milestone status

**Every milestone across all 9 roadmap docs is complete** as of this
writing: M0–M14 (`SIM_CORE_ROADMAP.md`), M15–M19 (`GAME_SETUP_ROADMAP.md`),
M20–M22 (`HISTORICAL_DYNAMICS_ROADMAP.md`), M23–M24
(`WIN_CONDITIONS_ROADMAP.md`), M25–M30/M34 (`FINANCIAL_MODEL_ROADMAP.md`,
M31 retired), M31/M35–M44/M49–M56 (`BASELINE_AI_ROADMAP.md`), M32–M33/M45–M46
(`ROUTE_CAPACITY_ROADMAP.md`), M47–M48 (`MARKETING_ROADMAP.md`). M56 is
the current highest issued number, and shipped no code change — four
route-opening-quality designs were tried and all reverted; see its own
writeup. Update this line the moment a new milestone is opened anywhere.

---

## Active priority

**Regional outcome imbalance — two rounds shipped, both with real,
honestly-reported side effects.** `docs/BASELINE_AI_ROADMAP.md`'s M43:
root-caused to gateway negotiation wins going unused, fixed by
prioritizing an already-won slot over chasing a fresh one. Measured:
overall QA-sweep win rate 70.7%→98.0%. M44 (requested directly by the
user): network contiguity — a new route must originate from the home
city or a city already served, closing an already-designed
`GAME_SETUP_ROADMAP.md` gap. Measured: **NorthAmerica's win share
jumped from 18.6% to 87.6%**, a near-total reversal of M43's own
"region size is a liability" finding (NorthAmerica's dense 22-city local
network is now a dominant advantage instead) — SouthAmerica mirrors it
in the other direction (45.6%→4.2% win share, 17.3%→88.2% bankrupt).
Flagged plainly as a strikingly lopsided result, not smoothed over —
whether it's an acceptable consequence of today's real, uneven city
density per region, or the next thing to investigate, is open.

**A fourth round shipped since, directly relevant here.**
`docs/ROUTE_CAPACITY_ROADMAP.md`'s M45 (universal slot capacity + idle
recontestability, requested directly by the user) measured a large
further move on the *same* 300-seed sweep: overall win rate **83.3%
(M44) → 36.3%**. Isolated by ablation (both mechanisms toggled off one
at a time, then together): AutoCapacity alone drops it to 56.7%,
idle-recontest alone to 74.7%, both together to 36.3% — a real
compounding interaction, not two additive effects. Bankruptcy moved in
*both* directions by region (EastAsia sharply worse, AustralAsia and
SouthAmerica both improved), but win share collapsed almost everywhere —
the win condition got harder to satisfy for anyone, not a mechanical
transfer to NorthAmerica. Full region-by-region table and the ablation
methodology are in M45's own write-up. Reinforces, rather than
supersedes, the standing conclusion below: `BaselineAi`/successor
strategies need a real rework before the cumulative effect of these
individually-correct scarcity mechanics can be fairly judged.

**A fifth round since, testing the opposite direction.** M46 (periodic,
demand-driven slot-capacity expansion, requested directly by the user) let
capacity *grow* over the campaign instead of staying fixed — the
hypothesis going in was that this might recover some of M45's own
win-rate drop. It didn't: overall win rate barely moved (36.3%→38.7%),
and bankruptcy rose in *every* region (NorthAmerica 8.4%→27.0% the
sharpest). Same mechanism-grounded read as the standing conclusion, from
the opposite direction this time: more room to grow reads to
`BaselineAi`/successor strategies as more legal room to keep piling on
leased aircraft and debt, since none of them judge profitability, only
legality. Full numbers and the ablation confirming the effect (M46
disabled reproduces M45's exact recorded baseline) are in M46's own
write-up. `ExpansionPeriodQuarters`/`GrowthIncrementFraction` are both
marked tunable in code if the growth rate itself turns out to be the
lever worth pulling, rather than the AI.

**M48 (`docs/MARKETING_ROADMAP.md`, fare gets a real demand effect),
unlike M47, actually moved this sweep — because unlike marketing,
`BaselineAi`'s own toolkit already submits `SetFareOrder`s reactively
(`RaisedFare`/`CutFare`, since M37) in both real loops.** Win rate
38.7%→40.7%, but average quarters-to-win roughly doubled (23.0→51.6) and
regional bankruptcy moved unevenly (EastAsia worse, SouthAmerica
better) — confirmed real via the same disable-and-reproduce ablation
every round here uses. Not root-caused further: fare reactivity and
demand elasticity now feed back into each other in a loop neither was
designed with the other in mind, flagged for whoever next works on
`BaselineAi`/successor strategies rather than chased here.

**M49 (`docs/BASELINE_AI_ROADMAP.md`, executives cost real money and are
capped at 4) is by far the largest single-milestone move in this entire
run.** Both real strategies hire unconditionally every quarter (M31's
own original design, never revisited) — harmless while free, a
compounding drain now that it costs $600,000 up front plus $300,000/quarter
per executive on staff. Overall win rate **40.7% → 9.7%**; bankruptcy
cleared 96%+ in four of six regions; NorthAmerica — dominant in *every*
prior round (84-85% win share) — collapsed to 24.1%, with SouthWestAsia
taking over as the leading region (75.9%). A genuine reshuffling, not
just a uniform decline, not root-caused past the mechanism itself.
Confirmed real by a two-step ablation (recurring salary alone accounts
for most of the collapse; disabling both changes reproduces M48's own
baseline exactly). Both real strategies' own unconditional hiring habit
is the single most direct next target for whoever next works on
`BaselineAi`/successor strategies — deliberately not touched by M49
itself, the same scope discipline M47/M48 both held to for their own new
mechanics.

**M50 (`docs/BASELINE_AI_ROADMAP.md`, executives: hire only when needed
and affordable) closes that gap directly, the very next round.**
`AiToolkit.ApplyManagementToolkit` now hires only when under the
headcount cap, the action cap was actually binding that quarter, and
cash comfortably clears the hiring cost. Win rate **9.7% → 23.7%**;
NorthAmerica recovers most of the way back (24.1%→74.6% win share);
SouthWestAsia drops back to 25.4%. Confirmed real via the standard
ablation (gating disabled reproduces M49's own baseline exactly). Still
well below M48's own 40.7% pre-M49 baseline — M49's real, permanent cost
is a genuine tax on the action economy even correctly gated, not
something a smarter hire trigger alone fully undoes. Why the recovery
stops at 23.7% rather than 40.7% is flagged, not chased, in M50's own
write-up.

**M51 (`docs/BASELINE_AI_ROADMAP.md`, fare cut removed from the shared
toolkit) answers the M48 question left open above.** M48's own doc
comment on the cut branch predates real fare elasticity ("revenue-negative
under today's demand model"); measured directly post-M50, at three
`FareCutFraction` values plus removed entirely, the branch nets out
negative regardless of magnitude — every tested fraction landed at or
below "removed" (23.7%/24.0%/24.7% win rate vs. **25.3% removed**),
converging toward but never reaching it. Not a tuning problem: a route
bad enough to trip the cut threshold isn't usually demand-constrained
enough for the elasticity gain to outweigh the revenue given up on
passengers it would have carried at the old fare anyway. Removed from
`AiToolkit.ApplyManagementToolkit`; `BaselineAi.DecideOrders`'s own
frozen legacy branch is untouched, same precedent M50 held to.

**M52 (`docs/BASELINE_AI_ROADMAP.md`, marketing campaigns finally usable
by AI) closes M47's own long-standing gap.** Needed a real prerequisite
first — a new `StrategyContext.RivalCountByServedRoute` field, since
`CompetitiveMarket`'s own boost only applies to a genuinely contested
pair and no strategy could previously tell which of its routes qualify.
New `AiToolkit.ApplyMarketingCampaign`: one contested route gets a
targeted `RouteMarketingOrder`, two or more get the broader
`NetworkMarketingOrder`. Win rate **25.3% → 25.0%** (76/300 → 75/300) —
a small but real, one-directional move, confirmed via the standard
ablation. Not a regression to fix, so the small size is itself the
finding: these two strategies rarely end up genuinely contesting the
same pair as each other in the first place (`DomesticProfitOptimizerStrategy`
stays home-region-only; `RapidExpansionStrategy` spreads thin rather
than concentrating), so the new lever rarely finds a target.

**M53 (`docs/BASELINE_AI_ROADMAP.md`, fare cuts and marketing reframed as
load-factor-recovery tools) directly reshapes M51 and M52, per the
user's own explicit instruction.** Fare cut reinstated in
`AiToolkit.ApplyManagementToolkit` (M51's own removal was a correct
answer to "is this revenue-positive?" — the user's reframing asks a
different question: does it help load factor recover, which it does by
construction under M48's elasticity). `ApplyMarketingCampaign` retargeted
from "highest-demand contested route" to "worst-load-factor contested
route below `CloseRouteLoadFactorFloor`." Win rate **25.0% → 23.7%**
(75/300 → 71/300) — isolated by ablation to be *entirely* the fare-cut
reinstatement: the retargeted marketing filter produced **zero**
campaigns across the whole 300-game population (disabling only marketing
reproduces the full M53 result to the digit; disabling only the fare cut
reproduces M51's own pre-marketing baseline, not M52's). Requiring a
route be both contested *and* struggling at once turned out to be
stricter than M52's own already-rare "just contested" trigger — evidently
never co-occurring here. Expected, not a defect: the fare-cut's
win-rate cost is the same tension M51 already found, now deliberately
accepted because the tool's purpose changed. Whether marketing's
targeting needs a looser fallback to actually fire in practice is
flagged, not chased, for whoever next works on this lever.

**M54 (`docs/BASELINE_AI_ROADMAP.md`, real profitability drives
unprofitable-route management) is the user's own direct follow-up
request: recognize an unprofitable route with real data, algorithmically
choose among marketing/fare/capacity/closing, contested vs uncontested
handled differently.** New `FareCalculator.EstimatedProfit` and
`StrategyContext.LastQuarterSeatsCarriedByRoute` (a real,
one-quarter-lagged carried figure, replacing the solo-carrier estimate
that was never accurate for a contested route) retarget both the fare
cut/shrink loop and `ApplyMarketingCampaign` from load factor to real
profit. **Win rate 23.7% → 39.0%** (117/300, avg 51.1), NorthAmerica back
to 84.6% — a clean, large win, reproduced identically twice. **Full route
closure — the fourth tool the request asked for — was designed, built,
measured, and deliberately not shipped**: it collapsed win rate to 6.0%
and handed every win to SouthWestAsia. A same-quarter contiguity-bridge
safety check (built and measured first, on the reasonable hypothesis
that closure was severing an airline's own network reach) changed
nothing; direct instrumentation found the real cause instead — 19,679
full closures against 25,722 route opens in one sweep, essentially
churning the network as fast as it grows. This is the already-flagged
thrash-cycle problem (`BASELINE_AI_ROADMAP.md`'s own "Kept in mind"
section), confirmed to hit closure even harder than the growth-side
thrash it was first diagnosed from, and it needs the same real fix
(cross-quarter memory) that item has always needed — not built here.
Closure was pulled entirely rather than shipped broken; its own
graph-reachability safety-check code was removed too as genuinely dead
code, with a note for whoever builds the real fix to reuse it.

**M55 (`docs/BASELINE_AI_ROADMAP.md`, real cross-quarter route memory)
built exactly the fix M43 and M54 both called for, and split the
thrash-cycle problem cleanly in two.** New `AiRouteHistory` per
(airline, pair) — `LastShrunkQuarter`, `LastClosedQuarter`,
`ConsecutiveUnprofitableQuarters` — fed one-quarter-lagged into
`StrategyContext`. The growth side is genuinely fixed:
`RecentlyShrunkPairKeys` stops `FindBestPerformingRoute` from re-picking
a route it just shrunk (M43's own traced mechanism), and
`RecentlyClosedPairKeys` stops a new-pair search from immediately
reopening a pair just closed. **Win rate 39.0% → 41.3%** (124/300, avg
60.8), reproduced identically. The close side was re-tried with real
memory — requiring sustained unprofitability, not a single quarter's
reading, before eligibility — on the hypothesis that M54's own churn was
the same kind of timing problem. It wasn't: a 2-quarter confirmation
window still closed 68% as many routes as were opened and made things
worse (12.0% win rate); a 6-quarter window improved but still
underperformed doing nothing (24.7% vs. 41.3%). The real driver, now
understood: this AI's own route-*opening* heuristics send a large,
persistent share of new routes into genuinely sustained unprofitability
— a selection-quality problem, not a timing one memory can filter out.
Closure was pulled a second time; `ConsecutiveUnprofitableQuarters`
stays tracked as infrastructure for whoever improves route-opening
quality first.

Three items found during these investigations, deliberately deferred
rather than folded in — all filed under `BASELINE_AI_ROADMAP.md`'s own
"Kept in mind" now:
- **The route open/shrink/regrow thrash cycle** — real, but a tried
  static-threshold fix measurably made things *worse* (win rate
  98.0%→85.3%); the real fix needs cross-quarter state, filed for a
  future AI-strategy-expansion round.
- **Europe's own idle-fleet stall** under `DomesticProfitOptimizerStrategy`
  — Europe remains the worst-performing region (86.3% bankrupt after
  both rounds) throughout; root cause not yet isolated.
- **NorthAmerica's own post-M44 dominance (87.6% win share)** — real,
  causally understood (dense local network), but flagged for a decision
  on whether it's acceptable as-is.

`RegionalEconomics.Factor` is confirmed not the cause of the original
imbalance (NorthAmerica and EastAsia shared the same 1.117 factor yet
landed at opposite outcomes pre-M43) — the real driver was downstream
AI strategy/network-topology behavior, not starting conditions.

---

## Deferred / "Kept in mind, not scheduled" items, by roadmap doc

### `GAME_SETUP_ROADMAP.md`
- **AI differentiation** — the 4 airlines have differentiated starting
  conditions since M15–M19, but nothing beyond M39–M41's region-aware
  strategies makes them play differently by home region's own character.
- **Route-entry slot requirement** — done. Both halves closed:
  network-contiguity via `docs/BASELINE_AI_ROADMAP.md`'s M44, and
  universal slot capacity (every city, not just the 15 gateways, plus
  idle-slot recontestability) via `docs/ROUTE_CAPACITY_ROADMAP.md`'s
  M45. (This bullet previously claimed `QuarterEngine` never enforced
  gateway slot ownership against a plain `RouteOrder` — that was already
  stale/incorrect even before M45: M33 wired real engine-level rejection,
  not just an AI self-check, confirmed directly in `QuarterEngine.RunQuarter`'s
  own `availableSlotsByCity` parameter.)
- **Gateway home-city exception** — every region's gateway sits inside
  the ordinary M9 contested mechanic, including for the local airline.
  Revisit only with real evidence this makes early gateway negotiation
  uninteresting.
- **SouthWestAsia has no gateway of its own** — every other home region
  has one; Dubai is the obvious candidate, but designating it removes it
  from M16's eligible-city average (a real tradeoff, not resolved).
- **AustralAsia's 0.755 economic factor** — lowest ever measured for any
  region, shipped as-is. Directly relevant to the active-priority item
  above.

### `FINANCIAL_MODEL_ROADMAP.md`
- **Service level** remains open — `Airline.Quality` is still a single
  placeholder knob, now standing in for service level alone (marketing
  got a real, direct lever via `docs/MARKETING_ROADMAP.md`'s M47; fare
  itself was settled by M34 and, as of M48, genuinely affects demand
  too — elasticity-based, bidirectional, bounded so a premium never
  forces carried passengers down while demand is still above capacity).
- **Interest rate doesn't scale with leverage** — fixed 2% regardless of
  debt carried.
- ~~**No ongoing gate-holding cost**~~ — closed by
  `docs/ROUTE_CAPACITY_ROADMAP.md`'s M45: a gateway slot left unused 4
  consecutive quarters becomes contestable by another airline (the
  original owner keeps using it freely until someone else actually wins
  it away). Non-gateway `AutoCapacity` cities were never owned in the
  first place, so this never applied there.
- **Age-based aircraft depreciation** — flat resale fraction regardless
  of age, deliberately simple until fleet age is tracked for other
  reasons.

### `BASELINE_AI_ROADMAP.md`
- **`AllianceCourtOrder`** — excluded everywhere; alliances have no game
  consequence yet, so teaching the AI to court one would be cosmetic.
- ~~**`HireExecutiveOrder`'s uncapped, costless action-cap growth**~~ —
  closed by `docs/BASELINE_AI_ROADMAP.md`'s M49: a one-time hiring cost,
  a recurring per-quarter salary, and a hard cap of 4 executives.
- ~~**Both real strategies hiring unconditionally regardless of cost**~~
  — the gap M49 itself surfaced, closed by `docs/BASELINE_AI_ROADMAP.md`'s
  M50: hire only when the headcount cap has room, the action cap was
  actually binding this quarter, and cash comfortably clears the cost.
  Recovers most, not all, of M49's own win-rate drop (9.7%→23.7%,
  against M48's pre-M49 40.7%) — see the active-priority note below for
  the still-open gap between 23.7% and 40.7%.
- ~~**The route open/shrink/regrow thrash cycle**~~ — closed by M55:
  real cross-quarter memory (`AiRouteHistory`/`RecentlyShrunkPairKeys`)
  stops growth from re-picking a route it just shrunk, exactly the
  mechanism M43 traced. A tried static load-factor floor had measurably
  made things worse; the fix needed memory, not a threshold.
- **The open/close/reopen side is separate and still open, now
  understood differently than first guessed.** M54's full-closure
  attempt measured 19,679 closures against 25,722 opens and was pulled.
  M55 re-tried it with real sustained-unprofitability memory (the fix
  the growth side itself needed) and it *still* underperformed doing
  nothing, even at a patient 6-quarter confirmation window (24.7% vs.
  41.3% win rate, 58% closure-to-open ratio) — not a timing problem
  after all, but this AI's own route-*opening* heuristics sending a
  large, persistent share of new routes into genuinely sustained
  unprofitability. **M56 attacked route-opening quality directly (the
  real 51.7%-bad-open driver traced to `DomesticProfitOptimizerStrategy`'s
  own revenue-only ranking) and tried four designs, all reverted** —
  ranking by real profit, by captured demand, a minimal profitability
  floor, and a load-factor-derated ranking all collapsed win rate
  (worst: 10.0%; best: 29.0%, still far below 41.3%), because every one
  of them rewards profitability/utilization once a candidate's own
  capacity exceeds its pair's demand, which is structurally the same
  as rewarding ever-smaller aircraft without bound — and this game's
  economy (action budget as the scarce resource, not capacity) punishes
  that hard. See `BASELINE_AI_ROADMAP.md`'s M56 for the full math and
  all four measured regressions. Still filed for whoever attempts a
  genuinely non-monotonic size-match objective next; `AiToolkit`'s own
  removed `ClosureWouldSeverContiguity` and
  `AiRouteHistory.ConsecutiveUnprofitableQuarters` itself (still
  tracked, unconsumed) remain real starting points for reintroducing
  closure once that prerequisite exists.
- **Europe's own idle-fleet stall under `DomesticProfitOptimizerStrategy`**
  — Europe remains the worst-performing region (86.3% bankrupt after
  M43+M44 both) throughout; root cause not yet isolated.
- **NorthAmerica's own post-M44 dominance (87.6% win share)** — real,
  causally understood (its dense 22-city local network is now a
  compounding advantage under network-contiguity), but a strikingly
  lopsided result flagged for a decision on whether it's acceptable.
- **Changing an existing route's aircraft *type*** — no atomic order
  exists (M54); growing a route requires the exact type already flying
  it. A genuine "swap type" capability needs a new order type or
  deliberate close-then-reopen sequencing, and is doubly blocked on the
  same cross-quarter-state gap as the thrash-cycle item above.

### `WIN_CONDITIONS_ROADMAP.md`
- **Difficulty tiers** — explicitly deferred, no `Difficulty` concept
  exists anywhere yet.
- **Briefing integration** — win-streak progress isn't surfaced in
  `BriefingGenerator`'s exception queue.
- **Reconciling the QA harness's old informal "wins" column** (highest
  final cash — never a real victory condition) **with real win data.**
  Both coexist today.
- **Whether permanent, unrecoverable bankruptcy needs campaign-pacing
  revisiting** — the one item left open after M42's win-condition-constant
  re-tuning check (both constants were reaffirmed, not revised).

### `HISTORICAL_DYNAMICS_ROADMAP.md`
- **Gate capacity growing over time** — done, via
  `docs/ROUTE_CAPACITY_ROADMAP.md`'s M46. **A city that wasn't a gateway
  in 1955 becoming one later** remains open — a different, harder
  problem M46 explicitly didn't attempt: a new M9 contested asset
  appearing mid-campaign, touching `LandingRightsBoard` and M15's
  eligible-city logic.
- **AI awareness of historical trends** — `BaselineAi`/successor
  strategies already react to current-quarter demand, so a city with a
  real growth trajectory naturally looks more attractive once its weight
  rises; whether the AI should ever *anticipate* a known future trend
  (buy into Dubai before it takes off) is separate, harder scope.

---

## Other noted work — real findings, not yet acted on

1. **`WIN_CONDITIONS_ROADMAP.md` contains a stale, now-incorrect claim**:
   "M13's own demo campaign never triggers a winner... it's deliberately
   flat and symmetric." A later, unrelated milestone (fleet ownership
   differentiation) broke that symmetry — the M13 loop now reliably
   produces a winner (Continental Wings, quarter 10) every run. Confirmed
   directly during `HISTORICAL_DYNAMICS_ROADMAP.md`'s M22 but left
   unfixed there (out of that milestone's own scope). **A small, targeted
   doc fix, whenever someone's next in that file.**
2. **`SIM_CORE_ROADMAP.md`'s M13 section's whole bankruptcy-arc-chasing
   history (9 rounds of city-expansion updates) is now permanently moot**
   for that specific loop — a winner is always declared before any of it
   would play out. Documented plainly during M22; the section itself
   wasn't restructured.
3. **The AI's sensitivity to major structural demand Trends is a real,
   measured open question** (`HISTORICAL_DYNAMICS_ROADMAP.md` round 16):
   four different curve shapes for China's opening produced non-monotonic,
   hard-to-predict swings in aggregate win rate and regional bankruptcy —
   the signature of real threshold-sensitivity in
   `BaselineAi`/`RapidExpansionStrategy`'s own decision logic, not
   something a gentler curve reliably calms. Directly relevant to the
   active-priority item below.

---

## Sequencing note: "dynamic demand" vs. Historical Dynamics

Project memory records that regional-balance work
(`RegionalEconomics.Factor`, the EastAsia/AustralAsia-vs-everyone-else
imbalance M39–M41 first measured) was **explicitly deferred until a
"dynamic demand" milestone landed** — reasoning being that a static-constant
fix would be immediately invalidated by dynamic demand arriving later.

**Resolved 2026-08-31:** the user directed work to proceed on regional
balance now, treating `HISTORICAL_DYNAMICS_ROADMAP.md` (M20–M22, complete)
as satisfying that prerequisite, even though it's scoped to authored
historical vignettes on specific cities/regions rather than a general
per-region dynamic-economy mechanism. Recorded here so the reasoning isn't
lost if this sequencing question resurfaces later.
