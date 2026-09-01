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
M31 retired), M31/M35–M44 (`BASELINE_AI_ROADMAP.md`), M32–M33/M45–M46
(`ROUTE_CAPACITY_ROADMAP.md`), M47–M48 (`MARKETING_ROADMAP.md`, new). M48
is the current highest issued number. Update this line the moment a new
milestone is opened anywhere.

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
- **`HireExecutiveOrder`'s uncapped, costless action-cap growth** — a
  pre-existing core-mechanic question (should raising the cap ever cost
  something, or have a ceiling?), surfaced but not resolved.
- **The route open/shrink/regrow thrash cycle** — real (M43), a tried
  static load-factor floor measurably made things worse. Needs real
  cross-quarter state (which route was shrunk how recently), filed for
  a future AI-strategy-expansion round, not a quick patch.
- **Europe's own idle-fleet stall under `DomesticProfitOptimizerStrategy`**
  — Europe remains the worst-performing region (86.3% bankrupt after
  M43+M44 both) throughout; root cause not yet isolated.
- **NorthAmerica's own post-M44 dominance (87.6% win share)** — real,
  causally understood (its dense 22-city local network is now a
  compounding advantage under network-contiguity), but a strikingly
  lopsided result flagged for a decision on whether it's acceptable.

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
