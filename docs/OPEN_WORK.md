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

**Every milestone across all 10 roadmap docs is complete**, as of this
writing: M0–M14 (`SIM_CORE_ROADMAP.md`),
M15–M19 (`GAME_SETUP_ROADMAP.md`), M20–M22 (`HISTORICAL_DYNAMICS_ROADMAP.md`),
M23–M24 (`WIN_CONDITIONS_ROADMAP.md`), M25–M30/M34
(`FINANCIAL_MODEL_ROADMAP.md`, M31 retired), M31/M35–M44/M49–M58
(`BASELINE_AI_ROADMAP.md`), M32–M33/M45–M46/M59 (`ROUTE_CAPACITY_ROADMAP.md`),
M47–M48 (`MARKETING_ROADMAP.md`), M61-M64 (`HUB_DEMAND_ROADMAP.md`). M64 is
the current highest *shipped* number (M60 was attempted, measured, and
fully reverted — see below, not a shipped number). M56 shipped no code
change (four route-opening-quality designs tried and reverted); M57
followed with a real, contained fix. **M58 (`NetworkOptimizerStrategy`, a
third roster strategy) is now merged to `main`** — resumed from its
paused `m58a-win-condition-plumbing` branch after M59-M64 shipped, found
and fixed three real regressions in sequence (win-proximity dominance,
futile marketing spend, and the real root cause: a broken cross-strategy
Suitability comparison that structurally disqualified
`DomesticProfitOptimizerStrategy` for 90.3% of mid/late-game selections —
see `BASELINE_AI_ROADMAP.md`'s own M58 section for the full evidence
chain). NorthAmerica's own bankruptcy rate improved only marginally
(85.0%→83.9%) even after the real fix landed. **Two follow-up rounds dug
deeper and found two real, distinct defects** (not the regional-demand-shock
issue it was first assumed to be): round 1 found `NetworkOptimizerStrategy`'s
own new-pair opening had no real cash discipline, letting NorthAmerica's
dense city pool drive its network to more than double in size right
before the 1979 oil shock (fixed, 83.9%→80.7%, but the bankruptcy wave
shifted to years 33-35 rather than disappearing); round 2 found a
plateaued network runs a small, permanently-un-fixable structural deficit
once a route shrinks to 1 aircraft (no closure path existed for one),
fixed via a new, opt-in, patient closure path — substantial, broad
bankruptcy improvement (NorthAmerica 80.7%→75.2%, every other region down
20-40+ points) alongside a real, root-caused (not a bug) win-rate
side effect. See the Active priority section below for the full evidence
chain both rounds, and the still-open, different (1973-oil-crisis-driven)
NorthAmerica cluster round 2 surfaced. **A third round (`WIN_CONDITIONS_ROADMAP.md`'s
own M23 follow-up) closed the near-universal stalemate this same
investigation surfaced** — 94.7% of games ran the full campaign with no
declared winner even with 2+ solvent airlines; a sole-survivor win path
plus an end-of-campaign ranking fallback dropped that to 0% stalemates,
with zero effect on bankruptcy rates (confirmed). **A fourth round
(`BASELINE_AI_ROADMAP.md`'s own "Bankruptcy conditions" section) answered
the long-deferred `WIN_CONDITIONS_ROADMAP.md` "does bankruptcy need a
comeback path" question directly** — a new real borrowing lever
(`AiToolkit.MaintainCashRunway`) fixed the exact fixable case it targeted
(SouthAmerica 37.6%→10.5%) and honestly couldn't touch the genuinely
structural one (NorthAmerica flat, 74.5%→74.1%). **M61-M64 shipped
a real, active, known regression** — see the Active priority section
below for the full picture; this is not the same kind of "clean and done"
as the other ranges listed here. Update this line the moment a new
milestone is opened, or the M61-M64 regression is resolved.

**M60 (hub-and-spoke demand, the first attempt) was tried, measured, and
reverted — not shipped, superseded by M61-M64's later, different
approach.** Two formula-level fixes to `GravityDemandModel.ForPair` were
tried (an additive catchment bonus, then a weight-dependent
distance-exponent discount) and both failed real verification: the
first structurally favored small cities more, not less; the second
flipped the intended city-pair rankings correctly but inflated total
world demand 31× (even for pairs with no hub involvement at all),
collapsing a real 300-game sweep's win rate from 39.0% to 0.3%. Both
were fully reverted before M61-M64's own, differently-designed attempt
began. See `docs/HUB_DEMAND_DESIGN.md` for the real architectural finding
this surfaced (connecting-passenger routing already exists and works via
`NetworkPlanner`/`CompetitiveMarket`; what's actually missing is a
hub-*quality* concept influencing hub *selection*, not the O&D demand
formula itself) and `docs/HUB_DEMAND_ROADMAP.md` for what M61-M64 actually
built from that finding, and where it currently stands.

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

**A sixth round, `docs/HUB_DEMAND_ROADMAP.md`'s M61-M64, shipped with a
known, real, active regression — the current top priority for this
area.** M61 (new `HubQuality` city field) and M62 (`NetworkPlanner`
hub-selection quality/loyalty/substitution) both shipped clean, real,
positive, zero-regression. M63 (`GravityDemandModel.ForPair`'s bounded
hub-demand bonus) initially shipped *disabled* after a real 300-game
sweep collapsed the win rate 39.0%→7.0% when enabled. M64 (route-opening
consolidation bonus, the fix M63's own investigation called for) found
and fixed a real regression of its *own* (isolated to
`DomesticProfitOptimizerStrategy`'s wiring specifically — dropped, kept
only in `RapidExpansionStrategy`) — but even fixed, **M63's original
regression persists almost unchanged when combined** (6.3% win rate,
nearly identical to the original 7.0% collapse).
`RapidExpansionStrategy`'s own consolidation-awareness provides zero
protection. Decisive ablation (`DomesticProfitOptimizerStrategy`
temporarily removed from the roster entirely) confirms it's the real,
primary driver of *both* regressions: with only `RapidExpansionStrategy`
running and M63 enabled, the game is healthier than the M59/M62 baseline
(50.0% win rate, bankruptcy down sharply everywhere) — though a real,
smaller regional shift toward EastAsia remains even then (70.0% vs.
NorthAmerica's 26.0%), still unexplained. **The user made an explicit,
informed decision to ship M63 live with the full, real 2-strategy roster
anyway**, accepting the known 6.3%-win-rate collapse as a real, load-
bearing regression from the M59/M62 baseline (39.0%), rather than keep
blocking on a fourth investigation round. **`DomesticProfitOptimizerStrategy`'s
own dedicated fix for its interaction with M63's boosted demand figures
is now the active priority for this area** — not the same
`ConsolidationMultiplier` approach already proven harmful there once (see
M64's own write-up for the full evidence chain), a genuinely new design
question. The smaller, still-unexplained EastAsia regional shift (present
even in the healthy `RapidExpansionStrategy`-only state) is a real,
separate follow-up once the primary collapse is fixed.

**A seventh round, `BASELINE_AI_ROADMAP.md`'s M58, surfaced a concrete
NorthAmerica lead, then an eighth round (M58 follow-up) traced and
partially fixed it — real progress, with a fresh, still-open cluster
found in the process, not the original regional-demand-shock imbalance
this section otherwise tracks.** M58 itself (`NetworkOptimizerStrategy`,
resumed and fixed through three real regression rounds) recovered
Europe/SouthWestAsia/AustralAsia/SouthAmerica meaningfully but left
NorthAmerica's bankruptcy rate almost unchanged (85.0%→83.9%), clustered
51.7% in a single 10-quarter window right after the 1979 oil shock. Real
per-quarter instrumentation (not the demand model — `GravityDemandModel`'s
own global shock multiplier applies identically to every region) found
the actual cause: `NetworkOptimizerStrategy`'s own step 3 (new pair) had
no real cash discipline, so NorthAmerica's uniquely dense 22-city pool
(M44) let its network more than double in size (routes 5.3→12.0, fleet
7.1→15.0) in the years leading straight into the shock, draining cash
from $372.9M to $106.3M funding it. **Fixed** (`BASELINE_AI_ROADMAP.md`'s
own M58-follow-up section has the full evidence chain, including a first
attempt that measurably made things worse before the real fix landed):
opening a brand-new pair now only happens with real, committed capital,
never an ungated lease fallback. Genuinely flattens the growth curve and
eliminates the collapse-exactly-at-the-shock pattern — but only moves the
aggregate number 83.9%→**80.7%**, because the bankruptcy wave **shifted
later** rather than disappearing: 55.7% of all NorthAmerica bankruptcies
(123/221) now cluster at years 33-35, a window with **no authored
historical shock at all**.

**A ninth round (M58 follow-up, round 2) traced that new cluster and
fixed a real, different defect - substantial, broad improvement, plus a
real, understood (not a bug) win-rate side effect worth knowing about.**
Root cause: a plateaued network runs a small but *structurally flat* net
loss (Revenue and OperatingCost nearly perfectly balanced; the entire
deficit is SalaryCost) for 30+ consecutive quarters before cash runs
out - because a persistently-unprofitable route, once shrunk to 1
aircraft by existing logic, had **no closure path at all** in the
then-current toolkit, only ever a fare cut, forever. Fixed via a new,
opt-in `AiToolkit.ApplyManagementToolkit` parameter
(`enableSustainedSingleAircraftClosure`, default false - only
`NetworkOptimizerStrategy` passes true), with a real bug found and fixed
*before* this could ship: the underlying unprofitable-streak counter
keeps accumulating under Starting-era strategies that never check it, so
the very first time this branch ever ran, a synchronized "backlog dump"
(108 routes closing in a single quarter across a 100-game sample)
collapsed the win rate 7.3%→4.0% - a naive fix (capping credit to
quarters-since-MidGame) measured no better, since every route with a
long backlog still landed on the same calendar quarter; the real fix
gives *zero* credit (not partial) to a streak older than MidGame itself,
confirmed to genuinely desynchronize the dump (`BASELINE_AI_ROADMAP.md`'s
own M58-follow-up-round-2 section has the full evidence chain).
**Measured: NorthAmerica 80.7%→75.2% bankrupt, and every other region
down 20-40+ points** (Europe 93.4%→50.8%, SouthWestAsia 89.5%→62.9%,
AustralAsia 74.2%→52.6%, SouthAmerica 77.6%→38.0%) - real, broad
survival improvement. Overall win rate also dropped (7.3%→5.3%) and
average win quarter dropped (69.3→56.4) - root-caused, not left
unexplained: `RegionalDominanceTracker.Advance` excludes a bankrupt
airline from a region's leadership calculation *entirely*, so the old,
higher-bankruptcy baseline let a survivor frequently become the *sole
remaining eligible carrier* in a region once rivals died, "winning" by
attrition rather than genuine competition - more survivors means more
real contested competition and fewer wins by default (stalemate rate
rose 93.0%→94.7%, exactly consistent with this). Matches this project's
own design pillar directly (CLAUDE.md: "any feature that makes this feel
more like a spreadsheet and less like a territorial fight is suspect") -
shipped with this understanding in hand, by explicit user decision.

**A new, still-real NorthAmerica cluster remains, at years 23-25 this
time (Q91-100, 78% of its bankruptcies) - the live follow-up target.**
Confirmed genuine (not a bug) via the same closure-timing diagnostic:
organic, freshly-started ~8-quarter streaks, not a backlog artifact.
Very plausibly the 1973 oil crisis (years 19-22) pushing many
independent routes into unprofitability at the same real time, with the
matching 8-quarters-later closure wave landing in the same narrow
window - a genuinely harder problem than a synchronization bug, since a
uniform patience threshold can't desynchronize a real, shared external
shock. Concrete next step for whoever picks this up: either a
per-route-timing-aware threshold (harder, no obvious design yet) or
accepting this as an inherent, authored-history-driven pattern rather
than something to eliminate. The low overall win rate/high stalemate
rate (94.7%), while now understood, was not itself judged a problem to
fix this round - `WinConditionChecker`'s own design is a separate,
un-scoped question if it's ever revisited.

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
  all four measured regressions. **M57 built the non-monotonic
  size-match objective M56 called for, scoped strictly to aircraft-type
  selection for an already-chosen pair (never letting it influence which
  pair wins)** — real, measured, no regression: SouthAmerica's own
  bankruptcy rate improved 70.5%→58.6% with zero change elsewhere, but
  the overall win rate stayed flat at 41.3%, so this is progress, not a
  close. `AiToolkit`'s own removed `ClosureWouldSeverContiguity` and
  `AiRouteHistory.ConsecutiveUnprofitableQuarters` itself (still
  tracked, unconsumed) remain real starting points for reintroducing
  closure once route-opening quality is judged good enough.
- ~~**`DomesticProfitOptimizerStrategy`'s own pair *selection*, still
  revenue-only after M57**~~ — closed by the M56 follow-up
  (`BASELINE_AI_ROADMAP.md`): switched Phase 1's ranking key from
  `EstimatedRevenue` to raw demand + hub bonus (`RapidExpansionStrategy`'s
  own proven-safe metric shape, not `AiToolkit.ConsolidationMultiplier`
  itself), same seed-1/300-game/244-quarter sweep before and after.
  NorthAmerica's own structural growth-into-bankruptcy — the exact
  pattern this whole thread traced to — is gone (74.1%→0.4% bankrupt;
  the dominant Q91-100/years-23-25 bankruptcy spike, 161 events pre-fix,
  is now zero events in that window). Bankruptcy fell broadly elsewhere
  too (Europe 48.2%→18.3%, SouthWestAsia 60.0%→2.9%, AustralAsia
  48.5%→37.1%, EastAsia 11.7%→6.2%); SouthAmerica ticked up slightly
  (10.5%→12.2%). The 100%-clean/100%-real-winner game-health metric held
  at ceiling both before and after — none of M56's own four reverted
  designs' regression signature (win rate collapsing to 10-29%).
- **Europe's own idle-fleet stall under `DomesticProfitOptimizerStrategy`**
  — Europe remains a weak-performing region; root cause not yet isolated.
- **EastAsia's own dominance, now sharper (70.0% win share) once
  NorthAmerica stops self-destructing** — real, not newly introduced by
  the M56-follow-up fix above: EastAsia was already the single strongest
  region before it (52.0% win share, already more than double anyone
  else), and Europe/SouthAmerica's own win shares (25.3%→2.7%,
  4.0%→0.0%) were being propped up by inheriting wins whenever
  NorthAmerica collapsed into bankruptcy rather than by winning against
  a full field. Once that artificial clearing stopped, EastAsia's
  already-existing structural edge (a compact, naturally-contiguous city
  pool that doesn't overreach into marginal candidates the way
  NorthAmerica's larger 22-city pool used to) simply became visible
  instead of masked. This is the same shape of finding as the older
  "NorthAmerica's own post-M44 dominance (87.6% win share)" entry this
  replaces — which region ends up lopsided keeps moving, the underlying
  regional-balance question doesn't. Feeds directly into the active
  regional-balance work; not itself a reason to hold this fix back.
- **Changing an existing route's aircraft *type*** — no atomic order
  exists (M54); growing a route requires the exact type already flying
  it. A genuine "swap type" capability needs a new order type or
  deliberate close-then-reopen sequencing, and is doubly blocked on the
  same cross-quarter-state gap as the thrash-cycle item above.

### `HUB_DEMAND_DESIGN.md`
- **Player-designated hubs** — a real system letting the player (and AI)
  explicitly mark specific cities as their own hubs, with associated
  bonuses and restrictions (e.g. preferential treatment in itinerary
  routing, possibly a cap on how many an airline can hold at once).
  Surfaced by the user during the hub-and-spoke demand design pass as the
  real long-term answer to "how does the game know an airline already
  treats a city as a hub" — deliberately kept out of that round's scope.
  Until this exists, hub loyalty/substitution in
  `NetworkPlanner.FindBestHub` uses a cheaper derived proxy (share of the
  airline's own existing routes touching a city) instead of a real
  designation; when this system is eventually built, it should replace
  that proxy rather than coexist with it.

### `WIN_CONDITIONS_ROADMAP.md`
- **Difficulty tiers** — explicitly deferred, no `Difficulty` concept
  exists anywhere yet.
- **Briefing integration** — win-streak progress isn't surfaced in
  `BriefingGenerator`'s exception queue.
- **Reconciling the QA harness's old informal "wins" column** (highest
  final cash — never a real victory condition) **with real win data.**
  Both coexist today.
- ~~**Whether permanent, unrecoverable bankruptcy needs campaign-pacing
  revisiting**~~ — investigated directly (`BASELINE_AI_ROADMAP.md`'s own
  "Bankruptcy conditions" section): real 300-game instrumentation found
  100% of bankruptcies are triggered by cash going negative (never the
  $80M debt ceiling), almost always a slow, multi-quarter bleed with
  plenty of warning. A new `AiToolkit.MaintainCashRunway` gives every
  strategy a real borrowing lever before that point — real, substantial
  fix for a fixable cash crunch (SouthAmerica bankruptcy 37.6%→10.5%),
  honestly limited against a genuinely structural deficit (NorthAmerica's
  own oil-shock-era cluster stayed flat, 74.5%→74.1%) — bankruptcy
  itself is still permanent once it actually happens; this doesn't add
  reorganization, it prevents more of the *avoidable* cases from
  happening in the first place.

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
