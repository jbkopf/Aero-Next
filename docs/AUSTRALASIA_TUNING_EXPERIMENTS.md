# AustralAsia tuning — exploratory experiment plan

Status: **plan only, nothing run past Phase 1 factor S.** This document is the
shared script for working through a set of candidate fixes for AustralAsia's
two problems, measured against the current shipped baseline.

## The problem being tuned

From the 300-game sweep (seed 1, 244 quarters, `DistressStrategy` active,
post-`8183074` V2 starting-route fix):

| Region | Seats | Bankruptcy | Win share |
|---|---|---|---|
| AustralAsia | 97 | **19.6%** | **1.0%** |
| (next worst bankruptcy: Europe) | 197 | 7.1% | 5.3% |
| (rest) | — | ≤4.5% | — |

Two distinct failures:

1. **Survival.** 19.6% bankruptcy vs ≤7.1% everywhere else. Concentrated by
   starting city: Papeete 100% (5/5), Jakarta 100% (5/5), Auckland 100% (3/3),
   Singapore 83% (5/6), KL 17% (1/6); every other AustralAsia start ~0%.
2. **Competitiveness.** 1.0% win share — the region cannot mount a title run
   even in games where its airline stays solvent. Same ceiling Europe hit:
   never reaches 3 simultaneously-led regions.

### Root causes identified so far

- **Papeete** — geographically hopeless at 1955 tech. Nearest in-region
  neighbour is Nadi at 3,492 km / 185 pax per quarter; every route it can
  physically fly loses $2.3M+/quarter (5.7% load factor on an L-049). Not
  fixable by cash or aircraft selection.
- **Auckland** — exactly one profitable domestic route (akl→bne, +$285K/qtr).
  Every other akl route loses $380K–$2.3M/qtr. `RapidExpansionStrategy`
  inevitably opens one of the losers and spirals.
- **Jakarta / Singapore** — V2 resolver hands them a token 28-seat Il-14
  (+$316K/qtr, kept because it clears the "≥ $0" bar) when nearby demand
  (sin↔kul 583k pax/qtr) would support a DC-7 earning ~$1M/qtr. Capital-thin
  start into demand they can't capture.
- **AustralAsia starting cash is a genuine outlier**: $15.1M vs $20–22M
  everywhere else (`RegionalEconomics.Factor` 0.755, dragged down by the
  region's many thin Pacific cities).

### What Phase 1 factor S (starting cash) already showed

Clean sweep, AustralAsia cash floored, seat count held at 97:

| AA starting cash | Bankruptcy | Win share | Total bankruptcy |
|---|---|---|---|
| $15.1M (baseline) | 19.6% | 1.0% | 4.2% |
| $17–18M | **21.6–23.7%** (worse) | ~1.5% | 4.3–4.6% |
| $19M | **9.3%** | 1.7% | 3.3% |
| $20M | 9.3% | 1.7% | 3.2% |
| $22.3M+ | 9.3% | 2.0% | 3.3% |

Findings: **cliff at ~$19M**, not a slope; a half-measure to $17–18M
*backfires* (keeps `RapidExpansionStrategy` alive through more losing opens
without runway to recover); saturates at 9.3% — the residual is structural,
not financial; win share barely moves. No collateral damage to other regions
at any level.

---

## Factors

Four interventions, each a parameterised knob so a run is a config change, not
a code edit. All knobs are **AustralAsia-scoped** and **off by default** —
baseline behaviour is byte-identical when every knob is off.

### S — starting cash floor

- **Knob:** `AERO_AA_CASH_FLOOR` = factor value (e.g. `1.00` → `max(0.755, 1.00) × $20M`).
- **Where:** `StartingConditionsResolver.Resolve`, cash calc only — **must not**
  touch `RegionalEconomics.Factor` itself, which also feeds the AI
  filler-region ranking in `GameSetup` (confound: bumping the raw factor
  reassigns ~100 extra seats to AustralAsia and invalidates the comparison —
  observed this session).
- **Levels:** off / `0.95` ($19M) / `1.00` ($20M) / `1.117` ($22.3M, parity with the richest regions).
- **Expected:** halves survival failure above the cliff; nothing for win share.

### A — starting aircraft selection bar

- **Knob:** `AERO_AA_AIRCRAFT_MODE` = `keep` (current V2) / `bestcombo` (always
  `MostProfitableCombination` for AA starts) / `ratio:X` (re-optimise when the
  original pick earns < X × the best available combo, e.g. `ratio:0.5`).
- **Where:** `StartingConditionsResolver.Resolve`.
- **Note:** the starting fleet is delivered `Owned` for free
  (`FullGameRunner` line ~108) — a bigger starting aircraft does **not** cost
  starting cash, only per-quarter operating cost. So S and A are less coupled
  than expected, but `bestcombo` handing a DC-7 still raises the quarterly
  burn, which interacts with S.
- **Expected:** directly addresses Jakarta / Singapore under-provisioning;
  does nothing for Auckland (one-route city) or Papeete.

### D — demand figures

- **Knob:** `AERO_AA_DEMAND_MULT` = multiplier on `GravityDemandModel.ForPair`
  output when **both endpoints are in AustralAsia**, default `1.0`.
- **Where:** `GravityDemandModel.ForPair`, applied to the final
  `PassengersPerQuarter` — does **not** touch authored `demandWeight` or
  `RegionalEconomics.Factor` (clean-ish; still shifts the global demand-pool
  balance vs other regions' intercontinental routes).
- **Levels:** `1.0` / `1.5` / `2.0`. Optionally a second mechanism —
  distance-decay exponent `2.0 → 1.8` for AA pairs (more physically motivated:
  island region, no road/rail substitution) but wider blast radius; hold in
  reserve for Phase 2 if the flat multiplier disappoints.
- **⚠ Design-pillar tension (CLAUDE.md):** "a home region cannot fund a win";
  "the largest demand pools are intercontinental". Boosting intra-AA demand
  pushes against both. **Stop condition:** if D lifts AA win share by making
  it win *from its home region* (check: winners' led-region set includes
  AustralAsia and ≤2 others), reject that level regardless of the bankruptcy
  improvement.
- **Expected:** fixes Auckland and the thin medium-haul routes (akl→mel,
  nou→syd) that currently lose ~$1M/qtr; the only factor that plausibly moves
  win share.

### C — additional cities

- **Knob:** `AERO_AA_EXTRA_CITIES=1` merges `data/cities.australasia-extra.json`.
- **Candidates** (need real lat/lon + authored `demandWeight`, `gateCapacity`,
  `autoCapacity`, `hubQuality`; source population/traffic from OurAirports /
  OpenFlights):
  - **Wellington NZ** — 640 km neighbour for Auckland (its core problem).
  - **Christchurch NZ** — thickens the NZ domestic triangle.
  - **Darwin AU** — Perth↔Asia bridge (per→drw ~2,650 km, drw→cgk ~2,600 km).
  - **Cairns AU** — Coral Sea / east-coast thickening.
  - **Denpasar (Bali) ID** — dense short-haul off Jakarta.
  - **Port Moresby PG** — bridges the Nadi/Nouméa cluster toward the mainland.
- **Confounds (highest blast radius of any factor):**
  - Adds AA eligible cities → changes `RegionalEconomics.Factor` → changes
    **both** AA cash and the filler-region ranking. Must recompute the
    baseline S level and re-pin the ranking, or freeze the ranking to raw
    pre-C factors for the duration.
  - More non-gateway cities = more possible bad *human* starts unless demand
    weights are set so none is a trap (run the
    `StartingConditionsTests.Resolve_EveryEligibleStartingCity...` check
    against the merged set).
  - Changes total city count → cross-region connecting-itinerary pool → every
    region's numbers move. Full-sweep readout mandatory.
- **Expected:** the only factor that helps Papeete (via a nearer neighbour);
  thickens the whole network so `RapidExpansionStrategy` has fewer losing
  routes to fall into.

---

## Metrics (response variables)

Emitted per run as one CSV row: `combo_id, S, A, D, C, <metrics...>`.

**Primary**
- AustralAsia bankruptcy rate
- AustralAsia win share
- Total (all-region) bankruptcy rate
- Full per-region table: seats / bankruptcy / win share (collateral check)

**Secondary**
- Avg game length (quarters) — solvency raises it, which reshuffles win
  distribution; needed to read win-share deltas honestly.
- AustralAsia bankruptcy **by starting city** (the ppt/akl/cgk/sin/kul
  breakdown — this is the real diagnostic; see Phase 0).
- Distribution of which roster strategy AA airlines are dominated by over the
  Starting era.
- Quarter of first insolvency for AA airlines that go bankrupt (does the fix
  delay death or prevent it?).
- For any run that lifts AA win share: the led-region set of AA winners
  (design-pillar guard for factor D).

**Stop conditions** (scope the offending factor/level out, per the V1 lesson
where full-reoptimisation spiked EastAsia to 24.5%):
- Total bankruptcy rises vs baseline.
- Any other region's bankruptcy rises meaningfully (> ~2 pts).
- AA win share rises via home-region wins (factor D guard).
- Avg game length swings enough to make the win-share comparison meaningless
  (> ~30 quarters) without a clear mechanism.

---

## Fixed parameters (held constant across every run)

- 300 games, `configSeed = 1..300`, `campaignSeed = 500_001..`, 244 quarters.
- `DistressStrategy` active (the shipped config).
- All data files unchanged except the factor under test.
- Same machine, Release build.

Confirm the baseline (all knobs off) reproduces the table at the top of this
doc **byte-identical** before trusting any delta.

---

## Phases

### Phase 0 — Instrumentation ✅ (done)

1. ~~By-starting-city breakdown as a permanent `FullGameResult` field.~~
   `StartingCityIdByName` — commit `c5ce839`.
2. ~~The factor knobs, off-by-default, byte-identical baseline.~~
   `ExperimentTuning` (S, A, D) + first `AeroNext.QaHarness.Tests` project —
   commits `2f7b782`, `05e05de`. **Factor C carries no knob**: it is
   caller-side (merge a city file before building seats) and blocked on data
   authoring — folded into Phase 1's C step. The batch runner takes a
   `citiesFile` per combo.
3. ~~Batch runner.~~ Scratchpad `m45sweep` — `Combo(label, ExperimentTuning,
   extraCitiesFile?)` list, one sweep each, appends a row to
   `austral-experiments.csv` plus a console AA-by-starting-city block.
4. ~~Baseline row vs the top-of-doc table.~~ Reproduces **exactly**:
   AustralAsia 97 / 19.6% / 1.0%, total 4.2%, avg quarter 146.0, every other
   region to the seat.

**Sweep-infra discipline (learned the hard way in Phase 0):** a force-killed
`dotnet run` / `dotnet exec` leaves the *app* process orphaned and burning a
core; several orphans accumulating is what makes later sweeps look "hung"
(they are just starved 4–8×). Run one sweep at a time, let it finish on its
own (the completion notification), never `TaskStop` a running sweep. If one
must be killed, `Get-Process dotnet | Stop-Process -Force` and confirm zero
before the next run.

### Phase 1 — One factor at a time (OFAT)

Each factor alone, every level, vs baseline. **S is done** (table above).
Remaining: A (3 levels), D (3 levels), C (1–2 city sets).

Output: per-factor dose-response curve. Identify which factors move which
metric, and the shape (S was a cliff — expect others may be non-linear too).
Kill any factor that trips a stop condition at every level.

**Decision gate:** carry only factors with a real effect into Phase 2.

### Phase 2 — Combinations

Full `2^k` factorial over the surviving factors, each at {off, its best Phase-1
level}. With all four surviving: 16 cells (~40–60 min of sweeps — acceptable
as one scripted batch). With three: 8 cells.

Reading the results:
- **Main effects** — does each factor still help in the presence of the others?
- **Interactions** — the hypotheses to test explicitly:
  - **S × A**: A picks the right aircraft, S funds the runway to operate it.
    Expect the best *survival* cell.
  - **D alone vs S alone**: do they fix different starting cities (D→akl/nou,
    S→cgk/sin)? If so they're additive.
  - **C makes S redundant?** Adding cities raises the raw factor; if C alone
    gets AA cash near the cliff, S may add nothing on top.
  - **D × (anything)** for win share — D is the only factor expected to move
    it; does a combo unlock a real title run, and does it stay pillar-legal?

**Decision gate:** pick the 1–2 combinations that best fix survival without
collateral, and (separately) the best combination for win share if any.

### Phase 3 — Confirmation

- Re-run the 1–2 chosen combinations at higher replication: seeds 1..600 or
  1..1000, to check the deltas are stable and not seed artefacts.
- Full per-region collateral table.
- The win-share needle specifically: is AA's rise real and pillar-legal?
- If a combination wins, write it up in the relevant roadmap
  (`GAME_SETUP_ROADMAP.md` / `BASELINE_AI_ROADMAP.md` / a demand-model doc),
  land the data/code changes as real commits, and re-run the canonical
  300-game sweep as the new baseline.

---

## Open questions to resolve before Phase 2

- Does factor C's ranking confound get handled by "freeze ranking to raw
  pre-C factors" or by "recompute and re-pin"? (Affects whether C results are
  comparable to S results.)
- For factor D, flat multiplier vs distance-decay softening — decide after
  Phase 1 D shows whether the flat version is enough.
- Is there a fifth factor worth screening — `RapidExpansionStrategy`
  profitability-gating on new-route opens for low-cash airlines? It's the
  direct fix for Auckland (which S/A/C only partially help), but it's an
  AI-logic change, not an AustralAsia-scoped data knob, so it's a different
  kind of intervention. Hold as a candidate; add if Phase 1 leaves Auckland
  unsolved.
