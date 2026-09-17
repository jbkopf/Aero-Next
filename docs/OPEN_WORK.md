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
M47–M48 (`MARKETING_ROADMAP.md`), M61-M65 (`HUB_DEMAND_ROADMAP.md`; M65 is
a regional-balance change, not hub-and-spoke, logged there only because it
touches `GravityDemandModel` — see `docs/AUSTRALASIA_TUNING_EXPERIMENTS.md`
for the real write-up, and its sibling
`docs/SOUTHAMERICA_TUNING_EXPERIMENTS.md` for a second, separately-landed
regional-balance fix — `data/cities.json`'s Belo Horizonte entry — that
never got its own M-number since it shipped as a plain data change). M65
is the current highest *shipped* number (M60 was attempted, measured, and
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

**A new NorthAmerica cluster was found at years 23-25 (Q91-100, 78% of
its bankruptcies), very plausibly the 1973 oil crisis (years 19-22)
pushing many independent routes into unprofitability at the same real
time, with the matching 8-quarters-later closure wave landing in the
same narrow window** - flagged at the time as a genuinely harder problem
than a synchronization bug, since a uniform patience threshold can't
desynchronize a real, shared external shock, and left as the "live
follow-up target" with no concrete design yet.

**Resolved since, as a side effect of unrelated work - re-confirmed
directly under today's game state.** `BASELINE_AI_ROADMAP.md`'s own M56
follow-up (switching `DomesticProfitOptimizerStrategy`'s Phase 1 ranking
from `EstimatedRevenue` to raw demand + hub bonus, done for a completely
different reason - fixing that strategy's own capacity-blind pair
selection) eliminated NorthAmerica's structural growth-into-bankruptcy
pattern outright: 74.1%→0.4% bankrupt, and the Q91-100 spike specifically
went from 161 events to zero in that same sweep. This section's own
"live follow-up target" framing was never updated to reflect that at the
time. **Re-checked freshly here, under the current game state** (every
change this session - Cairo/Lagos/Brussels weights, Africa counting
toward the win condition, the `GameDifficulty` mechanism, and especially
the full-random filler default, which reshuffled regional win-share
patterns broadly): a 300-game sweep on the real production wiring found
NorthAmerica bankruptcy at 1.52% (3/197 seats) - and **zero of those
three land anywhere near Q91-100**; all three cluster instead around
Q31-40 (~1962), a sample far too small (n=3) to read as a real pattern
rather than noise. The oil-crisis cluster is gone, not just smaller, and
stays gone after everything else that's changed since. Closed - not an
open item any more.

---

## Deferred / "Kept in mind, not scheduled" items, by roadmap doc

### `GAME_SETUP_ROADMAP.md`
- **AI differentiation** — the 4 airlines have differentiated starting
  conditions since M15–M19, but nothing beyond M39–M41's region-aware
  strategies makes them play differently by home region's own character.
- **A real difficulty setting for computer-controlled airlines** — nothing
  in the game today lets a player dial AI strength up or down; every
  AI-controlled seat plays identically regardless of how the human wants
  the game to feel. One concrete, already-scoped dimension: *how
  random vs. how optimal an AI's starting setup is*. The filler-seat
  investigation (`docs/OPEN_WORK.md`'s own item 4, above) built exactly
  half the needed mechanism already —
  `GameSetup.CreateSeats`'s `fillerRegionRng`/`fillerWeightExponent`
  turns *which region* an AI-filler seat gets from a strict
  richest-`RegionalEconomics.Factor`-first cut into a tunable weighted
  draw (0.0 = uniform regardless of wealth; higher sharpens back toward
  the deterministic ranking) — a "hard AI" could use a high exponent (or
  no `fillerRegionRng` at all, the shipped default) and an "easy AI" a low
  one or 0.0.

  **The city-choice half is now built, validated, and its own "always
  optimal by default" question closed — the same way the filler-region
  exponent's was.** `StartingConditionsResolver.ResolveWithWeightedRouteRandomness`
  (rank-weighted: combination rank *i* gets weight `1/(i+1)^weightExponent`
  — 0.0 = uniform random among every valid combination, larger = biased
  toward `MostProfitableRoute`'s own pick) replaced the earlier binary
  0.0/1.0 switch, wired through `ExperimentTuning.GlobalRouteWeightExponent`.

  **Tried making `Resolve` itself unconditionally optimal by default, per
  direct instruction — measured, reverted, root-caused, re-measured, and
  shipped.** The old "V1" experiment (`docs/AUSTRALASIA_TUNING_EXPERIMENTS.md`)
  had already rejected a universal always-optimal policy once (fixed
  Europe, spiked EastAsia bankruptcy to 24.5%). Worth re-testing since
  M65's AustralAsia tuning, the SouthAmerica Belo Horizonte fix, and this
  doc's own gateway-relevance fix (item 4, above) had all shipped since —
  maybe they'd defused it. A real 300-game canonical sweep (seed 1, 244
  quarters) with `Resolve` made unconditionally optimal:

  | | Old heuristic (`Resolve`, shipped) | Unconditionally optimal |
  |---|---|---|
  | Total bankruptcy | 1.50% | **4.42%** |
  | Avg game length | 153.2q | 177.3q |
  | Europe bankrupt / win share | 0.0% / 5.7% | 0.5% / 12.3% |
  | EastAsia bankrupt / win share | 3.1% / 51.7% | **10.7%** / 46.3% |
  | AustralAsia bankrupt / win share | 5.2% / 7.7% | **15.5%** / 3.7% |
  | SouthAmerica bankrupt / win share | 1.3% / 6.3% | 1.3% / 4.3% |

  Smaller than the original V1 shock (EastAsia 3.1%→10.7% here vs.
  4.5%→24.5% then), but the same failure mode, confirmed still real on
  the current codebase: Europe wins, EastAsia/AustralAsia pay for it, and
  total bankruptcy nearly triples. A follow-up screen (150 games/level)
  across `GlobalRouteWeightExponent` itself, hoping the dial had a safer
  middle ground between uniform-random and always-optimal, found the
  opposite of what a "difficulty dial" should look like — monotonic in
  the wrong direction: total bankruptcy was *worse* toward the
  uniform-random end (12.50% at exponent 0.5, 8.33% at 1.0) and only
  improved moving toward always-optimal, plateauing around 4.8–5.2%
  (exponent ≥ 2) — never back near the 1.50% heuristic baseline at any
  point on the dial. `StartingConditionsResolver.Resolve` was reverted to
  the original heuristic-first behavior at this point.

- **EastAsia dominance, root-caused and fixed — the filler-region exponent
  above stops being just a "future difficulty setting" scaffold and
  becomes the real shipped default.** EastAsia won 47.0% of games in
  every sweep this session (vs. Europe's 24.0%, the next highest) despite
  starting *weak*, not strong: a real 1955-effective-demand check found
  EastAsia's total (326) is the second-lowest of all six regions —
  Beijing alone drops from a static weight of 95 to an effective 28.5
  under the Maoist-era closed-economy multiplier (0.3x). A "does it win
  late once China's real economic boom lands" follow-up was tested and
  **refuted directly**: EastAsia's median win year is 1975, right in the
  middle of the pack (Europe 1971, SouthWestAsia 1965, NorthAmerica/
  SouthAmerica 1976) — 88.7% of its wins land in 1975-1984, while Beijing
  is still under Deng's early-reform 0.4x, decades before the 2001 WTO
  boom or the 2015 peak (3.5x).

  **The real mechanism was geography plus a walkover, not strength or
  timing.** EastAsia has the tightest, richest home cluster of any region
  (1,433 km avg intra-region distance, tied-highest per-city demand
  density) and sits closer to its two neighbors than any other region
  sits to its own best neighbor (2,518 km to AustralAsia, 4,179 km to
  SouthWestAsia — the asymmetry is stark: AustralAsia's own path back to
  EastAsia is 5,067 km, roughly double). A dominated-region breakdown of
  EastAsia's 136 genuine dominance wins found **99.3% are exclusively
  {home, AustralAsia, SouthWestAsia}** — it essentially never expands
  beyond this one 3-region combination. The decisive finding: EastAsia's
  win rate is **61.3% when neither AustralAsia nor SouthWestAsia is
  seated by a real competing airline, and exactly 0.0% (0/16 games) when
  both are** — traced straight back to `GameSetup.CreateSeats`'s own
  richest-4-by-`RegionalEconomics.Factor` filler cut, which structurally
  excludes AustralAsia (rank 6, Factor 0.756) and SouthWestAsia (rank 5,
  Factor 1.006 — a 2.3% miss behind Europe's own 4th-place 1.029) from
  filler in most games. EastAsia's dominance was almost entirely about
  walking into unclaimed land, not beating real rivals.

  **Fixed by making the already-built weighted-random filler
  (`fillerRegionRng`/`fillerWeightExponent`, this section's own scaffold
  above) the real default at the one real "how does a game actually get
  set up" call site, `AeroNext.QaHarness/Program.cs`** — reuses `configRng`
  sequentially (already consumed by `GameConfigGenerator.Generate` just
  above it) rather than a second seed, one continuous seeded stream per
  game per CLAUDE.md's determinism rule, at `fillerWeightExponent: 0.0`
  (pure uniform among unclaimed regions, ignoring wealth entirely — 0.0
  vs. 0.5 were measured near-identical in an earlier screen, so the real
  lever is abandoning the deterministic cut at all, not fine-tuning past
  it). `GameSetup.CreateSeats`'s own method-level default is untouched
  (still `fillerRegionRng: null` = the original deterministic cut,
  byte-identical for every existing test/caller that doesn't opt in) —
  only the real harness's own call site changed.

  **Measured on the actual shipped wiring, not a scratchpad approximation
  — two independent 300-game runs of the real CLI** (seed 1 and seed
  1000, avoiding the earlier round's own scratchpad tool, which derived
  its filler RNG from a separate offset stream and was never
  cross-validated against a second seed range):
  | | Seed 1 (300 games) | Seed 1000 (300 games) |
  |---|---|---|
  | NorthAmerica win share | 39.0% | 41.7% |
  | EastAsia win share | 23.3% | 20.0% |
  | SouthWestAsia win share | 14.0% | 11.7% |
  | AustralAsia win share | 9.7% | 11.3% |
  | Europe win share | 7.7% | 9.7% |
  | SouthAmerica win share | 6.3% | 5.7% |
  | Games with a bankruptcy | 19/300 | 17/300 |
  | Avg win quarter | 118.6 | 110.2 |

  Consistent across both independent seed ranges — real signal, not
  noise. EastAsia's own share collapses from 47.0% to ~20-23%, more than
  half; NorthAmerica becomes the new leading region at ~40%, plausible
  given its own real fundamentals (most cities of any region, tied-highest
  long-run `RegionalEconomics.Factor`, and — unlike EastAsia — no
  early-campaign historical handicap to overcome first). Bankruptcy stays
  essentially unchanged from every deterministic-filler baseline this
  session measured (~1.7-1.9% per-seat) — this redistributes who gets a
  fair shot at winning, it doesn't destabilize anything.

  **Root-caused instead of left rejected: a real bug, not an inherent
  tradeoff.** Every route-picking method here built its own
  `GravityDemandModel` with no `schedule`/`year` — invisible to
  `historical_events.json`'s authored, city/region-scoped 1955 demand
  suppression (e.g. Shanghai's Maoist-era "closed economy" entry, a real
  0.3x multiplier the actual per-quarter market always applies).
  "Most profitable" chased the biggest *mature* demand weight it could
  see — disproportionately the same handful of cities carrying the
  largest 1955 suppression — sizing an aircraft for demand that was never
  really there on quarter 1. Traced directly: fuk→pvg's static estimate
  saw 27,939 passengers/quarter; the real quarter-1 market delivers
  exactly 8,382 (27,939 × 0.3) — a 22,000-seat-quarter aircraft chasing a
  phantom number 3.3x too large. `StartingConditionsResolver.Quarter1DemandModel`
  now builds the exact schedule/year-aware model every real per-quarter
  call site already uses, closing the gap for every method here.

  Re-measured "always optimal" with the fix in place (300-game canonical
  sweep):

  | | Heuristic (with the fix) | Always optimal (with the fix) |
  |---|---|---|
  | Total bankruptcy | 1.75% | 2.33% |
  | Avg game length | 145.1q | 137.8q |
  | NorthAmerica bankrupt / win share | 0.0% / 15.3% | 0.4% / 18.0% |
  | Europe bankrupt / win share | 0.0% / 4.7% | 1.0% / 6.3% |
  | EastAsia bankrupt / win share | 4.5% / 59.3% | **2.1%** / 61.7% |
  | SouthWestAsia bankrupt / win share | 0.0% / 9.7% | 3.8% / 8.0% |
  | AustralAsia bankrupt / win share | 5.2% / 6.0% | **12.4%** / 4.0% |
  | SouthAmerica bankrupt / win share | 1.3% / 5.0% | 1.3% / 2.0% |

  EastAsia — the region the bug hurt worst before (10.7% pre-fix) — now
  *improves* under always-optimal (4.5%→2.1%), safer than the heuristic
  itself, once it can see the real, suppressed 1955 demand instead of a
  phantom mature-market number. Doing the arithmetic on raw counts, every
  region's own change nets out to +7 bankruptcies total (21→28), and
  AustralAsia alone accounts for all seven of them (5→12) — every other
  region is flat or a net improvement. **Shipped**: `Resolve` now always
  makes the fully-optimal pick, unconditionally, as the real game's
  default. AustralAsia's own remaining problem (a bigger, pricier aircraft
  against the game's most capital-poor starting region — the same failure
  mode the original Factor A experiment already flagged, now re-triggered
  at scale) is a separate, real, not-yet-investigated issue, isolated and
  well-understood enough to pursue on its own.

  **The difficulty-setting hook survives the flip.**
  `ResolveWithWeightedRouteRandomness`/`GlobalRouteWeightExponent` remain
  the adjustable mechanism `Resolve`'s new default plugs into — a large
  exponent converges on exactly what `Resolve` now always picks, a small
  one diverges toward uniform-random. No real difficulty setting exists
  yet to drive it (still not wired to an actual difficulty concept
  anywhere — no such setting exists yet in `AeroNext.Sim`/the eventual
  Godot client), but the structural guarantee holds: a future setting only
  has to pick an exponent (and pass its own seeded `SimRng`) instead of
  calling `Resolve` directly, no re-plumbing of `StartingConditionsResolver`
  itself required. Its own exponent screen predates this demand-model fix
  and hasn't been re-verified against it — treat the "low exponent is
  worse, not safer" shape as a caveat for whoever designs the real
  setting, not a re-confirmed guarantee at every level. Likely also wants
  a second dimension beyond setup — in-campaign strategy
  aggressiveness/skill — but that's a separate, larger design question
  (`BASELINE_AI_ROADMAP.md`'s own strategy roster) not scoped here.
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
  100% of bankruptcies are triggered by cash going negative, almost always
  a slow, multi-quarter bleed with plenty of warning. A new
  `AiToolkit.MaintainCashRunway` gives every strategy a real borrowing
  lever before that point — real, substantial fix for a fixable cash
  crunch (SouthAmerica bankruptcy 37.6%→10.5%), honestly limited against a
  genuinely structural deficit (NorthAmerica's own oil-shock-era cluster
  stayed flat, 74.5%→74.1%). **The "Bankruptcy conditions follow-up"
  section closes the gap this left: cash going negative no longer freezes
  the same quarter it happens** — a one-quarter grace (`FinancialState.InDistress`)
  gives the airline's very next turn a real chance to react before
  `IsBankrupt` can ever become true, and the $80M debt ceiling stopped
  being a bankruptcy trigger at all — it's now a cap enforced at the loan
  request itself (`QuarterEngine.RunQuarter` rejects an over-ceiling
  `RefinanceOrder` outright), with `FinancialResolver.RemainingBorrowingCapacity`
  the real, queryable headroom a future loan-request UI would show.
  Measured: no regression, Europe's own bankruptcy rate improved further
  (18.3%→10.7%), every other region moved little or not at all — exactly
  the structural-vs-fixable split the original investigation already
  drew. Bankruptcy itself is still permanent once it actually happens;
  this doesn't add reorganization, it gives more of the *avoidable* cases
  one more real chance before that point.
- ~~**Distress-aware AI reaction**~~ — built (`DistressStrategy`,
  `docs/BASELINE_AI_ROADMAP.md`'s own section) - dispatched for exactly
  as long as `StrategyContext.InDistress` is true, no growth phase,
  `AiToolkit.ApplyManagementToolkit` reused wholesale otherwise. Measured
  a real, honest wash (not a regression, not a clean win) against the
  grace-period-only baseline - shipped anyway as a real, reasonable
  behavior. `CloseRouteOrder`/`RetireAircraftOrder`'s own aggressive,
  unconditional-retirement version was tried and found to cost real value
  in Europe specifically (some idle units really were useful again once
  solvent) - the shipped version relies on `ApplyManagementToolkit`'s own
  narrower stranded-unit check instead.
- ~~**Africa never counts toward regional dominance**~~ — closed on the
  `africa-win-condition` branch (not yet merged to `main`, kept separate
  by the user's own explicit request "so we can tweak it"; validated and
  confirmed final). User's own hypothesis, tested directly: the "Africa
  is a bridge region" exclusion (`RegionalDominanceTracker`'s prior doc
  comment, unchanged since M24) predates the Asia split
  (`bb07d41`/`ac7e0ad`, both 2026-08-29) that turned one undifferentiated
  "Asia" into three home regions including AustralAsia - the current
  weakest home region by `RegionalEconomics.Factor` (0.756). A real
  comparison found Africa's own eligible cities (Cairo 50, Lagos 45,
  Nairobi 40...) carry demand in the same range as AustralAsia's or
  SouthAmerica's, not the near-zero chokepoint numbers CLAUDE.md describes
  for Wake/Honolulu/Anchorage/Gander/Azores - Africa's own
  `RegionalEconomics.Factor` equivalent (0.689) is close to, not
  dramatically below, AustralAsia's.

  **The compromise landed**: Africa stays permanently non-home-eligible
  (`HomeRegions.All`, `EligibleStartingCities`, `RegionalEconomics.Factor`
  all untouched) but now counts toward the dominance win condition, via a
  new `DominanceTrackedRegions.All` (`AeroNext.Core.Competition`) that
  `RegionalDominanceTracker`/`WinConditionChecker`/`AiToolkit.RegionsNotYetLed`
  all read instead of `HomeRegions.All`.

  **An independent, genuinely important bug found and fixed along the
  way**: `StrategyContext.RegionsLedLastQuarter` was only ever populated
  in `AeroNext.Sim`'s own lighter M13 demo loop - never in
  `FullGameRunner.cs`, the harness every real 300-game sweep (including
  the original win-condition-awareness measurement, `BASELINE_AI_ROADMAP.md`'s
  own section) actually runs on. The whole `regionsNotYetLed`
  reinforcement mechanism shipped there has been dead code in every real
  measurement since. **This retroactively changes how that section's own
  "0/197, completely unmoved" result should be read** - see the
  correction added to that section directly.

  **Three-way isolated 300-game sweep** (seed 1, 244 quarters) separates
  "did fixing the dead wiring help" from "did Africa specifically help":
  | | Europe reaches3Led | Europe winShare | EastAsia reaches3Led | Total bankrupt | Avg quarters |
  |---|---|---|---|---|---|
  | Dead wiring, no Africa (prior baseline) | 0.5% | 6.0% | 60.0% | 1.92% | 139.4 |
  | Wiring fixed, Africa still excluded | 0.5% (unchanged) | 4.7% | 69.7% | 1.92% | 128.0 |
  | Wiring fixed + Africa counts | **18.8%** | **13.3%** | 58.6% | 1.67% | 94.4 |

  The middle row is the control: fixing the dead-code bug *alone* changes
  nothing for Europe (byte-identical 0.5%) - this actually **validates**
  the original investigation's own conclusion ("a selection-priority
  nudge can't manufacture investment") rather than overturning it, just
  via a genuinely working mechanism this time instead of an accidentally
  inert one. It's Africa specifically that moves Europe (0.5%→18.8%
  reaches3Led) and SouthWestAsia (5.7%→43.8%, not shown above) - exactly
  the two regions a touch-matrix diagnostic predicted, since they're the
  only two whose eligible starting cities are geographically close enough
  to reach Africa at all (see below). NorthAmerica/EastAsia both cede
  some ground - a healthy rebalancing of a previously very lopsided
  distribution (EastAsia 60-70% win share), not a regression: total
  bankruptcy improved slightly and average game length dropped sharply
  (more decisive competition, not a stalemate-reduction artifact).

  **Contestedness check, run before calling this settled**: is Africa's
  leadership genuinely fought over, or does whoever touches it first just
  keep it? Real per-game leadership-handoff tracking found Africa is the
  *least* contested region in the game (3.0% of games see a real
  leadership change, vs. 4.7-11.7% for the six real regions and 32.0% for
  AustralAsia specifically) with the lowest average handoff count (0.05).
  Europe holds it at game-end in 57.3% of *all* 300 games; SouthWestAsia
  15.7%; nobody else meaningfully. Traced to real geography, not an AI
  artifact: average distance from each home region's eligible starting
  cities to the *nearest* Africa city is 1,603km for Europe vs. 3,375km
  for SouthWestAsia (next closest) vs. 6,381-10,733km for the other four
  regions - Europe is genuinely, literally next door to North Africa the
  way Madrid is to Casablanca, while NorthAmerica/EastAsia/AustralAsia
  sit far enough away to be effectively locked out for most of the
  campaign regardless of AI behavior. **Read as a real, earned strategic
  asymmetry consistent with CLAUDE.md's own chokepoint-geography design
  pillar** (asymmetric opportunity by geography is the point, not a
  fairness bug to flatten) rather than something needing further
  tuning - Africa functions as a private Europe-vs-SouthWestAsia arena,
  not a universally-accessible 7th region, and that's judged acceptable
  by the user's own final call.

  985 tests passing, every changed file fully covered. **Merged to `main`**
  (the `africa-win-condition` branch, commits `4823f39`/`6ddacb7`),
  confirmed final by the user; the branch itself has since been deleted.

  **Cairo weight follow-up, same investigation.** A city-by-city
  comparison against the rest of the 100-city roster (`data/cities.json`)
  found Cairo (`demandWeight` 50) sitting well below its real-world tier -
  tied with Zurich/Vienna despite being one of the ~20 largest metro areas
  on Earth and a historically major aviation crossroads, while cities
  with a comparable real hub role (Istanbul, Moscow) sit at 70. The rest
  of Africa's roster (Lagos 45, Nairobi 40...) reads as internally
  consistent with the same wealth/GDP-proxy pattern the *whole* dataset
  already follows (small rich cities consistently outrank larger poorer
  ones everywhere, not just in Africa) - Cairo was the one specific
  outlier against that pattern. Bumped 50->70 to match Istanbul/Moscow's
  tier and measured directly: a large, real move for one line of JSON -
  **Europe winShare 13.3%->23.0%, EastAsia 47.7%<-56.7%** (essentially a
  1:1 transfer), bankruptcy unchanged (1.67%->1.75%, noise), every other
  region flat. Mechanistically consistent with the contestedness finding
  above (Europe already wins Africa's leadership in 57.3% of all games) -
  strengthening the dominant African gateway made an already-favored leg
  more decisive. Kept at 70 by the user's own direct call after seeing
  the real size of the move; a smaller bump (e.g. 60) or a matching Lagos
  adjustment were both discussed as live alternatives if this needs
  revisiting later.

  **Follow-up: a full-dataset pass (`data/cities.json`, all 100 cities)
  for the same pattern.** Real-world population/GDP/aviation-hub
  comparison flagged six candidates: Karachi (50, tied with Zurich/Vienna
  despite being one of the world's largest cities - the same pre-bump
  Cairo pattern), Lagos (45, already flagged above), Manila (55, hub=0,
  despite genuine historic Pacific-gateway significance), Cairo's own
  `hubQuality` (still 0 despite the weight bump), Bangkok (65, hub=0),
  and Brussels (55, hub=0, despite outsized EU-institutional travel
  significance for its size). Delhi (70)/Mumbai (65) already getting real
  population recognition elsewhere in the dataset made Karachi/Lagos read
  as inconsistent treatment, not a deliberate policy.

  **Tried as a full batch first, per the user's own explicit process
  ("try the full batch; if negative, narrow to individual cities").**
  Real regression found immediately: 3 `GameSetupTests` failures traced
  to Karachi's bump flipping `RegionalEconomics.Factor`'s ranking -
  SouthWestAsia has only 9 eligible cities, so +15 to one of them swung
  its own average enough to edge out Europe for the filler-AI "richest 4"
  pool. Measured directly: Europe's own seat count collapsed 197->96
  (SouthWestAsia's own seat count nearly doubled, 105->206), Europe
  winShare **23.0%->8.0%** - worse than before Cairo was ever bumped, the
  exact opposite of this whole investigation's own goal. AustralAsia
  bankruptcy also spiked 7.2%->15.5%.

  **Isolated city by city.** Reverting Karachi alone fixed Europe
  completely (seats back to 197, winShare actually improved to 24.7%,
  even better than Cairo-only) - confirms Karachi specifically, not the
  batch generally, broke the ranking; **rejected**, kept at 50. AustralAsia's
  bankruptcy spike persisted at 15.5% even with Karachi reverted, isolating
  it to Manila and/or Bangkok (the only two changes inside AustralAsia
  itself). Tested each alone: Manila alone -> 8.2% (noise-level vs. the
  7.2% baseline), Bangkok alone -> 7.2% (exact match) - **both clean
  individually**. Manila + Bangkok *together* -> 15.5% again - a real
  compounding interaction between two individually-safe changes, the same
  "additive assumptions don't hold" shape this session has hit before
  (M45's AutoCapacity+idle-recontest interaction). Since neither showed a
  clear *positive* effect on its own either (Bangkok: literally zero
  measurable change; Manila: noise-level), both were **dropped** rather
  than keeping one arbitrarily.

  **Final landed state**: Lagos 45->60, Cairo `hubQuality` 0->20,
  Brussels `hubQuality` 0->10 - all three confirmed clean in the same
  sweep (total bankruptcy 1.67%, AustralAsia 7.2%, Europe winShare 24.0%,
  all matching or improving on the Cairo-only baseline). Karachi, Manila,
  and Bangkok left untouched at their original values. 985 tests passing.

  **Follow-up experiment: `WinConditionChecker.RequiredRegionCount` 3->4,
  tried and reverted.** With Africa now in `DominanceTrackedRegions.All`
  (7 regions instead of 6), 4 would restore the original "roughly half"
  design intent 3-of-6 had. Measured directly (300-game sweep, seed 1):
  genuine dominance wins collapsed **89.7% of games -> 0.7%**, with 99% of
  games instead running the full 244-quarter campaign and falling back to
  `CampaignRanking`'s end-of-campaign tie-break - a materially weaker
  mechanic (no streak-length requirement) than the real win condition it
  replaced. Worse for Europe specifically, not better (winShare
  24.0%->8.3%), since EastAsia's raw strength wins the fallback tie-break
  more often once nobody can reach the real bar at all.

  **A direct follow-up question the user asked, answered cleanly**: is
  this a streak-sustainment problem (airlines reach 4 but can't hold it
  4 consecutive quarters) or a breadth-ceiling problem (airlines never
  reach 4 at all)? A dedicated diagnostic (`MaxRegionsLedByName`, peak
  simultaneous regions led per seat over the *full* campaign length,
  independent of `RequiredStreakQuarters`) found it's overwhelmingly the
  latter: across 1,200 sampled airline-seats, only **2 (0.17%, both
  SouthWestAsia) ever reached 4 simultaneous regions led at any point,
  even momentarily.** Every other region's hard ceiling is 3 - including
  EastAsia, the strongest region in the game, which peaks at 3 in 75.9%
  of its own games and never once reaches 4 across 290 sampled seats.
  `RequiredStreakQuarters` is nearly irrelevant to this result - the
  breadth ceiling itself sits at 3 for essentially the entire cast.

  **Reverted to 3 as the shipped default** by direct user decision (the
  const on both `WinConditionChecker.RequiredRegionCount` and
  `BalancedGrowthStrategy.DefaultTargetRegionCount` reads 3 again,
  byte-identical to every pre-existing caller). The reasoning and full
  numbers stay recorded in each constant's own doc comment so a future
  reader doesn't re-attempt this blind.

  **Follow-up: productized as a real, setup-time-selectable difficulty
  mechanism instead of a one-off tuning knob**, per direct user request -
  "tied into the difficulty selection." New `GameDifficulty` enum
  (`AeroNext.Core.Competition`) with two real, measured tiers - `Normal`
  (3, the shipped default) and `Hard` (4, the round-2 pairing above -
  genuine dominance wins at 40.7% of games rather than a placeholder
  value). `DifficultySettings.RegionCounts(GameDifficulty)` maps a
  difficulty to a `RegionCountSettings(RequiredRegionCount, TargetRegionCount)`
  record - both counts always move together, never independently, since
  round 1 of this same investigation already proved raising
  `RequiredRegionCount` alone very nearly breaks the win condition.

  **Wired through both real call sites** (`FullGameRunner.Run` gained a
  `GameDifficulty difficulty = GameDifficulty.Normal` parameter;
  `AeroNext.Sim/Program.cs`'s own demo loop stays pinned to `Normal`) -
  each computes `DifficultySettings.RegionCounts` once and threads it two
  ways: `WinConditionChecker.CheckWinner`'s new optional
  `requiredRegionCount` parameter (default = the constant, so every
  existing caller is unaffected), and a new
  `StrategyContext.TargetRegionCountOverride` (nullable, permissive-
  default, read only by `BalancedGrowthStrategy` as
  `context.TargetRegionCountOverride ?? DefaultTargetRegionCount`) built
  fresh into every seat's own `StrategyContext` each quarter.

  **Before any measurement, audited for other hidden ambition caps** per
  the user's own explicit instruction - found none. `TargetRegionCount`
  is the only region-count cap anywhere in the AI (confirmed by a full
  grep across every strategy file). `NetworkOptimizerStrategy` can
  structurally never open a cross-region route at all, but that's
  deliberate ("stabilize, don't expand") and only takes over during real
  financial trouble (`NetworkTroubleRatio > 0`) - a healthy airline stays
  on `BalancedGrowthStrategy` and keeps chasing breadth.
  `RapidExpansionStrategy` has no region cap at all.
  `DomesticProfitOptimizerStrategy` never opens cross-region routes but
  is Starting-era only, out of scope by the same framing the original
  win-condition-awareness investigation used.

  985 tests grew to 995 (two dedicated `BalancedGrowthStrategyTests`
  proving the override changes real `DecideOrders` behavior end-to-end,
  three `GameDifficultyTests`, two new `WinConditionCheckerTests` for the
  override parameter, one `FullGameRunnerTuningTests` byte-identical
  regression guard) - every changed file fully covered by the merged
  coverage report.

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
4. **AI-filler seat assignment systematically hands the strongest regions
   each other as opponents, compounding their advantage.**
   `docs/SOUTHAMERICA_TUNING_EXPERIMENTS.md`'s Rio investigation: filler
   regions are assigned richest-`RegionalEconomics.Factor`-first
   (`GameSetup.CreateSeats`), so a region's filler seat systematically
   co-occurs with NorthAmerica's and EastAsia's own filler seats (the two
   always-richest regions) far more than a random seed would — a
   structural field-toughness disadvantage for every *other* region's
   filler seats, unrelated to any one region's own geography or economics.
   Discovered while measuring a Belo Horizonte city fix for SouthAmerica:
   it swamped that fix's aggregate effect (Rio's own win rate fixed
   cleanly in the random-human-seeded comparison, 0.0%→15.0%, but
   filler-AI-seeded Rio games stayed near 0% because their field is
   *always* tougher, city fix or not). Likely a bigger lever on the
   overall regional-balance picture (`EastAsia`'s persistently outsized
   win share, in particular) than any single region's own city geography.

   **Follow-up round, real diagnostic data.** A full sweep across all six
   regions (`GameSetup.CreateSeats`'s own richest-first ranking) confirmed
   and sharpened this: AustralAsia and SouthWestAsia are excluded from
   filler *entirely* (0 filler seats across 300 games each — they rank last
   and 5th by `RegionalEconomics.Factor`, so the top-4 cut never reaches
   them; they appear in only 28%/29% of games, exclusively via random human
   picks). Among regions that do get filler seats, the effect cuts both
   ways: EastAsia-filler *out*performs EastAsia-random (65.4% vs 35.3% win
   rate) because it's deterministically always Beijing, EastAsia's richest
   eligible city and a genuinely dense cluster (Seoul 950km, Shanghai
   1,100km, both $950K+/qtr pairs) — while NorthAmerica-filler (14.6% vs
   28.2%), SouthAmerica-filler (0.0% vs 14.1%), and Europe-filler (0.0% vs
   19.8%) all *under*perform their own random counterpart, despite strong
   cities in some cases (Atlanta, NorthAmerica's filler default, has the
   highest `hubQuality` in the dataset). Europe-filler's games are the
   starkest evidence for the co-occurrence theory over a per-city one: when
   Europe is filler, the other 3 seats are *always* EastAsia+NorthAmerica+
   SouthAmerica (100% each) — Europe only reaches the filler cut when the
   3 richer regions already have too, so Europe-filler literally never gets
   an easy field.

   **A second, entangled finding, also chased to a rejection.** Every
   region's filler-default city gets a badly under-provisioned Q1
   guaranteed route from `StartingConditionsResolver.Resolve`'s own "keep
   the heuristic pick if profit ≥ $0" rule — even Paris (Europe's filler
   default) gets sent to Athens on Q1 despite 8 genuinely strong domestic
   pairs ($500K–$840K/qtr) sitting right there; $270K–$820K/quarter left on
   the table at every region's filler-default city, universally. A new
   generalized resolver, `ResolveWithMinProfitFraction` (keep the heuristic
   only when it clears a given fraction of the best available, instead of
   just $0), was tested at 0.3/0.5/0.7 against this exact hypothesis — and
   rejected: it's a cliff, not a dial. Below 0.3 it's inert. At 0.5+ total
   bankruptcy nearly quadruples (2.58%→9.50%) and the damage lands hardest
   on EastAsia specifically (4.5%→25.9% bankrupt) — giving *everyone* a
   bigger, pricier starting aircraft reproduces the AustralAsia factor-A
   failure mode (higher fixed costs break previously-safe airlines)
   universally rather than in one region. And even at 0.7,
   AustralAsia/SouthAmerica/SouthWestAsia's own filler seats stay at
   exactly 0.0% — their own Q1 dollar gaps were the smallest of the six
   regions measured, so this mechanism was never going to reach them
   anyway; their problem really is the co-occurrence effect above, not
   under-provisioning. Kept as `ExperimentTuning.GlobalMinProfitFraction`,
   off by default, tested-but-rejected scaffolding — same treatment every
   other rejected factor in this investigation got.

   **Net position, both rounds done**: the co-occurrence effect (richest-
   region-first filler assignment handing EastAsia/NorthAmerica each other
   disproportionately, and locking AustralAsia/SouthWestAsia out of filler
   entirely) is now the *only* live explanation left standing — the
   per-city-quality theory (which the Rio and Q1-threshold work both
   partially chased) accounts for real, separate effects (Rio's own
   isolation, fixed; Q1 under-provisioning, confirmed real but not a safe
   lever) without resolving the core imbalance. The actual fix, if pursued,
   touches `GameSetup.CreateSeats` itself — real, live gameplay code, not a
   QA-harness knob — and needs its own careful design pass (randomizing
   filler region choice weakens the "richest AI" flavor; randomizing filler
   *city* choice only is a narrower option) rather than another drive-by
   experiment. Not designed or scoped here, flagged for whoever picks up
   regional balance next.

   **Two real fixes landed from this investigation, both real gameplay
   code (not QA-harness knobs), both validated by a real 300-game canonical
   sweep:**
   - **Gateway-negotiation relevance.** `RapidExpansionStrategy`'s own
     gateway search ranked every unserved gateway on Earth by "is it
     cross-region" before demand weight, with no reachability check at
     all — the home region's *own* gateway sorted dead last, behind every
     other region's, however far away (traced directly: Paris bidding
     Tokyo/JFK/Hong Kong/LA/São Paulo/Sydney/Cairo/Honolulu/Dakar/
     Anchorage/Gander/Wake Island before ever reaching London or
     Amsterdam). `AiToolkit.DistanceFromNetworkKm` +
     `MaxEraAircraftRangeKm` now gate both `RapidExpansionStrategy`'s and
     `BalancedGrowthStrategy`'s own gateway searches to reachable
     candidates first. Measured: total bankruptcy 2.58%→1.50%, Europe's
     own bankruptcy (the traced case) 5.1%→0.0%, avg game length
     169.7→153.2 quarters, win share stable across every region (no
     redistribution, just a safer game). One small-sample city (Taipei,
     14 seats) showed a real bankruptcy increase on the same seeds, but
     investigated directly: 7 of its 8 bankruptcies land at quarter
     148–214 (30+ years in, far past the fix's own early-game window) and
     its early negotiation spend isn't elevated versus its own surviving
     seeds — most likely ordinary seed-level path divergence in a
     deterministic sim (an early change cascading into a different
     200-quarter trajectory for that specific seed), not a flaw in the
     fix.
   - **Q1 route choice: always-optimal shipped as the real default, root
     cause fixed, difficulty-setting hook kept.** Making `Resolve` itself
     unconditionally optimal was tried per direct instruction and
     initially reverted: total bankruptcy 1.50%→4.42% (EastAsia
     3.1%→10.7%, AustralAsia 5.2%→15.5%), the same failure mode the old
     "V1" experiment found, smaller but still real. Traced to a real bug
     rather than an inherent tradeoff — every route-picking method here
     built its demand model blind to `historical_events.json`'s authored
     1955 demand suppression (Shanghai's Maoist-era entry alone: a 3.3x
     overestimate) — and fixed via
     `StartingConditionsResolver.Quarter1DemandModel`. Re-measured with
     the fix in place: total bankruptcy 1.75%→2.33%, but EastAsia now
     *improves* under always-optimal (4.5%→2.1%) instead of driving the
     regression; every region's own net change sums to +7 bankruptcies,
     and AustralAsia alone accounts for all seven. `Resolve` now ships as
     always-optimal for real.
     `StartingConditionsResolver.ResolveWithWeightedRouteRandomness`/
     `ExperimentTuning.GlobalRouteWeightExponent` remain the mechanism for
     a future difficulty setting to dial this back — `Resolve`'s new
     default is exactly that mechanism's own high-exponent endpoint, so no
     re-plumbing is needed once a real setting exists to drive it. Full
     write-up, numbers, and the reasoning for why the exponent dial's low
     end isn't a validated "safer" endpoint (pre-fix measurement, not
     re-verified): `GAME_SETUP_ROADMAP.md`'s own "difficulty setting" item,
     below.
   - **AustralAsia's own remaining regression (5.2%→12.4% under
     always-optimal): two distinct real mechanisms, traced directly, one
     fix landed so far.**
     - **Mechanism A — self-contestation.** "Most profitable" is
       deterministic, so whenever two starting cities are each other's own
       best real option (Saipan↔Guam; also observed for Tehran↔Baghdad in
       SouthWestAsia), *both* airlines pick the same pair in reverse and
       fly it with the same aircraft. `CompetitiveMarket.Resolve` then
       splits real demand ~50/50 between them, against a start each sized
       for monopoly capacity — traced directly: real Q1 load factor ~68%
       instead of 100%, net income already negative before any external
       shock. Root cause of *this specific* traced case turned out to be
       `AeroNext.QaHarness.GameConfigGenerator` — its own uniform-random
       region draw for simulated humans, independent per seat with no
       collision awareness, frequently lands 2-3 of the 4 seats in the
       same region purely by chance.

       **A related change was tried and reverted while investigating.**
       `GameSetup.CreateSeats`'s own AI filler had always excluded every
       human-claimed region from its own candidate pool (an accidental
       side effect of "richest *unclaimed* region," never a deliberate
       design) — this was loosened, per direct instruction, to let AI
       reach for a human's own region too, keeping only AI-vs-AI
       exclusivity. Measured (300-game canonical sweep): total bankruptcy
       2.33%→3.00%, driven by NorthAmerica (0.4%→1.4%) and EastAsia
       (2.1%→3.2%) — the richest regions, now shared far more often
       (EastAsia's own seat count rose 290→341 across the same 300 games)
       — re-triggering Mechanism A there instead of fixing it, since
       nothing about that change touched the underlying deterministic-
       collision problem. **Reverted per direct follow-up instruction**:
       AI should never contest a human's own region at all — a
       deliberate product/UX decision (an AI competing with a human from
       turn one, in a region the human specifically chose, isn't the same
       kind of fair fight two humans sharing a region is, especially once
       the real UI is expected to warn about that latter case) rather than
       a balance question this investigation should resolve on its own.
       `GameSetup.CreateSeats` is back to its original behavior: AI filler
       excludes every human-claimed region, exactly as it always did
       before this investigation touched it.

       **Contested-vs-uncontested breakdown, 300-game canonical sweep,
       measured twice** — once while the filler-AI region-sharing change
       above was still live, once after its revert — to isolate what
       Mechanism A looks like from real gameplay's own perspective versus
       what the (reverted) change was contributing on top of it.

       *Post-revert (current shipped state)*: uncontested games (all 4
       seats in distinct regions) rose from 146/300 (48.7%) to **219/300
       (73.0%)** — most of the contestation the filler change had been
       creating is gone with it. Uncontested bankruptcy: 0.80% (7/876) —
       the real baseline. Every region's own rate still jumps well above
       that the moment it's contested, confirming Mechanism A is real
       gameplay behavior, not an artifact of the reverted change:
       NorthAmerica 0.00% (15 games — down from 40; most of its own
       contestation *was* the reverted AI-vs-human overlap), Europe 0.00%
       (14 games, unchanged), EastAsia 19.35% (15 games — down from 58,
       but *more* dangerous per instance now that it's purely human-vs-
       human, not a mix that included AI), SouthWestAsia 11.76% (17
       games, unchanged — SouthWestAsia sits below the filler's own reach
       just like AustralAsia, so was never affected either way),
       SouthAmerica 0.00% (14 games — down from 26), AustralAsia
       **21.74%** (10 games, unchanged in both counts and rate — see
       below for why). The overall shape (contested >> uncontested,
       AustralAsia worst per-instance) holds in both measurements; only
       the raw frequency of NorthAmerica/EastAsia/SouthAmerica's own
       contestation moved, exactly tracking how much of it the reverted
       change had been adding.

       **Directly verified, not inferred**: across all 300 games (in
       both measurements), AustralAsia holds 97 total seats and every
       single one is human (`IsHuman: true`); zero are AI filler.
       Structural, not incidental — AustralAsia ranks 5th or 6th of 6 home
       regions by `RegionalEconomics.Factor`, and the deterministic filler
       (the only kind any of these sweeps exercise; `fillerRegionRng` is
       still off by default, wired to nothing real) only ever takes the
       *richest* remaining regions, at most 4 of them — AustralAsia never
       clears that bar regardless of human occupancy, with or without the
       reverted change. So every one of AustralAsia's own contested games
       is two or more *humans* independently landing there via
       `GameConfigGenerator`'s own uncoordinated random draw — completely
       orthogonal to the filler-AI question either way.

       **User's own call, given this: not worth pursuing further for
       AustralAsia specifically.** Real human players independently
       choosing the same low-value region simultaneously is unlikely once
       the real UI surfaces enough information (regional economics,
       other players' visible commitments) to discourage it — unlike
       `GameConfigGenerator`'s own QA-only draw, which has no such
       awareness and no reason to avoid it. Since AI filler can't reach
       AustralAsia at all today, a real game's own AustralAsia airline
       should generally go uncontested through its fragile early game,
       only meeting real rivals there later once it has a stronger
       network to absorb the shock. This means the AustralAsia bankruptcy
       figures in this section's own sweeps (12.4%/15.5%/21.74%
       depending on which round) are real numbers for the QA harness's
       own stress-test distribution, but likely overstate a real game's
       actual risk — recorded here so a future reader doesn't re-alarm on
       them without this context. Mechanism A stays a real, worth-fixing
       problem for the regions two *humans* can still independently
       collide in (NorthAmerica/EastAsia/SouthAmerica above), just not one
       this project should solve by letting AI compete with a human —
       that's the UI's own job (a warning, per direct instruction), not
       this method's.
     - **Mechanism B — the idle second aircraft gets a bad second route.**
       No demand mismatch in the solo (non-contested) case: Q1 alone is
       fine, even better than the heuristic's (+$447K vs. +$167K). By
       quarter 5 the seat's own strategy reuses the idle second dc7 (owned
       since Q1, never purchased again) for a second route, and that
       route's own marginal economics are negative, flipping a profitable
       single-route network into a structural ≈$1M/quarter loss that
       compounds via borrowing until bankruptcy around quarter 30-32.

       **First traced to `RapidExpansionStrategy.FindBestUnservedPairCrossRegionFirst`**
       (`src/AeroNext.Core/Ai/RapidExpansionStrategy.cs`): ranked candidate
       pairs by `AiToolkit.EffectiveDemandScore` (raw demand × a hub bonus)
       filtered only by `aircraft.RangeKm` reachability — no comparison
       anywhere against the aircraft's own `SeatsPerQuarter`, so a fixed,
       oversized idle aircraft could get pointed at a pair that can't fill
       it. **Fixed**: a new `MinLoadFactorForNewPair` (0.4) constant now
       filters candidates inside that method, applied only to the
       idle-fleet-reuse path (`FindBestUnservedPairCrossRegionFirstUsingIdleFleet`,
       its one caller) — deliberately *not* the buy-new path below it.
       Extending the same multi-type search to buy-new was tried first and
       reverted after a real 300-game sweep: trying every era-available
       type (not just the single biggest-range one buy-new has always
       tried) let the strategy successfully *afford and open* routes far
       more often — a cheaper, better-fitting type clears
       `FinancialState.CanAfford` more easily than the single expensive
       type it used to be stuck with — compounding into real
       over-expansion for a strategy with no route cap by design.
       Measured directly: SouthAmerica bankruptcy rose 1.3%→5.1%, traced
       to a real seat (Lima) that opened 4 routes in 9 quarters instead of
       the 2 it opened in the same window without the change, cumulative
       operating cost outrunning revenue. Scoped back to idle-fleet reuse
       only (the actual originally-traced bug) and re-measured: **total
       bankruptcy and every region's own rate came back byte-identical to
       the pre-fix baseline** (2.33% total, SouthAmerica back to 1.3%) —
       the regression is gone, and the fix is a real, tested,
       zero-regression correctness improvement, but has **no measurable
       effect on this session's own 300-game sample** — a good outcome
       for what it targeted, just a narrow one.

       **Second, deeper trace found this specific traced case (Saipan)
       isn't `RapidExpansionStrategy`'s doing at all.** Strategy-name
       tracing showed the seat switches to `DomesticProfitOptimizerStrategy`
       by quarter 5 (heavy Q2-4 losses drop `CashRatio`, and that
       strategy's own `Suitability` is `1 - CashRatio` — lower cash reads
       as *more* suitable) — `RapidExpansionStrategy` never gets the
       chance to open the actual bad second route here. The real
       mechanism is `DomesticProfitOptimizerStrategy`'s own Phase 1
       (`FindMostProfitableDomesticPairAcrossTypes`, that class's own file):
       ranks *which pair* wins by raw demand alone, deliberately capacity-
       blind, by design — its own class doc comment documents **four
       previously tried and reverted fixes** (ranking by real profit,
       real captured demand, a minimum profitability floor, a load-factor-
       derated ranking), all reverted after real 300-game sweeps because
       this game's coarse, discrete aircraft catalog means "which pair
       happens to have an available type near its own best fit" is an
       accident of the catalog, not a real signal — comparing a fit-
       sensitive number *across pairs* leaks that accident into which
       pair gets chosen at all. Phase 2 (already real, cost-subtracted
       profit-based) only ever picks the best *aircraft* for whichever
       pair Phase 1 already locked in — it can't rescue a badly-chosen
       pair. The extensively-documented four-strikes history is real prior
       art any fifth attempt needs to reckon with, and the
       `RapidExpansionStrategy` episode just above independently
       demonstrated the exact same "broadening a search space quietly
       enables more/worse activity" failure shape firsthand.

       **Fifth attempt, genuinely different shape, real and positive
       result.** Rather than touching Phase 1/2 ranking at all (the exact
       mechanism every prior attempt shared, and the reason each one
       leaked a catalog accident into pair selection), added
       `MeetsCatastropheFloor` — a post-hoc gate applied *after* Phases 1
       and 2 have already picked their (pair, aircraft), for both the
       idle-fleet and buy-new call sites independently. Phase 1/2 run
       completely unmodified and still pick exactly what they always
       would; the gate only decides whether to *act* on that pick at all.
       If the pick's own real load factor
       (`pair.PassengersPerQuarter / route.SeatsPerQuarter`) is below
       `MinLoadFactorBeforeSkipping` (0.25 — deliberately far below
       `FareCalculator`'s own ~77% break-even, a catastrophe backstop, not
       a profitability gate), that candidate is nulled out: the idle-fleet
       case falls through to try buying new instead (a genuinely
       different resource, not a downgrade to a worse pair on the same
       resource); the buy-new case has nothing left to fall through to
       and skips opening anything that quarter.

       Measured on the same canonical 300-game sweep (seed 1, 244
       quarters): **total bankruptcy 2.33%→1.92%, AustralAsia
       12.4%→7.2%**, with NorthAmerica (0.4%), Europe (1.0%), EastAsia
       (2.1%), SouthWestAsia (3.8%) and SouthAmerica (1.3%) all
       byte-identical to the pre-fix baseline — the first clean,
       targeted, zero-side-effect improvement to AustralAsia's own
       bankruptcy rate found across this whole investigation. Two new
       unit tests (`DomesticProfitOptimizerStrategyTests.cs`) cover both
       null-out branches directly — a grossly oversized idle aircraft
       falling through to a well-fitted buy-new type, and a grossly
       oversized-only era-available type causing a clean skip — both
       fixture demand/capacity numbers verified numerically against the
       exact test cities/aircraft, not guessed. `DomesticProfitOptimizerStrategy.cs`
       reads fully covered by the merged coverage report.

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
