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

**Every milestone across all 8 roadmap docs is complete** as of this
writing: M0–M14 (`SIM_CORE_ROADMAP.md`), M15–M19 (`GAME_SETUP_ROADMAP.md`),
M20–M22 (`HISTORICAL_DYNAMICS_ROADMAP.md`), M23–M24
(`WIN_CONDITIONS_ROADMAP.md`), M25–M30/M34 (`FINANCIAL_MODEL_ROADMAP.md`,
M31 retired), M31/M35–M42 (`BASELINE_AI_ROADMAP.md`), M32–M33
(`ROUTE_CAPACITY_ROADMAP.md`). M42 is the current highest issued number.
Update this line the moment a new milestone is opened anywhere.

---

## Active priority

**EastAsia/AustralAsia-vs-everyone-else regional imbalance** — see the
project-memory note below. Planning underway as of 2026-08-31.

---

## Deferred / "Kept in mind, not scheduled" items, by roadmap doc

### `GAME_SETUP_ROADMAP.md`
- **AI differentiation** — the 4 airlines have differentiated starting
  conditions since M15–M19, but nothing beyond M39–M41's region-aware
  strategies makes them play differently by home region's own character.
- **Route-entry slot requirement + network contiguity** — fully designed,
  not implemented. `QuarterEngine` still never checks landing-rights
  ownership against a plain `RouteOrder` (only `NegotiationOrder` touches
  `LandingRightsBoard`). Two rules ready to build: (1) a `RouteOrder`
  touching a gateway the airline holds no slot at should be rejected; (2)
  new routes must grow outward from a city the airline already serves
  (home city always valid; the valid-origin set for a quarter is cities
  touched by routes active at that quarter's start).
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
- **Service level and marketing** remain a single placeholder
  (`Airline.Quality`) standing in for both; fare itself is fully settled
  (M34).
- **Interest rate doesn't scale with leverage** — fixed 2% regardless of
  debt carried.
- **No ongoing gate-holding cost** — a won slot is free to hold forever.
- **Age-based aircraft depreciation** — flat resale fraction regardless
  of age, deliberately simple until fleet age is tracked for other
  reasons.

### `BASELINE_AI_ROADMAP.md`
- **`AllianceCourtOrder`** — excluded everywhere; alliances have no game
  consequence yet, so teaching the AI to court one would be cosmetic.
- **`HireExecutiveOrder`'s uncapped, costless action-cap growth** — a
  pre-existing core-mechanic question (should raising the cap ever cost
  something, or have a ceiling?), surfaced but not resolved.

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
- **Gate capacity / gateway status changing over time** — an airport's
  physical capacity growing via terminal expansions, or a city that
  wasn't a gateway in 1955 becoming one later. Structurally bigger than a
  demand schedule: a new M9 contested asset appearing mid-campaign,
  touching `LandingRightsBoard` and M15's eligible-city logic. A natural
  follow-on, not part of that roadmap.
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
