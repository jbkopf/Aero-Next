# AustralAsia tuning — exploratory experiment plan

Status: **closed out. Factor D shipped** (M65:
`GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers`, ×1.5). Factors
S, A, C were each measured and not adopted. See "Outcome" at the bottom for
the full decision trail and the new shipped baseline numbers.

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

### Phase 1 — One factor at a time (OFAT) ✅ (done)

Each factor alone vs baseline (300 games):

| Factor | AA bankruptcy | Total bankruptcy | AA win share | Verdict |
|---|---|---|---|---|
| baseline | 19.6% | 4.2% | 1.0% | — |
| **A** = MostProfitable | **33.0%** ↑↑ | 5.2% ↑ | 1.7% | **killed** |
| **D** = 1.25 | 12.4% | 3.6% | 2.3% | carried |
| **D** = 1.5 | **5.2%** | **3.0%** | **4.3%** | **carried — best** |
| D = 2.0 | 5.2% | 3.0% | 2.3% | carried as alt |
| D = 3.0 | 5.2% | 3.2% | 2.3% | minor collateral — capped D at 1.5 |
| **S** = 0.95/1.00/1.117 | 9.3% (all three) | 3.3% | 1.7–2.0% | carried |

**A killed**: fixes the capital-starved cities (cgk, sin) but the bigger
starting aircraft's higher operating cost breaks several previously-safe
ones (spn, ceb, gum: 0%→100%) — net regression, total bankruptcy rose.

**D pillar check** (docs' own stop condition: reject if AA wins by fattening
on its home region instead of real dominance) — for every AA win, is
`WinnerRegionsDominated >= 3` (real 3-region dominance) or a weaker
end-of-campaign ranking win?

| D level | AA wins | dominance | ranking | dominance share |
|---|---|---|---|---|
| baseline | 3 | 3 | 0 | 100% |
| 1.25 | 7 | 6 | 1 | 86% |
| **1.5** | 13 | 11 | 2 | **85%** |
| 2.0 | 7 | 3 | 4 | 43% |

Clean at 1.5; degrades at 2.0 — extra wins shift toward the weaker
ranking-declared path. **This, not just the bankruptcy/win-share numbers,
is why D is capped at 1.5.**

### Phase 2 — Combinations ✅ (done)

`2^3` factorial over D, S, C (A already killed), each {off, best level}. C's
cash-dilution confound neutralized by pinning AA cash to the pre-C factor
(0.755) whenever C is on and S is off, so C tests network shape only:

| D | S | C | AA bankrupt | AA win share | Total bankrupt |
|---|---|---|---|---|---|
| 0 | 0 | 0 | 19.6% | 1.0% | 4.2% |
| 0 | 0 | 1 | 20.6% | 1.3% | 3.7% |
| 0 | 1 | 0 | 9.3% | 1.7% | 3.3% |
| 0 | 1 | 1 | 16.5% | 3.3% | 3.4% |
| **1** | **0** | **0** | **5.2%** | **4.3%** | **3.0%** |
| 1 | 0 | 1 | 15.5% | 2.3% | 3.4% |
| 1 | 1 | 0 | 6.2% | 3.3% | 3.0% |
| 1 | 1 | 1 | 12.4% | 3.7% | 3.3% |

**D alone (D=1.5, S off, C off) is the winning cell** — best on every metric.
Adding S or C on top only makes it worse.

**C rejected** — raises AA bankruptcy in all four D×S comparisons despite
the cash-dilution fix. By-city evidence: Nadi (previously 0%) →100%
bankrupt with C on; Singapore's D=1.5 fix (0%) gets undone (→80% with C);
Port Moresby 80%, Darwin 25–50%. The 7 invented cities' weight/distance
calibration likely pulls mid-game AI route-selection into candidates that
look attractive by point-estimate but perform worse in practice, displacing
capacity that used to go to safer cities. Not chased further — recalibrating
against real traffic data (OurAirports/OpenFlights) is separate, larger
work; C is investigated and rejected as authored, not iterated on blind.

**S adds nothing on top of D** — D1S1 (6.2%/3.3%) is *worse* than D1S0
(5.2%/4.3%) on both bankruptcy and win share, at 300 games. Small enough to
be noise at this replication → Phase 3 settles it.

### Phase 3 — Confirmation ✅ (done)

900 games (3× replication), baseline vs D=1.5 vs D=1.5+S=1.0:

| Combo | AA bankrupt | AA win share | Total bankrupt | AA wins (dominance / ranking) |
|---|---|---|---|---|
| baseline | 22.0% | 2.1% | 4.5% | 19 (12 / 7 = 63%) |
| **D=1.5** | **5.5%** | **5.1%** | **3.1%** | **46 (29 / 17 = 63%)** |
| D=1.5+S=1.0 | 5.5% | 3.2% | 3.1% | 29 (11 / 18 = **38%**) |

**Confirmed, at 3× the sample**: D alone and D+S tie on bankruptcy, but
D+S has a **lower win share (5.1%→3.2%) and a lower dominance-win fraction
(63%→38%)** — S doesn't just fail to add value on top of D, it measurably
degrades win quality (more wins settle by end-of-campaign ranking, fewer by
real 3-region dominance). **S not adopted.**

Baseline at 900 games also surfaced two smaller AA trouble cities invisible
at 300 (bkk 11.1%, per 4.3% — below the noise floor of a 300-game sample);
D=1.5 improves both (bkk→5.6%, per→0%), though bkk isn't fully zeroed —
the one small residual beyond Papeete's unfixable 100%.

No collateral to any other region at 900 games (Europe 5.8%→5.8%, EastAsia
3.4%→3.3%, NorthAmerica 0.7%→0.7%, SouthAmerica 2.4%→2.4%, SouthWestAsia
2.3%→2.3%, all D=1.5 vs baseline).

---

## Outcome

**Shipped**: `GravityDemandModel.AustralAsiaTunedIntraRegionMultipliers`
(×1.5 on pairs with both endpoints in AustralAsia) — every real per-quarter
demand-model construction site (`FullGameRunner.cs`,
`AeroNext.Sim/Program.cs`) passes it unconditionally, the same pattern as
`enableHubDemandBonus: true`. No longer a toggle in `ExperimentTuning` —
the "D factor" question is resolved, so it graduated from experiment
scaffolding to real, permanent gameplay behavior.

**Not adopted**: S (starting-cash floor) and A (aircraft-selection bar)
stay in `ExperimentTuning` as off-by-default, fully-tested scaffolding —
harmless to keep, not real behavior. C (extra cities) is rejected as
authored; the 7-city file lives only in the scratchpad, not the repo.

**New shipped canonical baseline** (300 games, seed 1, 244 quarters,
supersedes the table at the top of this doc):

| Region | Seats | Bankruptcy | Win share |
|---|---|---|---|
| AustralAsia | 97 | 5.2% *(was 19.6%)* | 4.3% *(was 1.0%)* |
| EastAsia | 290 | 4.5% | 48.0% |
| Europe | 197 | 7.1% | 5.3% |
| NorthAmerica | 274 | 0.0% | 26.7% |
| SouthAmerica | 237 | 1.3% | 4.3% |
| SouthWestAsia | 105 | 1.0% | 11.3% |

Total bankruptcy: 3.0% (was 4.2%). Avg quarter 146.3.

**Open, not pursued here**: `RapidExpansionStrategy` profitability-gating
on new-route opens for low-cash airlines — the direct fix for Auckland's
"exactly one profitable route" trap. D=1.5 fixed Auckland's specific
bankruptcy (thicker intra-region demand made its other routes viable too),
so this is no longer blocking, but it's a real, separate AI-logic
improvement if revisited later.
